using System.Data;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Meridian.Api.Features.Snapshots;

/// <summary>
/// Computes one snapshot in one REPEATABLE READ transaction, so every statement sees the same approved
/// data. A failure rolls back both inserts and a retry starts clean.
/// </summary>
public class SnapshotJobHandler(IDbContextFactory<AppDbContext> dbFactory) : IJobHandler
{
    private static readonly TimeSpan StatementTimeout = TimeSpan.FromMinutes(10);

    public async Task RunAsync(ClaimedJob job, CancellationToken ct)
    {
        long snapshotId = job.ReadPayload<SnapshotJobPayload>().SnapshotId;

        await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
        db.Database.SetCommandTimeout(StatementTimeout);

        Snapshot snapshot = await db.Snapshots.AsNoTracking().SingleAsync(s => s.Id == snapshotId, ct);
        if (snapshot.Status == SnapshotStatus.Completed)
        {
            return;
        }

        await db.Snapshots
            .Where(s => s.Id == snapshotId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SnapshotStatus.Running), ct);

        long[] voteIds = SnapshotJson.Read<SnapshotFilter>(snapshot.Filter).VoteIds;
        SnapshotRules rules = SnapshotJson.Read<SnapshotRules>(snapshot.Rules);

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);

        await InsertContributionsAsync(db, snapshotId, voteIds, rules.AbstentionFactor, ct);
        await InsertScoresAsync(db, snapshotId, rules.MinCountedVotes, ct);

        int pendingConflicts = await db.Conflicts
            .Where(c => voteIds.Contains(c.VoteId) && c.Status == ConflictStatus.Pending)
            .Select(c => c.VoteId)
            .Distinct()
            .CountAsync(ct);

        await db.Snapshots
            .Where(s => s.Id == snapshotId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SnapshotStatus.Completed)
                .SetProperty(x => x.PendingConflictsAtCreation, pendingConflicts)
                .SetProperty(x => x.CompletedAt, DateTimeOffset.UtcNow), ct);

        await transaction.CommitAsync(ct);
    }

    public async Task OnFailedAsync(ClaimedJob job, string error, CancellationToken ct)
    {
        long snapshotId = job.ReadPayload<SnapshotJobPayload>().SnapshotId;

        await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
        await db.Snapshots
            .Where(s => s.Id == snapshotId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SnapshotStatus.Failed)
                .SetProperty(x => x.Error, error)
                .SetProperty(x => x.CompletedAt, DateTimeOffset.UtcNow), ct);
    }

    // One row per country and selected vote, with the inputs copied so later changes can't reach them
    private static Task InsertContributionsAsync(AppDbContext db, long snapshotId, long[] voteIds, decimal abstentionFactor, CancellationToken ct)
    {
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO snapshot_contributions
              (snapshot_id, country_code, vote_id, ballot, position, weight, source_import_id, counted, points, reason)
            SELECT {snapshotId}, cv.country_code, v.id, cv.ballot, v.position, v.weight, cv.source_import_id,
                   cv.ballot IN ('Y','N','A'),
                   CASE WHEN cv.ballot = v.position THEN v.weight
                        WHEN cv.ballot = 'A'        THEN v.weight * {abstentionFactor}
                        ELSE 0 END,
                   CASE WHEN cv.ballot = 'X'        THEN 'Absent'
                        WHEN cv.ballot = 'A'        THEN 'Abstained (half)'
                        WHEN cv.ballot = v.position THEN 'Matched'
                        ELSE 'Opposed' END
            FROM country_votes cv
            JOIN votes v ON v.id = cv.vote_id
            WHERE v.id = ANY({voteIds}) AND v.position IS NOT NULL
            """, ct);
    }

    // COALESCE: a country that was absent from every vote has no counted weight, and no score. Ranking
    // compares the unrounded scores
    private static Task InsertScoresAsync(AppDbContext db, long snapshotId, int minCountedVotes, CancellationToken ct)
    {
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO snapshot_scores
              (snapshot_id, country_code, counted_votes, counted_weight, earned_weight, score, rank)
            SELECT {snapshotId}, country_code, n, COALESCE(w, 0), COALESCE(e, 0),
                   CASE WHEN n >= {minCountedVotes} THEN ROUND(100 * e / w, 4) END,
                   CASE WHEN n >= {minCountedVotes} THEN RANK() OVER (ORDER BY
                        CASE WHEN n >= {minCountedVotes} THEN e / w END DESC NULLS LAST) END
            FROM (SELECT country_code,
                         COUNT(*) FILTER (WHERE counted)    AS n,
                         SUM(weight) FILTER (WHERE counted) AS w,
                         SUM(points)                        AS e
                  FROM snapshot_contributions
                  WHERE snapshot_id = {snapshotId}
                  GROUP BY country_code) s
            """, ct);
    }
}
