using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Seeding;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Shared;

public static class SharedServiceCollectionExtensions
{
    public static IServiceCollection AddShared(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextFactory<AppDbContext>(o => o
            .UseNpgsql(configuration.GetConnectionString("Default"))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<Seeder>();

        return services;
    }
}
