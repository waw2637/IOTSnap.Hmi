using IOTSnap.Hmi.Features.Designer;
using Xunit;

namespace IOTSnap.Hmi.Tests;

public class RuntimeAlarmSummaryTests
{
    [Fact]
    public void GetBadgeText_UsesActiveAndAcknowledgedState()
    {
        Assert.Equal("Active", RuntimeAlarmSummary.GetBadgeText(isActive: true, isAcknowledged: false));
        Assert.Equal("Acked", RuntimeAlarmSummary.GetBadgeText(isActive: true, isAcknowledged: true));
        Assert.Equal("Cleared", RuntimeAlarmSummary.GetBadgeText(isActive: false, isAcknowledged: false));
    }

    [Fact]
    public void GetBadgeClass_UsesAlarmTone()
    {
        Assert.Equal("alarm-badge-active", RuntimeAlarmSummary.GetBadgeClass(isActive: true, isAcknowledged: false));
        Assert.Equal("alarm-badge-acked", RuntimeAlarmSummary.GetBadgeClass(isActive: true, isAcknowledged: true));
        Assert.Equal("alarm-badge-cleared", RuntimeAlarmSummary.GetBadgeClass(isActive: false, isAcknowledged: false));
    }
}
