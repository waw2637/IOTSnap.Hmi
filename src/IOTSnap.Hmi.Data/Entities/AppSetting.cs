namespace IOTSnap.Hmi.Data.Entities;

public sealed class AppSetting
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
