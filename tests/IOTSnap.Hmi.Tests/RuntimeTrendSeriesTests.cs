using IOTSnap.Hmi.Features.Designer;
using Xunit;

namespace IOTSnap.Hmi.Tests;

public class RuntimeTrendSeriesTests
{
    [Fact]
    public void BuildSvgPoints_UsesNormalizedValuesForPolyline()
    {
        var points = RuntimeTrendSeries.BuildSvgPoints(new[] { 10m, 20m, 30m }, 100, 50, 4);

        Assert.Equal("4,46 50,25.0 96,4", points);
    }

    [Fact]
    public void BuildSvgMarkup_IncludesAxesAndPolyline()
    {
        var markup = RuntimeTrendSeries.BuildSvgMarkup(new[] { 10m, 20m, 30m }, 100, 50, 4);

        Assert.Contains("<polyline", markup);
        Assert.Contains("<line", markup);
        Assert.Contains("stroke=\"#4cc9f0\"", markup);
    }

    [Fact]
    public void BuildSvgMarkup_IncludesAxisLabels()
    {
        var markup = RuntimeTrendSeries.BuildSvgMarkup(new[] { 10m, 20m, 30m }, 100, 50, 4);

        Assert.Contains("<text", markup);
        Assert.Contains("10", markup);
    }

    [Fact]
    public void BuildSvgMarkup_IncludesSummaryStats()
    {
        var markup = RuntimeTrendSeries.BuildSvgMarkup(new[] { 10m, 20m, 30m }, 100, 50, 4);

        Assert.Contains("Min", markup);
        Assert.Contains("Max", markup);
        Assert.Contains("Last", markup);
    }

    [Fact]
    public void BuildSvgMarkup_IncludesDeltaIndicator()
    {
        var markup = RuntimeTrendSeries.BuildSvgMarkup(new[] { 10m, 20m, 30m }, 100, 50, 4);

        Assert.Contains("Delta", markup);
    }
}
