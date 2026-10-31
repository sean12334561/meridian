using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Meridian.Api.Features.Imports.Classification;

/// <summary>
/// Phase 2: checks that compare a file's rows with each other. It only touches this import's rows that
/// have no outcome yet, so it needs no lock, can run beside other imports, and is safe to run again.
/// </summary>
public class InFileChecks(IDbContextFactory<AppDbContext> dbFactory)
{
    private static readonly TimeSpan StatementTimeout = TimeSpan.FromMinutes(10);

    public async Task RunAsync(Guid importId, CancellationToken ct)
    {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
        db.Database.SetCommandTimeout(StatementTimeout);

        // The rows were just loaded, so the planner has no statistics for them. Without these it picks
        // a nested loop, and a file with many duplicates takes quadratic time. Outside the transaction:
        // ANALYZE holds a table lock until commit, which would make other imports' checks wait
        await db.Database.ExecuteSqlRawAsync("ANALYZE import_rows", ct);

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);

        await MarkNewVotesWithDifferingDetailsAsync(db, importId, ct);
        await MarkDuplicatesAsync(db, importId, ct);
        await MarkContradictionsAsync(db, importId, ct);

        await db.Imports
            .Where(i => i.Id == importId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Phase, ImportPhase.Applying), ct);

        await transaction.CommitAsync(ct);
    }

    // First: a duplicate is matched without the details, so removing duplicates could hide a difference.
    // An existing vote is left alone: its ballots can still be used, and the report notes the difference
    private static async Task MarkNewVotesWithDifferingDetailsAsync(AppDbContext db, Guid importId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE import_rows r
            SET outcome = 'Contradiction', reason = 'Vote details differ between rows of this file'
            WHERE r.import_id = {importId} AND r.outcome IS NULL AND r.vote_ref IN (
                SELECT d.vote_ref FROM import_rows d
                WHERE d.import_id = {importId} AND d.outcome IS NULL
                  AND NOT EXISTS (SELECT 1 FROM votes v WHERE v.vote_ref = d.vote_ref)
                GROUP BY d.vote_ref
                HAVING COUNT(DISTINCT (d.session, d.vote_date, d.title)) > 1)
            """, ct);
    }

    // The freshest of the repeats stays: published_at decides whether a closed decision is reopened
    private static async Task MarkDuplicatesAsync(AppDbContext db, Guid importId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE import_rows r
            SET outcome = 'Duplicate', reason = 'Same as line ' || d.kept_line
            FROM (SELECT line_no, FIRST_VALUE(line_no) OVER (PARTITION BY vote_ref, country_code, ballot
                                                         ORDER BY published_at DESC, line_no) AS kept_line
                  FROM import_rows
                  WHERE import_id = {importId} AND outcome IS NULL) d
            WHERE r.import_id = {importId} AND r.line_no = d.line_no AND d.line_no <> d.kept_line
            """, ct);
    }

    private static async Task MarkContradictionsAsync(AppDbContext db, Guid importId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE import_rows r
            SET outcome = 'Contradiction', reason = 'Different ballots for this country in the file'
            WHERE r.import_id = {importId} AND r.outcome IS NULL AND (r.vote_ref, r.country_code) IN (
                SELECT vote_ref, country_code FROM import_rows
                WHERE import_id = {importId} AND outcome IS NULL
                GROUP BY vote_ref, country_code
                HAVING COUNT(DISTINCT ballot) > 1)
            """, ct);
    }
}
