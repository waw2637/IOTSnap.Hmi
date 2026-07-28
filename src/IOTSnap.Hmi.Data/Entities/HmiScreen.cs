namespace IOTSnap.Hmi.Data.Entities;

public sealed class HmiScreen
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public bool IsPublished { get; set; }
    public DateTimeOffset? PublishedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<HmiWidget> Widgets { get; set; } = new List<HmiWidget>();
}
