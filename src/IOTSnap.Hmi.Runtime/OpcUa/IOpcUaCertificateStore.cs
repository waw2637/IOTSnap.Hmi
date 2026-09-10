namespace IOTSnap.Hmi.Runtime.OpcUa;

public interface IOpcUaCertificateStore
{
    Task<IReadOnlyList<OpcUaTrustedCertificateSummary>> ListTrustedCertificatesAsync(CancellationToken cancellationToken = default);
    Task<OpcUaTrustedCertificateSummary> ImportTrustedCertificateAsync(string fileName, Stream content, OpcUaTrustStoreKind trustKind, CancellationToken cancellationToken = default);
}
