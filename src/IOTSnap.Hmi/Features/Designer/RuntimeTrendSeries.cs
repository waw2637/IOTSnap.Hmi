namespace IOTSnap.Hmi.Features.Designer;

public static class RuntimeTrendSeries
{
    public static string BuildSvgPoints(IEnumerable<decimal> values, int width, int height, int padding)
    {
        var data = values.ToArray();
        if (data.Length == 0)
        {
            return string.Empty;
        }

        var min = data.Min();
        var max = data.Max();
        var range = max - min;
        var effectiveRange = range == 0 ? 1m : range;

        var points = new List<string>(data.Length);
        for (var index = 0; index < data.Length; index++)
        {
            var value = data[index];
            var normalized = (value - min) / effectiveRange;
            var x = padding + ((width - (padding * 2)) * index / Math.Max(1, data.Length - 1));
            var y = height - padding - ((height - (padding * 2)) * normalized);
            points.Add($"{x},{y}");
        }

        return string.Join(" ", points);
    }

    public static string BuildSvgMarkup(IEnumerable<decimal> values, int width, int height, int padding)
    {
        var data = values.ToArray();
        if (data.Length == 0)
        {
            return string.Empty;
        }

        var points = BuildSvgPoints(data, width, height, padding);
        if (string.IsNullOrWhiteSpace(points))
        {
            return string.Empty;
        }

        var min = data.Min();
        var max = data.Max();
        var current = data[^1];
        var previous = data.Length > 1 ? data[^2] : current;
        var delta = current - previous;
        var deltaLabel = delta >= 0 ? $"+{delta:0.##}" : delta.ToString("0.##");
        var deltaColor = delta >= 0 ? "#7bdcff" : "#ff8c42";
        var mid = (min + max) / 2m;
        var lowLabel = min.ToString("0.##");
        var midLabel = mid.ToString("0.##");
        var highLabel = max.ToString("0.##");
        var currentLabel = current.ToString("0.##");
        var summary = $"<g><rect x=\"{padding}\" y=\"{height - 10}\" width=\"{width - (padding * 2)}\" height=\"8\" rx=\"3\" fill=\"rgba(255,255,255,0.04)\" /><text x=\"{padding + 4}\" y=\"{height - 4}\" fill=\"#8aa0bc\" font-size=\"3.6\">Min {lowLabel}</text><text x=\"{padding + 28}\" y=\"{height - 4}\" fill=\"#8aa0bc\" font-size=\"3.6\">Max {highLabel}</text><text x=\"{padding + 56}\" y=\"{height - 4}\" fill=\"#7bdcff\" font-size=\"3.6\">Last {currentLabel}</text><text x=\"{padding + 78}\" y=\"{height - 4}\" fill=\"{deltaColor}\" font-size=\"3.6\">Delta {deltaLabel}</text></g>";

        return $"<svg viewBox=\"0 0 {width} {height}\" preserveAspectRatio=\"none\" class=\"runtime-trend-svg\"><line x1=\"{padding}\" y1=\"{height - padding}\" x2=\"{width - padding}\" y2=\"{height - padding}\" stroke=\"#38506b\" stroke-width=\"0.8\" /><line x1=\"{padding}\" y1=\"{padding}\" x2=\"{padding}\" y2=\"{height - padding}\" stroke=\"#38506b\" stroke-width=\"0.8\" /><text x=\"{padding}\" y=\"{padding + 4}\" fill=\"#8aa0bc\" font-size=\"4\">{highLabel}</text><text x=\"{padding}\" y=\"{height / 2}\" fill=\"#8aa0bc\" font-size=\"4\">{midLabel}</text><text x=\"{padding}\" y=\"{height - 2}\" fill=\"#8aa0bc\" font-size=\"4\">{lowLabel}</text><text x=\"{width - padding - 10}\" y=\"{padding + 4}\" fill=\"#7bdcff\" font-size=\"4\">Now {currentLabel}</text><polyline points=\"{points}\" stroke=\"#4cc9f0\" stroke-width=\"2.5\" fill=\"none\" stroke-linecap=\"round\" stroke-linejoin=\"round\" />{summary}</svg>";
    }
}
