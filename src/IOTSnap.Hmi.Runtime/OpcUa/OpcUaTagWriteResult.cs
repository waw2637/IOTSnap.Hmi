namespace IOTSnap.Hmi.Runtime.OpcUa;

public sealed class OpcUaTagWriteResult
{
    public bool Succeeded { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
