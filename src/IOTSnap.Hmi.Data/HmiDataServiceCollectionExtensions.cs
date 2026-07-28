using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IOTSnap.Hmi.Data;

public static class HmiDataServiceCollectionExtensions
{
    public static IServiceCollection AddHmiData(this IServiceCollection services, string connectionString)
    {
        services.AddDbContextFactory<HmiDbContext>(options => options.UseSqlite(connectionString));
        return services;
    }
}
