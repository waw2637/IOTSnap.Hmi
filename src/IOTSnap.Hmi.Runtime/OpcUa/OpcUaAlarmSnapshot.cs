namespace IOTSnap.Hmi.Runtime.OpcUa;

public sealed class OpcUaAlarmSnapshot
{
    public string NodeId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Area { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public string StatusCode { get; init; } = string.Empty;
    public string? LastValueText { get; init; }
    public string AlarmText { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public bool IsAcknowledged { get; init; }
    public DateTimeOffset FirstRaisedUtc { get; init; }
    public DateTimeOffset LastRaisedUtc { get; init; }
    public DateTimeOffset? AcknowledgedUtc { get; init; }
    public string? AcknowledgedBy { get; init; }
    public DateTimeOffset? ClearedUtc { get; init; }
    public DateTimeOffset LastUpdatedUtc { get; init; }
}
