namespace IOTSnap.Hmi.Runtime.OpcUa;

public sealed record OpcUaTrustedCertificateSummary(
    string FileName,
    string Subject,
    string Thumbprint,
    OpcUaTrustStoreKind TrustKind,
    DateTimeOffset ImportedUtc,
    long SizeBytes);
