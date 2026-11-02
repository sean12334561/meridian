namespace Meridian.Api.Features.Conflicts;

public static class ConflictsFeatureExtensions
{
    public static IServiceCollection AddConflictsFeature(this IServiceCollection services)
    {
        services.AddScoped<GetConflicts.Handler>();
        services.AddScoped<GetConflict.Handler>();
        services.AddScoped<DecideConflict.Handler>();

        return services;
    }
}
