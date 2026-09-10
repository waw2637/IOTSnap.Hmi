namespace IOTSnap.Hmi.Runtime.OpcUa;

public sealed record OpcUaDiscoveredTag(
    string NodeId,
    string BrowsePath,
    string DisplayName,
    string DataType,
    bool IsWritable);
