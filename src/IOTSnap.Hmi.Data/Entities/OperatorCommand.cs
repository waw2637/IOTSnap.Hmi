namespace IOTSnap.Hmi.Data.Entities;

public sealed class OperatorCommand
{
    public int Id { get; set; }
    public string CommandId { get; set; } = Guid.NewGuid().ToString("N");
    public string IdempotencyKey { get; set; } = string.Empty;
    public string ActorUsername { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public string ScreenSlug { get; set; } = string.Empty;
    public string WidgetKey { get; set; } = string.Empty;
    public string BindingRole { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    public string RequestedValue { get; set; } = string.Empty;
    public bool RequiresConfirmation { get; set; }
    public string Status { get; set; } = "Requested";
    public string? OpcUaResponse { get; set; }
    public string? ObservedValue { get; set; }
    public DateTimeOffset RequestedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AcceptedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public DateTimeOffset? ObservedUtc { get; set; }
    public DateTimeOffset? ConfirmationExpiresUtc { get; set; }
}
