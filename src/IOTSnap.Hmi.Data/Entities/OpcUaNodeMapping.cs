namespace IOTSnap.Hmi.Data.Entities;

public sealed class OpcUaNodeMapping
{
    public int Id { get; set; }
    public int OpcUaConnectionProfileId { get; set; }
    public OpcUaConnectionProfile ConnectionProfile { get; set; } = null!;
    public string DisplayName { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    public string DataType { get; set; } = "Auto";
    public int SamplingIntervalMs { get; set; } = 1000;
    public bool IsWritable { get; set; }
    public string Area { get; set; } = "Default";
}
