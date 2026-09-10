namespace IOTSnap.Hmi.Runtime.OpcUa;

public interface IOpcUaDiscovery
{
    Task<IReadOnlyList<OpcUaDiscoveredEndpoint>> DiscoverEndpointsAsync(string discoveryUrl, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OpcUaDiscoveredTag>> BrowseTagsAsync(OpcUaBrowseRequest request, CancellationToken cancellationToken = default);
}
