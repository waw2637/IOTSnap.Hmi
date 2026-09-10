using IOTSnap.Hmi.Data.Entities;
using IOTSnap.Hmi.Runtime.OpcUa;
using Opc.Ua;
using System.Reflection;
using Xunit;

namespace IOTSnap.Hmi.Tests;

public class OpcUaEndpointSelectionTests
{
    [Fact]
    public void FindMatchingEndpoint_SelectsConfiguredSecureEndpoint()
    {
        var profile = new OpcUaConnectionProfile
        {
            EndpointUrl = "opc.tcp://cp1:50000",
            UseSecurity = true,
            SecurityPolicy = "Basic256Sha256",
            SecurityMode = "SignAndEncrypt"
        };

        var endpoints = new EndpointDescriptionCollection
        {
            CreateEndpoint(SecurityPolicies.None, MessageSecurityMode.None, (byte)1),
            CreateEndpoint(SecurityPolicies.Basic256Sha256, MessageSecurityMode.Sign, (byte)2),
            CreateEndpoint(SecurityPolicies.Basic256Sha256, MessageSecurityMode.SignAndEncrypt, (byte)3),
            CreateEndpoint(SecurityPolicies.Aes256_Sha256_RsaPss, MessageSecurityMode.SignAndEncrypt, (byte)4)
        };

        var matched = InvokeFindMatchingEndpoint(endpoints, profile);

        Assert.NotNull(matched);
        Assert.Equal(SecurityPolicies.Basic256Sha256, matched!.SecurityPolicyUri);
        Assert.Equal(MessageSecurityMode.SignAndEncrypt, matched.SecurityMode);
    }

    [Fact]
    public void FindMatchingEndpoint_SelectsUnsecuredEndpointWhenSecurityDisabled()
    {
        var profile = new OpcUaConnectionProfile
        {
            EndpointUrl = "opc.tcp://cp1:50000",
            UseSecurity = false,
            SecurityPolicy = "Aes256_Sha256_RsaPss",
            SecurityMode = "SignAndEncrypt"
        };

        var endpoints = new EndpointDescriptionCollection
        {
            CreateEndpoint(SecurityPolicies.Basic256Sha256, MessageSecurityMode.SignAndEncrypt, (byte)5),
            CreateEndpoint(SecurityPolicies.None, MessageSecurityMode.None, (byte)1)
        };

        var matched = InvokeFindMatchingEndpoint(endpoints, profile);

        Assert.NotNull(matched);
        Assert.Equal(SecurityPolicies.None, matched!.SecurityPolicyUri);
        Assert.Equal(MessageSecurityMode.None, matched.SecurityMode);
    }

    [Fact]
    public void FindMatchingEndpoint_ReturnsNullWhenConfiguredModeIsUnavailable()
    {
        var profile = new OpcUaConnectionProfile
        {
            EndpointUrl = "opc.tcp://cp1:50000",
            UseSecurity = true,
            SecurityPolicy = "Basic256Sha256",
            SecurityMode = "SignAndEncrypt"
        };

        var endpoints = new EndpointDescriptionCollection
        {
            CreateEndpoint(SecurityPolicies.Basic256Sha256, MessageSecurityMode.Sign, (byte)2),
            CreateEndpoint(SecurityPolicies.Aes256_Sha256_RsaPss, MessageSecurityMode.SignAndEncrypt, (byte)3)
        };

        var matched = InvokeFindMatchingEndpoint(endpoints, profile);

        Assert.Null(matched);
    }

    private static EndpointDescription CreateEndpoint(string securityPolicyUri, MessageSecurityMode securityMode, byte securityLevel)
        => new()
        {
            EndpointUrl = "opc.tcp://cp1:50000",
            TransportProfileUri = Profiles.UaTcpTransport,
            SecurityPolicyUri = securityPolicyUri,
            SecurityMode = securityMode,
            SecurityLevel = securityLevel
        };

    private static EndpointDescription? InvokeFindMatchingEndpoint(
        EndpointDescriptionCollection endpoints,
        OpcUaConnectionProfile profile)
    {
        var method = typeof(OpcUaRuntimeService).GetMethod(
            "FindMatchingEndpoint",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return (EndpointDescription?)method!.Invoke(null, [endpoints, profile]);
    }
}
