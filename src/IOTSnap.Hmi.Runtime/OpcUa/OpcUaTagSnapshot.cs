namespace IOTSnap.Hmi.Runtime.OpcUa;

public sealed class OpcUaTagSnapshot
{
    public string DisplayName { get; init; } = string.Empty;
    public string NodeId { get; init; } = string.Empty;
    public string Area { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public bool IsWritable { get; init; }
    public int SamplingIntervalMs { get; init; }
    public string? ValueText { get; init; }
    public string? StatusCode { get; init; }
    public DateTimeOffset? SourceTimestampUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
    public bool IsStale { get; init; }
    public bool HasAlarm { get; init; }
    public string AlarmText { get; init; } = string.Empty;
}
