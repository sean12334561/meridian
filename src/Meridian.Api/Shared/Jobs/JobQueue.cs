using System.Text.Json;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Meridian.Api.Shared.Jobs;

/// <summary>
/// A job queue on the jobs table. Updates to a claimed job match its status and attempt count, so a
/// worker that lost its lease can't change a job another worker has since claimed.
/// </summary>
public class JobQueue(
    AppDbContext db,
    IDbContextFactory<AppDbContext> dbFactory,
    IOptions<WorkerOptions> options,
    ILogger<JobQueue> logger)
{
    public const int MaxAttempts = 3;

    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>Adds the job to the context, so the caller's SaveChanges commits it with their work.</summary>
    public void Enqueue(string type, object payload)
    {
        db.Jobs.Add(new Job
        {
            Type = type,
            Payload = JsonSerializer.Serialize(payload, JsonSerializerOptions.Web),
            Status = JobStatus.Pending,
        });
    }

    public async Task<ClaimedJob?> TryClaimAsync(CancellationToken ct)
    {
        List<Job> claimed = await db.Jobs
            .FromSqlInterpolated($"""
                UPDATE jobs SET status = 'Running', attempts = attempts + 1,
                                locked_until = now() + {Lease}
                WHERE id = (SELECT id FROM jobs
                            WHERE (status = 'Pending' AND run_after <= now())
                               OR (status = 'Running' AND locked_until < now())
                            ORDER BY created_at
                            FOR UPDATE SKIP LOCKED LIMIT 1)
                RETURNING *
                """)
            .AsNoTracking()
            .ToListAsync(ct);

        return claimed.Count == 0 ? null : ClaimedJob.From(claimed[0]);
    }

    /// <summary>Renews the lease on a timer until disposed. Uses its own context: the handler is using the scoped one.</summary>
    public IAsyncDisposable StartHeartbeat(ClaimedJob job)
    {
        return new Heartbeat(this, job);
    }

    public async Task CompleteAsync(ClaimedJob job, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE jobs SET status = 'Succeeded', locked_until = NULL
            WHERE id = {job.Id} AND status = 'Running' AND attempts = {job.Attempts}
            """, ct);
    }

    /// <summary>Returns true when the job has no attempts left and is now Failed.</summary>
    public async Task<bool> FailOrRetryAsync(ClaimedJob job, Exception exception, CancellationToken ct)
    {
        if (job.Attempts >= MaxAttempts)
        {
            int failed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE jobs SET status = 'Failed', locked_until = NULL, last_error = {exception.Message}
                WHERE id = {job.Id} AND status = 'Running' AND attempts = {job.Attempts}
                """, ct);
            return failed > 0;
        }

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE jobs SET status = 'Pending', locked_until = NULL, last_error = {exception.Message},
                            run_after = now() + {RetryDelay * job.Attempts}
            WHERE id = {job.Id} AND status = 'Running' AND attempts = {job.Attempts}
            """, ct);
        return false;
    }

    /// <summary>Hands the job back on shutdown. It wasn't the job's fault, so the attempt isn't counted.</summary>
    public async Task ReleaseAsync(ClaimedJob job)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE jobs SET status = 'Pending', attempts = attempts - 1, locked_until = NULL
            WHERE id = {job.Id} AND status = 'Running' AND attempts = {job.Attempts}
            """, CancellationToken.None);
    }

    private async Task BeatAsync(ClaimedJob job, CancellationToken ct)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(options.Value.HeartbeatIntervalMs));

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await using AppDbContext heartbeatDb = await dbFactory.CreateDbContextAsync(ct);
                await heartbeatDb.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE jobs SET locked_until = now() + {Lease}
                    WHERE id = {job.Id} AND status = 'Running' AND attempts = {job.Attempts}
                    """, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Keep beating: one missed renewal is fine, a stopped heartbeat loses the lease
                logger.LogWarning(ex, "Heartbeat failed for job {JobId}", job.Id);
            }
        }
    }

    private class Heartbeat : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _running;

        public Heartbeat(JobQueue queue, ClaimedJob job)
        {
            _running = queue.BeatAsync(job, _stop.Token);
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            try
            {
                await _running;
            }
            catch (OperationCanceledException)
            {
            }
            _stop.Dispose();
        }
    }
}
