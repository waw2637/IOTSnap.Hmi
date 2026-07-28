namespace IOTSnap.Hmi.Data.Entities;

public sealed class HmiWidgetBinding
{
    public int Id { get; set; }
    public int HmiWidgetId { get; set; }
    public HmiWidget Widget { get; set; } = null!;
    public string BindingRole { get; set; } = "PrimaryValue";
    public string SourceType { get; set; } = "OpcTag";
    public string SourceKey { get; set; } = string.Empty;
    public bool WriteRequiresConfirm { get; set; }
    public string MinRole { get; set; } = HmiRoles.Operator;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
