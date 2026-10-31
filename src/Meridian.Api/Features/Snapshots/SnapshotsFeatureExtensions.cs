using Meridian.Api.Shared.Jobs;

namespace Meridian.Api.Features.Snapshots;

public static class SnapshotsFeatureExtensions
{
    public static IServiceCollection AddSnapshotsFeature(this IServiceCollection services)
    {
        services.AddScoped<CreateSnapshot.Handler>();

        services.AddKeyedScoped<IJobHandler, SnapshotJobHandler>(SnapshotJobPayload.JobType);

        return services;
    }
}
