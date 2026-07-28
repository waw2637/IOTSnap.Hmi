using IOTSnap.Hmi.Data;
using IOTSnap.Hmi.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using System.Text;

namespace IOTSnap.Hmi.Runtime.OpcUa;

public sealed class OpcUaRuntimeService(
    IDbContextFactory<HmiDbContext> dbContextFactory,
    ILogger<OpcUaRuntimeService> logger) : BackgroundService, IOpcUaRuntime
{
    private static readonly ITelemetryContext Telemetry = DefaultTelemetry.Create(_ => { });
    private readonly SemaphoreSlim _statusLock = new(1, 1);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private ISession? _session;
    private Subscription? _subscription;
    private int? _activeProfileId;
    private OpcUaRuntimeStatus _status = new();
    private readonly Dictionary<string, OpcUaTagSnapshot> _tagSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _tagSnapshotLock = new(1, 1);

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

        return await query
            .OrderByDescending(x => x.IsActive)
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
                IsActive = x.IsActive,
                IsAcknowledged = x.IsAcknowledged,
                FirstRaisedUtc = x.FirstRaisedUtc,
                LastRaisedUtc = x.LastRaisedUtc,
                AcknowledgedUtc = x.AcknowledgedUtc,
                AcknowledgedBy = x.AcknowledgedBy,
                ClearedUtc = x.ClearedUtc,
                LastUpdatedUtc = x.LastUpdatedUtc
            })
            .ToListAsync(cancellationToken);
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

        alarm.IsAcknowledged = true;
        alarm.AcknowledgedBy = string.IsNullOrWhiteSpace(acknowledgedBy) ? "unknown" : acknowledgedBy.Trim();
        alarm.AcknowledgedUtc = DateTimeOffset.UtcNow;
        alarm.LastUpdatedUtc = DateTimeOffset.UtcNow;
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

        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshStatusAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
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

            var profile = await db.OpcUaConnectionProfiles
                .Include(x => x.NodeMappings)
                .OrderByDescending(x => x.UpdatedUtc)
                .FirstOrDefaultAsync(cancellationToken);

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

            await SetStatusAsync(new OpcUaRuntimeStatus
            {
                IsConnected = sessionResult.IsConnected,
                ActiveProfileName = profile.Name,
                EndpointUrl = profile.EndpointUrl,
                ConfiguredNodeCount = profile.NodeMappings.Count,
                UpdatedUtc = DateTimeOffset.UtcNow,
                Detail = sessionResult.Detail
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
            if (_session is not null
                && _session.Connected
                && _activeProfileId == profile.Id)
            {
                var monitoredCount = await RebuildSubscriptionAsync(_session, profile, cancellationToken);
                return (true, $"Connected. Monitoring {monitoredCount} node(s).");
            }

            await DisconnectSessionInternalAsync(cancellationToken);

            var config = await BuildAppConfigurationAsync(cancellationToken);
            var endpointDescription = await CoreClientUtils.SelectEndpointAsync(
                config,
                profile.EndpointUrl,
                profile.UseSecurity,
                15000,
                Telemetry,
                cancellationToken);
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

    private static IUserIdentity BuildUserIdentity(OpcUaConnectionProfile profile)
    {
        if (string.Equals(profile.AuthenticationMode, "UsernamePassword", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(profile.Username)
            && !string.IsNullOrWhiteSpace(profile.Password))
        {
            return new UserIdentity(new UserNameIdentityToken
            {
                UserName = profile.Username,
                DecryptedPassword = Encoding.UTF8.GetBytes(profile.Password)
            });
        }

        return new UserIdentity(new AnonymousIdentityToken());
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

    private static async Task<ApplicationConfiguration> BuildAppConfigurationAsync(CancellationToken cancellationToken)
    {
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
                    StorePath = "OPC Foundation/CertificateStores/MachineDefault",
                    SubjectName = "CN=IOTSnap.Hmi"
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = "OPC Foundation/CertificateStores/UA Certificate Authorities"
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = "OPC Foundation/CertificateStores/UA Applications"
                },
                RejectedCertificateStore = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = "OPC Foundation/CertificateStores/RejectedCertificates"
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
        foreach (var node in profile.NodeMappings)
        {
            try
            {
                var monitoredItem = new MonitoredItem(subscription.DefaultItem)
                {
                    DisplayName = node.DisplayName,
                    StartNodeId = NodeId.Parse(node.NodeId),
                    AttributeId = Attributes.Value,
                    SamplingInterval = Math.Max(100, node.SamplingIntervalMs),
                    QueueSize = 1,
                    DiscardOldest = true
                };

                monitoredItem.Notification += HandleMonitoredItemNotification;
                subscription.AddItem(monitoredItem);
                seedSnapshots[node.NodeId] = new OpcUaTagSnapshot
                {
                    DisplayName = node.DisplayName,
                    NodeId = node.NodeId,
                    Area = node.Area,
                    DataType = node.DataType,
                    IsWritable = node.IsWritable,
                    SamplingIntervalMs = Math.Max(100, node.SamplingIntervalMs),
                    UpdatedUtc = DateTimeOffset.UtcNow,
                    ValueText = null,
                    StatusCode = "Unknown",
                    AlarmText = "Pending first update"
                };
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
            foreach (var item in seedSnapshots)
            {
                _tagSnapshots[item.Key] = item.Value;
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

        var nodeId = monitoredItem.ResolvedNodeId?.ToString() ?? monitoredItem.StartNodeId?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return;
        }

        var valueText = value.WrappedValue.Value?.ToString();
        var timestamp = value.SourceTimestamp == DateTime.MinValue
            ? (DateTimeOffset?)null
            : new DateTimeOffset(DateTime.SpecifyKind(value.SourceTimestamp, DateTimeKind.Utc));
        var updatedUtc = DateTimeOffset.UtcNow;

        _tagSnapshotLock.Wait();
        try
        {
            if (!_tagSnapshots.TryGetValue(nodeId, out var existing))
            {
                _tagSnapshots[nodeId] = new OpcUaTagSnapshot
                {
                    DisplayName = monitoredItem.DisplayName,
                    NodeId = nodeId,
                    Area = "Live",
                    DataType = "Auto",
                    IsWritable = false,
                    SamplingIntervalMs = 1000,
                    ValueText = valueText,
                    StatusCode = value.StatusCode.ToString(),
                    SourceTimestampUtc = timestamp,
                    UpdatedUtc = updatedUtc,
                    AlarmText = string.Empty
                };
                return;
            }

            _tagSnapshots[nodeId] = new OpcUaTagSnapshot
            {
                DisplayName = existing.DisplayName,
                NodeId = existing.NodeId,
                Area = existing.Area,
                DataType = existing.DataType,
                IsWritable = existing.IsWritable,
                SamplingIntervalMs = existing.SamplingIntervalMs,
                ValueText = valueText,
                StatusCode = value.StatusCode.ToString(),
                SourceTimestampUtc = timestamp,
                UpdatedUtc = updatedUtc,
                AlarmText = string.Empty
            };
        }
        finally
        {
            _tagSnapshotLock.Release();
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
                        IsActive = true,
                        IsAcknowledged = false,
                        FirstRaisedUtc = now,
                        LastRaisedUtc = now,
                        LastUpdatedUtc = now
                    });
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
                alarm.LastRaisedUtc = now;
                alarm.LastUpdatedUtc = now;
                alarm.ClearedUtc = null;

                if (wasInactive)
                {
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
}
