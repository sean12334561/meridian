using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Meridian.IntegrationTests.Imports;

/// <summary>The assignment's section 7.2 example, end to end. Votes are numbered per test, as they stay approved.</summary>
public class ImportScenarioTests(PostgresFixture postgres, AzuriteFixture azurite)
    : ImportPipelineTestBase(postgres, azurite), IClassFixture<PostgresFixture>, IClassFixture<AzuriteFixture>
{
    private const string Published = "2024-04-02T09:00:00Z";
    private const string Corrected = "2024-05-02T09:00:00Z";

    private static string[] Original(int v)
    {
        return
        [
            Row(v, 1, "BRA", "Y"),
            Row(v, 1, "IND", "Y"),
            Row(v, 2, "JPN", "Y"),
            Row(v, 2, "JPN", "Y"),
            Row(v, 3, "FRA", "Y"),
            Row(v, 3, "FRA", "N"),
            Row(v, 4, "FRN", "X"),
            Row(v, 4, "NZL", "Y"),
        ];
    }

    private static string[] Revised(int v)
    {
        return
        [
            Row(v, 1, "BRA", "A", title: "Corrected title", published: Corrected),
            Row(v, 1, "IND", "Y", title: "Corrected title", published: Corrected),
            Row(v, 3, "FRA", "N", published: Corrected),
            Row(v, 4, "FRA", "X", published: Corrected),
            Row(v, 5, "BRA", "Y", published: Corrected),
        ];
    }

    [Fact]
    public async Task The_original_file_gets_the_outcomes_of_the_assignment_table()
    {
        Import import = await RunFile(Original(100));

        Assert.Equal(
            [
                RowOutcome.New, RowOutcome.New, RowOutcome.New, RowOutcome.Duplicate,
                RowOutcome.Contradiction, RowOutcome.Contradiction, RowOutcome.Invalid, RowOutcome.Invalid,
            ],
            (await Rows(import)).Select(r => r.Outcome!.Value));

        Import stored = await Reload(import);
        Assert.Equal((ImportStatus.Completed, ImportPhase.Done, 8), (stored.Status, stored.Phase, stored.ProcessedRows));

        await using AppDbContext db = postgres.CreateDbContext();
        Assert.Equal(
            [(Ref(100, 1), "BRA", 'Y'), (Ref(100, 1), "IND", 'Y'), (Ref(100, 2), "JPN", 'Y')],
            (await Ballots(db, 100)).Select(b => (b.VoteRef, b.CountryCode, b.Ballot)));
        Assert.Equal(2, await db.Votes.CountAsync(v => v.VoteRef.StartsWith("MA/2100/")));

        long vote = await VoteId(db, 100, 1);
        CountryVote bra = await db.CountryVotes.SingleAsync(cv => cv.VoteId == vote && cv.CountryCode == "BRA");
        Assert.Equal((import.Id, 2), (bra.SourceImportId, bra.SourceLine));
        Assert.Equal(
            [(HistoryType.BallotsImported, """{"count": 2}"""), (HistoryType.VoteCreated, null)],
            (await db.VoteHistory.Where(h => h.VoteId == vote).ToListAsync())
            .OrderBy(h => h.Type.ToString())
            .Select(h => (h.Type, h.Type == HistoryType.VoteCreated ? null : h.NewValue)));
    }

    [Fact]
    public async Task The_revised_file_opens_conflicts_and_never_changes_approved_data()
    {
        Import original = await RunFile(Original(200));

        Import revised = await RunFile(Revised(200));

        Assert.Equal(
            [
                (RowOutcome.Conflict, null), (RowOutcome.Unchanged, null), (RowOutcome.New, null),
                (RowOutcome.New, null), (RowOutcome.New, null),
            ],
            (await Rows(revised)).Select(r => (r.Outcome!.Value, r.Reason)));

        await using AppDbContext db = postgres.CreateDbContext();
        long vote = await VoteId(db, 200, 1);
        Vote approved = await db.Votes.SingleAsync(v => v.Id == vote);
        Assert.Equal("Title", approved.Title);
        Assert.Equal('Y', (await db.CountryVotes.SingleAsync(cv => cv.VoteId == vote && cv.CountryCode == "BRA")).Ballot);
        Assert.Equal(1, await db.CountryVotes.CountAsync(cv => cv.VoteId == vote && cv.CountryCode == "BRA"));

        Conflict ballot = await db.Conflicts.SingleAsync(c => c.VoteId == vote && c.Target == "BRA");
        Assert.Equal(
            ("Y", original.Id, (int?)2, "A", revised.Id, 2, ConflictStatus.Pending),
            (ballot.ApprovedValue, ballot.ApprovedSourceImportId, ballot.ApprovedSourceLine,
             ballot.ProposedValue, ballot.ProposedSourceImportId, ballot.ProposedSourceLine, ballot.Status));

        Conflict title = await db.Conflicts.SingleAsync(c => c.VoteId == vote && c.Target == "title");
        Assert.Equal(
            ("Title", original.Id, (int?)null, "Corrected title", revised.Id),
            (title.ApprovedValue, title.ApprovedSourceImportId, title.ApprovedSourceLine, title.ProposedValue, title.ProposedSourceImportId));

        Assert.Equal(
            (await Rows(revised))[0].ConflictId,
            ballot.Id);
        Assert.Equal(2, await db.VoteHistory.CountAsync(h => h.VoteId == vote && h.Type == HistoryType.ConflictOpened));
        Assert.Equal(1, await db.Votes.CountAsync(v => v.VoteRef == Ref(200, 2)));
    }

    [Fact]
    public async Task Uploading_the_revised_file_again_changes_nothing()
    {
        await RunFile(Original(300));
        await RunFile(Revised(300));
        (int votes, int ballots, int conflicts, int history) before = await Counts();

        Import again = await RunFile(Revised(300));

        Assert.Equal(before, await Counts());
        List<ImportRow> rows = await Rows(again);
        Assert.Equal(
            [
                (RowOutcome.Conflict, "Already pending"), (RowOutcome.Unchanged, null), (RowOutcome.Unchanged, null),
                (RowOutcome.Unchanged, null), (RowOutcome.Unchanged, null),
            ],
            rows.Select(r => (r.Outcome!.Value, r.Reason)));
        Assert.NotNull(rows[0].ConflictId);
    }

    [Fact]
    public async Task A_vote_can_arrive_in_parts()
    {
        await RunFile(Row(400, 1, "BRA", "Y"));

        Import second = await RunFile(Row(400, 1, "IND", "N"));

        Assert.Equal(RowOutcome.New, (await Rows(second)).Single().Outcome);
        await using AppDbContext db = postgres.CreateDbContext();
        Assert.Equal(2, (await Ballots(db, 400)).Count);
        Assert.Equal(2, await db.VoteHistory.CountAsync(
            h => h.VoteId == db.Votes.Single(v => v.VoteRef == Ref(400, 1)).Id && h.Type == HistoryType.BallotsImported));
    }

    [Fact]
    public async Task Differing_details_inside_a_file_open_no_conflict_on_an_existing_vote()
    {
        await RunFile(Row(500, 1, "BRA", "Y"));

        Import import = await RunFile(Row(500, 1, "BRA", "Y"), Row(500, 1, "IND", "N", title: "Other"));

        Assert.Equal([RowOutcome.Unchanged, RowOutcome.New], (await Rows(import)).Select(r => r.Outcome!.Value));
        await using AppDbContext db = postgres.CreateDbContext();
        long vote = await VoteId(db, 500, 1);
        Assert.False(await db.Conflicts.AnyAsync(c => c.VoteId == vote));
    }

    [Theory]
    [InlineData(610, "title", "Corrected", 79, "2024-02-06")]
    [InlineData(620, "session", "Title", 80, "2024-02-06")]
    [InlineData(630, "vote_date", "Title", 79, "2024-02-07")]
    public async Task A_changed_detail_opens_one_conflict_for_that_field(int v, string target, string title, int session, string date)
    {
        await RunFile(Row(v, 1, "BRA", "Y"));

        await RunFile(Row(v, 1, "BRA", "Y", title: title, session: session, date: date));

        await using AppDbContext db = postgres.CreateDbContext();
        long vote = await VoteId(db, v, 1);
        Assert.Equal(target, (await db.Conflicts.SingleAsync(c => c.VoteId == vote)).Target);
    }

    [Fact]
    public async Task A_retry_after_a_crash_between_batches_finishes_the_rest()
    {
        // 501 votes make two batches
        string[] rows = Enumerable.Range(1, 501).Select(n => Row(700, n, "BRA", "Y")).ToArray();
        Import import = await CreateImport($"{Header}\n{string.Join('\n', rows)}\n");
        await RunChecks(import);
        await using AppDbContext db = postgres.CreateDbContext();
        await db.ImportRows.Where(r => r.ImportId == import.Id && r.LineNo == 2)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Outcome, RowOutcome.New));

        await Run(await Reload(import));

        Assert.Equal(500, await db.CountryVotes.CountAsync(cv => cv.SourceImportId == import.Id));
        Assert.Equal(501, (await Reload(import)).ProcessedRows);
        Assert.Equal(ImportStatus.Completed, (await Reload(import)).Status);
    }

    private static string Ref(int v, int n)
    {
        return $"MA/2{v}/{n:D3}";
    }

    private static string Row(int v, int n, string country, string ballot, string title = "Title", int session = 79, string date = "2024-02-06", string published = Published)
    {
        return $"{Ref(v, n)},{session},{date},{title},{country},Name,{ballot},{published}";
    }

    private async Task<Import> RunFile(params string[] rows)
    {
        Import import = await CreateImport($"{Header}\n{string.Join('\n', rows)}\n");
        await Run(import);
        return import;
    }

    private async Task<(int, int, int, int)> Counts()
    {
        await using AppDbContext db = postgres.CreateDbContext();
        return (await db.Votes.CountAsync(), await db.CountryVotes.CountAsync(), await db.Conflicts.CountAsync(), await db.VoteHistory.CountAsync());
    }

    private static async Task<long> VoteId(AppDbContext db, int v, int n)
    {
        string voteRef = Ref(v, n);
        return await db.Votes.Where(x => x.VoteRef == voteRef).Select(x => x.Id).SingleAsync();
    }

    private static Task<List<BallotRow>> Ballots(AppDbContext db, int v)
    {
        return db.CountryVotes
            .Join(db.Votes, cv => cv.VoteId, x => x.Id, (cv, x) => new BallotRow { VoteRef = x.VoteRef, CountryCode = cv.CountryCode, Ballot = cv.Ballot })
            .Where(b => b.VoteRef.StartsWith($"MA/2{v}/"))
            .OrderBy(b => b.VoteRef).ThenBy(b => b.CountryCode)
            .ToListAsync();
    }

    private class BallotRow
    {
        public required string VoteRef { get; init; }
        public required string CountryCode { get; init; }
        public required char Ballot { get; init; }
    }
}
