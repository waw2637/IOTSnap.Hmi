using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace IOTSnap.Hmi.Runtime.OpcUa;

public sealed class OpcUaDiscoveryService(IHostEnvironment hostEnvironment) : IOpcUaDiscovery
{
    private static readonly ITelemetryContext Telemetry = DefaultTelemetry.Create(_ => { });

    public async Task<IReadOnlyList<OpcUaDiscoveredEndpoint>> DiscoverEndpointsAsync(string discoveryUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(discoveryUrl, UriKind.Absolute, out var discoveryUri)
            || !string.Equals(discoveryUri.Scheme, "opc.tcp", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Discovery URL must be an absolute opc.tcp URL.");
        }

        var config = await BuildConfigurationAsync(cancellationToken);
        using var discoveryClient = await DiscoveryClient.CreateAsync(
            config,
            discoveryUri,
            EndpointConfiguration.Create(config),
            DiagnosticsMasks.None,
            cancellationToken);

        var servers = await discoveryClient.FindServersAsync(null, cancellationToken);
        var serverLookup = servers.ToDictionary(x => x.ApplicationUri, StringComparer.Ordinal);
        var endpoints = await discoveryClient.GetEndpointsAsync(null, cancellationToken);

        return endpoints
            .Where(endpoint => string.Equals(endpoint.TransportProfileUri, Profiles.UaTcpTransport, StringComparison.Ordinal))
            .OrderBy(endpoint => endpoint.EndpointUrl, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(endpoint => endpoint.SecurityLevel)
            .Select(endpoint =>
            {
                serverLookup.TryGetValue(endpoint.Server.ApplicationUri ?? string.Empty, out var server);
                var anonymous = endpoint.UserIdentityTokens?.Any(x => x.TokenType == UserTokenType.Anonymous) == true;
                var usernamePassword = endpoint.UserIdentityTokens?.Any(x => x.TokenType == UserTokenType.UserName) == true;

                return new OpcUaDiscoveredEndpoint(
                    discoveryUrl,
                    endpoint.EndpointUrl,
                    server?.ApplicationName?.Text ?? endpoint.Server.ApplicationName?.Text ?? "Unknown",
                    endpoint.Server.ApplicationUri ?? string.Empty,
                    endpoint.Server.ProductUri ?? string.Empty,
                    GetSecurityPolicyName(endpoint.SecurityPolicyUri),
                    endpoint.SecurityPolicyUri ?? SecurityPolicies.None,
                    endpoint.SecurityMode.ToString(),
                    endpoint.SecurityMode != MessageSecurityMode.None,
                    endpoint.SecurityLevel,
                    anonymous,
                    usernamePassword);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<OpcUaDiscoveredTag>> BrowseTagsAsync(OpcUaBrowseRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.EndpointUrl))
        {
            throw new InvalidOperationException("Endpoint URL is required.");
        }

        var config = await BuildConfigurationAsync(cancellationToken);
        var session = await CreateSessionAsync(config, request, cancellationToken);
        using var _ = session;

        var browser = new Browser(session)
        {
            BrowseDirection = BrowseDirection.Forward,
            NodeClassMask = (int)(NodeClass.Object | NodeClass.Variable),
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
            IncludeSubtypes = true
        };

        var results = new List<OpcUaDiscoveredTag>();
        var queue = new Queue<(NodeId NodeId, string Path, int Depth)>();
        var browseRoot = await ResolveBrowseRootAsync(browser, session.NamespaceUris, request, cancellationToken);
        queue.Enqueue((browseRoot.NodeId, browseRoot.Path, 0));
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (queue.Count > 0 && results.Count < request.MaxTags)
        {
            var (nodeId, path, depth) = queue.Dequeue();
            if (!visited.Add(nodeId.ToString()))
            {
                continue;
            }

            var references = await browser.BrowseAsync(nodeId, cancellationToken);
            foreach (var reference in references)
            {
                var childNodeId = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                if (childNodeId is null)
                {
                    continue;
                }

                var childName = reference.DisplayName?.Text ?? reference.BrowseName?.Name ?? childNodeId.ToString();
                var childPath = $"{path}/{childName}";

                if (reference.NodeClass == NodeClass.Variable)
                {
                    var variableNode = await session.ReadNodeAsync(childNodeId, cancellationToken) as VariableNode;
                    if (variableNode is not null)
                    {
                        results.Add(new OpcUaDiscoveredTag(
                            childNodeId.ToString(),
                            childPath,
                            childName,
                            ResolveDataTypeName(variableNode.DataType, session.NamespaceUris),
                            (variableNode.AccessLevel & AccessLevels.CurrentWrite) == AccessLevels.CurrentWrite));

                        if (results.Count >= request.MaxTags)
                        {
                            break;
                        }
                    }
                }

                if (depth < request.MaxDepth && reference.NodeClass == NodeClass.Object)
                {
                    queue.Enqueue((childNodeId, childPath, depth + 1));
                }
            }
        }

        return results
            .OrderBy(x => x.BrowsePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<(NodeId NodeId, string Path)> ResolveBrowseRootAsync(
        Browser browser,
        NamespaceTable namespaceUris,
        OpcUaBrowseRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RootBrowsePath))
        {
            return (ObjectIds.ObjectsFolder, "Objects");
        }

        var segments = request.RootBrowsePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return (ObjectIds.ObjectsFolder, "Objects");
        }

        var currentNodeId = ObjectIds.ObjectsFolder;
        var currentPath = "Objects";
        var segmentIndex = string.Equals(segments[0], "Objects", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        for (var index = segmentIndex; index < segments.Length; index++)
        {
            var targetSegment = segments[index];
            var references = await browser.BrowseAsync(currentNodeId, cancellationToken);
            var match = references.FirstOrDefault(reference =>
                reference.NodeClass == NodeClass.Object
                && string.Equals(reference.DisplayName?.Text ?? reference.BrowseName?.Name, targetSegment, StringComparison.OrdinalIgnoreCase));

            if (match?.NodeId is null)
            {
                throw new InvalidOperationException($"Browse root path '{request.RootBrowsePath}' could not be resolved at segment '{targetSegment}'.");
            }

            currentNodeId = ExpandedNodeId.ToNodeId(match.NodeId, namespaceUris)
                ?? throw new InvalidOperationException($"Browse root path '{request.RootBrowsePath}' resolved an invalid node identifier.");
            currentPath = $"{currentPath}/{targetSegment}";
        }

        return (currentNodeId, currentPath);
    }

    private static async Task<ISession> CreateSessionAsync(
        ApplicationConfiguration config,
        OpcUaBrowseRequest request,
        CancellationToken cancellationToken)
    {
        var discoveredEndpoints = await DiscoverMatchingEndpointsAsync(config, request.EndpointUrl, cancellationToken);
        var endpoint = discoveredEndpoints
            .Where(x => string.Equals(x.TransportProfileUri, Profiles.UaTcpTransport, StringComparison.Ordinal))
            .Where(x => string.Equals(x.SecurityPolicyUri ?? SecurityPolicies.None, ResolveSecurityPolicyUri(request.UseSecurity, request.SecurityPolicy), StringComparison.Ordinal))
            .Where(x => x.SecurityMode == ResolveSecurityMode(request.UseSecurity, request.SecurityMode))
            .OrderByDescending(x => x.SecurityLevel)
            .FirstOrDefault();

        if (endpoint is null)
        {
            throw new InvalidOperationException("No matching endpoint was found for the requested security settings.");
        }

        var identity = BuildUserIdentity(request);
        return await new DefaultSessionFactory(Telemetry).CreateAsync(
            config,
            new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(config)),
            false,
            false,
            "IOTSnap.Hmi.Discovery",
            60000,
            identity,
            null,
            cancellationToken);
    }

    private static async Task<EndpointDescriptionCollection> DiscoverMatchingEndpointsAsync(
        ApplicationConfiguration config,
        string endpointUrl,
        CancellationToken cancellationToken)
    {
        using var discoveryClient = await DiscoveryClient.CreateAsync(
            config,
            new Uri(endpointUrl),
            EndpointConfiguration.Create(config),
            DiagnosticsMasks.None,
            cancellationToken);

        return await discoveryClient.GetEndpointsAsync(null, cancellationToken);
    }

    private static IUserIdentity BuildUserIdentity(OpcUaBrowseRequest request)
    {
        if (string.Equals(request.AuthenticationMode, "UsernamePassword", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(request.Username)
            && !string.IsNullOrWhiteSpace(request.Password))
        {
            return new UserIdentity(new UserNameIdentityToken
            {
                UserName = request.Username,
                DecryptedPassword = System.Text.Encoding.UTF8.GetBytes(request.Password)
            });
        }

        return new UserIdentity(new AnonymousIdentityToken());
    }

    private async Task<ApplicationConfiguration> BuildConfigurationAsync(CancellationToken cancellationToken)
    {
        OpcUaCertificateStorePaths.EnsureDirectories(hostEnvironment);

        var config = new ApplicationConfiguration
        {
            ApplicationName = "IOTSnap.Hmi.Discovery",
            ApplicationUri = $"urn:{Utils.GetHostName()}:IOTSnap.Hmi.Discovery",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = "Directory",
                    StorePath = OpcUaCertificateStorePaths.GetOwnDirectory(hostEnvironment),
                    SubjectName = "CN=IOTSnap.Hmi.Discovery"
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = "Directory",
                    StorePath = OpcUaCertificateStorePaths.GetTrustedIssuerDirectory(hostEnvironment)
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = "Directory",
                    StorePath = OpcUaCertificateStorePaths.GetTrustedPeerDirectory(hostEnvironment)
                },
                RejectedCertificateStore = new CertificateTrustList
                {
                    StoreType = "Directory",
                    StorePath = OpcUaCertificateStorePaths.GetRejectedDirectory(hostEnvironment)
                },
                AutoAcceptUntrustedCertificates = true,
                AddAppCertToTrustedStore = true
            },
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
        config.CertificateValidator.CertificateValidation += (_, e) => e.Accept = true;
        return config;
    }

    private static string ResolveDataTypeName(NodeId? dataType, NamespaceTable namespaceUris)
    {
        if (dataType is null)
        {
            return "Unknown";
        }

        var builtInType = TypeInfo.GetBuiltInType(dataType);
        if (builtInType != BuiltInType.Null)
        {
            return builtInType.ToString();
        }

        return ExpandedNodeId.ToNodeId(dataType, namespaceUris)?.ToString() ?? dataType.ToString();
    }

    private static string GetSecurityPolicyName(string? securityPolicyUri)
        => securityPolicyUri switch
        {
            null or "" => "None",
            var uri when string.Equals(uri, SecurityPolicies.None, StringComparison.Ordinal) => "None",
            var uri when string.Equals(uri, SecurityPolicies.Basic128Rsa15, StringComparison.Ordinal) => nameof(SecurityPolicies.Basic128Rsa15),
            var uri when string.Equals(uri, SecurityPolicies.Basic256, StringComparison.Ordinal) => nameof(SecurityPolicies.Basic256),
            var uri when string.Equals(uri, SecurityPolicies.Basic256Sha256, StringComparison.Ordinal) => nameof(SecurityPolicies.Basic256Sha256),
            var uri when string.Equals(uri, SecurityPolicies.Aes128_Sha256_RsaOaep, StringComparison.Ordinal) => nameof(SecurityPolicies.Aes128_Sha256_RsaOaep),
            var uri when string.Equals(uri, SecurityPolicies.Aes256_Sha256_RsaPss, StringComparison.Ordinal) => nameof(SecurityPolicies.Aes256_Sha256_RsaPss),
            var uri => uri
        };

    private static string ResolveSecurityPolicyUri(bool useSecurity, string? securityPolicy)
        => !useSecurity
            ? SecurityPolicies.None
            : (securityPolicy ?? string.Empty).Trim() switch
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
                var policy => throw new InvalidOperationException($"Unsupported OPC UA security policy '{policy}'.")
            };

    private static MessageSecurityMode ResolveSecurityMode(bool useSecurity, string? securityMode)
    {
        if (!useSecurity)
        {
            return MessageSecurityMode.None;
        }

        return Enum.TryParse<MessageSecurityMode>(securityMode, true, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Unsupported OPC UA security mode '{securityMode}'.");
    }
}
