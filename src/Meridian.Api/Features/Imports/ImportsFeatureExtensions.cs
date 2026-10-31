using Meridian.Api.Features.Imports.Classification;
using Meridian.Api.Features.Imports.Parsing;
using Meridian.Api.Shared.Jobs;

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

        services.AddScoped<ImportParser>();
        services.AddScoped<InFileChecks>();
        services.AddKeyedScoped<IJobHandler, ImportJobHandler>(ImportJobPayload.JobType);

        return services;
    }
}
