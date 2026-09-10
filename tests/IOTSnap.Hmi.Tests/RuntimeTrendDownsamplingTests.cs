using IOTSnap.Hmi.Features.Designer;
using Xunit;

namespace IOTSnap.Hmi.Tests;

public class RuntimeTrendDownsamplingTests
{
    [Fact]
    public void TakeEvenly_PreservesFirstLastAndRequestedCount()
    {
        var result = RuntimeTrendDownsampling.TakeEvenly(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, 4);

        Assert.Equal(new[] { 0, 3, 6, 9 }, result);
    }
}
