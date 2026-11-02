namespace Meridian.Api.Shared.Jobs;

/// <summary>Runs one job type. A run may be repeated, so handlers must be safe to re-run.</summary>
public interface IJobHandler
{
    Task RunAsync(ClaimedJob job, CancellationToken ct);

    /// <summary>Called once the job has used its attempts, to mark its import or snapshot Failed.</summary>
    Task OnFailedAsync(ClaimedJob job, string error, CancellationToken ct);
}
