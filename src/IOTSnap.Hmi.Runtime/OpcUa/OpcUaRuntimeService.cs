using IOTSnap.Hmi.Data;
using IOTSnap.Hmi.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.DataProtection;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using System.Text;
using System.Threading.Channels;

namespace IOTSnap.Hmi.Runtime.OpcUa;

public sealed class OpcUaRuntimeService(
    IDbContextFactory<HmiDbContext> dbContextFactory,
    ILogger<OpcUaRuntimeService> logger,
    IDataProtectionProvider dataProtectionProvider,
    IHostEnvironment hostEnvironment) : BackgroundService, IOpcUaRuntime
{
    private static readonly ITelemetryContext Telemetry = DefaultTelemetry.Create(_ => { });
    private readonly SemaphoreSlim _statusLock = new(1, 1);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private ISession? _session;
    private Subscription? _subscription;
    private int? _activeProfileId;
    private string? _activeSubscriptionSignature;
    private int _activeMonitoredCount;
    private long _receivedNotificationCount;
    private long _persistedTrendSampleCount;
    private string? _lastSampleNodeId;
    private string? _lastSampleValueText;
    private string? _lastTrendPersistenceError;
    private OpcUaRuntimeStatus _status = new();
    private readonly Dictionary<string, OpcUaTagSnapshot> _tagSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MonitoredNodeConfiguration> _configuredNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _tagSnapshotLock = new(1, 1);
    private readonly Channel<TrendSampleWrite> _trendSampleChannel = Channel.CreateUnbounded<TrendSampleWrite>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public async Task<OpcUaRuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await _statusLock.WaitAsync(cancellationToken);
        try
        {
            return new OpcUaRuntimeStatus
            {
                IsConnected = _status.IsConnected,
                ActiveProfileName = _status.ActiveProfileName,
                EndpointUrl = _status.EndpointUrl,
                ConfiguredNodeCount = _status.ConfiguredNodeCount,
                UpdatedUtc = _status.UpdatedUtc,
                Detail = _status.Detail
            };
        }
        finally
        {
            _statusLock.Release();
        }
    }

    public Task RefreshNowAsync(CancellationToken cancellationToken = default)
        => RefreshStatusAsync(cancellationToken);

    public async Task<IReadOnlyList<OpcUaAlarmSnapshot>> GetAlarmsAsync(bool includeCleared = false, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.OpcUaAlarmStates.AsNoTracking();

        if (!includeCleared)
        {
            query = query.Where(x => x.IsActive || !x.IsAcknowledged);
        }

        var alarms = await query
            .ToListAsync(cancellationToken);

        return alarms
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.Severity)
            .ThenByDescending(x => x.LastUpdatedUtc)
            .Select(x => new OpcUaAlarmSnapshot
            {
                NodeId = x.NodeId,
                DisplayName = x.DisplayName,
                Area = x.Area,
                DataType = x.DataType,
                StatusCode = x.StatusCode,
                LastValueText = x.LastValueText,
                AlarmText = x.AlarmText,
                Severity = x.Severity,
                IsActive = x.IsActive,
                IsAcknowledged = x.IsAcknowledged,
                FirstRaisedUtc = x.FirstRaisedUtc,
                LastRaisedUtc = x.LastRaisedUtc,
                AcknowledgedUtc = x.AcknowledgedUtc,
                AcknowledgedBy = x.AcknowledgedBy,
                ClearedUtc = x.ClearedUtc,
                LastUpdatedUtc = x.LastUpdatedUtc
            })
            .ToList();
    }

    public async Task<OpcUaTagWriteResult> WriteTagAsync(string nodeId, string valueText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return new OpcUaTagWriteResult
            {
                Succeeded = false,
                Message = "NodeId is required."
            };
        }

        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            if (_session is null || !_session.Connected)
            {
                return new OpcUaTagWriteResult
                {
                    Succeeded = false,
                    Message = "OPC UA session is not connected."
                };
            }

            OpcUaTagSnapshot? snapshot;
            await _tagSnapshotLock.WaitAsync(cancellationToken);
            try
            {
                _tagSnapshots.TryGetValue(nodeId, out snapshot);
            }
            finally
            {
                _tagSnapshotLock.Release();
            }

            if (snapshot is null)
            {
                return new OpcUaTagWriteResult
                {
                    Succeeded = false,
                    Message = "Tag is not currently mapped."
                };
            }

            if (!snapshot.IsWritable)
            {
                return new OpcUaTagWriteResult
                {
                    Succeeded = false,
                    Message = "Tag is read-only."
                };
            }

            if (!TryConvertValue(valueText, snapshot.DataType, out var typedValue, out var conversionError))
            {
                return new OpcUaTagWriteResult
                {
                    Succeeded = false,
                    Message = conversionError
                };
            }

            var writeValues = new WriteValueCollection
            {
                new()
                {
                    NodeId = NodeId.Parse(nodeId),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(typedValue))
                }
            };

            var response = await _session.WriteAsync(null, writeValues, cancellationToken);
            var statusCode = response.Results?.Count > 0 ? response.Results[0] : StatusCodes.BadUnexpectedError;

            if (StatusCode.IsGood(statusCode))
            {
                return new OpcUaTagWriteResult
                {
                    Succeeded = true,
                    Message = "Write accepted."
                };
            }

            return new OpcUaTagWriteResult
            {
                Succeeded = false,
                Message = $"Write failed: {statusCode}"
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Write command failed for node {NodeId}", nodeId);
            return new OpcUaTagWriteResult
            {
                Succeeded = false,
                Message = $"Write failed: {ex.Message}"
            };
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task<OpcUaAlarmCommandResult> AcknowledgeAlarmAsync(string nodeId, string acknowledgedBy, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return new OpcUaAlarmCommandResult
            {
                Succeeded = false,
                Message = "NodeId is required."
            };
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var alarm = await db.OpcUaAlarmStates.FirstOrDefaultAsync(x => x.NodeId == nodeId, cancellationToken);
        if (alarm is null)
        {
            return new OpcUaAlarmCommandResult
            {
                Succeeded = false,
                Message = "Alarm not found."
            };
        }

        if (alarm.IsAcknowledged)
        {
            return new OpcUaAlarmCommandResult
            {
                Succeeded = true,
                Message = $"Alarm is already acknowledged for {alarm.DisplayName}."
            };
        }

        alarm.IsAcknowledged = true;
        alarm.AcknowledgedBy = string.IsNullOrWhiteSpace(acknowledgedBy) ? "unknown" : acknowledgedBy.Trim();
        alarm.AcknowledgedUtc = DateTimeOffset.UtcNow;
        alarm.LastUpdatedUtc = DateTimeOffset.UtcNow;
        db.OpcUaAlarmTransitions.Add(new OpcUaAlarmTransition { NodeId = nodeId, Transition = "Acknowledged", Severity = alarm.Severity, ActorUsername = alarm.AcknowledgedBy, Detail = alarm.AlarmText, OccurredUtc = alarm.AcknowledgedUtc.Value });
        await db.SaveChangesAsync(cancellationToken);

        return new OpcUaAlarmCommandResult
        {
            Succeeded = true,
            Message = $"Alarm acknowledged for {alarm.DisplayName}."
        };
    }

    public async Task<IReadOnlyList<OpcUaTagSnapshot>> GetTagSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        await _tagSnapshotLock.WaitAsync(cancellationToken);
        try
        {
            var nowUtc = DateTimeOffset.UtcNow;
            return _tagSnapshots.Values
                .Select(x => ApplyHealthState(x, nowUtc))
                .OrderBy(x => x.Area)
                .ThenBy(x => x.DisplayName)
                .ToList();
        }
        finally
        {
            _tagSnapshotLock.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OPC UA runtime service started");
        var consecutiveFailures = 0;
        var trendWriterTask = DrainTrendSamplesAsync(stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await RefreshStatusAsync(stoppingToken);
                var status = await GetStatusAsync(stoppingToken);
                consecutiveFailures = status.IsConnected ? 0 : consecutiveFailures + 1;
                await Task.Delay(OpcUaReconnectPolicy.GetDelay(consecutiveFailures), stoppingToken);
            }
        }
        finally
        {
            _trendSampleChannel.Writer.TryComplete();
            await trendWriterTask;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await DisconnectSessionAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    private async Task RefreshStatusAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            var profiles = await db.OpcUaConnectionProfiles
                .Include(x => x.NodeMappings)
                .ToListAsync(cancellationToken);
            var profile = profiles.OrderByDescending(x => x.UpdatedUtc).FirstOrDefault();

            if (profile is null)
            {
                await DisconnectSessionAsync(cancellationToken);
                await SetStatusAsync(new OpcUaRuntimeStatus
                {
                    IsConnected = false,
                    Detail = "No OPC UA profile has been configured yet.",
                    UpdatedUtc = DateTimeOffset.UtcNow
                }, cancellationToken);
                return;
            }

            if (string.IsNullOrWhiteSpace(profile.ProtectedPassword) && !string.IsNullOrWhiteSpace(profile.Password))
            {
                profile.ProtectedPassword = dataProtectionProvider.CreateProtector("IOTSnap.Hmi.OpcUaProfilePassword.v1").Protect(profile.Password);
                profile.Password = null;
                profile.UpdatedUtc = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }

            var endpointIsValid = Uri.TryCreate(profile.EndpointUrl, UriKind.Absolute, out var endpointUri)
                && string.Equals(endpointUri.Scheme, "opc.tcp", StringComparison.OrdinalIgnoreCase);

            if (!profile.Enabled)
            {
                await DisconnectSessionAsync(cancellationToken);
                await SetStatusAsync(new OpcUaRuntimeStatus
                {
                    IsConnected = false,
                    ActiveProfileName = profile.Name,
                    EndpointUrl = profile.EndpointUrl,
                    ConfiguredNodeCount = profile.NodeMappings.Count,
                    UpdatedUtc = DateTimeOffset.UtcNow,
                    Detail = "The active profile is disabled."
                }, cancellationToken);
                return;
            }

            if (!endpointIsValid)
            {
                await DisconnectSessionAsync(cancellationToken);
                await SetStatusAsync(new OpcUaRuntimeStatus
                {
                    IsConnected = false,
                    ActiveProfileName = profile.Name,
                    EndpointUrl = profile.EndpointUrl,
                    ConfiguredNodeCount = profile.NodeMappings.Count,
                    UpdatedUtc = DateTimeOffset.UtcNow,
                    Detail = "Endpoint must be an absolute opc.tcp URL."
                }, cancellationToken);
                return;
            }

            var sessionResult = await EnsureSessionAndSubscriptionAsync(profile, cancellationToken);
            var sampleDiagnostics = $"Samples received={_receivedNotificationCount}, persisted={_persistedTrendSampleCount}, lastNode={_lastSampleNodeId ?? "n/a"}";
            if (!string.IsNullOrWhiteSpace(_lastTrendPersistenceError))
            {
                sampleDiagnostics = $"{sampleDiagnostics}, lastPersistError={_lastTrendPersistenceError}";
            }

            await SetStatusAsync(new OpcUaRuntimeStatus
            {
                IsConnected = sessionResult.IsConnected,
                ActiveProfileName = profile.Name,
                EndpointUrl = profile.EndpointUrl,
                ConfiguredNodeCount = profile.NodeMappings.Count,
                UpdatedUtc = DateTimeOffset.UtcNow,
                Detail = $"{sessionResult.Detail} {sampleDiagnostics}"
            }, cancellationToken);

            await SynchronizeAlarmStateAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OPC UA runtime status refresh failed");
            await SetStatusAsync(new OpcUaRuntimeStatus
            {
                IsConnected = false,
                UpdatedUtc = DateTimeOffset.UtcNow,
                Detail = "Unable to refresh runtime status. Check logs for details."
            }, cancellationToken);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task SetStatusAsync(OpcUaRuntimeStatus status, CancellationToken cancellationToken)
    {
        await _statusLock.WaitAsync(cancellationToken);
        try
        {
            _status = status;
        }
        finally
        {
            _statusLock.Release();
        }
    }

    private async Task<(bool IsConnected, string Detail)> EnsureSessionAndSubscriptionAsync(
        OpcUaConnectionProfile profile,
        CancellationToken cancellationToken)
    {
        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            var subscriptionSignature = BuildSubscriptionSignature(profile);
            if (_session is not null
                && _session.Connected
                && _activeProfileId == profile.Id)
            {
                if (string.Equals(_activeSubscriptionSignature, subscriptionSignature, StringComparison.Ordinal))
                {
                    return (true, $"Connected. Monitoring {_activeMonitoredCount} node(s).");
                }

                var monitoredCount = await RebuildSubscriptionAsync(_session, profile, cancellationToken);
                await RefreshMappedNodeValuesAsync(_session, profile, cancellationToken);
                _activeSubscriptionSignature = subscriptionSignature;
                _activeMonitoredCount = monitoredCount;
                return (true, $"Connected. Monitoring {monitoredCount} node(s).");
            }

            await DisconnectSessionInternalAsync(cancellationToken);

            var config = await BuildAppConfigurationAsync(cancellationToken);
            var endpointDescription = await SelectConfiguredEndpointAsync(config, profile, cancellationToken);
            var endpoint = new ConfiguredEndpoint(null, endpointDescription, EndpointConfiguration.Create(config));

            var identity = BuildUserIdentity(profile);
            _session = await new DefaultSessionFactory(Telemetry).CreateAsync(
                config,
                endpoint,
                false,
                false,
                "IOTSnap.Hmi.Session",
                60000,
                identity,
                null,
                cancellationToken);

            _activeProfileId = profile.Id;
            var totalMonitoredItems = await RebuildSubscriptionAsync(_session, profile, cancellationToken);
            await RefreshMappedNodeValuesAsync(_session, profile, cancellationToken);
            _activeSubscriptionSignature = subscriptionSignature;
            _activeMonitoredCount = totalMonitoredItems;

            return (_session.Connected, $"Connected. Monitoring {totalMonitoredItems} node(s).");
        }
        catch (Exception ex)
        {
            await DisconnectSessionInternalAsync(cancellationToken);
            logger.LogWarning(ex, "OPC UA connection attempt failed for endpoint {Endpoint}", profile.EndpointUrl);
            return (false, $"Connection failed: {ex.Message}");
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private static string BuildSubscriptionSignature(OpcUaConnectionProfile profile)
        => string.Join('|', profile.NodeMappings
            .OrderBy(x => x.NodeId, StringComparer.Ordinal)
            .Select(x => $"{x.NodeId}:{x.SamplingIntervalMs}:{x.IsWritable}"));

    private static async Task<EndpointDescription> SelectConfiguredEndpointAsync(
        ApplicationConfiguration config,
        OpcUaConnectionProfile profile,
        CancellationToken cancellationToken)
    {
        var endpointConfiguration = EndpointConfiguration.Create(config);
        using var discoveryClient = await DiscoveryClient.CreateAsync(
            config,
            new Uri(profile.EndpointUrl),
            endpointConfiguration,
            DiagnosticsMasks.None,
            cancellationToken);

        var endpoints = await discoveryClient.GetEndpointsAsync(null, cancellationToken);
        var matchedEndpoint = FindMatchingEndpoint(endpoints, profile);
        if (matchedEndpoint is not null)
        {
            return matchedEndpoint;
        }

        var availableProfiles = string.Join(", ", endpoints.Select(DescribeEndpoint));
        throw new ServiceResultException(
            StatusCodes.BadConfigurationError,
            $"No OPC UA endpoint matches UseSecurity={profile.UseSecurity}, SecurityPolicy={profile.SecurityPolicy}, SecurityMode={profile.SecurityMode}. Available endpoints: {availableProfiles}");
    }

    private static EndpointDescription? FindMatchingEndpoint(
        EndpointDescriptionCollection endpoints,
        OpcUaConnectionProfile profile)
    {
        var desiredPolicyUri = ResolveSecurityPolicyUri(profile.UseSecurity, profile.SecurityPolicy);
        var desiredSecurityMode = ResolveSecurityMode(profile.UseSecurity, profile.SecurityMode);

        return endpoints
            .Where(endpoint => string.Equals(endpoint.TransportProfileUri, Profiles.UaTcpTransport, StringComparison.Ordinal))
            .Where(endpoint => string.Equals(endpoint.SecurityPolicyUri ?? SecurityPolicies.None, desiredPolicyUri, StringComparison.Ordinal))
            .Where(endpoint => endpoint.SecurityMode == desiredSecurityMode)
            .OrderByDescending(endpoint => endpoint.SecurityLevel)
            .ThenBy(endpoint => endpoint.EndpointUrl, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string ResolveSecurityPolicyUri(bool useSecurity, string? securityPolicy)
    {
        if (!useSecurity)
        {
            return SecurityPolicies.None;
        }

        return (securityPolicy ?? string.Empty).Trim() switch
        {
            "" => SecurityPolicies.Basic256Sha256,
            nameof(SecurityPolicies.None) => SecurityPolicies.None,
            nameof(SecurityPolicies.Basic128Rsa15) => SecurityPolicies.Basic128Rsa15,
            nameof(SecurityPolicies.Basic256) => SecurityPolicies.Basic256,
            nameof(SecurityPolicies.Basic256Sha256) => SecurityPolicies.Basic256Sha256,
            nameof(SecurityPolicies.Aes128_Sha256_RsaOaep) => SecurityPolicies.Aes128_Sha256_RsaOaep,
            nameof(SecurityPolicies.Aes256_Sha256_RsaPss) => SecurityPolicies.Aes256_Sha256_RsaPss,
            var policy when policy.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || policy.StartsWith("https://", StringComparison.OrdinalIgnoreCase) => policy,
            var policy => throw new ServiceResultException(
                StatusCodes.BadConfigurationError,
                $"Unsupported OPC UA security policy '{policy}'.")
        };
    }

    private static MessageSecurityMode ResolveSecurityMode(bool useSecurity, string? securityMode)
    {
        if (!useSecurity)
        {
            return MessageSecurityMode.None;
        }

        return Enum.TryParse<MessageSecurityMode>(securityMode, ignoreCase: true, out var parsedMode)
            ? parsedMode
            : throw new ServiceResultException(
                StatusCodes.BadConfigurationError,
                $"Unsupported OPC UA security mode '{securityMode}'.");
    }

    private static string DescribeEndpoint(EndpointDescription endpoint)
        => $"{endpoint.EndpointUrl} [{endpoint.SecurityPolicyUri ?? SecurityPolicies.None} / {endpoint.SecurityMode}]";

    private IUserIdentity BuildUserIdentity(OpcUaConnectionProfile profile)
    {
        var password = GetPassword(profile);
        if (string.Equals(profile.AuthenticationMode, "UsernamePassword", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(profile.Username)
            && !string.IsNullOrWhiteSpace(password))
        {
            return new UserIdentity(new UserNameIdentityToken
            {
                UserName = profile.Username,
                DecryptedPassword = Encoding.UTF8.GetBytes(password)
            });
        }

        return new UserIdentity(new AnonymousIdentityToken());
    }

    private string? GetPassword(OpcUaConnectionProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ProtectedPassword)) return null;

        try
        {
            return dataProtectionProvider.CreateProtector("IOTSnap.Hmi.OpcUaProfilePassword.v1")
                .Unprotect(profile.ProtectedPassword);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not decrypt OPC UA credentials for profile {ProfileName}", profile.Name);
            return null;
        }
    }

    private static bool TryConvertValue(string valueText, string dataType, out object typedValue, out string error)
    {
        var normalizedDataType = string.IsNullOrWhiteSpace(dataType)
            ? "Auto"
            : dataType.Trim();

        switch (normalizedDataType.ToLowerInvariant())
        {
            case "bool":
            case "boolean":
                if (bool.TryParse(valueText, out var boolValue))
                {
                    typedValue = boolValue;
                    error = string.Empty;
                    return true;
                }
                break;

            case "int":
            case "int32":
                if (int.TryParse(valueText, out var intValue))
                {
                    typedValue = intValue;
                    error = string.Empty;
                    return true;
                }
                break;

            case "long":
            case "int64":
                if (long.TryParse(valueText, out var longValue))
                {
                    typedValue = longValue;
                    error = string.Empty;
                    return true;
                }
                break;

            case "float":
            case "single":
                if (float.TryParse(valueText, out var floatValue))
                {
                    typedValue = floatValue;
                    error = string.Empty;
                    return true;
                }
                break;

            case "double":
                if (double.TryParse(valueText, out var doubleValue))
                {
                    typedValue = doubleValue;
                    error = string.Empty;
                    return true;
                }
                break;

            case "string":
                typedValue = valueText;
                error = string.Empty;
                return true;
        }

        if (string.Equals(normalizedDataType, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            if (bool.TryParse(valueText, out var boolValue))
            {
                typedValue = boolValue;
                error = string.Empty;
                return true;
            }

            if (long.TryParse(valueText, out var longValue))
            {
                typedValue = longValue;
                error = string.Empty;
                return true;
            }

            if (double.TryParse(valueText, out var doubleValue))
            {
                typedValue = doubleValue;
                error = string.Empty;
                return true;
            }

            typedValue = valueText;
            error = string.Empty;
            return true;
        }

        typedValue = valueText;
        error = $"Unable to convert '{valueText}' to {normalizedDataType}.";
        return false;
    }

    private async Task<ApplicationConfiguration> BuildAppConfigurationAsync(CancellationToken cancellationToken)
    {
        OpcUaCertificateStorePaths.EnsureDirectories(hostEnvironment);

        var config = new ApplicationConfiguration
        {
            ApplicationName = "IOTSnap.Hmi",
            ApplicationUri = $"urn:{Utils.GetHostName()}:IOTSnap.Hmi",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = OpcUaCertificateStorePaths.GetOwnDirectory(hostEnvironment),
                    SubjectName = "CN=IOTSnap.Hmi"
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = OpcUaCertificateStorePaths.GetTrustedIssuerDirectory(hostEnvironment)
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = OpcUaCertificateStorePaths.GetTrustedPeerDirectory(hostEnvironment)
                },
                RejectedCertificateStore = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = OpcUaCertificateStorePaths.GetRejectedDirectory(hostEnvironment)
                },
                AutoAcceptUntrustedCertificates = true,
                AddAppCertToTrustedStore = true
            },
            TransportConfigurations = new TransportConfigurationCollection(),
            TransportQuotas = new TransportQuotas
            {
                OperationTimeout = 15000
            },
            ClientConfiguration = new ClientConfiguration
            {
                DefaultSessionTimeout = 60000
            }
        };

        await config.ValidateAsync(ApplicationType.Client);
        var application = new ApplicationInstance(Telemetry)
        {
            ApplicationName = config.ApplicationName,
            ApplicationType = ApplicationType.Client,
            ApplicationConfiguration = config
        };
        await application.CheckApplicationInstanceCertificatesAsync(false, 2048, cancellationToken);
        config.CertificateValidator.CertificateValidation += (_, e) => { e.Accept = true; };

        cancellationToken.ThrowIfCancellationRequested();
        return config;
    }

    private async Task<int> RebuildSubscriptionAsync(ISession session, OpcUaConnectionProfile profile, CancellationToken cancellationToken)
    {
        if (_subscription is not null)
        {
            try
            {
                await _subscription.DeleteAsync(true, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to delete existing OPC UA subscription");
            }

            _subscription.Dispose();
            _subscription = null;
        }

        var subscription = new Subscription(session.DefaultSubscription)
        {
            DisplayName = "IOTSnap.Hmi.Runtime",
            PublishingInterval = Math.Max(100, profile.PublishingIntervalMs)
        };

        var monitoredCount = 0;
        var seedSnapshots = new Dictionary<string, OpcUaTagSnapshot>(StringComparer.OrdinalIgnoreCase);
        var configuredNodes = new Dictionary<string, MonitoredNodeConfiguration>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in profile.NodeMappings)
        {
            try
            {
                var configuredNodeId = NormalizeNodeId(node.NodeId);
                var monitoredItem = new MonitoredItem(subscription.DefaultItem)
                {
                    DisplayName = node.DisplayName,
                    StartNodeId = NodeId.Parse(configuredNodeId),
                    AttributeId = Attributes.Value,
                    SamplingInterval = Math.Max(100, node.SamplingIntervalMs),
                    QueueSize = 1,
                    DiscardOldest = true
                };

                monitoredItem.Notification += HandleMonitoredItemNotification;
                subscription.AddItem(monitoredItem);
                seedSnapshots[configuredNodeId] = new OpcUaTagSnapshot
                {
                    DisplayName = node.DisplayName,
                    NodeId = configuredNodeId,
                    Area = node.Area,
                    DataType = node.DataType,
                    IsWritable = node.IsWritable,
                    SamplingIntervalMs = Math.Max(100, node.SamplingIntervalMs),
                    UpdatedUtc = DateTimeOffset.UtcNow,
                    ValueText = null,
                    StatusCode = "Unknown",
                    AlarmText = "Pending first update"
                };
                configuredNodes[configuredNodeId] = new MonitoredNodeConfiguration(
                    configuredNodeId,
                    node.DisplayName,
                    node.Area,
                    node.DataType,
                    node.IsWritable,
                    Math.Max(100, node.SamplingIntervalMs));
                monitoredCount++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping invalid node mapping {NodeId}", node.NodeId);
            }
        }

        await _tagSnapshotLock.WaitAsync(cancellationToken);
        try
        {
            _tagSnapshots.Clear();
            _configuredNodes.Clear();
            foreach (var item in seedSnapshots)
            {
                _tagSnapshots[item.Key] = item.Value;
            }

            foreach (var item in configuredNodes)
            {
                _configuredNodes[item.Key] = item.Value;
            }
        }
        finally
        {
            _tagSnapshotLock.Release();
        }

        session.AddSubscription(subscription);
        await subscription.CreateAsync(cancellationToken);
        _subscription = subscription;
        return monitoredCount;
    }

    private void HandleMonitoredItemNotification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs eventArgs)
    {
        if (eventArgs.NotificationValue is not MonitoredItemNotification notification)
        {
            return;
        }

        var value = notification.Value;
        if (value is null)
        {
            return;
        }

        var configuredNodeId = ResolveConfiguredNodeId(monitoredItem.StartNodeId?.ToString(), monitoredItem.ResolvedNodeId?.ToString());
        if (string.IsNullOrWhiteSpace(configuredNodeId))
        {
            return;
        }

        var timestamp = value.SourceTimestamp == DateTime.MinValue
            ? (DateTimeOffset?)null
            : new DateTimeOffset(DateTime.SpecifyKind(value.SourceTimestamp, DateTimeKind.Utc));
        Interlocked.Increment(ref _receivedNotificationCount);
        RecordTagValue(configuredNodeId, monitoredItem.DisplayName, value, timestamp, persistTrendSample: true);
    }

    private async Task DrainTrendSamplesAsync(CancellationToken cancellationToken)
    {
        var reader = _trendSampleChannel.Reader;
        while (await reader.WaitToReadAsync(cancellationToken))
        {
            var batch = new List<TrendSampleWrite>();
            while (reader.TryRead(out var sample))
            {
                batch.Add(sample);
                if (batch.Count >= 64)
                {
                    break;
                }
            }

            if (batch.Count == 0)
            {
                continue;
            }

            try
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                foreach (var sample in batch)
                {
                    db.OpcUaTrendSamples.Add(new OpcUaTrendSample
                    {
                        NodeId = sample.NodeId,
                        ValueText = sample.ValueText,
                        StatusCode = sample.StatusCode,
                        SampledUtc = sample.SampledUtc
                    });
                }

                await db.SaveChangesAsync(cancellationToken);
                await DeleteExpiredTrendSamplesAsync(db, batch.Max(x => x.SampledUtc), cancellationToken);
                Interlocked.Add(ref _persistedTrendSampleCount, batch.Count);
                var lastSample = batch[^1];
                _lastSampleNodeId = lastSample.NodeId;
                _lastSampleValueText = lastSample.ValueText;
                _lastTrendPersistenceError = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _lastTrendPersistenceError = ex.Message;
                logger.LogWarning(ex, "Failed to persist {Count} OPC UA trend sample(s)", batch.Count);
            }
        }
    }

    private async Task RefreshMappedNodeValuesAsync(
        ISession session,
        OpcUaConnectionProfile profile,
        CancellationToken cancellationToken)
    {
        var samples = new List<TrendSampleWrite>(profile.NodeMappings.Count);

        foreach (var node in profile.NodeMappings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var configuredNodeId = NormalizeNodeId(node.NodeId);
                var value = await session.ReadValueAsync(NodeId.Parse(configuredNodeId), cancellationToken);
                if (value is null)
                {
                    continue;
                }

                var timestamp = value.SourceTimestamp == DateTime.MinValue
                    ? (DateTimeOffset?)null
                    : new DateTimeOffset(DateTime.SpecifyKind(value.SourceTimestamp, DateTimeKind.Utc));
                RecordTagValue(configuredNodeId, node.DisplayName, value, timestamp, persistTrendSample: false);
                samples.Add(new TrendSampleWrite(configuredNodeId, value.WrappedValue.Value?.ToString(), value.StatusCode.ToString(), DateTimeOffset.UtcNow));
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Initial/read refresh failed for OPC UA node {NodeId}", node.NodeId);
            }
        }

        await PersistTrendSamplesAsync(samples, cancellationToken);
    }

    private void RecordTagValue(
        string configuredNodeId,
        string? displayName,
        DataValue value,
        DateTimeOffset? sourceTimestampUtc,
        bool persistTrendSample)
    {
        var normalizedNodeId = NormalizeNodeId(configuredNodeId);
        var valueText = value.WrappedValue.Value?.ToString();
        var updatedUtc = DateTimeOffset.UtcNow;

        if (persistTrendSample)
        {
            _trendSampleChannel.Writer.TryWrite(new TrendSampleWrite(normalizedNodeId, valueText, value.StatusCode.ToString(), updatedUtc));
        }

        _tagSnapshotLock.Wait();
        try
        {
            if (!_tagSnapshots.TryGetValue(normalizedNodeId, out var existing))
            {
                _configuredNodes.TryGetValue(normalizedNodeId, out var configured);
                _tagSnapshots[normalizedNodeId] = new OpcUaTagSnapshot
                {
                    DisplayName = configured?.DisplayName ?? displayName ?? normalizedNodeId,
                    NodeId = normalizedNodeId,
                    Area = configured?.Area ?? "Live",
                    DataType = configured?.DataType ?? "Auto",
                    IsWritable = configured?.IsWritable ?? false,
                    SamplingIntervalMs = configured?.SamplingIntervalMs ?? 1000,
                    ValueText = valueText,
                    StatusCode = value.StatusCode.ToString(),
                    SourceTimestampUtc = sourceTimestampUtc,
                    UpdatedUtc = updatedUtc,
                    AlarmText = string.Empty
                };
                return;
            }

            _tagSnapshots[normalizedNodeId] = new OpcUaTagSnapshot
            {
                DisplayName = existing.DisplayName,
                NodeId = existing.NodeId,
                Area = existing.Area,
                DataType = existing.DataType,
                IsWritable = existing.IsWritable,
                SamplingIntervalMs = existing.SamplingIntervalMs,
                ValueText = valueText,
                StatusCode = value.StatusCode.ToString(),
                SourceTimestampUtc = sourceTimestampUtc,
                UpdatedUtc = updatedUtc,
                AlarmText = string.Empty
            };
        }
        finally
        {
            _tagSnapshotLock.Release();
        }
    }

    private string ResolveConfiguredNodeId(string? startNodeId, string? resolvedNodeId)
    {
        foreach (var candidate in new[] { startNodeId, resolvedNodeId })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var normalized = NormalizeNodeId(candidate);
            if (_configuredNodes.ContainsKey(normalized))
            {
                return normalized;
            }
        }

        return NormalizeNodeId(startNodeId ?? resolvedNodeId ?? string.Empty);
    }

    private async Task PersistTrendSamplesAsync(
        IReadOnlyList<TrendSampleWrite> samples,
        CancellationToken cancellationToken)
    {
        if (samples.Count == 0)
        {
            return;
        }

        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            foreach (var sample in samples)
            {
                db.OpcUaTrendSamples.Add(new OpcUaTrendSample
                {
                    NodeId = sample.NodeId,
                    ValueText = sample.ValueText,
                    StatusCode = sample.StatusCode,
                    SampledUtc = sample.SampledUtc
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            await DeleteExpiredTrendSamplesAsync(db, samples.Max(x => x.SampledUtc), cancellationToken);

            Interlocked.Add(ref _persistedTrendSampleCount, samples.Count);
            var lastSample = samples[^1];
            _lastSampleNodeId = lastSample.NodeId;
            _lastSampleValueText = lastSample.ValueText;
            _lastTrendPersistenceError = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _lastTrendPersistenceError = ex.Message;
            logger.LogWarning(ex, "Failed to persist {Count} OPC UA trend sample(s)", samples.Count);
        }
    }

    private static async Task DeleteExpiredTrendSamplesAsync(
        HmiDbContext db,
        DateTimeOffset newestSampleUtc,
        CancellationToken cancellationToken)
    {
        var retentionCutoff = newestSampleUtc.AddDays(-30);
        await db.OpcUaTrendSamples
            .Where(x => x.SampledUtc < retentionCutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static string NormalizeNodeId(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return string.Empty;
        }

        try
        {
            return NodeId.Parse(nodeId).ToString();
        }
        catch
        {
            return nodeId.Trim();
        }
    }

    private static OpcUaTagSnapshot ApplyHealthState(OpcUaTagSnapshot snapshot, DateTimeOffset nowUtc)
    {
        var staleThresholdMs = Math.Max(5000, snapshot.SamplingIntervalMs * 3);
        var isStale = nowUtc - snapshot.UpdatedUtc > TimeSpan.FromMilliseconds(staleThresholdMs);

        var statusCodeText = snapshot.StatusCode ?? "Unknown";
        var qualityIsGood = statusCodeText.StartsWith("Good", StringComparison.OrdinalIgnoreCase);
        var hasAlarm = isStale || !qualityIsGood;

        var alarmText = string.Empty;
        if (isStale)
        {
            alarmText = "Stale data";
        }
        else if (!qualityIsGood)
        {
            alarmText = $"Bad quality: {statusCodeText}";
        }

        return new OpcUaTagSnapshot
        {
            DisplayName = snapshot.DisplayName,
            NodeId = snapshot.NodeId,
            Area = snapshot.Area,
            DataType = snapshot.DataType,
            IsWritable = snapshot.IsWritable,
            SamplingIntervalMs = snapshot.SamplingIntervalMs,
            ValueText = snapshot.ValueText,
            StatusCode = snapshot.StatusCode,
            SourceTimestampUtc = snapshot.SourceTimestampUtc,
            UpdatedUtc = snapshot.UpdatedUtc,
            IsStale = isStale,
            HasAlarm = hasAlarm,
            AlarmText = alarmText
        };
    }

    private static int GetAlarmSeverity(OpcUaTagSnapshot snapshot)
        => snapshot.IsStale ? 700
            : (snapshot.StatusCode?.StartsWith("Good", StringComparison.OrdinalIgnoreCase) == true ? 0 : 500);

    private async Task SynchronizeAlarmStateAsync(CancellationToken cancellationToken)
    {
        List<OpcUaTagSnapshot> snapshots;

        await _tagSnapshotLock.WaitAsync(cancellationToken);
        try
        {
            var nowUtc = DateTimeOffset.UtcNow;
            snapshots = _tagSnapshots.Values
                .Select(x => ApplyHealthState(x, nowUtc))
                .ToList();
        }
        finally
        {
            _tagSnapshotLock.Release();
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var existing = await db.OpcUaAlarmStates.ToListAsync(cancellationToken);
        var byNodeId = existing.ToDictionary(x => x.NodeId, StringComparer.OrdinalIgnoreCase);
        var seenNodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in snapshots)
        {
            seenNodeIds.Add(snapshot.NodeId);
            byNodeId.TryGetValue(snapshot.NodeId, out var alarm);

            if (snapshot.HasAlarm)
            {
                if (alarm is null)
                {
                    db.OpcUaAlarmStates.Add(new OpcUaAlarmState
                    {
                        NodeId = snapshot.NodeId,
                        DisplayName = snapshot.DisplayName,
                        Area = snapshot.Area,
                        DataType = snapshot.DataType,
                        StatusCode = snapshot.StatusCode ?? string.Empty,
                        LastValueText = snapshot.ValueText,
                        AlarmText = snapshot.AlarmText,
                        Severity = GetAlarmSeverity(snapshot),
                        IsActive = true,
                        IsAcknowledged = false,
                        FirstRaisedUtc = now,
                        LastRaisedUtc = now,
                        LastUpdatedUtc = now
                    });
                    db.OpcUaAlarmTransitions.Add(new OpcUaAlarmTransition { NodeId = snapshot.NodeId, Transition = "Raised", Severity = GetAlarmSeverity(snapshot), Detail = snapshot.AlarmText, OccurredUtc = now });
                    continue;
                }

                var wasInactive = !alarm.IsActive;
                alarm.IsActive = true;
                alarm.DisplayName = snapshot.DisplayName;
                alarm.Area = snapshot.Area;
                alarm.DataType = snapshot.DataType;
                alarm.StatusCode = snapshot.StatusCode ?? string.Empty;
                alarm.LastValueText = snapshot.ValueText;
                alarm.AlarmText = snapshot.AlarmText;
                alarm.Severity = GetAlarmSeverity(snapshot);
                alarm.LastRaisedUtc = now;
                alarm.LastUpdatedUtc = now;
                alarm.ClearedUtc = null;

                if (wasInactive)
                {
                    db.OpcUaAlarmTransitions.Add(new OpcUaAlarmTransition { NodeId = snapshot.NodeId, Transition = "ReRaised", Severity = alarm.Severity, Detail = alarm.AlarmText, OccurredUtc = now });
                    alarm.IsAcknowledged = false;
                    alarm.AcknowledgedUtc = null;
                    alarm.AcknowledgedBy = null;
                    alarm.FirstRaisedUtc = now;
                }

                continue;
            }

            if (alarm is null)
            {
                continue;
            }

            if (alarm.IsActive)
            {
                alarm.IsActive = false;
                alarm.ClearedUtc = now;
                alarm.AlarmText = "Cleared";
                alarm.LastUpdatedUtc = now;
                db.OpcUaAlarmTransitions.Add(new OpcUaAlarmTransition { NodeId = alarm.NodeId, Transition = "Cleared", Severity = alarm.Severity, Detail = alarm.AlarmText, OccurredUtc = now });
            }
        }

        foreach (var alarm in existing)
        {
            if (seenNodeIds.Contains(alarm.NodeId))
            {
                continue;
            }

            if (!alarm.IsActive)
            {
                continue;
            }

            alarm.IsActive = false;
            alarm.ClearedUtc = now;
            alarm.AlarmText = "Tag no longer monitored";
            alarm.LastUpdatedUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task DisconnectSessionAsync(CancellationToken cancellationToken)
    {
        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            await DisconnectSessionInternalAsync(cancellationToken);
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private async Task DisconnectSessionInternalAsync(CancellationToken cancellationToken = default)
    {
        if (_subscription is not null)
        {
            try
            {
                await _subscription.DeleteAsync(true, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed while deleting OPC UA subscription during disconnect");
            }

            _subscription.Dispose();
            _subscription = null;
        }

        if (_session is not null)
        {
            try
            {
                await _session.CloseAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed while closing OPC UA session");
            }

            _session.Dispose();
            _session = null;
            _activeProfileId = null;
            _activeSubscriptionSignature = null;
            _activeMonitoredCount = 0;
        }

        await _tagSnapshotLock.WaitAsync(cancellationToken);
        try
        {
            _tagSnapshots.Clear();
        }
        finally
        {
            _tagSnapshotLock.Release();
        }
    }

    private sealed record TrendSampleWrite(string NodeId, string? ValueText, string? StatusCode, DateTimeOffset SampledUtc);
    private sealed record MonitoredNodeConfiguration(string NodeId, string DisplayName, string Area, string DataType, bool IsWritable, int SamplingIntervalMs);
}
