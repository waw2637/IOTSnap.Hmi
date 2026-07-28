using System.IO.Compression;
using System.Text;
using System.Text.Json;
using IOTSnap.Hmi.Data.Entities;

namespace IOTSnap.Hmi.Features.Designer;

public sealed class HmiProjectPackage
{
    public const string FileTypeVersion = "1.1";

    public string Format { get; init; } = "iot-snap-hmi-package";
    public string Version { get; init; } = FileTypeVersion;
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? Author { get; set; }
    public string? Description { get; set; }
    public HmiProjectPackageMetadata Metadata { get; set; } = new();
    public HmiProjectPackageScreen Screen { get; init; } = new();

    public static HmiProjectPackage Create(HmiProjectPackageScreen screen) => new()
    {
        Screen = screen
    };
}

public sealed class HmiProjectPackageScreen
{
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public int Width { get; init; } = 1366;
    public int Height { get; init; } = 768;
    public bool IsPublished { get; init; }
    public string? Theme { get; init; }
    public IReadOnlyList<HmiProjectPackageWidget> Widgets { get; init; } = Array.Empty<HmiProjectPackageWidget>();

    public HmiProjectPackageScreen()
    {
    }

    public HmiProjectPackageScreen(string name, string slug, int width, int height, bool isPublished, IEnumerable<HmiProjectPackageWidget> widgets)
    {
        Name = name;
        Slug = slug;
        Width = width;
        Height = height;
        IsPublished = isPublished;
        Widgets = widgets.ToList();
    }
}

public sealed class HmiProjectPackageWidget
{
    public string Key { get; init; } = string.Empty;
    public string WidgetType { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Category { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int ZIndex { get; init; }
    public string PropertiesJson { get; init; } = "{}";
    public IReadOnlyList<HmiProjectPackageBinding> Bindings { get; init; } = Array.Empty<HmiProjectPackageBinding>();

    public HmiProjectPackageWidget()
    {
    }

    public HmiProjectPackageWidget(string key, string widgetType, string title, int x, int y, int width, int height, int zIndex, string propertiesJson, IEnumerable<HmiProjectPackageBinding> bindings)
    {
        Key = key;
        WidgetType = widgetType;
        Title = title;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        ZIndex = zIndex;
        PropertiesJson = propertiesJson;
        Bindings = bindings.ToList();
    }
}

public sealed class HmiProjectPackageBinding
{
    public string BindingRole { get; init; } = "Text";
    public string SourceType { get; init; } = "OpcTag";
    public string? DisplayName { get; init; }
    public string SourceKey { get; init; } = string.Empty;
    public bool WriteRequiresConfirm { get; init; }
    public string MinRole { get; init; } = HmiRoles.Viewer;

    public HmiProjectPackageBinding()
    {
    }

    public HmiProjectPackageBinding(string bindingRole, string sourceType, string sourceKey, bool writeRequiresConfirm, string minRole)
    {
        BindingRole = bindingRole;
        SourceType = sourceType;
        WriteRequiresConfirm = writeRequiresConfirm;
        SourceKey = sourceKey;
        MinRole = minRole;
    }
}

public sealed class HmiProjectPackageMetadata
{
    public string? Background { get; init; }
    public string? AccentColor { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}

public static class HmiProjectPackageSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string Serialize(HmiProjectPackage package)
    {
        return JsonSerializer.Serialize(package, Options);
    }

    public static HmiProjectPackage? Deserialize(string json)
    {
        return JsonSerializer.Deserialize<HmiProjectPackage>(json, Options);
    }

    public static byte[] CreateArchive(HmiProjectPackage package, IReadOnlyDictionary<string, byte[]>? assets = null)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using (var manifestStream = manifestEntry.Open())
            using (var writer = new StreamWriter(manifestStream, new UTF8Encoding(false)))
            {
                writer.Write(Serialize(package));
            }

            if (assets is not null)
            {
                foreach (var asset in assets)
                {
                    var entry = archive.CreateEntry($"assets/{asset.Key}", CompressionLevel.Optimal);
                    using (var entryStream = entry.Open())
                    {
                        entryStream.Write(asset.Value, 0, asset.Value.Length);
                    }
                }
            }
        }

        return stream.ToArray();
    }

    public static HmiProjectPackage? ReadArchive(byte[] archiveBytes)
    {
        using var stream = new MemoryStream(archiveBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
        var manifestEntry = archive.GetEntry("manifest.json");
        if (manifestEntry is null)
        {
            return null;
        }

        using var manifestStream = manifestEntry.Open();
        using var reader = new StreamReader(manifestStream, Encoding.UTF8, false, 1024, true);
        var manifestJson = reader.ReadToEnd();
        return Deserialize(manifestJson);
    }

    public static IReadOnlyDictionary<string, byte[]> ReadAssets(byte[] archiveBytes)
    {
        using var stream = new MemoryStream(archiveBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
        var assets = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries.Where(x => x.FullName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(x.Name)))
        {
            using var entryStream = entry.Open();
            using var memory = new MemoryStream();
            entryStream.CopyTo(memory);
            assets[entry.Name] = memory.ToArray();
        }

        return assets;
    }
}
