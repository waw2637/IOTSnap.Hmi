namespace IOTSnap.Hmi.Runtime.OpcUa;

public sealed class OpcUaRuntimeStatus
{
    public bool IsConnected { get; set; }
    public string ActiveProfileName { get; set; } = "Not configured";
    public string EndpointUrl { get; set; } = "n/a";
    public int ConfiguredNodeCount { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Detail { get; set; } = "Runtime not started.";
}
