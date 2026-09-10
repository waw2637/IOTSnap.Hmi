using IOTSnap.Hmi.Data.Entities;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace IOTSnap.Hmi.Tests;

public class OpcUaCredentialProtectionTests
{
    [Fact]
    public void ProtectedPassword_RoundTripsWithoutExposingPlaintext()
    {
        var provider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var protector = provider.CreateProtector("IOTSnap.Hmi.OpcUaProfilePassword.v1");
        const string secret = "PLC-password-123";
        var profile = new OpcUaConnectionProfile { ProtectedPassword = protector.Protect(secret), Password = null };

        Assert.Null(profile.Password);
        Assert.DoesNotContain(secret, profile.ProtectedPassword!, StringComparison.Ordinal);
        Assert.Equal(secret, protector.Unprotect(profile.ProtectedPassword!));
    }
}
