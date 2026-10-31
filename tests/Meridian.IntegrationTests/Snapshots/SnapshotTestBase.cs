using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Api.Features.Snapshots;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Jobs;
using Meridian.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Meridian.IntegrationTests.Snapshots;

public abstract class SnapshotTestBase(PostgresFixture postgres, AzuriteFixture azurite) : ApiTestBase(postgres, azurite)
{
    protected const string Published = "2024-04-02T09:00:00Z";

    private readonly PostgresFixture _postgres = postgres;

    /// <summary>Loads one vote with the given ballots and sets its position.</summary>
    protected async Task<long> LoadVote(string voteRef, string position, int weight, params (string Country, string Ballot)[] ballots)
    {
        await RunFile(ballots.Select(b => Row(voteRef, b.Country, b.Ballot)).ToArray());
        long id = await VoteId(voteRef);
        (await Put($"/api/votes/{id}/position", new { desiredOutcome = position, weight })).EnsureSuccessStatusCode();
        return id;
    }

    protected async Task<long> VoteId(string voteRef)
    {
        await using AppDbContext db = _postgres.CreateDbContext();
        return await db.Votes.Where(v => v.VoteRef == voteRef).Select(v => v.Id).SingleAsync();
    }

    protected static string Row(string voteRef, string country, string ballot, string published = Published)
    {
        return $"{voteRef},79,2024-02-06,Title,{country},Name,{ballot},{published}";
    }

    protected async Task<long> CreateSnapshot(object request)
    {
        HttpResponseMessage response = await Post("/api/snapshots", request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SnapshotCreatedDto>(Json))!.SnapshotId;
    }

    /// <summary>Runs the snapshot's job directly, without a worker.</summary>
    protected async Task<long> Compute(params long[] voteIds)
    {
        long id = await CreateSnapshot(new { name = "Test", voteIds });
        await RunSnapshotJob(id);
        return id;
    }

    protected Task RunSnapshotJob(long id)
    {
        SnapshotJobHandler handler = new(new FixtureDbContextFactory(_postgres));
        return handler.RunAsync(SnapshotJob(id), default);
    }

    protected static ClaimedJob SnapshotJob(long id)
    {
        return new ClaimedJob
        {
            Id = 1,
            Type = SnapshotJobPayload.JobType,
            Payload = JsonSerializer.Serialize(new { snapshotId = id, correlationId = "test" }),
            Attempts = 1,
            CorrelationId = null,
        };
    }

    protected async Task<List<SnapshotScore>> Scores(long id)
    {
        await using AppDbContext db = _postgres.CreateDbContext();
        return await db.SnapshotScores.Where(s => s.SnapshotId == id).OrderBy(s => s.CountryCode).ToListAsync();
    }

    protected async Task<List<SnapshotContribution>> Contributions(long id)
    {
        await using AppDbContext db = _postgres.CreateDbContext();
        return await db.SnapshotContributions.Where(c => c.SnapshotId == id).OrderBy(c => c.CountryCode).ThenBy(c => c.VoteId).ToListAsync();
    }
}
