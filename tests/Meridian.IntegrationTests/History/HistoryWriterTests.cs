using System.Text.Json.Nodes;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.History;
using Meridian.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Meridian.IntegrationTests.History;

public class HistoryWriterTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const int ActorId = 1;

    public async Task InitializeAsync()
    {
        await using AppDbContext db = postgres.CreateDbContext();
        if (!await db.Users.AnyAsync(u => u.Id == ActorId))
        {
            db.Users.Add(new User { Id = ActorId, Name = "Dana", Role = Role.Analyst });
            await db.SaveChangesAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Position_change_commits_with_its_history_row()
    {
        Vote vote = await CreateVote(await CreateImport());

        await using AppDbContext db = postgres.CreateDbContext();
        Vote tracked = await db.Votes.SingleAsync(v => v.Id == vote.Id);
        tracked.Position = "N";
        tracked.Weight = 4;
        new HistoryWriter(db).PositionChanged(vote.Id, (null, null), ("N", 4), ActorId);
        await db.SaveChangesAsync();

        VoteHistoryEntry entry = Assert.Single(await History(vote.Id));
        Assert.Equal(HistoryType.PositionChanged, entry.Type);
        Assert.Equal("position", entry.Target);
        Assert.Equal(ActorId, entry.ActorId);
        AssertJson("""{"position":null,"weight":null}""", entry.OldValue);
        AssertJson("""{"position":"N","weight":4}""", entry.NewValue);
        Assert.True(entry.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Rolled_back_change_leaves_no_history_row()
    {
        Vote vote = await CreateVote(await CreateImport());

        await using AppDbContext db = postgres.CreateDbContext();
        await using (IDbContextTransaction tx = await db.Database.BeginTransactionAsync())
        {
            Vote tracked = await db.Votes.SingleAsync(v => v.Id == vote.Id);
            tracked.Position = "Y";
            new HistoryWriter(db).PositionChanged(vote.Id, (null, null), ("Y", null), ActorId);
            await db.SaveChangesAsync();
            await tx.RollbackAsync();
        }

        await using AppDbContext check = postgres.CreateDbContext();
        Assert.Null((await check.Votes.SingleAsync(v => v.Id == vote.Id)).Position);
        Assert.Empty(await History(vote.Id));
    }

    [Theory]
    [InlineData("DEU", HistoryType.ValueChanged)]
    [InlineData("title", HistoryType.DetailsChanged)]
    [InlineData("vote_date", HistoryType.DetailsChanged)]
    [InlineData("session", HistoryType.DetailsChanged)]
    public async Task Accepted_conflict_records_the_decision_and_the_change(string target, HistoryType changeType)
    {
        Import import = await CreateImport();
        Vote vote = await CreateVote(import);
        Conflict conflict = await CreateConflict(vote, import, target);
        conflict.Status = ConflictStatus.Accepted;

        await using (AppDbContext db = postgres.CreateDbContext())
        {
            new HistoryWriter(db).ConflictDecided(conflict, ActorId);
            await db.SaveChangesAsync();
        }

        List<VoteHistoryEntry> entries = await History(vote.Id);
        Assert.Equivalent(new[] { HistoryType.ConflictAccepted, changeType }, entries.Select(e => e.Type));
        Assert.All(entries, entry =>
        {
            Assert.Equal(target, entry.Target);
            Assert.Equal(conflict.Id, entry.ConflictId);
            Assert.Equal(import.Id, entry.ImportId);
            AssertJson("\"Y\"", entry.OldValue);
            AssertJson("\"N\"", entry.NewValue);
        });
    }

    [Fact]
    public async Task Rejected_conflict_records_only_the_decision()
    {
        Import import = await CreateImport();
        Vote vote = await CreateVote(import);
        Conflict conflict = await CreateConflict(vote, import, "DEU");
        conflict.Status = ConflictStatus.Rejected;

        await using (AppDbContext db = postgres.CreateDbContext())
        {
            new HistoryWriter(db).ConflictDecided(conflict, ActorId);
            await db.SaveChangesAsync();
        }

        VoteHistoryEntry entry = Assert.Single(await History(vote.Id));
        Assert.Equal(HistoryType.ConflictRejected, entry.Type);
    }

    [Theory]
    [InlineData(ConflictStatus.Pending)]
    [InlineData(ConflictStatus.Superseded)]
    public async Task A_conflict_without_a_decision_is_refused(ConflictStatus status)
    {
        await using AppDbContext db = postgres.CreateDbContext();

        Assert.Throws<InvalidOperationException>(() =>
            new HistoryWriter(db).ConflictDecided(new Conflict { Status = status }, ActorId));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Failed_decision_records_who_tried_what()
    {
        Import import = await CreateImport();
        Vote vote = await CreateVote(import);
        Conflict conflict = await CreateConflict(vote, import, "DEU");

        await using (AppDbContext db = postgres.CreateDbContext())
        {
            new HistoryWriter(db).DecisionNotApplied(conflict, ConflictStatus.Rejected, ActorId);
            await db.SaveChangesAsync();
        }

        VoteHistoryEntry entry = Assert.Single(await History(vote.Id));
        Assert.Equal(HistoryType.DecisionNotApplied, entry.Type);
        Assert.Equal(conflict.Id, entry.ConflictId);
        Assert.Equal(ActorId, entry.ActorId);
        AssertJson("""{"attempted":"Rejected"}""", entry.NewValue);
    }

    [Fact]
    public async Task Bulk_vote_created_writes_one_row_per_listed_vote_with_its_details()
    {
        Import import = await CreateImport();
        Vote first = await CreateVote(import);
        Vote second = await CreateVote(import);
        Vote notListed = await CreateVote(import);

        await InTransaction(history =>
            history.VotesCreatedAsync([first.Id, second.Id], import.Id, ActorId, default));

        VoteHistoryEntry entry = Assert.Single(await History(first.Id));
        Assert.Equal(HistoryType.VoteCreated, entry.Type);
        Assert.Equal(import.Id, entry.ImportId);
        Assert.Equal(ActorId, entry.ActorId);
        AssertJson("""{"session":79,"voteDate":"2024-02-06","title":"Title"}""", entry.NewValue);
        Assert.Single(await History(second.Id));
        Assert.Empty(await History(notListed.Id));
    }

    [Fact]
    public async Task Bulk_ballots_imported_counts_this_imports_ballots_and_skips_votes_without_any()
    {
        Import earlier = await CreateImport();
        Import import = await CreateImport();
        Vote gained = await CreateVote(earlier);
        Vote unchanged = await CreateVote(earlier);
        await CreateCountryVote(gained, earlier, "DEU");
        await CreateCountryVote(gained, import, "FRA");
        await CreateCountryVote(gained, import, "ITA");
        await CreateCountryVote(unchanged, earlier, "DEU");

        await InTransaction(history =>
            history.BallotsImportedAsync([gained.Id, unchanged.Id], import.Id, ActorId, default));

        VoteHistoryEntry entry = Assert.Single(await History(gained.Id));
        Assert.Equal(HistoryType.BallotsImported, entry.Type);
        Assert.Equal(import.Id, entry.ImportId);
        AssertJson("""{"count":2}""", entry.NewValue);
        Assert.Empty(await History(unchanged.Id));
    }

    [Fact]
    public async Task Bulk_conflicts_opened_keeps_both_values_and_the_conflict()
    {
        Import import = await CreateImport();
        Vote vote = await CreateVote(import);
        Conflict ballot = await CreateConflict(vote, import, "DEU");
        Conflict details = await CreateConflict(vote, import, "title");
        Conflict notListed = await CreateConflict(vote, import, "FRA");

        await InTransaction(history =>
            history.ConflictsOpenedAsync([ballot.Id, details.Id], ActorId, default));

        List<VoteHistoryEntry> entries = await History(vote.Id);
        Assert.Equal(
            [ballot.Id, details.Id],
            entries.Select(e => e.ConflictId!.Value).Order());
        Assert.DoesNotContain(entries, e => e.ConflictId == notListed.Id);
        Assert.All(entries, entry =>
        {
            Assert.Equal(HistoryType.ConflictOpened, entry.Type);
            Assert.Equal(import.Id, entry.ImportId);
            AssertJson("\"Y\"", entry.OldValue);
            AssertJson("\"N\"", entry.NewValue);
        });
    }

    [Fact]
    public async Task Bulk_rows_roll_back_with_the_transaction()
    {
        Import import = await CreateImport();
        Vote vote = await CreateVote(import);

        await using AppDbContext db = postgres.CreateDbContext();
        await using (IDbContextTransaction tx = await db.Database.BeginTransactionAsync())
        {
            await new HistoryWriter(db).VotesCreatedAsync([vote.Id], import.Id, ActorId, default);
            await tx.RollbackAsync();
        }

        Assert.Empty(await History(vote.Id));
    }

    [Fact]
    public async Task Bulk_methods_refuse_to_run_outside_a_transaction()
    {
        Import import = await CreateImport();
        Vote vote = await CreateVote(import);

        await using AppDbContext db = postgres.CreateDbContext();
        HistoryWriter history = new(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            history.VotesCreatedAsync([vote.Id], import.Id, ActorId, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            history.BallotsImportedAsync([vote.Id], import.Id, ActorId, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            history.ConflictsOpenedAsync([1], ActorId, default));
        Assert.Empty(await History(vote.Id));
    }

    private static void AssertJson(string expected, string? actual) =>
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual!)),
            $"Expected {expected}, got {actual}");

    private async Task InTransaction(Func<HistoryWriter, Task> write)
    {
        await using AppDbContext db = postgres.CreateDbContext();
        await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync();
        await write(new HistoryWriter(db));
        await tx.CommitAsync();
    }

    private async Task<List<VoteHistoryEntry>> History(long voteId)
    {
        await using AppDbContext db = postgres.CreateDbContext();
        return await db.VoteHistory.Where(h => h.VoteId == voteId).ToListAsync();
    }

    private async Task<Import> CreateImport()
    {
        Guid id = Guid.NewGuid();
        Import import = new()
        {
            Id = id,
            FileName = "votes.csv",
            BlobKey = $"imports/{id}.csv",
            Sha256 = new string('a', 64),
            UploadedBy = ActorId,
            UploadedAt = DateTimeOffset.UtcNow,
        };
        await using AppDbContext db = postgres.CreateDbContext();
        db.Imports.Add(import);
        await db.SaveChangesAsync();
        return import;
    }

    private async Task<Vote> CreateVote(Import import)
    {
        Vote vote = new()
        {
            VoteRef = Guid.NewGuid().ToString(),
            Session = 79,
            VoteDate = new DateOnly(2024, 2, 6),
            Title = "Title",
            DetailsSourceImportId = import.Id,
        };
        await using AppDbContext db = postgres.CreateDbContext();
        db.Votes.Add(vote);
        await db.SaveChangesAsync();
        return vote;
    }

    private async Task CreateCountryVote(Vote vote, Import import, string countryCode)
    {
        await using AppDbContext db = postgres.CreateDbContext();
        db.CountryVotes.Add(new CountryVote
        {
            VoteId = vote.Id,
            CountryCode = countryCode,
            Ballot = 'Y',
            SourceImportId = import.Id,
            SourceLine = 2,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Conflict> CreateConflict(Vote vote, Import import, string target)
    {
        Conflict conflict = new()
        {
            VoteId = vote.Id,
            Target = target,
            ApprovedValue = "Y",
            ApprovedSourceImportId = import.Id,
            ApprovedSourceLine = 2,
            ProposedValue = "N",
            ProposedSourceImportId = import.Id,
            ProposedSourceLine = 3,
            PublishedAt = DateTimeOffset.UtcNow,
            Status = ConflictStatus.Pending,
        };
        await using AppDbContext db = postgres.CreateDbContext();
        db.Conflicts.Add(conflict);
        await db.SaveChangesAsync();
        return conflict;
    }
}
