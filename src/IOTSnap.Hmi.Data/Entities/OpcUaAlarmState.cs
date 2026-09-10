namespace IOTSnap.Hmi.Data.Entities;

public sealed class OpcUaAlarmState
{
    public int Id { get; set; }
    public string NodeId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string StatusCode { get; set; } = string.Empty;
    public string? LastValueText { get; set; }
    public string AlarmText { get; set; } = string.Empty;
    public int Severity { get; set; } = 500;
    public bool IsActive { get; set; }
    public bool IsAcknowledged { get; set; }
    public DateTimeOffset FirstRaisedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastRaisedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AcknowledgedUtc { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTimeOffset? ClearedUtc { get; set; }
    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
