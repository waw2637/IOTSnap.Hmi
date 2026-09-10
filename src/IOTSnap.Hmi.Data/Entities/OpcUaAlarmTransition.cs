namespace IOTSnap.Hmi.Data.Entities;

public sealed class OpcUaAlarmTransition
{
    public long Id { get; set; }
    public string NodeId { get; set; } = string.Empty;
    public string Transition { get; set; } = string.Empty;
    public int Severity { get; set; }
    public string? ActorUsername { get; set; }
    public string Detail { get; set; } = string.Empty;
    public DateTimeOffset OccurredUtc { get; set; } = DateTimeOffset.UtcNow;
}
