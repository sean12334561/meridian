using System.Net;
using Meridian.Api.Features.Snapshots;
using Meridian.Api.Shared.Data.Entities;
using Meridian.IntegrationTests.Infrastructure;

namespace Meridian.IntegrationTests.Snapshots;

public class SnapshotReadTests(PostgresFixture postgres, AzuriteFixture azurite)
    : SnapshotTestBase(postgres, azurite), IClassFixture<PostgresFixture>, IClassFixture<AzuriteFixture>
{
    [Fact]
    public async Task A_snapshot_shows_its_rules_and_ranking_with_unranked_countries_last()
    {
        long a = await LoadVote("MA/6100/001", "Y", 4, ("BRA", "Y"), ("IND", "N"), ("FRA", "X"));
        long b = await LoadVote("MA/6100/002", "Y", 2, ("BRA", "N"), ("IND", "Y"), ("FRA", "X"));

        long id = await Compute(a, b);

        SnapshotDetailDto snapshot = await GetOk<SnapshotDetailDto>($"/api/snapshots/{id}");
        Assert.Equal((SnapshotStatus.Completed, "Dana", 2), (snapshot.Snapshot.Status, snapshot.Snapshot.CreatedByName, snapshot.VoteCount));
        Assert.Equal((0.5m, 1), (snapshot.Rules.AbstentionFactor, snapshot.Rules.MinCountedVotes));
        Assert.Equal(
            [("BRA", "Brazil", (int?)1), ("IND", "India", 2), ("FRA", "France", null)],
            snapshot.Ranking.Select(r => (r.CountryCode, r.CountryName, r.Rank)));
    }

    [Fact]
    public async Task A_pending_snapshot_is_readable_with_an_empty_ranking()
    {
        long vote = await LoadVote("MA/6101/001", "Y", 4, ("BRA", "Y"));

        long id = await CreateSnapshot(new { name = "Waiting", voteIds = new[] { vote } });

        SnapshotDetailDto snapshot = await GetOk<SnapshotDetailDto>($"/api/snapshots/{id}");
        Assert.Equal(SnapshotStatus.Pending, snapshot.Snapshot.Status);
        Assert.Empty(snapshot.Ranking);
        Assert.Contains(await GetOk<List<SnapshotSummaryDto>>("/api/snapshots"), s => s.Id == id);
    }

    [Fact]
    public async Task The_breakdown_lists_every_vote_for_the_country()
    {
        long a = await LoadVote("MA/6102/001", "Y", 4, ("BRA", "Y"));
        long b = await LoadVote("MA/6102/002", "Y", 2, ("BRA", "A"));
        long id = await Compute(a, b);

        CountryBreakdownDto breakdown = await GetOk<CountryBreakdownDto>($"/api/snapshots/{id}/countries/bra");

        Assert.Equal((83.3333m, 2), (breakdown.Score.Score!.Value, breakdown.Score.CountedVotes));
        Assert.Equal(
            [("MA/6102/001", 'Y', "Y", 4, 4m, "Matched"), ("MA/6102/002", 'A', "Y", 2, 1m, "Abstained (half)")],
            breakdown.Contributions.Select(c => (c.VoteRef, c.Ballot, c.Position, c.Weight, c.Points, c.Reason)));
        Assert.Equal("votes.csv", breakdown.Contributions[0].SourceFileName);
    }

    [Fact]
    public async Task Unknown_snapshots_and_countries_get_a_404()
    {
        long vote = await LoadVote("MA/6103/001", "Y", 4, ("BRA", "Y"));
        long id = await Compute(vote);

        Assert.Equal(HttpStatusCode.NotFound, (await Get("/api/snapshots/999999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Get($"/api/snapshots/{id}/countries/FRA")).StatusCode);
    }
}
