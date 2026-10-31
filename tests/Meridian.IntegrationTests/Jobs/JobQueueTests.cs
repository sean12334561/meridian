using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Jobs;
using Meridian.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.IntegrationTests.Jobs;

public class JobQueueTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using AppDbContext db = postgres.CreateDbContext();
        await db.Jobs.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Enqueued_job_commits_with_the_callers_save()
    {
        await using AppDbContext db = postgres.CreateDbContext();
        await using (IDbContextTransaction tx = await db.Database.BeginTransactionAsync())
        {
            CreateQueue(db).Enqueue("import", new { importId = 1 });
            await db.SaveChangesAsync();
            await tx.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        Assert.Empty(await Jobs());

        CreateQueue(db).Enqueue("import", new { importId = 1 });
        Assert.Empty(await Jobs());

        await db.SaveChangesAsync();
        Assert.Equal(JobStatus.Pending, Assert.Single(await Jobs()).Status);
    }

    [Fact]
    public async Task Claiming_takes_the_oldest_job_and_starts_its_lease()
    {
        await Enqueue("import", new { importId = 7, correlationId = "abc" });
        await Enqueue("snapshot", new { snapshotId = 8 });

        ClaimedJob? job = await Claim();

        Assert.NotNull(job);
        Assert.Equal("import", job.Type);
        Assert.Equal(1, job.Attempts);
        Assert.Equal("abc", job.CorrelationId);
        Assert.Equal(7, job.ReadPayload<ImportPayload>().ImportId);

        Job stored = await Stored(job);
        Assert.Equal(JobStatus.Running, stored.Status);
        Assert.InRange(stored.LockedUntil!.Value, DateTimeOffset.UtcNow.AddSeconds(100), DateTimeOffset.UtcNow.AddSeconds(130));
    }

    [Fact]
    public async Task Nothing_to_claim_returns_null()
    {
        Assert.Null(await Claim());
    }

    [Fact]
    public async Task A_running_job_with_a_live_lease_is_not_claimed()
    {
        await Enqueue("import", new { });
        await Claim();

        Assert.Null(await Claim());
    }

    [Fact]
    public async Task An_expired_lease_is_claimed_again_as_the_next_attempt()
    {
        await Enqueue("import", new { });
        ClaimedJob first = (await Claim())!;
        await ExpireLease(first);

        ClaimedJob? second = await Claim();

        Assert.Equal(first.Id, second?.Id);
        Assert.Equal(2, second?.Attempts);
    }

    [Fact]
    public async Task A_job_is_never_claimed_twice_by_concurrent_workers()
    {
        for (int i = 0; i < 20; i++)
            await Enqueue("import", new { });

        ClaimedJob?[][] claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ClaimAll()));

        long[] ids = claims.SelectMany(c => c).Select(j => j!.Id).ToArray();
        Assert.Equal(20, ids.Length);
        Assert.Equal(20, ids.Distinct().Count());

        async Task<ClaimedJob?[]> ClaimAll()
        {
            List<ClaimedJob?> claimed = [];
            while (await Claim() is { } job)
                claimed.Add(job);
            return claimed.ToArray();
        }
    }

    [Fact]
    public async Task Complete_marks_the_job_succeeded()
    {
        await Enqueue("import", new { });
        ClaimedJob job = (await Claim())!;

        await WithQueue(q => q.CompleteAsync(job, default));

        Job stored = await Stored(job);
        Assert.Equal(JobStatus.Succeeded, stored.Status);
        Assert.Null(stored.LockedUntil);
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    public async Task A_failure_before_the_limit_retries_later(int attempt, int delaySeconds)
    {
        await Enqueue("import", new { });
        ClaimedJob job = await ClaimAtAttempt(attempt);

        bool failed = await FailOrRetry(job);

        Assert.False(failed);
        Job stored = await Stored(job);
        Assert.Equal(JobStatus.Pending, stored.Status);
        Assert.Equal("boom", stored.LastError);
        Assert.Null(stored.LockedUntil);
        Assert.InRange(
            stored.RunAfter,
            DateTimeOffset.UtcNow.AddSeconds(delaySeconds - 10),
            DateTimeOffset.UtcNow.AddSeconds(delaySeconds + 10));
        Assert.Null(await Claim());
    }

    [Fact]
    public async Task The_third_failure_fails_the_job()
    {
        await Enqueue("import", new { });
        ClaimedJob job = await ClaimAtAttempt(3);

        bool failed = await FailOrRetry(job);

        Assert.True(failed);
        Job stored = await Stored(job);
        Assert.Equal(JobStatus.Failed, stored.Status);
        Assert.Equal("boom", stored.LastError);
        Assert.Null(await Claim());
    }

    [Fact]
    public async Task A_worker_that_lost_its_lease_cannot_change_the_job()
    {
        await Enqueue("import", new { });
        ClaimedJob stale = (await Claim())!;
        await ExpireLease(stale);
        ClaimedJob current = (await Claim())!;

        await WithQueue(q => q.CompleteAsync(stale, default));
        await FailOrRetry(stale);
        await WithQueue(q => q.ReleaseAsync(stale));

        Job stored = await Stored(current);
        Assert.Equal(JobStatus.Running, stored.Status);
        Assert.Equal(2, stored.Attempts);
    }

    [Fact]
    public async Task Release_returns_the_job_without_using_an_attempt()
    {
        await Enqueue("import", new { });
        ClaimedJob job = (await Claim())!;

        await WithQueue(q => q.ReleaseAsync(job));

        Job stored = await Stored(job);
        Assert.Equal(JobStatus.Pending, stored.Status);
        Assert.Equal(0, stored.Attempts);
        Assert.Equal(job.Id, (await Claim())?.Id);
    }

    [Fact]
    public async Task Heartbeat_renews_the_lease_until_disposed()
    {
        await Enqueue("import", new { });
        ClaimedJob job = (await Claim())!;
        await ExpireLease(job);

        await using AppDbContext db = postgres.CreateDbContext();
        await using (CreateQueue(db).StartHeartbeat(job))
        {
            await Wait.Until(async () => (await Stored(job)).LockedUntil > DateTimeOffset.UtcNow);
        }

        await ExpireLease(job);
        await Task.Delay(200);
        Assert.True((await Stored(job)).LockedUntil < DateTimeOffset.UtcNow);
    }

    private JobQueue CreateQueue(AppDbContext db) => new(
        db,
        new FixtureDbContextFactory(postgres),
        Options.Create(new WorkerOptions { HeartbeatIntervalMs = 20 }),
        NullLogger<JobQueue>.Instance);

    private async Task WithQueue(Func<JobQueue, Task> action)
    {
        await using AppDbContext db = postgres.CreateDbContext();
        await action(CreateQueue(db));
    }

    private async Task Enqueue(string type, object payload)
    {
        await using AppDbContext db = postgres.CreateDbContext();
        CreateQueue(db).Enqueue(type, payload);
        await db.SaveChangesAsync();
    }

    private async Task<ClaimedJob?> Claim()
    {
        await using AppDbContext db = postgres.CreateDbContext();
        return await CreateQueue(db).TryClaimAsync(default);
    }

    private async Task<ClaimedJob> ClaimAtAttempt(int attempt)
    {
        await using AppDbContext db = postgres.CreateDbContext();
        await db.Jobs.ExecuteUpdateAsync(s => s.SetProperty(j => j.Attempts, attempt - 1));
        return (await Claim())!;
    }

    private async Task<bool> FailOrRetry(ClaimedJob job)
    {
        await using AppDbContext db = postgres.CreateDbContext();
        return await CreateQueue(db).FailOrRetryAsync(job, new InvalidOperationException("boom"), default);
    }

    private async Task ExpireLease(ClaimedJob job)
    {
        await using AppDbContext db = postgres.CreateDbContext();
        await db.Jobs
            .Where(j => j.Id == job.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.LockedUntil, DateTimeOffset.UtcNow.AddMinutes(-1)));
    }

    private async Task<List<Job>> Jobs()
    {
        await using AppDbContext db = postgres.CreateDbContext();
        return await db.Jobs.ToListAsync();
    }

    private async Task<Job> Stored(ClaimedJob job)
    {
        await using AppDbContext db = postgres.CreateDbContext();
        return await db.Jobs.SingleAsync(j => j.Id == job.Id);
    }

    private class ImportPayload
    {
        public required int ImportId { get; init; }
    }
}
