namespace IOTSnap.Hmi.Runtime.OpcUa;

public sealed record OpcUaDiscoveredEndpoint(
    string DiscoveryUrl,
    string EndpointUrl,
    string ApplicationName,
    string ApplicationUri,
    string ProductUri,
    string SecurityPolicy,
    string SecurityPolicyUri,
    string SecurityMode,
    bool UseSecurity,
    byte SecurityLevel,
    bool SupportsAnonymous,
    bool SupportsUsernamePassword);
