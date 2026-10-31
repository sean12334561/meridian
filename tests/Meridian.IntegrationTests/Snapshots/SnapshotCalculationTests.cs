using System.Net;
using System.Text.Json;
using Meridian.Api.Features.Conflicts;
using Meridian.Api.Features.Snapshots;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Meridian.IntegrationTests.Snapshots;

public class SnapshotCalculationTests(PostgresFixture postgres, AzuriteFixture azurite)
    : SnapshotTestBase(postgres, azurite), IClassFixture<PostgresFixture>, IClassFixture<AzuriteFixture>
{
    private readonly PostgresFixture _postgres = postgres;

    [Fact]
    public async Task The_assignment_example_scores_66_7()
    {
        long a = await LoadVote("MA/6000/001", "Y", 4, ("BRA", "Y"));
        long b = await LoadVote("MA/6000/002", "Y", 2, ("BRA", "N"));

        long id = await Compute(a, b);

        SnapshotScore score = Assert.Single(await Scores(id));
        Assert.Equal((66.6667m, 2, 6m, 4m, (int?)1), (score.Score!.Value, score.CountedVotes, score.CountedWeight, score.EarnedWeight, score.Rank));
    }

    [Fact]
    public async Task An_abstention_earns_half_and_absence_is_not_counted()
    {
        long a = await LoadVote("MA/6001/001", "Y", 4, ("FRA", "A"));
        long b = await LoadVote("MA/6001/002", "Y", 1, ("FRA", "Y"));
        long c = await LoadVote("MA/6001/003", "N", 5, ("FRA", "X"));

        long id = await Compute(a, b, c);

        SnapshotScore score = Assert.Single(await Scores(id));
        Assert.Equal((60m, 2, 5m, 3m), (score.Score!.Value, score.CountedVotes, score.CountedWeight, score.EarnedWeight));
        Assert.Equal(
            [("A", true, 2m, "Abstained (half)"), ("Y", true, 1m, "Matched"), ("X", false, 0m, "Absent")],
            (await Contributions(id)).Select(x => (x.Ballot.ToString(), x.Counted, x.Points, x.Reason)));
    }

    [Fact]
    public async Task Ties_share_a_rank_and_countries_below_the_minimum_get_none()
    {
        long a = await LoadVote("MA/6002/001", "Y", 2, ("BRA", "Y"), ("IND", "Y"), ("JPN", "Y"), ("FRA", "N"), ("DEU", "X"));
        long b = await LoadVote("MA/6002/002", "Y", 2, ("BRA", "Y"), ("IND", "Y"), ("JPN", "N"), ("FRA", "N"), ("DEU", "X"));
        long c = await LoadVote("MA/6002/003", "Y", 2, ("BRA", "N"), ("IND", "N"), ("JPN", "N"), ("FRA", "N"), ("DEU", "X"));
        long d = await LoadVote("MA/6002/004", "Y", 2, ("ITA", "Y"));

        long id = await CreateSnapshot(new { name = "Minimum", voteIds = new[] { a, b, c, d }, minCountedVotes = 2 });
        await RunSnapshotJob(id);

        List<SnapshotScore> scores = await Scores(id);
        Assert.Equal(
            [("BRA", (int?)1), ("DEU", null), ("FRA", 4), ("IND", 1), ("ITA", null), ("JPN", 3)],
            scores.Select(s => (s.CountryCode, s.Rank)));
        Assert.Null(scores.Single(s => s.CountryCode == "ITA").Score);
        SnapshotScore absent = scores.Single(s => s.CountryCode == "DEU");
        Assert.Equal((0, 0m, null), (absent.CountedVotes, absent.CountedWeight, absent.Score));
    }

    [Fact]
    public async Task A_snapshot_never_changes_after_data_positions_or_decisions_change()
    {
        long vote = await LoadVote("MA/6003/001", "Y", 4, ("BRA", "Y"));
        long id = await Compute(vote);
        List<SnapshotScore> scores = await Scores(id);
        List<SnapshotContribution> contributions = await Contributions(id);

        await RunFile(Row("MA/6003/001", "BRA", "A", "2024-06-02T09:00:00Z"));
        await using (AppDbContext db = _postgres.CreateDbContext())
        {
            long conflict = await db.Conflicts.Where(c => c.VoteId == vote).Select(c => c.Id).SingleAsync();
            await Post($"/api/conflicts/{conflict}/decision", new DecideConflict.Request { Decision = Decision.Accept });
        }

        await Put($"/api/votes/{vote}/position", new { desiredOutcome = "N", weight = 1 });

        Assert.Equivalent(scores, await Scores(id));
        Assert.Equivalent(contributions, await Contributions(id));

        long newer = await Compute(vote);
        Assert.Equal((50m, 0.5m), ((await Scores(newer))[0].Score!.Value, (await Contributions(newer))[0].Points));
    }

    [Fact]
    public async Task The_snapshot_stores_its_rules_and_the_votes_with_pending_conflicts()
    {
        long vote = await LoadVote("MA/6004/001", "Y", 4, ("BRA", "Y"));
        await RunFile(Row("MA/6004/001", "BRA", "A", "2024-06-02T09:00:00Z"));

        long id = await Compute(vote);

        await using AppDbContext db = _postgres.CreateDbContext();
        Snapshot snapshot = await db.Snapshots.SingleAsync(s => s.Id == id);
        Assert.Equal((SnapshotStatus.Completed, (int?)1), (snapshot.Status, snapshot.PendingConflictsAtCreation));
        SnapshotRules rules = SnapshotJson.Read<SnapshotRules>(snapshot.Rules);
        Assert.Equal((0.5m, 1), (rules.AbstentionFactor, rules.MinCountedVotes));
        Assert.Equal([vote], SnapshotJson.Read<SnapshotFilter>(snapshot.Filter).VoteIds);
    }

    [Fact]
    public async Task Votes_without_a_position_block_the_snapshot_and_are_listed()
    {
        long set = await LoadVote("MA/6005/001", "Y", 4, ("BRA", "Y"));
        await RunFile(Row("MA/6005/002", "BRA", "Y"));
        long missing = await VoteId("MA/6005/002");

        HttpResponseMessage response = await Post("/api/snapshots", new { name = "Blocked", voteIds = new[] { set, missing } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("MA/6005/002", problem.RootElement.GetProperty("missingPositions")[0].GetProperty("voteRef").GetString());
    }

    [Fact]
    public async Task Excluded_votes_are_stored_and_left_out()
    {
        long set = await LoadVote("MA/6006/001", "Y", 4, ("BRA", "Y"));
        await RunFile(Row("MA/6006/002", "BRA", "Y"));
        long missing = await VoteId("MA/6006/002");

        long id = await CreateSnapshot(new { name = "Excluded", voteIds = new[] { set, missing }, excludeMissingPositions = true });
        await RunSnapshotJob(id);

        Assert.Equal(set, Assert.Single(await Contributions(id)).VoteId);
        await using AppDbContext db = _postgres.CreateDbContext();
        Assert.Equal([missing], SnapshotJson.Read<SnapshotRules>((await db.Snapshots.SingleAsync(s => s.Id == id)).Rules).ExcludedVotes);
    }

    [Fact]
    public async Task A_filter_and_a_list_add_up()
    {
        long a = await LoadVote("MA/6007/001", "Y", 1, ("BRA", "Y"));

        long id = await CreateSnapshot(new { name = "Both", from = "2024-02-06", to = "2024-02-06", voteIds = new[] { a } });

        await using AppDbContext db = _postgres.CreateDbContext();
        long[] selected = SnapshotJson.Read<SnapshotFilter>((await db.Snapshots.SingleAsync(s => s.Id == id)).Filter).VoteIds;
        Assert.Contains(a, selected);
    }

    [Theory]
    [InlineData("""{"name":"No selector"}""")]
    [InlineData("""{"name":"","voteIds":[1]}""")]
    [InlineData("""{"name":"Zero","voteIds":[1],"minCountedVotes":0}""")]
    [InlineData("""{"name":"Nothing matches","session":1}""")]
    [InlineData("""{"name":"Unknown","voteIds":[99999999]}""")]
    public async Task Bad_requests_get_a_400(string body)
    {
        HttpResponseMessage response = await Post("/api/snapshots", JsonSerializer.Deserialize<JsonElement>(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_failed_job_marks_the_snapshot_failed()
    {
        long vote = await LoadVote("MA/6008/001", "Y", 1, ("BRA", "Y"));
        long id = await CreateSnapshot(new { name = "Fails", voteIds = new[] { vote } });

        await new SnapshotJobHandler(new FixtureDbContextFactory(_postgres)).OnFailedAsync(SnapshotJob(id), "Broken", default);

        await using AppDbContext db = _postgres.CreateDbContext();
        Snapshot snapshot = await db.Snapshots.SingleAsync(s => s.Id == id);
        Assert.Equal((SnapshotStatus.Failed, "Broken"), (snapshot.Status, snapshot.Error));
    }
}
