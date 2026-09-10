using IOTSnap.Hmi.Features.Designer;
using Xunit;

namespace IOTSnap.Hmi.Tests;

public class HmiWidgetPropertiesTests
{
    [Fact]
    public void ParseAndSerialize_PreservesScalingAndRangeProperties()
    {
        var properties = HmiWidgetProperties.Parse("{\"property\":\"Text\",\"unit\":\"%\",\"displayScale\":0.1,\"displayOffset\":2.5,\"decimalPlaces\":1,\"valueMin\":0,\"valueMax\":100,\"inputMin\":-10,\"inputMax\":10,\"inputStep\":0.5,\"inputMode\":\"Number\",\"targetScreenSlug\":\"line-overview\"}");

        Assert.Equal("Text", properties.Property);
        Assert.Equal("%", properties.Unit);
        Assert.Equal(0.1m, properties.DisplayScale);
        Assert.Equal(2.5m, properties.DisplayOffset);
        Assert.Equal(1, properties.DecimalPlaces);
        Assert.Equal(0m, properties.ValueMin);
        Assert.Equal(100m, properties.ValueMax);
        Assert.Equal(-10m, properties.InputMin);
        Assert.Equal(10m, properties.InputMax);
        Assert.Equal(0.5m, properties.InputStep);
        Assert.Equal("Number", properties.InputMode);
        Assert.Equal("line-overview", properties.TargetScreenSlug);

        var json = properties.ToJson();
        Assert.Contains("displayScale", json);
        Assert.Contains("inputMin", json);
        Assert.Contains("inputMode", json);
        Assert.Contains("targetScreenSlug", json);
    }

    [Fact]
    public void GetKeypadButtons_IncludesDecimalAndSignControls()
    {
        var properties = new HmiWidgetProperties { InputMode = "Number" };

        var buttons = properties.GetKeypadButtons();

        Assert.Contains("7", buttons);
        Assert.Contains("0", buttons);
        Assert.Contains(".", buttons);
        Assert.Contains("±", buttons);
        Assert.Contains("⌫", buttons);
    }
}
