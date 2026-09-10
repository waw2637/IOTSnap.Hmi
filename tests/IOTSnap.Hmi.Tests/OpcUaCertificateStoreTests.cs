using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using IOTSnap.Hmi.Runtime.OpcUa;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace IOTSnap.Hmi.Tests;

public class OpcUaCertificateStoreTests
{
    [Fact]
    public async Task ImportTrustedCertificateAsync_StoresCertificateInRequestedTrustStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "iotsnap-opc-certs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var store = new OpcUaCertificateStore(new TestHostEnvironment(root));
            using var certificate = CreateTestCertificate();
            var certificateBytes = certificate.Export(X509ContentType.Cert);

            await using var stream = new MemoryStream(certificateBytes);
            var imported = await store.ImportTrustedCertificateAsync("plc-server.cer", stream, OpcUaTrustStoreKind.ApplicationPeer);
            var listed = await store.ListTrustedCertificatesAsync();

            Assert.Equal(OpcUaTrustStoreKind.ApplicationPeer, imported.TrustKind);
            Assert.Contains(listed, x => x.Thumbprint == imported.Thumbprint && x.TrustKind == OpcUaTrustStoreKind.ApplicationPeer);
            Assert.True(File.Exists(Path.Combine(root, "data", "opcua", "pki", "trusted", imported.FileName)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ImportTrustedCertificateAsync_RejectsUnsupportedFileExtension()
    {
        var root = Path.Combine(Path.GetTempPath(), "iotsnap-opc-certs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var store = new OpcUaCertificateStore(new TestHostEnvironment(root));
            await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.ImportTrustedCertificateAsync("plc-server.txt", stream, OpcUaTrustStoreKind.IssuerAuthority));

            Assert.Contains(".cer", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static X509Certificate2 CreateTestCertificate()
    {
        using var key = ECDsa.Create();
        var request = new CertificateRequest("CN=IOTSnap Test PLC", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "IOTSnap.Hmi.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }
}
