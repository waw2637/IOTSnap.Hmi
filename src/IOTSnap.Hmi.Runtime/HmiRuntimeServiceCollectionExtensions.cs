using IOTSnap.Hmi.Runtime.OpcUa;
using Microsoft.Extensions.DependencyInjection;

namespace IOTSnap.Hmi.Runtime;

public static class HmiRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddHmiRuntime(this IServiceCollection services)
    {
        services.AddSingleton<IOpcUaCertificateStore, OpcUaCertificateStore>();
        services.AddSingleton<OpcUaRuntimeService>();
        services.AddSingleton<IOpcUaDiscovery, OpcUaDiscoveryService>();
        services.AddSingleton<IOpcUaRuntime>(sp => sp.GetRequiredService<OpcUaRuntimeService>());
        services.AddHostedService(sp => sp.GetRequiredService<OpcUaRuntimeService>());
        return services;
    }
}
