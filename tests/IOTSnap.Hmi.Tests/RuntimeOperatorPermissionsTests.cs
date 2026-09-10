using IOTSnap.Hmi.Data.Entities;
using IOTSnap.Hmi.Features.Designer;
using Xunit;

namespace IOTSnap.Hmi.Tests;

public class RuntimeOperatorPermissionsTests
{
    [Theory]
    [InlineData(HmiRoles.Viewer, false)]
    [InlineData(HmiRoles.Operator, true)]
    [InlineData(HmiRoles.Admin, true)]
    [InlineData(null, false)]
    public void CanControl_OnlyAllowsOperatorAndAdminRoles(string? role, bool expected)
    {
        Assert.Equal(expected, RuntimeOperatorPermissions.CanControl(role));
    }
}
