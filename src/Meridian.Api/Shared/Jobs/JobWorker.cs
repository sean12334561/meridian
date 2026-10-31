using Microsoft.Extensions.Options;

namespace Meridian.Api.Shared.Jobs;

public class JobWorker(IServiceScopeFactory scopes, IOptions<WorkerOptions> options, ILogger<JobWorker> logger)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.WhenAll(Enumerable.Range(0, options.Value.Concurrency).Select(_ => LoopAsync(stoppingToken)));
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await RunNextJobAsync(ct))
                    await Task.Delay(options.Value.PollIntervalMs, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                // A database outage must not end the loop, or the worker stops without exiting
                logger.LogError(ex, "Job loop failed");
                await Task.Delay(options.Value.PollIntervalMs, ct);
            }
        }
    }

    private async Task<bool> RunNextJobAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        JobQueue queue = scope.ServiceProvider.GetRequiredService<JobQueue>();

        ClaimedJob? job = await queue.TryClaimAsync(ct);
        if (job is null)
            return false;

        using IDisposable? logScope = logger.BeginScope(
            "JobId:{JobId} CorrelationId:{CorrelationId}", job.Id, job.CorrelationId);
        await using IAsyncDisposable heartbeat = queue.StartHeartbeat(job);

        IJobHandler? handler = null;
        try
        {
            handler = scope.ServiceProvider.GetRequiredKeyedService<IJobHandler>(job.Type);

            // Errors stop at the limit in FailOrRetryAsync, so only a crash gets a job past it
            if (job.Attempts > JobQueue.MaxAttempts)
                throw new InvalidOperationException(
                    $"The worker stopped while running this job {JobQueue.MaxAttempts} times.");

            await handler.RunAsync(job, ct);
            await queue.CompleteAsync(job, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Job {JobType} failed on attempt {Attempt}", job.Type, job.Attempts);

            bool failed = await queue.FailOrRetryAsync(job, ex, ct);
            if (failed && handler is not null)
                await handler.OnFailedAsync(job, ex.Message, ct);
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            await queue.ReleaseAsync(job);
        }

        return true;
    }
}
