namespace IOTSnap.Hmi.Data.Entities;

public sealed class OperatorAuditEntry
{
    public long Id { get; set; }
    public string ActorUsername { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
    public string? Detail { get; set; }
    public DateTimeOffset OccurredUtc { get; set; } = DateTimeOffset.UtcNow;
}
