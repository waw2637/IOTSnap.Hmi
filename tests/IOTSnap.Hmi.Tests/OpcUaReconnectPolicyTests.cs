using IOTSnap.Hmi.Runtime.OpcUa;
using Xunit;

namespace IOTSnap.Hmi.Tests;

public class OpcUaReconnectPolicyTests
{
    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(10, 60)]
    public void GetDelay_UsesBoundedExponentialBackoff(int failures, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), OpcUaReconnectPolicy.GetDelay(failures));
    }
}
