namespace IOTSnap.Hmi.Data.Entities;

public sealed class HmiScreenPublication
{
    public long Id { get; set; }
    public int HmiScreenId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string SnapshotJson { get; set; } = string.Empty;
    public string? PublishedBy { get; set; }
    public DateTimeOffset PublishedUtc { get; set; } = DateTimeOffset.UtcNow;
}
