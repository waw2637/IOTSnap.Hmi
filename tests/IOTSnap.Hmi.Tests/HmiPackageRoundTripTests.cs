using IOTSnap.Hmi.Features.Designer;
using Xunit;

namespace IOTSnap.Hmi.Tests;

public class HmiPackageRoundTripTests
{
    [Fact]
    public void Validate_RejectsUnsupportedVersion()
    {
        var package = HmiProjectPackage.Create(new HmiProjectPackageScreen("Main", "main", 800, 600, false, [new HmiProjectPackageWidget("value", "Numeric", "Value", 0, 0, 100, 100, 0, "{}", [])]));
        var json = HmiProjectPackageSerializer.Serialize(package).Replace("\"version\": \"1.1\"", "\"version\": \"99.0\"");

        Assert.Null(HmiProjectPackageSerializer.Deserialize(json));
    }

    [Fact]
    public void Validate_RejectsDuplicateWidgetKeys()
    {
        var widgets = new[]
        {
            new HmiProjectPackageWidget("value", "Numeric", "Value", 0, 0, 100, 100, 0, "{}", []),
            new HmiProjectPackageWidget("value", "TrendChart", "Trend", 100, 0, 100, 100, 1, "{}", [])
        };
        var package = HmiProjectPackage.Create(new HmiProjectPackageScreen("Main", "main", 800, 600, false, widgets));

        Assert.False(HmiProjectPackageSerializer.Validate(package, out var error));
        Assert.Contains("unique", error, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void RoundTrip_PreservesScreenIdentityAndBindings()
    {
        var screen = new HmiProjectPackageScreen(
            "Main",
            "main",
            1366,
            768,
            true,
            new[]
            {
                new HmiProjectPackageWidget(
                    "widget-1",
                    "Numeric",
                    "Tank Level",
                    40,
                    40,
                    320,
                    160,
                    1,
                    "{\"unit\":\"%\"}",
                    new[]
                    {
                        new HmiProjectPackageBinding("Text", "OpcTag", "ns=2;s=Plant.Tank.Level", false, "Viewer")
                    })
            });

        var package = HmiProjectPackage.Create(screen);
        package.Author = "Copilot";
        package.Description = "Demo package";
        package.Metadata = new HmiProjectPackageMetadata
        {
            Background = "blue",
            AccentColor = "#1f4fd7",
            Notes = "Portable HMI export",
            Tags = new[] { "demo", "runtime" }
        };

        var json = HmiProjectPackageSerializer.Serialize(package);
        var restored = HmiProjectPackageSerializer.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Equal("Main", restored!.Screen.Name);
        Assert.Equal("main", restored.Screen.Slug);
        Assert.Equal(1366, restored.Screen.Width);
        Assert.Equal(768, restored.Screen.Height);
        Assert.True(restored.Screen.IsPublished);
        Assert.Single(restored.Screen.Widgets);
        var widget = restored.Screen.Widgets[0];
        Assert.Equal("widget-1", widget.Key);
        Assert.Equal("Numeric", widget.WidgetType);
        Assert.Single(widget.Bindings);
        Assert.Equal("Text", widget.Bindings[0].BindingRole);
        Assert.Equal("ns=2;s=Plant.Tank.Level", widget.Bindings[0].SourceKey);
        Assert.Equal("Copilot", restored.Author);
        Assert.Equal("Demo package", restored.Description);
        Assert.Equal("blue", restored.Metadata.Background);
        Assert.Equal("#1f4fd7", restored.Metadata.AccentColor);
        Assert.Contains("demo", restored.Metadata.Tags);
    }
}
