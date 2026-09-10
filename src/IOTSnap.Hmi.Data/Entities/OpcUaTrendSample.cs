namespace IOTSnap.Hmi.Data.Entities;

public sealed class OpcUaTrendSample
{
    public long Id { get; set; }
    public string NodeId { get; set; } = string.Empty;
    public string? ValueText { get; set; }
    public string? StatusCode { get; set; }
    public DateTimeOffset SampledUtc { get; set; } = DateTimeOffset.UtcNow;
}
