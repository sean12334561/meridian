namespace Meridian.Api.Features.Imports;

public static class ImportsFeatureExtensions
{
    public static IServiceCollection AddImportsFeature(this IServiceCollection services)
    {
        services.AddOptions<UploadOptions>().BindConfiguration(UploadOptions.SectionName);
        services.AddScoped<UploadLimitFilter>();

        services.AddScoped<UploadImport.Handler>();
        services.AddScoped<GetImports.Handler>();
        services.AddScoped<GetImport.Handler>();

        return services;
    }
}
