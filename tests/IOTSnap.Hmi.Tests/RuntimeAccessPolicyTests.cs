using System.Security.Claims;
using IOTSnap.Hmi.Data.Entities;
using IOTSnap.Hmi.Features.Designer;
using Xunit;

namespace IOTSnap.Hmi.Tests;

public class RuntimeAccessPolicyTests
{
    [Fact]
    public void ResolveActor_UsesAuthenticatedClaimsIdentity()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "operator1"),
            new Claim(ClaimTypes.GivenName, "Casey Operator"),
            new Claim(ClaimTypes.Role, HmiRoles.Operator)
        ], "Cookies"));

        var actor = RuntimeAccessPolicy.ResolveActor(principal);

        Assert.NotNull(actor);
        Assert.Equal("operator1", actor!.Username);
        Assert.Equal("Casey Operator", actor.DisplayName);
        Assert.Equal(HmiRoles.Operator, actor.Role);
    }

    [Theory]
    [InlineData(HmiRoles.Admin, HmiRoles.Operator, true)]
    [InlineData(HmiRoles.Operator, HmiRoles.Viewer, true)]
    [InlineData(HmiRoles.Viewer, HmiRoles.Operator, false)]
    public void IsRoleAuthorized_UsesExpectedHierarchy(string actualRole, string requiredRole, bool expected)
    {
        var authorized = RuntimeAccessPolicy.IsRoleAuthorized(actualRole, requiredRole);

        Assert.Equal(expected, authorized);
    }

    [Fact]
    public void ResolveWriteAccess_RequiresPublishedWritableBindingAndSufficientRole()
    {
        var bindings = new[]
        {
            new RuntimeBindingAccess("main", "start", "Action", "OpcTag", "ns=3;s=Plant.Line.Start", HmiRoles.Operator, true, true),
            new RuntimeBindingAccess("main", "start", "Action", "OpcTag", "ns=3;s=Plant.Line.Start", HmiRoles.Admin, false, true)
        };
        var target = new RuntimeActionTarget("main", "start", "Action", "ns=3;s=Plant.Line.Start");

        var allowed = RuntimeAccessPolicy.ResolveWriteAccess(bindings, target, HmiRoles.Operator);
        var denied = RuntimeAccessPolicy.ResolveWriteAccess(bindings, target, HmiRoles.Viewer);

        Assert.NotNull(allowed);
        Assert.Null(denied);
    }

    [Fact]
    public void ResolveAlarmAccess_DeniesUnpublishedBindings()
    {
        var bindings = new[]
        {
            new RuntimeBindingAccess("main", "level", "PrimaryValue", "OpcTag", "ns=2;s=Plant.Alarm.HighLevel", HmiRoles.Viewer, false, true)
        };
        var target = new RuntimeActionTarget("main", "level", "PrimaryValue", "ns=2;s=Plant.Alarm.HighLevel");

        var access = RuntimeAccessPolicy.ResolveAlarmAccess(bindings, target, HmiRoles.Admin);

        Assert.Null(access);
    }

    [Fact]
    public void ResolveWriteAccess_DeniesMismatchedWidgetPath()
    {
        var bindings = new[]
        {
            new RuntimeBindingAccess("main", "start", "Action", "OpcTag", "ns=3;s=Plant.Line.Start", HmiRoles.Operator, true, true)
        };
        var substitutedTarget = new RuntimeActionTarget("main", "different-widget", "Action", "ns=3;s=Plant.Line.Start");

        var access = RuntimeAccessPolicy.ResolveWriteAccess(bindings, substitutedTarget, HmiRoles.Admin);

        Assert.Null(access);
    }
}
