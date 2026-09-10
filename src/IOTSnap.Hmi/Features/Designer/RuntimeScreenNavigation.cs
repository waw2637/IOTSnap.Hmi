namespace IOTSnap.Hmi.Features.Designer;

public static class RuntimeScreenNavigation
{
    public static string? ResolveTargetScreenSlug(string? targetSlug, IEnumerable<string> availableSlugs)
    {
        if (string.IsNullOrWhiteSpace(targetSlug))
        {
            return null;
        }

        return availableSlugs
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .FirstOrDefault(x => string.Equals(x, targetSlug, StringComparison.OrdinalIgnoreCase));
    }
}
