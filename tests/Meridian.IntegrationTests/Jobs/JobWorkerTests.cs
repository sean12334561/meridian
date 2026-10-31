using Meridian.Api.Shared;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Jobs;
using Meridian.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.IntegrationTests.Jobs;

public class JobWorkerTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly TestHandler _handler = new();

    public async Task InitializeAsync()
    {
        await using AppDbContext db = postgres.CreateDbContext();
        await db.Jobs.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_job_runs_on_its_keyed_handler_and_succeeds()
    {
        Job job = await AddJob("test");

        await WhileWorkerRuns(() => WaitForStatus(job, JobStatus.Succeeded));

        Assert.Equal(job.Id, Assert.Single(_handler.Ran));
        Assert.Empty(_handler.Failed);
    }

    [Fact]
    public async Task A_throwing_job_is_retried_later_and_the_handler_is_not_told()
    {
        _handler.Run = (_, _) => throw new InvalidOperationException("boom");
        Job job = await AddJob("test");

        await WhileWorkerRuns(() => Wait.Until(async () => (await Reload(job)).LastError == "boom"));

        Job stored = await Reload(job);
        Assert.Equal(JobStatus.Pending, stored.Status);
        Assert.Equal(1, stored.Attempts);
        Assert.Empty(_handler.Failed);
    }

    [Fact]
    public async Task The_third_error_fails_the_job_and_tells_the_handler()
    {
        _handler.Run = (_, _) => throw new InvalidOperationException("boom");
        Job job = await AddJob("test", attempts: 2);

        await WhileWorkerRuns(() => Wait.Until(() => Task.FromResult(_handler.Failed.Count > 0)));

        Assert.Equal(JobStatus.Failed, (await Reload(job)).Status);
        Assert.Equal((job.Id, "boom"), Assert.Single(_handler.Failed));
    }

    [Fact]
    public async Task A_job_that_keeps_crashing_its_worker_fails_without_running()
    {
        Job job = await AddJob("test", JobStatus.Running, attempts: 3, DateTimeOffset.UtcNow.AddMinutes(-1));

        await WhileWorkerRuns(() => WaitForStatus(job, JobStatus.Failed));
        await Wait.Until(() => Task.FromResult(_handler.Failed.Count > 0));

        Assert.Empty(_handler.Ran);
        Assert.Contains("worker stopped", Assert.Single(_handler.Failed).Error);
    }

    [Fact]
    public async Task An_unknown_job_type_does_not_stop_the_worker()
    {
        Job unknown = await AddJob("no-such-type");
        Job next = await AddJob("test");

        await WhileWorkerRuns(async () =>
        {
            await WaitForStatus(next, JobStatus.Succeeded);
            await Wait.Until(async () => (await Reload(unknown)).LastError is not null);
        });

        Job stored = await Reload(unknown);
        Assert.Equal(JobStatus.Pending, stored.Status);
        Assert.NotNull(stored.LastError);
    }

    [Fact]
    public async Task Shutdown_hands_a_running_job_back_without_using_an_attempt()
    {
        TaskCompletionSource started = new();
        _handler.Run = async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        };
        Job job = await AddJob("test");

        await WhileWorkerRuns(() => started.Task);

        await WaitForStatus(job, JobStatus.Pending);
        Assert.Equal(0, (await Reload(job)).Attempts);
    }

    private async Task WhileWorkerRuns(Func<Task> test)
    {
        await using ServiceProvider services = BuildServices();
        IHostedService worker = new JobWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<IOptions<WorkerOptions>>(),
            services.GetRequiredService<ILogger<JobWorker>>());

        await worker.StartAsync(default);
        try
        {
            await test();
        }
        finally
        {
            await worker.StopAsync(default);
        }
    }

    private ServiceProvider BuildServices()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = postgres.ConnectionString,
                ["Worker:PollIntervalMs"] = "20",
                ["Worker:HeartbeatIntervalMs"] = "20",
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddShared(configuration);
        services.AddKeyedSingleton<IJobHandler>("test", _handler);
        return services.BuildServiceProvider();
    }

    private async Task<Job> AddJob(
        string type, JobStatus status = JobStatus.Pending, int attempts = 0, DateTimeOffset? lockedUntil = null)
    {
        Job job = new() { Type = type, Payload = "{}", Status = status, Attempts = attempts, LockedUntil = lockedUntil };
        await using AppDbContext db = postgres.CreateDbContext();
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    private async Task<Job> Reload(Job job)
    {
        await using AppDbContext db = postgres.CreateDbContext();
        return await db.Jobs.SingleAsync(j => j.Id == job.Id);
    }

    private Task WaitForStatus(Job job, JobStatus status) =>
        Wait.Until(async () => (await Reload(job)).Status == status);

    private class TestHandler : IJobHandler
    {
        public Func<ClaimedJob, CancellationToken, Task> Run { get; set; } = (_, _) => Task.CompletedTask;
        public List<long> Ran { get; } = [];
        public List<(long Id, string Error)> Failed { get; } = [];

        public Task RunAsync(ClaimedJob job, CancellationToken ct)
        {
            lock (Ran)
                Ran.Add(job.Id);
            return Run(job, ct);
        }

        public Task OnFailedAsync(ClaimedJob job, string error, CancellationToken ct)
        {
            lock (Failed)
                Failed.Add((job.Id, error));
            return Task.CompletedTask;
        }
    }
}
