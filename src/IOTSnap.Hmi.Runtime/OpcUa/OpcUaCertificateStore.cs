using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Hosting;

namespace IOTSnap.Hmi.Runtime.OpcUa;

public sealed class OpcUaCertificateStore(IHostEnvironment hostEnvironment) : IOpcUaCertificateStore
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cer",
        ".crt",
        ".der",
        ".pem"
    };

    public async Task<IReadOnlyList<OpcUaTrustedCertificateSummary>> ListTrustedCertificatesAsync(CancellationToken cancellationToken = default)
    {
        OpcUaCertificateStorePaths.EnsureDirectories(hostEnvironment);
        var results = new List<OpcUaTrustedCertificateSummary>();

        foreach (var trustKind in Enum.GetValues<OpcUaTrustStoreKind>())
        {
            var directory = GetDirectory(trustKind);
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                    using var certificate = LoadCertificate(bytes);
                    var fileInfo = new FileInfo(path);
                    results.Add(new OpcUaTrustedCertificateSummary(
                        fileInfo.Name,
                        certificate.Subject,
                        certificate.Thumbprint ?? string.Empty,
                        trustKind,
                        fileInfo.LastWriteTimeUtc,
                        fileInfo.Length));
                }
                catch
                {
                    // Ignore malformed files when listing; import validation prevents new bad entries.
                }
            }
        }

        return results
            .OrderBy(x => x.TrustKind)
            .ThenBy(x => x.Subject, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<OpcUaTrustedCertificateSummary> ImportTrustedCertificateAsync(
        string fileName,
        Stream content,
        OpcUaTrustStoreKind trustKind,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("Certificate file name is required.");
        }

        var extension = Path.GetExtension(fileName);
        if (!SupportedExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Supported certificate files are .cer, .crt, .der, and .pem.");
        }

        OpcUaCertificateStorePaths.EnsureDirectories(hostEnvironment);

        using var memory = new MemoryStream();
        await content.CopyToAsync(memory, cancellationToken);
        if (memory.Length == 0)
        {
            throw new InvalidOperationException("The selected certificate file is empty.");
        }

        using var certificate = LoadCertificate(memory.ToArray());
        var exportBytes = certificate.Export(X509ContentType.Cert);
        var baseName = SanitizeFileName(Path.GetFileNameWithoutExtension(fileName));
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "trusted-certificate";
        }

        var targetPath = GetUniquePath(GetDirectory(trustKind), $"{baseName}-{certificate.Thumbprint}.cer");
        await File.WriteAllBytesAsync(targetPath, exportBytes, cancellationToken);

        var fileInfo = new FileInfo(targetPath);
        return new OpcUaTrustedCertificateSummary(
            fileInfo.Name,
            certificate.Subject,
            certificate.Thumbprint ?? string.Empty,
            trustKind,
            fileInfo.LastWriteTimeUtc,
            fileInfo.Length);
    }

    private string GetDirectory(OpcUaTrustStoreKind trustKind)
        => trustKind switch
        {
            OpcUaTrustStoreKind.ApplicationPeer => OpcUaCertificateStorePaths.GetTrustedPeerDirectory(hostEnvironment),
            OpcUaTrustStoreKind.IssuerAuthority => OpcUaCertificateStorePaths.GetTrustedIssuerDirectory(hostEnvironment),
            _ => throw new InvalidOperationException($"Unsupported trust store kind '{trustKind}'.")
        };

    private static X509Certificate2 LoadCertificate(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal)
            ? X509Certificate2.CreateFromPem(text)
            : X509CertificateLoader.LoadCertificate(bytes);
    }

    private static string SanitizeFileName(string value)
    {
        var sanitized = new string(value
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-')
            .ToArray());

        while (sanitized.Contains("--", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("--", "-", StringComparison.Ordinal);
        }

        return sanitized.Trim('-');
    }

    private static string GetUniquePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        return Path.Combine(directory, $"{stem}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}{extension}");
    }
}
