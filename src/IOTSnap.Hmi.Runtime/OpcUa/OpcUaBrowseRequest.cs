namespace IOTSnap.Hmi.Runtime.OpcUa;

public sealed record OpcUaBrowseRequest(
    string EndpointUrl,
    bool UseSecurity,
    string SecurityPolicy,
    string SecurityMode,
    string AuthenticationMode,
    string? Username,
    string? Password,
    int MaxDepth = 6,
    int MaxTags = 250,
    string? RootBrowsePath = null);
