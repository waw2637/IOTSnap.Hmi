using System.Globalization;

namespace IOTSnap.Hmi.Features.Designer;

public static class RuntimeCommandConfirmation
{
    public static bool Matches(string requestedValue, string? observedValue)
    {
        if (string.IsNullOrWhiteSpace(observedValue))
        {
            return false;
        }

        if (bool.TryParse(requestedValue, out var requestedBool)
            && bool.TryParse(observedValue, out var observedBool))
        {
            return requestedBool == observedBool;
        }

        if (decimal.TryParse(requestedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var requestedNumber)
            && decimal.TryParse(observedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var observedNumber))
        {
            return requestedNumber == observedNumber;
        }

        return string.Equals(requestedValue.Trim(), observedValue.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
