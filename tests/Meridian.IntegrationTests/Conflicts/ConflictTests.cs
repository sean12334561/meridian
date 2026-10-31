using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Api.Features.Conflicts;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Paging;
using Meridian.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Meridian.IntegrationTests.Conflicts;

/// <summary>Each test uses its own vote year, as approved data stays between tests.</summary>
public class ConflictTests(PostgresFixture postgres, AzuriteFixture azurite)
    : ApiTestBase(postgres, azurite), IClassFixture<PostgresFixture>, IClassFixture<AzuriteFixture>
{
    private const string Published = "2024-04-02T09:00:00Z";
    private const string Later = "2024-06-02T09:00:00Z";

    private readonly PostgresFixture _postgres = postgres;

    [Fact]
    public async Task The_inbox_lists_pending_conflicts_and_filters_by_vote()
    {
        ConflictDto conflict = await OpenBallotConflict("MA/3000/001");
        await OpenBallotConflict("MA/3000/002");

        PagedResult<ConflictDto> filtered = await GetOk<PagedResult<ConflictDto>>($"/api/conflicts?voteId={conflict.VoteId}");

        Assert.Equal(conflict.Id, Assert.Single(filtered.Items).Id);
        Assert.Equal(1, filtered.Total);
    }

    [Fact]
    public async Task A_conflict_shows_both_values_and_their_sources()
    {
        Import original = await RunFile(Row("MA/3001/001", "BRA", "Y"));
        Import revised = await RunFile(Row("MA/3001/001", "BRA", "A", published: Later));

        ConflictDto conflict = await SingleConflict("MA/3001/001");

        Assert.Equal(("Y", "A", "BRA"), (conflict.ApprovedValue, conflict.ProposedValue, conflict.Target));
        Assert.Equal(
            (original.Id, 2, "Dana", DateTimeOffset.Parse(Published)),
            (conflict.ApprovedSource.ImportId, conflict.ApprovedSource.Line!.Value, conflict.ApprovedSource.UploadedByName, conflict.ApprovedSource.PublishedAt!.Value));
        Assert.Equal(
            (revised.Id, 2, DateTimeOffset.Parse(Later)),
            (conflict.ProposedSource.ImportId, conflict.ProposedSource.Line!.Value, conflict.ProposedSource.PublishedAt!.Value));
    }

    [Fact]
    public async Task Accepting_a_ballot_changes_the_approved_value_and_records_who_and_what()
    {
        ConflictDto conflict = await OpenBallotConflict("MA/3002/001");

        HttpResponseMessage response = await Decide(conflict.Id, Decision.Accept, Dana, "Corrected by the body");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ConflictDto decided = (await response.Content.ReadFromJsonAsync<ConflictDto>(Json))!;
        Assert.Equal((ConflictStatus.Accepted, "Dana", "Corrected by the body"), (decided.Status, decided.DecidedByName, decided.Note));

        await using AppDbContext db = _postgres.CreateDbContext();
        CountryVote ballot = await db.CountryVotes.SingleAsync(cv => cv.VoteId == conflict.VoteId && cv.CountryCode == "BRA");
        Assert.Equal(('A', conflict.ProposedSource.ImportId, 2), (ballot.Ballot, ballot.SourceImportId, ballot.SourceLine));

        List<VoteHistoryEntry> history = await db.VoteHistory.Where(h => h.ConflictId == conflict.Id).OrderBy(h => h.Id).ToListAsync();
        Assert.Equal(
            [HistoryType.ConflictOpened, HistoryType.ConflictAccepted, HistoryType.ValueChanged],
            history.Select(h => h.Type));
        Assert.Equal(("\"Y\"", "\"A\"", Dana), (history[2].OldValue, history[2].NewValue, history[2].ActorId));
    }

    [Theory]
    [InlineData("MA/3003/001", "title", "Corrected", 79, "2024-02-06")]
    [InlineData("MA/3003/002", "session", "Title", 80, "2024-02-06")]
    [InlineData("MA/3003/003", "vote_date", "Title", 79, "2024-02-07")]
    public async Task Accepting_a_detail_changes_the_vote(string voteRef, string target, string title, int session, string date)
    {
        await RunFile(Row(voteRef, "BRA", "Y"));
        Import revised = await RunFile(Row(voteRef, "BRA", "Y", title, session, date));
        ConflictDto conflict = await SingleConflict(voteRef);
        Assert.Equal(target, conflict.Target);

        Assert.Equal(HttpStatusCode.OK, (await Decide(conflict.Id, Decision.Accept)).StatusCode);

        await using AppDbContext db = _postgres.CreateDbContext();
        Vote vote = await db.Votes.SingleAsync(v => v.Id == conflict.VoteId);
        Assert.Equal(
            (title, session, DateOnly.Parse(date), revised.Id),
            (vote.Title, vote.Session, vote.VoteDate, vote.DetailsSourceImportId));
        Assert.True(await db.VoteHistory.AnyAsync(h => h.ConflictId == conflict.Id && h.Type == HistoryType.DetailsChanged));
    }

    [Fact]
    public async Task Rejecting_keeps_the_approved_value()
    {
        ConflictDto conflict = await OpenBallotConflict("MA/3004/001");

        await Decide(conflict.Id, Decision.Reject);

        await using AppDbContext db = _postgres.CreateDbContext();
        Assert.Equal('Y', (await db.CountryVotes.SingleAsync(cv => cv.VoteId == conflict.VoteId)).Ballot);
        Assert.Equal(ConflictStatus.Rejected, (await db.Conflicts.SingleAsync(c => c.Id == conflict.Id)).Status);
        Assert.False(await db.VoteHistory.AnyAsync(h => h.ConflictId == conflict.Id && h.Type == HistoryType.ValueChanged));
    }

    [Fact]
    public async Task Accepting_one_proposal_supersedes_the_others_for_that_target()
    {
        ConflictDto first = await OpenBallotConflict("MA/3005/001");
        await RunFile(Row("MA/3005/001", "BRA", "N", published: Later));
        await RunFile(Row("MA/3005/001", "IND", "Y"));
        await RunFile(Row("MA/3005/001", "IND", "N", published: Later));

        await Decide(first.Id, Decision.Accept);

        await using AppDbContext db = _postgres.CreateDbContext();
        Conflict other = await db.Conflicts.SingleAsync(c => c.VoteId == first.VoteId && c.Target == "BRA" && c.ProposedValue == "N");
        Assert.Equal((ConflictStatus.Superseded, (int?)Dana), (other.Status, other.DecidedBy));
        Assert.True(await db.VoteHistory.AnyAsync(h => h.ConflictId == other.Id && h.Type == HistoryType.ConflictSuperseded));
        Assert.Equal(ConflictStatus.Pending, (await db.Conflicts.SingleAsync(c => c.VoteId == first.VoteId && c.Target == "IND")).Status);
    }

    [Fact]
    public async Task Two_decisions_at_once_give_one_winner_and_one_409_with_the_current_state()
    {
        ConflictDto conflict = await OpenBallotConflict("MA/3006/001");

        HttpResponseMessage[] responses = await Task.WhenAll(
            Decide(conflict.Id, Decision.Accept, Dana), Decide(conflict.Id, Decision.Reject, Omer));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());

        HttpResponseMessage loser = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        using JsonDocument problem = JsonDocument.Parse(await loser.Content.ReadAsStringAsync());
        Assert.Equal("/problems/conflict-already-decided", problem.RootElement.GetProperty("type").GetString());
        JsonElement current = problem.RootElement.GetProperty("current");
        Assert.NotEqual("Pending", current.GetProperty("status").GetString());
        Assert.NotNull(current.GetProperty("decidedByName").GetString());

        await using AppDbContext db = _postgres.CreateDbContext();
        Assert.Equal(1, await db.VoteHistory.CountAsync(h => h.ConflictId == conflict.Id && h.Type == HistoryType.DecisionNotApplied));
        Assert.Equal(1, await db.VoteHistory.CountAsync(
            h => h.ConflictId == conflict.Id && (h.Type == HistoryType.ConflictAccepted || h.Type == HistoryType.ConflictRejected)));
    }

    [Fact]
    public async Task A_rejected_value_is_previously_decided_until_a_newer_publication()
    {
        ConflictDto conflict = await OpenBallotConflict("MA/3007/001");
        await Decide(conflict.Id, Decision.Reject);

        Import same = await RunFile(Row("MA/3007/001", "BRA", "A", published: Later));
        Import older = await RunFile(Row("MA/3007/001", "BRA", "A", published: Published));
        Import newer = await RunFile(Row("MA/3007/001", "BRA", "A", published: "2024-07-02T09:00:00Z"));

        Assert.Equal(RowOutcome.PreviouslyDecided, (await Rows(same)).Single().Outcome);
        Assert.Equal(RowOutcome.PreviouslyDecided, (await Rows(older)).Single().Outcome);
        Assert.Equal(RowOutcome.Conflict, (await Rows(newer)).Single().Outcome);
    }

    [Fact]
    public async Task A_value_replaced_by_a_decision_is_previously_decided()
    {
        ConflictDto conflict = await OpenBallotConflict("MA/3008/001");
        await Decide(conflict.Id, Decision.Accept);

        Import reupload = await RunFile(Row("MA/3008/001", "BRA", "Y"));
        Import revised = await RunFile(Row("MA/3008/001", "BRA", "A", published: Later));

        Assert.Equal(RowOutcome.PreviouslyDecided, (await Rows(reupload)).Single().Outcome);
        Assert.Equal(RowOutcome.Unchanged, (await Rows(revised)).Single().Outcome);
    }

    [Fact]
    public async Task A_rejected_detail_is_not_reopened_by_a_reupload()
    {
        await RunFile(Row("MA/3009/001", "BRA", "Y"));
        await RunFile(Row("MA/3009/001", "BRA", "Y", title: "Other", published: Later));
        ConflictDto conflict = await SingleConflict("MA/3009/001");
        await Decide(conflict.Id, Decision.Reject);

        await RunFile(Row("MA/3009/001", "BRA", "Y", title: "Other", published: Later));

        await using AppDbContext db = _postgres.CreateDbContext();
        Assert.Equal(1, await db.Conflicts.CountAsync(c => c.VoteId == conflict.VoteId));
    }

    [Fact]
    public async Task A_superseded_value_is_not_reopened_by_a_reupload()
    {
        ConflictDto first = await OpenBallotConflict("MA/3010/001");
        string other = Row("MA/3010/001", "BRA", "N", published: Later);
        await RunFile(other);
        await Decide(first.Id, Decision.Accept);

        Import reupload = await RunFile(other);

        Assert.Equal(RowOutcome.PreviouslyDecided, (await Rows(reupload)).Single().Outcome);
    }

    [Fact]
    public async Task A_bad_decision_gets_field_errors()
    {
        ConflictDto conflict = await OpenBallotConflict("MA/3011/001");

        HttpResponseMessage response = await Post($"/api/conflicts/{conflict.Id}/decision", new { note = new string('x', 1001) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        HttpValidationProblemDetails problem = (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>())!;
        Assert.True(problem.Errors.ContainsKey("decision"));
        Assert.True(problem.Errors.ContainsKey("note"));
    }

    [Fact]
    public async Task An_unknown_conflict_gets_a_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Get("/api/conflicts/999999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Decide(999999, Decision.Accept)).StatusCode);
    }

    private async Task<ConflictDto> OpenBallotConflict(string voteRef)
    {
        await RunFile(Row(voteRef, "BRA", "Y"));
        await RunFile(Row(voteRef, "BRA", "A", published: Later));
        return await SingleConflict(voteRef);
    }

    private async Task<ConflictDto> SingleConflict(string voteRef)
    {
        await using AppDbContext db = _postgres.CreateDbContext();
        long voteId = await db.Votes.Where(v => v.VoteRef == voteRef).Select(v => v.Id).SingleAsync();
        PagedResult<ConflictDto> conflicts = await GetOk<PagedResult<ConflictDto>>($"/api/conflicts?voteId={voteId}");
        return Assert.Single(conflicts.Items);
    }

    private Task<HttpResponseMessage> Decide(long id, Decision decision, int user = Dana, string? note = null)
    {
        return Post($"/api/conflicts/{id}/decision", new DecideConflict.Request { Decision = decision, Note = note }, user);
    }

    private static string Row(string voteRef, string country, string ballot, string title = "Title", int session = 79, string date = "2024-02-06", string published = Published)
    {
        return $"{voteRef},{session},{date},{title},{country},Name,{ballot},{published}";
    }
}
