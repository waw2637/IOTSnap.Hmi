namespace IOTSnap.Hmi.Data.Entities;

public sealed class HmiWidget
{
    public int Id { get; set; }
    public int HmiScreenId { get; set; }
    public HmiScreen Screen { get; set; } = null!;
    public string Key { get; set; } = string.Empty;
    public string WidgetType { get; set; } = "Numeric";
    public string Title { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 220;
    public int Height { get; set; } = 140;
    public int ZIndex { get; set; }
    public string PropertiesJson { get; set; } = "{}";
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<HmiWidgetBinding> Bindings { get; set; } = new List<HmiWidgetBinding>();
}
