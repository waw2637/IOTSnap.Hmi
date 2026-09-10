using IOTSnap.Hmi.Features.Designer;
using Xunit;

namespace IOTSnap.Hmi.Tests;

public class RuntimeCommandConfirmationTests
{
    [Theory]
    [InlineData("true", "True", true)]
    [InlineData("10.0", "10", true)]
    [InlineData("open", "OPEN", true)]
    [InlineData("true", "false", false)]
    [InlineData("10", "10.1", false)]
    [InlineData("open", null, false)]
    public void Matches_ComparesTypedAndTextValues(string requested, string? observed, bool expected)
    {
        Assert.Equal(expected, RuntimeCommandConfirmation.Matches(requested, observed));
    }
}
