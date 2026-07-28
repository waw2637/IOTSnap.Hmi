namespace IOTSnap.Hmi.Runtime.OpcUa;

public sealed class OpcUaAlarmCommandResult
{
    public bool Succeeded { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
