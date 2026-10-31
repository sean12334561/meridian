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
}
