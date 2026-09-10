using Microsoft.Extensions.Hosting;

namespace IOTSnap.Hmi.Runtime.OpcUa;

public static class OpcUaCertificateStorePaths
{
    public static string GetBaseDirectory(IHostEnvironment hostEnvironment)
        => Path.Combine(hostEnvironment.ContentRootPath, "data", "opcua", "pki");

    public static string GetOwnDirectory(IHostEnvironment hostEnvironment)
        => Path.Combine(GetBaseDirectory(hostEnvironment), "own");

    public static string GetTrustedPeerDirectory(IHostEnvironment hostEnvironment)
        => Path.Combine(GetBaseDirectory(hostEnvironment), "trusted");

    public static string GetTrustedIssuerDirectory(IHostEnvironment hostEnvironment)
        => Path.Combine(GetBaseDirectory(hostEnvironment), "issuer");

    public static string GetRejectedDirectory(IHostEnvironment hostEnvironment)
        => Path.Combine(GetBaseDirectory(hostEnvironment), "rejected");

    public static void EnsureDirectories(IHostEnvironment hostEnvironment)
    {
        Directory.CreateDirectory(GetBaseDirectory(hostEnvironment));
        Directory.CreateDirectory(GetOwnDirectory(hostEnvironment));
        Directory.CreateDirectory(GetTrustedPeerDirectory(hostEnvironment));
        Directory.CreateDirectory(GetTrustedIssuerDirectory(hostEnvironment));
        Directory.CreateDirectory(GetRejectedDirectory(hostEnvironment));
    }
}
