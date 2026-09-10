namespace IOTSnap.Hmi.Runtime.OpcUa;

public static class OpcUaReconnectPolicy
{
    public static TimeSpan GetDelay(int consecutiveFailures)
    {
        if (consecutiveFailures <= 0) return TimeSpan.FromSeconds(5);

        var seconds = Math.Min(60, 5 * Math.Pow(2, consecutiveFailures - 1));
        return TimeSpan.FromSeconds(seconds);
    }
}
