using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Meridian.IntegrationTests.Data;

public class SchemaTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const int ActorId = 1;
    private const string CheckViolation = "23514";
    private const string UniqueViolation = "23505";

    [Fact]
    public async Task Migration_creates_the_13_design_tables()
    {
        await using AppDbContext db = postgres.CreateDbContext();

        List<string> tables = await db.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value" FROM information_schema.tables
            WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory'
            """).ToListAsync();

        Assert.Equal(
            ["conflicts", "country_votes", "import_rows", "imports", "jobs", "member_states",
             "snapshot_contributions", "snapshot_publications", "snapshot_scores", "snapshots",
             "users", "vote_history", "votes"],
            tables.Order());
    }

    [Fact]
    public async Task Migrations_match_the_model()
    {
        await using AppDbContext db = postgres.CreateDbContext();

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task No_foreign_key_cascades()
    {
        await using AppDbContext db = postgres.CreateDbContext();

        List<string> cascading = await db.Database.SqlQuery<string>($"""
            SELECT conname AS "Value" FROM pg_constraint
            WHERE contype = 'f' AND confdeltype <> 'r'
            """).ToListAsync();

        Assert.Empty(cascading);
    }

    [Fact]
    public async Task Only_the_design_indexes_exist()
    {
        await using AppDbContext db = postgres.CreateDbContext();

        List<string> indexes = await db.Database.SqlQuery<string>($"""
            SELECT indexname AS "Value" FROM pg_indexes
            WHERE schemaname = 'public' AND indexname NOT LIKE 'pk\_%' AND indexname NOT LIKE 'PK\_%'
            """).ToListAsync();

        Assert.Equal(
            ["ix_conflicts_vote_id_target_proposed_value", "ix_country_votes_vote_id_country_code",
             "ix_import_rows_import_id_vote_ref", "ix_snapshot_publications_quarter", "ix_vote_history_vote_id", "ix_votes_vote_ref"],
            indexes.Order());
    }

    [Fact]
    public async Task Enums_are_stored_as_text()
    {
        (Vote vote, Import import) = await AddVote();
        await using AppDbContext db = postgres.CreateDbContext();
        db.Conflicts.Add(NewConflict(vote, import, proposed: "N"));
        await db.SaveChangesAsync();

        List<string> statuses = await db.Database.SqlQuery<string>(
            $"""SELECT status AS "Value" FROM conflicts WHERE vote_id = {vote.Id}""").ToListAsync();

        Assert.Equal(["Pending"], statuses);
    }

    [Theory]
    [InlineData('Y')]
    [InlineData('N')]
    [InlineData('A')]
    [InlineData('X')]
    public async Task Valid_ballots_are_accepted(char ballot)
    {
        (Vote vote, Import import) = await AddVote();
        await using AppDbContext db = postgres.CreateDbContext();
        db.CountryVotes.Add(NewBallot(vote, import, ballot));

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Unknown_ballot_is_rejected()
    {
        (Vote vote, Import import) = await AddVote();
        await using AppDbContext db = postgres.CreateDbContext();
        db.CountryVotes.Add(NewBallot(vote, import, 'Q'));

        await AssertViolation(db, CheckViolation, "ck_ballot");
    }

    [Fact]
    public async Task Second_ballot_for_the_same_vote_and_country_is_rejected()
    {
        (Vote vote, Import import) = await AddVote();
        await using AppDbContext db = postgres.CreateDbContext();
        db.CountryVotes.AddRange(NewBallot(vote, import, 'Y'), NewBallot(vote, import, 'N'));

        await AssertViolation(db, UniqueViolation, "ix_country_votes_vote_id_country_code");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task Weight_outside_1_to_5_is_rejected(int weight)
    {
        (Vote vote, _) = await AddVote();
        await using AppDbContext db = postgres.CreateDbContext();
        db.Attach(vote);
        vote.Weight = weight;

        await AssertViolation(db, CheckViolation, "ck_weight");
    }

    [Fact]
    public async Task Position_other_than_Y_or_N_is_rejected()
    {
        (Vote vote, _) = await AddVote();
        await using AppDbContext db = postgres.CreateDbContext();
        db.Attach(vote);
        vote.Position = "A";

        await AssertViolation(db, CheckViolation, "ck_position");
    }

    [Fact]
    public async Task Duplicate_vote_ref_is_rejected()
    {
        (Vote vote, Import import) = await AddVote();
        await using AppDbContext db = postgres.CreateDbContext();
        db.Votes.Add(NewVote(vote.VoteRef, import.Id));

        await AssertViolation(db, UniqueViolation, "ix_votes_vote_ref");
    }

    [Fact]
    public async Task Identical_pending_conflict_is_rejected()
    {
        (Vote vote, Import import) = await AddVote();
        await using AppDbContext db = postgres.CreateDbContext();
        db.Conflicts.Add(NewConflict(vote, import, proposed: "N"));
        await db.SaveChangesAsync();
        db.Conflicts.Add(NewConflict(vote, import, proposed: "N"));

        await AssertViolation(db, UniqueViolation, "ix_conflicts_vote_id_target_proposed_value");
    }

    [Fact]
    public async Task Same_proposal_may_reopen_once_the_first_is_resolved()
    {
        (Vote vote, Import import) = await AddVote();
        await using AppDbContext db = postgres.CreateDbContext();
        Conflict first = NewConflict(vote, import, proposed: "N");
        db.Conflicts.Add(first);
        await db.SaveChangesAsync();

        first.Status = ConflictStatus.Rejected;
        db.Conflicts.Add(NewConflict(vote, import, proposed: "N"));

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Different_pending_proposals_for_one_value_are_allowed()
    {
        (Vote vote, Import import) = await AddVote();
        await using AppDbContext db = postgres.CreateDbContext();
        db.Conflicts.AddRange(NewConflict(vote, import, proposed: "N"), NewConflict(vote, import, proposed: "A"));

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Second_current_publication_for_a_quarter_is_rejected()
    {
        string quarter = UniqueQuarter();
        Snapshot snapshot = await AddSnapshot();
        await using AppDbContext db = postgres.CreateDbContext();
        db.SnapshotPublications.Add(NewPublication(snapshot, quarter));
        await db.SaveChangesAsync();
        db.SnapshotPublications.Add(NewPublication(snapshot, quarter));

        await AssertViolation(db, UniqueViolation, "ix_snapshot_publications_quarter");
    }

    [Fact]
    public async Task Withdrawn_publication_frees_its_quarter()
    {
        string quarter = UniqueQuarter();
        Snapshot snapshot = await AddSnapshot();
        await using AppDbContext db = postgres.CreateDbContext();
        SnapshotPublication first = NewPublication(snapshot, quarter);
        db.SnapshotPublications.Add(first);
        await db.SaveChangesAsync();

        first.WithdrawnAt = DateTimeOffset.UtcNow;
        db.SnapshotPublications.Add(NewPublication(snapshot, quarter));

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Database_defaults_fill_timestamps_for_set_based_inserts()
    {
        await using AppDbContext db = postgres.CreateDbContext();
        Job job = new() { Type = "import", Payload = "{}" };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        Assert.NotEqual(default, job.CreatedAt);
        Assert.NotEqual(default, job.RunAfter);
    }

    [Fact]
    public async Task Api_resolves_the_context_from_the_connection_string_setting()
    {
        await using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("ConnectionStrings:Default", postgres.ConnectionString));
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();

        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.True(await db.Database.CanConnectAsync());
    }

    private async Task<(Vote Vote, Import Import)> AddVote()
    {
        await using AppDbContext db = postgres.CreateDbContext();
        if (!await db.Users.AnyAsync(u => u.Id == ActorId))
        {
            db.Users.Add(new User { Id = ActorId, Name = "Test", Role = Role.Admin });
        }

        Import import = new()
        {
            Id = Guid.NewGuid(),
            FileName = "votes.csv",
            BlobKey = "imports/votes.csv",
            Sha256 = new string('0', 64),
            UploadedBy = ActorId,
        };
        db.Imports.Add(import);
        await db.SaveChangesAsync();

        Vote vote = NewVote($"MA/{Guid.NewGuid():N}", import.Id);
        db.Votes.Add(vote);
        await db.SaveChangesAsync();
        return (vote, import);
    }

    private async Task<Snapshot> AddSnapshot()
    {
        await AddVote();    // ensures the user exists
        await using AppDbContext db = postgres.CreateDbContext();
        Snapshot snapshot = new() { Name = "Test", Filter = "{}", Rules = "{}", CreatedBy = ActorId };
        db.Snapshots.Add(snapshot);
        await db.SaveChangesAsync();
        return snapshot;
    }

    private static Vote NewVote(string voteRef, Guid importId) => new()
    {
        VoteRef = voteRef,
        Session = 79,
        VoteDate = new DateOnly(2024, 2, 6),
        Title = "Test vote",
        TitleSourceImportId = importId,
        VoteDateSourceImportId = importId,
        SessionSourceImportId = importId,
    };

    private static CountryVote NewBallot(Vote vote, Import import, char ballot) => new()
    {
        VoteId = vote.Id,
        CountryCode = "FRA",
        Ballot = ballot,
        SourceImportId = import.Id,
        SourceLine = 2,
    };

    private static Conflict NewConflict(Vote vote, Import import, string proposed) => new()
    {
        VoteId = vote.Id,
        Target = "FRA",
        ApprovedValue = "Y",
        ApprovedSourceImportId = import.Id,
        ApprovedSourceLine = 2,
        ProposedValue = proposed,
        ProposedSourceImportId = import.Id,
        ProposedSourceLine = 3,
        PublishedAt = DateTimeOffset.UtcNow,
    };

    private static SnapshotPublication NewPublication(Snapshot snapshot, string quarter) => new()
    {
        SnapshotId = snapshot.Id,
        Quarter = quarter,
        PublishedBy = ActorId,
    };

    private static string UniqueQuarter() => $"Q-{Guid.NewGuid():N}";

    private static async Task AssertViolation(AppDbContext db, string sqlState, string constraint)
    {
        DbUpdateException ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        PostgresException inner = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(sqlState, inner.SqlState);
        Assert.Equal(constraint, inner.ConstraintName);
    }
}
