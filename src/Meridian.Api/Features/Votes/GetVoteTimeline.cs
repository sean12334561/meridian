using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Errors;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Features.Votes;

public static class GetVoteTimeline
{
    public class Handler(AppDbContext db)
    {
        public async Task<List<TimelineEntryDto>> Handle(long id, CancellationToken ct)
        {
            if (!await db.Votes.AnyAsync(v => v.Id == id, ct))
            {
                throw new NotFoundException("Vote", id);
            }

            List<TimelineEntryDto> history = await (
                    from h in db.VoteHistory.AsNoTracking()
                    join actor in db.Users on h.ActorId equals actor.Id
                    from import in db.Imports.Where(i => i.Id == h.ImportId).DefaultIfEmpty()
                    where h.VoteId == id
                    select new TimelineEntryDto
                    {
                        Id = h.Id,
                        Type = h.Type.ToString(),
                        At = h.CreatedAt,
                        ActorName = actor.Name,
                        Target = h.Target,
                        OldValue = h.OldValue,
                        NewValue = h.NewValue,
                        ImportId = h.ImportId,
                        FileName = import == null ? null : import.FileName,
                        ConflictId = h.ConflictId,
                        SnapshotId = null,
                    })
                .ToListAsync(ct);

            List<TimelineEntryDto> published = await PublishedAsync(id, ct);
            List<TimelineEntryDto> withdrawn = await WithdrawnAsync(id, ct);

            return history
                .Concat(published)
                .Concat(withdrawn)
                .OrderByDescending(e => e.At)
                .ThenByDescending(e => e.Id)
                .ToList();
        }

        // Read here, not written as history: a publication would need thousands of rows otherwise
        private IQueryable<SnapshotPublication> PublicationsIncluding(long voteId)
        {
            return db.SnapshotPublications.FromSql($"""
                SELECT p.*
                FROM snapshot_publications p
                JOIN snapshots s ON s.id = p.snapshot_id
                WHERE s.filter -> 'voteIds' @> to_jsonb({voteId}::bigint)
                """);
        }

        private Task<List<TimelineEntryDto>> PublishedAsync(long voteId, CancellationToken ct)
        {
            return (
                    from p in PublicationsIncluding(voteId)
                    join actor in db.Users on p.PublishedBy equals actor.Id
                    select new TimelineEntryDto
                    {
                        Id = p.Id,
                        Type = "Published",
                        At = p.PublishedAt,
                        ActorName = actor.Name,
                        Target = p.Quarter,
                        OldValue = null,
                        NewValue = null,
                        ImportId = null,
                        FileName = null,
                        ConflictId = null,
                        SnapshotId = p.SnapshotId,
                    })
                .AsNoTracking()
                .ToListAsync(ct);
        }

        // Only a newer publication withdraws one, so its publisher is who withdrew this one
        private Task<List<TimelineEntryDto>> WithdrawnAsync(long voteId, CancellationToken ct)
        {
            return (
                    from p in PublicationsIncluding(voteId)
                    join next in db.SnapshotPublications on p.SupersededBy equals next.Id
                    join actor in db.Users on next.PublishedBy equals actor.Id
                    where p.WithdrawnAt != null
                    select new TimelineEntryDto
                    {
                        Id = p.Id,
                        Type = "Withdrawn",
                        At = p.WithdrawnAt!.Value,
                        ActorName = actor.Name,
                        Target = p.Quarter,
                        OldValue = null,
                        NewValue = null,
                        ImportId = null,
                        FileName = null,
                        ConflictId = null,
                        SnapshotId = p.SnapshotId,
                    })
                .AsNoTracking()
                .ToListAsync(ct);
        }
    }
}

public class TimelineEntryDto
{
    public required long Id { get; init; }

    /// <summary>A vote_history type, Published or Withdrawn.</summary>
    public required string Type { get; init; }

    public required DateTimeOffset At { get; init; }
    public required string ActorName { get; init; }

    /// <summary>A country code, a field name, or the quarter for Published and Withdrawn.</summary>
    public required string? Target { get; init; }

    /// <summary>JSON, as stored in vote_history.</summary>
    public required string? OldValue { get; init; }

    /// <summary>JSON, as stored in vote_history.</summary>
    public required string? NewValue { get; init; }

    public required Guid? ImportId { get; init; }
    public required string? FileName { get; init; }
    public required long? ConflictId { get; init; }
    public required long? SnapshotId { get; init; }
}
