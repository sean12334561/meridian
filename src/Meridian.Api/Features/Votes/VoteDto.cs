using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;

namespace Meridian.Api.Features.Votes;

public class VoteDto
{
    public required long Id { get; init; }
    public required string VoteRef { get; init; }
    public required int Session { get; init; }
    public required DateOnly VoteDate { get; init; }
    public required string Title { get; init; }
    public required string? Position { get; init; }
    public required int? Weight { get; init; }
    public required bool MissingPosition { get; init; }
    public required int OpenConflicts { get; init; }

    public static VoteDto From(Vote vote, int openConflicts)
    {
        return new VoteDto
        {
            Id = vote.Id,
            VoteRef = vote.VoteRef,
            Session = vote.Session,
            VoteDate = vote.VoteDate,
            Title = vote.Title,
            Position = vote.Position,
            Weight = vote.Weight,
            MissingPosition = vote.Position is null,
            OpenConflicts = openConflicts,
        };
    }

    /// <summary>Newest first. Shared by the vote list and the snapshot preview.</summary>
    public static IQueryable<VoteDto> Listing(AppDbContext db, IQueryable<Vote> votes)
    {
        return votes
            .OrderByDescending(v => v.VoteDate)
            .ThenBy(v => v.VoteRef)
            .Select(v => new VoteDto
            {
                Id = v.Id,
                VoteRef = v.VoteRef,
                Session = v.Session,
                VoteDate = v.VoteDate,
                Title = v.Title,
                Position = v.Position,
                Weight = v.Weight,
                MissingPosition = v.Position == null,
                OpenConflicts = db.Conflicts.Count(c => c.VoteId == v.Id && c.Status == ConflictStatus.Pending),
            });
    }
}
