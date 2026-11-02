using Meridian.Api.Shared.Jobs;

namespace Meridian.Api.Features.Snapshots;

public static class SnapshotsFeatureExtensions
{
    public static IServiceCollection AddSnapshotsFeature(this IServiceCollection services)
    {
        services.AddScoped<CreateSnapshot.Handler>();
        services.AddScoped<PreviewSnapshot.Handler>();
        services.AddScoped<GetSnapshots.Handler>();
        services.AddScoped<GetSnapshot.Handler>();
        services.AddScoped<GetSnapshotVotes.Handler>();
        services.AddScoped<GetCountryBreakdown.Handler>();
        services.AddScoped<PublishSnapshot.Handler>();

        services.AddKeyedScoped<IJobHandler, SnapshotJobHandler>(SnapshotJobPayload.JobType);

        return services;
    }
}
