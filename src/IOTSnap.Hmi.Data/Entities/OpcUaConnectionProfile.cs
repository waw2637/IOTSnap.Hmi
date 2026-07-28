namespace IOTSnap.Hmi.Data.Entities;

public sealed class OpcUaConnectionProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = "Primary PLC";
    public string EndpointUrl { get; set; } = "opc.tcp://127.0.0.1:4840";
    public bool Enabled { get; set; } = true;
    public bool UseSecurity { get; set; } = true;
    public string SecurityPolicy { get; set; } = "Basic256Sha256";
    public string SecurityMode { get; set; } = "SignAndEncrypt";
    public string AuthenticationMode { get; set; } = "Anonymous";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int PublishingIntervalMs { get; set; } = 1000;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<OpcUaNodeMapping> NodeMappings { get; set; } = new List<OpcUaNodeMapping>();
}
