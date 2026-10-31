using System.Net;
using Meridian.Api.Features.Conflicts;
using Meridian.Api.Features.Votes;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Paging;
using Meridian.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Meridian.IntegrationTests.Votes;

public class VoteReadTests(PostgresFixture postgres, AzuriteFixture azurite)
    : ApiTestBase(postgres, azurite), IClassFixture<PostgresFixture>, IClassFixture<AzuriteFixture>
{
    private const string Published = "2024-04-02T09:00:00Z";
    private const string Later = "2024-06-02T09:00:00Z";

    private readonly PostgresFixture _postgres = postgres;

    [Fact]
    public async Task The_list_filters_sorts_newest_first_and_shows_missing_positions_and_open_conflicts()
    {
        await RunFile(
            Row("MA/4000/001", "BRA", "Y", date: "2024-02-06", session: 79, title: "Fishing rights"),
            Row("MA/4000/002", "BRA", "Y", date: "2024-03-06", session: 79, title: "Water"),
            Row("MA/4000/003", "BRA", "Y", date: "2024-03-07", session: 80, title: "Water"));
        await RunFile(Row("MA/4000/002", "BRA", "N", date: "2024-03-06", session: 79, title: "Water", published: Later));

        PagedResult<VoteDto> all = await GetOk<PagedResult<VoteDto>>("/api/votes?q=MA/4000");
        Assert.Equal(["MA/4000/003", "MA/4000/002", "MA/4000/001"], all.Items.Select(v => v.VoteRef));
        Assert.All(all.Items, v => Assert.True(v.MissingPosition));
        Assert.Equal([0, 1, 0], all.Items.Select(v => v.OpenConflicts));

        Assert.Equal(3, (await GetOk<PagedResult<VoteDto>>("/api/votes?q=ma/4000&hasPosition=false")).Total);
        Assert.Equal(0, (await GetOk<PagedResult<VoteDto>>("/api/votes?q=ma/4000&hasPosition=true")).Total);
        Assert.Equal("MA/4000/001", (await GetOk<PagedResult<VoteDto>>("/api/votes?q=FISHING")).Items.Single().VoteRef);
        Assert.Equal("MA/4000/003", (await GetOk<PagedResult<VoteDto>>("/api/votes?session=80&q=MA/4000")).Items.Single().VoteRef);
        Assert.Equal(
            "MA/4000/002",
            (await GetOk<PagedResult<VoteDto>>("/api/votes?q=MA/4000&from=2024-03-01&to=2024-03-06")).Items.Single().VoteRef);
    }

    [Fact]
    public async Task A_vote_shows_its_ballots_with_their_source_and_flags_decided_ones()
    {
        await RunFile(Row("MA/4001/001", "BRA", "Y"), Row("MA/4001/001", "IND", "Y"));
        await RunFile(Row("MA/4001/001", "BRA", "A", published: Later));
        long id = await VoteId("MA/4001/001");
        long conflict = await PendingConflict(id);
        await Post($"/api/conflicts/{conflict}/decision", new DecideConflict.Request { Decision = Decision.Accept });

        VoteDetailDto vote = await GetOk<VoteDetailDto>($"/api/votes/{id}");

        Assert.Equal("MA/4001/001", vote.Vote.VoteRef);
        Assert.Equal(
            [("BRA", "Brazil", 'A', 2, true), ("IND", "India", 'Y', 3, false)],
            vote.CountryVotes.Select(c => (c.CountryCode, c.CountryName, c.Ballot, c.SourceLine, c.ChangedByDecision)));
    }

    [Fact]
    public async Task The_timeline_shows_what_happened_newest_first_with_actor_and_file()
    {
        await RunFile(Row("MA/4002/001", "BRA", "Y"));
        await RunFile(Row("MA/4002/001", "BRA", "A", published: Later));
        long id = await VoteId("MA/4002/001");
        long conflict = await PendingConflict(id);
        await Post($"/api/conflicts/{conflict}/decision", new DecideConflict.Request { Decision = Decision.Accept }, Omer);

        List<TimelineEntryDto> timeline = await GetOk<List<TimelineEntryDto>>($"/api/votes/{id}/timeline");

        Assert.Equal(
            ["ValueChanged", "ConflictAccepted", "ConflictOpened", "BallotsImported", "VoteCreated"],
            timeline.Select(t => t.Type));
        Assert.Equal(("Omer", "\"Y\"", "\"A\"", "votes.csv"), (timeline[0].ActorName, timeline[0].OldValue, timeline[0].NewValue, timeline[0].FileName));
    }

    [Theory]
    [InlineData(1_000_000, HttpStatusCode.OK)]
    [InlineData(1_000_001, HttpStatusCode.BadRequest)]
    [InlineData(int.MaxValue, HttpStatusCode.BadRequest)]
    public async Task Pages_stop_at_a_limit_that_cannot_overflow(int page, HttpStatusCode expected)
    {
        Assert.Equal(expected, (await Get($"/api/votes?page={page}&pageSize=100")).StatusCode);
    }

    [Fact]
    public async Task An_unknown_vote_gets_a_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Get("/api/votes/999999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Get("/api/votes/999999/timeline")).StatusCode);
    }

    private async Task<long> VoteId(string voteRef)
    {
        await using AppDbContext db = _postgres.CreateDbContext();
        return await db.Votes.Where(v => v.VoteRef == voteRef).Select(v => v.Id).SingleAsync();
    }

    private async Task<long> PendingConflict(long voteId)
    {
        await using AppDbContext db = _postgres.CreateDbContext();
        return await db.Conflicts.Where(c => c.VoteId == voteId).Select(c => c.Id).SingleAsync();
    }

    private static string Row(string voteRef, string country, string ballot, string title = "Title", int session = 79, string date = "2024-02-06", string published = Published)
    {
        return $"{voteRef},{session},{date},{title},{country},Name,{ballot},{published}";
    }
}
