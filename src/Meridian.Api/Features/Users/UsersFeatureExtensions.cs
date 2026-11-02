namespace Meridian.Api.Features.Users;

public static class UsersFeatureExtensions
{
    public static IServiceCollection AddUsersFeature(this IServiceCollection services)
    {
        services.AddScoped<GetUsers.Handler>();
        services.AddScoped<GetMe.Handler>();

        return services;
    }
}
