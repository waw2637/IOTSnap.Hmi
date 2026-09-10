namespace IOTSnap.Hmi.Features.Designer;

public static class RuntimeAlarmSummary
{
    public static string GetBadgeText(bool isActive, bool isAcknowledged)
    {
        if (!isActive)
        {
            return "Cleared";
        }

        return isAcknowledged ? "Acked" : "Active";
    }

    public static string GetBadgeClass(bool isActive, bool isAcknowledged)
    {
        if (!isActive)
        {
            return "alarm-badge-cleared";
        }

        return isAcknowledged ? "alarm-badge-acked" : "alarm-badge-active";
    }
}
