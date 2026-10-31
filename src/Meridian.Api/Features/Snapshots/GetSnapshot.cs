using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Errors;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Features.Snapshots;

public static class GetSnapshot
{
    public class Handler(AppDbContext db)
    {
        public async Task<SnapshotDetailDto> Handle(long id, CancellationToken ct)
        {
            Snapshot snapshot = await db.Snapshots.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct)
                                ?? throw new NotFoundException("Snapshot", id);

            return new SnapshotDetailDto
            {
                Snapshot = await SnapshotQueries.Summaries(db).SingleAsync(s => s.Id == id, ct),
                Error = snapshot.Error,
                Rules = SnapshotJson.Read<SnapshotRules>(snapshot.Rules),
                VoteCount = SnapshotJson.Read<SnapshotFilter>(snapshot.Filter).VoteIds.Length,
                PendingConflictsAtCreation = snapshot.PendingConflictsAtCreation,
                Ranking = await SnapshotQueries.Scores(db, id)
                    .OrderBy(s => s.Rank == null)
                    .ThenBy(s => s.Rank)
                    .ThenBy(s => s.CountryCode)
                    .ToListAsync(ct),
                Publications = await SnapshotQueries.Publications(db)
                    .Where(p => p.SnapshotId == id)
                    .OrderByDescending(p => p.PublishedAt)
                    .ToListAsync(ct),
            };
        }
    }
}
