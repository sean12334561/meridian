using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.History;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Meridian.Api.Features.Imports.Classification;

/// <summary>
/// Phase 3: compares the file with approved data, one batch of votes at a time. Each batch is one
/// transaction under the approved-data lock, which is released between batches so a decision never
/// waits long. A row gets its outcome in the same transaction, so a retry only sees unfinished votes.
/// </summary>
public class ApprovedDataClassifier(IDbContextFactory<AppDbContext> dbFactory)
{
    private const int BatchSize = 500;
    private static readonly TimeSpan StatementTimeout = TimeSpan.FromMinutes(10);

    public async Task RunAsync(Import import, CancellationToken ct)
    {
        foreach (string[] voteRefs in await PrepareBatchesAsync(import.Id, ct))
        {
            await ApplyBatchAsync(import, voteRefs, ct);
        }
    }

    private async Task<string[][]> PrepareBatchesAsync(Guid importId, CancellationToken ct)
    {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
        db.Database.SetCommandTimeout(StatementTimeout);

        // Parsing ended at total_rows. From here progress counts rows that have their final outcome
        await db.Imports
            .Where(i => i.Id == importId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                i => i.ProcessedRows,
                i => db.ImportRows.Count(r => r.ImportId == importId && r.Outcome != null)), ct);

        // Contradiction rows count too: their ballots are out, but a vote with only those can still change details
        List<string> voteRefs = await db.ImportRows
            .Where(r => r.ImportId == importId && (r.Outcome == null || r.Outcome == RowOutcome.Contradiction))
            .Select(r => r.VoteRef!)
            .Distinct()
            .OrderBy(voteRef => voteRef)
            .ToListAsync(ct);

        return voteRefs.Chunk(BatchSize).ToArray();
    }

    private async Task ApplyBatchAsync(Import import, string[] voteRefs, CancellationToken ct)
    {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
        db.Database.SetCommandTimeout(StatementTimeout);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);

        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({Locks.ApprovedData})", ct);

        HistoryWriter history = new(db);

        long[] createdVoteIds = await CreateMissingVotesAsync(db, import.Id, voteRefs, ct);
        await history.VotesCreatedAsync(createdVoteIds, import.Id, import.UploadedBy, ct);

        List<ConflictWrite> detailConflicts = await OpenDetailsConflictsAsync(db, import.Id, voteRefs, ct);

        int rejectedRows = await RejectNonMembersAsync(db, import.Id, voteRefs, ct);
        int classifiedRows = await ClassifyRowsAsync(db, import.Id, voteRefs, ct);

        await InsertNewBallotsAsync(db, import.Id, voteRefs, ct);
        long[] voteIds = await db.Votes.Where(v => voteRefs.Contains(v.VoteRef)).Select(v => v.Id).ToArrayAsync(ct);
        await history.BallotsImportedAsync(voteIds, import.Id, import.UploadedBy, ct);

        List<ConflictWrite> ballotConflicts = await OpenBallotConflictsAsync(db, import.Id, voteRefs, ct);
        List<ConflictWrite> conflicts = [.. detailConflicts, .. ballotConflicts];
        await history.ConflictsOpenedAsync(conflicts.Where(c => c.Opened).Select(c => c.Id).ToArray(), import.UploadedBy, ct);
        await history.ConflictsRepublishedAsync(conflicts.Where(c => !c.Opened).Select(c => c.Id).ToArray(), import.Id, import.UploadedBy, ct);
        await LinkRowsToConflictsAsync(db, import.Id, voteRefs, ct);

        await db.Imports
            .Where(i => i.Id == import.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ProcessedRows, i => i.ProcessedRows + rejectedRows + classifiedRows), ct);

        await transaction.CommitAsync(ct);
    }

    // Rows of a new vote with differing details are already Contradiction, so one set of details is left
    private static Task<long[]> CreateMissingVotesAsync(AppDbContext db, Guid importId, string[] voteRefs, CancellationToken ct)
    {
        return db.Database.SqlQuery<long>($"""
            INSERT INTO votes (vote_ref, session, vote_date, title,
                               session_source_import_id, vote_date_source_import_id, title_source_import_id)
            SELECT DISTINCT ON (r.vote_ref) r.vote_ref, r.session, r.vote_date, r.title,
                   r.import_id, r.import_id, r.import_id
            FROM import_rows r
            WHERE r.import_id = {importId} AND r.outcome IS NULL AND r.vote_ref = ANY({voteRefs})
              AND NOT EXISTS (SELECT 1 FROM votes v WHERE v.vote_ref = r.vote_ref)
            ORDER BY r.vote_ref, r.line_no
            RETURNING id AS "Value"
            """).ToArrayAsync(ct);
    }

    // One conflict per field that differs. A vote whose details differ inside the file opens none: the
    // file doesn't say which details it means. All its rows count, as Duplicate rows may hide a difference.
    // The freshest row speaks for the file, so a repeat with a newer published_at isn't lost
    private static Task<List<ConflictWrite>> OpenDetailsConflictsAsync(AppDbContext db, Guid importId, string[] voteRefs, CancellationToken ct)
    {
        return db.Database.SqlQuery<ConflictWrite>($"""
            WITH agreed AS (
                SELECT r.vote_ref, (ARRAY_AGG(r.line_no ORDER BY r.published_at DESC, r.line_no))[1] AS line_no
                FROM import_rows r
                WHERE r.import_id = {importId} AND r.vote_ref = ANY({voteRefs}) AND r.outcome IS DISTINCT FROM 'Invalid'
                GROUP BY r.vote_ref
                HAVING COUNT(DISTINCT (r.session, r.vote_date, r.title)) = 1)
            INSERT INTO conflicts (vote_id, target, approved_value, approved_source_import_id,
                                   proposed_value, proposed_source_import_id, proposed_source_line,
                                   published_at, status)
            SELECT v.id, f.target, f.approved, f.source,
                   f.proposed, r.import_id, r.line_no, r.published_at, 'Pending'
            FROM agreed a
            JOIN import_rows r ON r.import_id = {importId} AND r.line_no = a.line_no
            JOIN votes v ON v.vote_ref = r.vote_ref
            CROSS JOIN LATERAL (VALUES
                ('title', v.title, r.title, v.title_source_import_id),
                ('vote_date', v.vote_date::text, r.vote_date::text, v.vote_date_source_import_id),
                ('session', v.session::text, r.session::text, v.session_source_import_id)) f (target, approved, proposed, source)
            WHERE f.approved <> f.proposed
              AND NOT EXISTS (SELECT 1 FROM conflicts c
                              WHERE c.vote_id = v.id AND c.target = f.target
                                AND c.published_at >= r.published_at
                                AND (   (c.status IN ('Rejected', 'Superseded') AND c.proposed_value = f.proposed)
                                     OR (c.status = 'Accepted' AND c.approved_value = f.proposed)))
            ON CONFLICT (vote_id, target, proposed_value) WHERE status = 'Pending' DO UPDATE
                SET published_at = EXCLUDED.published_at
                WHERE conflicts.published_at < EXCLUDED.published_at
            RETURNING id, (xmax = 0) AS opened
            """).ToListAsync(ct);
    }

    // A file can propose a date that makes a country look like a member, but the vote's date is the
    // approved one until someone accepts the change. A ballot already stored is not a new one: a
    // difference there is for an analyst to decide
    private static Task<int> RejectNonMembersAsync(AppDbContext db, Guid importId, string[] voteRefs, CancellationToken ct)
    {
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE import_rows r
            SET outcome = 'Invalid', reason = r.country_code || ' was not a member on ' || v.vote_date
            FROM votes v
            WHERE r.import_id = {importId} AND r.outcome IS NULL AND r.vote_ref = ANY({voteRefs})
              AND v.vote_ref = r.vote_ref AND v.vote_date <> r.vote_date
              AND NOT EXISTS (SELECT 1 FROM country_votes cv WHERE cv.vote_id = v.id AND cv.country_code = r.country_code)
              AND NOT EXISTS (SELECT 1 FROM member_states m
                              WHERE m.country_code = r.country_code AND m.member_from <= v.vote_date
                                AND (m.member_to IS NULL OR v.vote_date <= m.member_to))
            """, ct);
    }

    private static Task<int> ClassifyRowsAsync(AppDbContext db, Guid importId, string[] voteRefs, CancellationToken ct)
    {
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE import_rows r
            SET outcome = classified.outcome,
                reason = CASE classified.outcome WHEN 'PreviouslyDecided' THEN 'Already decided' END
            FROM (SELECT s.line_no,
                         CASE WHEN cv.id IS NULL THEN 'New'
                              WHEN cv.ballot = s.ballot THEN 'Unchanged'
                              WHEN EXISTS (SELECT 1 FROM conflicts c
                                           WHERE c.vote_id = v.id AND c.target = s.country_code
                                             AND c.published_at >= s.published_at
                                             AND (   (c.status IN ('Rejected', 'Superseded') AND c.proposed_value = s.ballot::text)
                                                  OR (c.status = 'Accepted' AND c.approved_value = s.ballot::text)))
                                   THEN 'PreviouslyDecided'
                              ELSE 'Conflict' END AS outcome
                  FROM import_rows s
                  JOIN votes v ON v.vote_ref = s.vote_ref
                  LEFT JOIN country_votes cv ON cv.vote_id = v.id AND cv.country_code = s.country_code
                  WHERE s.import_id = {importId} AND s.outcome IS NULL AND s.vote_ref = ANY({voteRefs})) classified
            WHERE r.import_id = {importId} AND r.line_no = classified.line_no
            """, ct);
    }

    private static Task InsertNewBallotsAsync(AppDbContext db, Guid importId, string[] voteRefs, CancellationToken ct)
    {
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO country_votes (vote_id, country_code, ballot, source_import_id, source_line)
            SELECT v.id, r.country_code, r.ballot, r.import_id, r.line_no
            FROM import_rows r
            JOIN votes v ON v.vote_ref = r.vote_ref
            WHERE r.import_id = {importId} AND r.outcome = 'New' AND r.vote_ref = ANY({voteRefs})
            """, ct);
    }

    private static Task<List<ConflictWrite>> OpenBallotConflictsAsync(AppDbContext db, Guid importId, string[] voteRefs, CancellationToken ct)
    {
        return db.Database.SqlQuery<ConflictWrite>($"""
            INSERT INTO conflicts (vote_id, target, approved_value, approved_source_import_id, approved_source_line,
                                   proposed_value, proposed_source_import_id, proposed_source_line,
                                   published_at, status)
            SELECT v.id, r.country_code, cv.ballot::text, cv.source_import_id, cv.source_line,
                   r.ballot::text, r.import_id, r.line_no, r.published_at, 'Pending'
            FROM import_rows r
            JOIN votes v ON v.vote_ref = r.vote_ref
            JOIN country_votes cv ON cv.vote_id = v.id AND cv.country_code = r.country_code
            WHERE r.import_id = {importId} AND r.outcome = 'Conflict' AND r.vote_ref = ANY({voteRefs})
            ON CONFLICT (vote_id, target, proposed_value) WHERE status = 'Pending' DO UPDATE
                SET published_at = EXCLUDED.published_at
                WHERE conflicts.published_at < EXCLUDED.published_at
            RETURNING id, (xmax = 0) AS opened
            """).ToListAsync(ct);
    }

    private static Task LinkRowsToConflictsAsync(AppDbContext db, Guid importId, string[] voteRefs, CancellationToken ct)
    {
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE import_rows r
            SET conflict_id = c.id,
                reason = CASE WHEN c.proposed_source_import_id <> r.import_id THEN 'Already pending' END
            FROM votes v
            JOIN conflicts c ON c.vote_id = v.id AND c.status = 'Pending'
            WHERE r.import_id = {importId} AND r.outcome = 'Conflict' AND r.vote_ref = ANY({voteRefs})
              AND v.vote_ref = r.vote_ref AND c.target = r.country_code AND c.proposed_value = r.ballot::text
            """, ct);
    }

    /// <summary>A row returned by opening conflicts: a new conflict, or a pending one a newer publication backs.</summary>
    private class ConflictWrite
    {
        public required long Id { get; init; }
        public required bool Opened { get; init; }
    }
}
