using Meridian.Api.Shared.Data;
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

            return await (
                    from h in db.VoteHistory.AsNoTracking()
                    join actor in db.Users on h.ActorId equals actor.Id
                    from import in db.Imports.Where(i => i.Id == h.ImportId).DefaultIfEmpty()
                    where h.VoteId == id
                    orderby h.CreatedAt descending, h.Id descending
                    select new TimelineEntryDto
                    {
                        Type = h.Type.ToString(),
                        At = h.CreatedAt,
                        ActorName = actor.Name,
                        Target = h.Target,
                        OldValue = h.OldValue,
                        NewValue = h.NewValue,
                        ImportId = h.ImportId,
                        FileName = import == null ? null : import.FileName,
                        ConflictId = h.ConflictId,
                    })
                .ToListAsync(ct);
        }
    }
}

public class TimelineEntryDto
{
    public required string Type { get; init; }
    public required DateTimeOffset At { get; init; }
    public required string ActorName { get; init; }
    public required string? Target { get; init; }

    /// <summary>JSON, as stored in vote_history.</summary>
    public required string? OldValue { get; init; }

    /// <summary>JSON, as stored in vote_history.</summary>
    public required string? NewValue { get; init; }

    public required Guid? ImportId { get; init; }
    public required string? FileName { get; init; }
    public required long? ConflictId { get; init; }
}
