using System.Security.Claims;
using IOTSnap.Hmi.Data.Entities;

namespace IOTSnap.Hmi.Features.Designer;

public static class RuntimeAccessPolicy
{
    public static RuntimeActor? ResolveActor(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var username = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        var displayName = user.FindFirstValue(ClaimTypes.GivenName);
        return new RuntimeActor(
            username.Trim(),
            string.IsNullOrWhiteSpace(displayName) ? username.Trim() : displayName.Trim(),
            NormalizeRole(user.FindFirstValue(ClaimTypes.Role)));
    }

    public static bool IsRoleAuthorized(string actualRole, string requiredRole)
        => GetRoleRank(actualRole) >= GetRoleRank(requiredRole);

    public static RuntimeBindingAccess? ResolveWriteAccess(IEnumerable<RuntimeBindingAccess> bindings, RuntimeActionTarget target, string actualRole)
        => bindings.FirstOrDefault(binding =>
            binding.IsPublished
            && binding.Matches(target)
            && binding.SourceType.Equals("OpcTag", StringComparison.OrdinalIgnoreCase)
            && binding.IsWritable
            && IsRoleAuthorized(actualRole, binding.MinRole));

    public static RuntimeBindingAccess? ResolveAlarmAccess(IEnumerable<RuntimeBindingAccess> bindings, RuntimeActionTarget target, string actualRole)
        => bindings.FirstOrDefault(binding =>
            binding.IsPublished
            && binding.Matches(target)
            && binding.SourceType.Equals("OpcTag", StringComparison.OrdinalIgnoreCase)
            && IsRoleAuthorized(actualRole, binding.MinRole));

    public static string NormalizeRole(string? role)
    {
        if (string.Equals(role, HmiRoles.Admin, StringComparison.OrdinalIgnoreCase))
        {
            return HmiRoles.Admin;
        }

        if (string.Equals(role, HmiRoles.Operator, StringComparison.OrdinalIgnoreCase))
        {
            return HmiRoles.Operator;
        }

        return HmiRoles.Viewer;
    }

    private static int GetRoleRank(string? role)
        => NormalizeRole(role) switch
        {
            HmiRoles.Admin => 3,
            HmiRoles.Operator => 2,
            _ => 1
        };
}

public sealed record RuntimeActor(string Username, string DisplayName, string Role);

public sealed record RuntimeBindingAccess(
    string ScreenSlug,
    string WidgetKey,
    string BindingRole,
    string SourceType,
    string SourceKey,
    string MinRole,
    bool IsPublished,
    bool IsWritable,
    bool WriteRequiresConfirm = false)
{
    public bool Matches(RuntimeActionTarget target)
        => string.Equals(ScreenSlug, target.ScreenSlug, StringComparison.OrdinalIgnoreCase)
            && string.Equals(WidgetKey, target.WidgetKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(BindingRole, target.BindingRole, StringComparison.OrdinalIgnoreCase)
            && string.Equals(SourceKey, target.NodeId, StringComparison.OrdinalIgnoreCase);
}

public sealed record RuntimeActionTarget(string ScreenSlug, string WidgetKey, string BindingRole, string NodeId);
