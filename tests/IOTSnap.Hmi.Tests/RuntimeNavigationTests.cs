using IOTSnap.Hmi.Features.Designer;
using Xunit;

namespace IOTSnap.Hmi.Tests;

public class RuntimeNavigationTests
{
    [Fact]
    public void ResolveTargetScreenSlug_ReturnsMatchingSlug()
    {
        var slugs = new[] { "overview", "line-1" };

        var target = RuntimeScreenNavigation.ResolveTargetScreenSlug("line-1", slugs);

        Assert.Equal("line-1", target);
    }

    [Fact]
    public void ResolveTargetScreenSlug_ReturnsNullWhenNoMatchExists()
    {
        var slugs = new[] { "overview", "line-1" };

        var target = RuntimeScreenNavigation.ResolveTargetScreenSlug("line-2", slugs);

        Assert.Null(target);
    }
}
