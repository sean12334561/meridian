using Meridian.Api.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Features.Imports;

/// <summary>
/// Phase 4: stores the totals and per-vote results, and completes the import in the same statement.
/// The details status reads current approved values, which only a decision can have changed by now.
/// </summary>
public class ImportReportWriter(IDbContextFactory<AppDbContext> dbFactory)
{
    private static readonly TimeSpan StatementTimeout = TimeSpan.FromMinutes(10);

    public async Task CompleteAsync(Guid importId, CancellationToken ct)
    {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
        db.Database.SetCommandTimeout(StatementTimeout);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH per_vote AS (
                SELECT r.vote_ref,
                       COUNT(*) FILTER (WHERE r.outcome = 'New') AS new_rows,
                       COUNT(*) FILTER (WHERE r.outcome = 'Unchanged') AS unchanged_rows,
                       COUNT(*) FILTER (WHERE r.outcome = 'Duplicate') AS duplicate_rows,
                       COUNT(*) FILTER (WHERE r.outcome = 'Invalid') AS invalid_rows,
                       COUNT(*) FILTER (WHERE r.outcome = 'Contradiction') AS contradiction_rows,
                       COUNT(*) FILTER (WHERE r.outcome = 'Conflict') AS conflict_rows,
                       COUNT(*) FILTER (WHERE r.outcome = 'PreviouslyDecided') AS decided_rows,
                       COUNT(DISTINCT (r.session, r.vote_date, r.title)) FILTER (WHERE r.outcome <> 'Invalid') AS detail_sets,
                       MIN(r.session) FILTER (WHERE r.outcome <> 'Invalid') AS session,
                       MIN(r.vote_date) FILTER (WHERE r.outcome <> 'Invalid') AS vote_date,
                       MIN(r.title) FILTER (WHERE r.outcome <> 'Invalid') AS title
                FROM import_rows r
                WHERE r.import_id = {importId} AND r.vote_ref IS NOT NULL
                GROUP BY r.vote_ref),
            reported AS (
                SELECT p.*,
                       CASE WHEN p.detail_sets > 1 THEN 'DiffersInFile'
                            WHEN v.id IS NULL THEN 'NotStored'
                            WHEN p.detail_sets = 0
                              OR (p.session, p.vote_date, p.title) = (v.session, v.vote_date, v.title) THEN 'Same'
                            WHEN EXISTS (SELECT 1 FROM conflicts c
                                         WHERE c.vote_id = v.id AND c.status = 'Pending'
                                           AND c.target IN ('title', 'vote_date', 'session')) THEN 'Conflict'
                            ELSE 'PreviouslyDecided' END AS details
                FROM per_vote p
                LEFT JOIN votes v ON v.vote_ref = p.vote_ref)
            UPDATE imports i
            SET status = 'Completed', phase = 'Done', completed_at = now(),
                report = jsonb_build_object(
                    'totals', (SELECT jsonb_build_object(
                                   'rows', COUNT(*),
                                   'new', COUNT(*) FILTER (WHERE r.outcome = 'New'),
                                   'unchanged', COUNT(*) FILTER (WHERE r.outcome = 'Unchanged'),
                                   'duplicate', COUNT(*) FILTER (WHERE r.outcome = 'Duplicate'),
                                   'invalid', COUNT(*) FILTER (WHERE r.outcome = 'Invalid'),
                                   'contradiction', COUNT(*) FILTER (WHERE r.outcome = 'Contradiction'),
                                   'conflict', COUNT(*) FILTER (WHERE r.outcome = 'Conflict'),
                                   'previouslyDecided', COUNT(*) FILTER (WHERE r.outcome = 'PreviouslyDecided'),
                                   'alreadyPending', COUNT(*) FILTER (WHERE r.reason = 'Already pending'),
                                   'normalized', COUNT(*) FILTER (WHERE r.normalizations IS NOT NULL))
                               FROM import_rows r WHERE r.import_id = {importId}),
                    'conflictsOpened', (SELECT COUNT(*) FROM conflicts c WHERE c.proposed_source_import_id = {importId}),
                    'votes', (SELECT COALESCE(jsonb_agg(jsonb_build_object(
                                   'voteRef', p.vote_ref,
                                   'new', p.new_rows,
                                   'unchanged', p.unchanged_rows,
                                   'duplicate', p.duplicate_rows,
                                   'invalid', p.invalid_rows,
                                   'contradiction', p.contradiction_rows,
                                   'conflict', p.conflict_rows,
                                   'previouslyDecided', p.decided_rows,
                                   'details', p.details) ORDER BY p.vote_ref), '[]'::jsonb)
                              FROM reported p))
            WHERE i.id = {importId}
            """, ct);
    }
}
