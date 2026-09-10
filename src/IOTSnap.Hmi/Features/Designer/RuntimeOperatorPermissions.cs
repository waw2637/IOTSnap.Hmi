using IOTSnap.Hmi.Data.Entities;

namespace IOTSnap.Hmi.Features.Designer;

public static class RuntimeOperatorPermissions
{
    public static bool CanControl(string? role)
        => string.Equals(role, HmiRoles.Operator, StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, HmiRoles.Admin, StringComparison.OrdinalIgnoreCase);
}
