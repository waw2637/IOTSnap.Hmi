using System.Globalization;
using System.Text.Json;

namespace IOTSnap.Hmi.Features.Designer;

public sealed class HmiWidgetProperties
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public string Property { get; set; } = "Text";
    public string? Unit { get; set; }
    public decimal DisplayScale { get; set; } = 1m;
    public decimal DisplayOffset { get; set; } = 0m;
    public int DecimalPlaces { get; set; } = 2;
    public decimal? ValueMin { get; set; }
    public decimal? ValueMax { get; set; }
    public decimal? InputMin { get; set; }
    public decimal? InputMax { get; set; }
    public decimal InputStep { get; set; } = 1m;
    public string InputMode { get; set; } = "Number";
    public string? InputLabel { get; set; }

    public static HmiWidgetProperties Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new HmiWidgetProperties();
        }

        try
        {
            return JsonSerializer.Deserialize<HmiWidgetProperties>(json, SerializerOptions) ?? new HmiWidgetProperties();
        }
        catch (JsonException)
        {
            return new HmiWidgetProperties();
        }
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, SerializerOptions);
    }

    public string FormatValue(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        if (!decimal.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return rawValue;
        }

        var scaledValue = (number * DisplayScale) + DisplayOffset;
        var formatted = scaledValue.ToString($"F{Math.Max(0, DecimalPlaces)}", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(Unit)
            ? formatted
            : $"{formatted} {Unit}";
    }

    public bool IsValueWithinRange(decimal? value)
    {
        if (value is null)
        {
            return true;
        }

        if (ValueMin is not null && value < ValueMin)
        {
            return false;
        }

        if (ValueMax is not null && value > ValueMax)
        {
            return false;
        }

        return true;
    }

    public string GetRangeSummary()
    {
        var parts = new List<string>();
        if (ValueMin is not null)
        {
            parts.Add($"min {ValueMin.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (ValueMax is not null)
        {
            parts.Add($"max {ValueMax.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        return parts.Count == 0 ? string.Empty : string.Join(" • ", parts);
    }
}
