using FluentValidation;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Errors;
using Meridian.Api.Shared.Paging;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Features.Snapshots;

public static class GetSnapshotVotes
{
    public class Request : PageRequest;

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            Include(new PageRequestValidator());
        }
    }

    public class Handler(AppDbContext db, IValidator<Request> validator)
    {
        public async Task<PagedResult<SnapshotVoteDto>> Handle(long id, Request request, CancellationToken ct)
        {
            await validator.ValidateAndThrowAsync(request, ct);

            string filter = await db.Snapshots.AsNoTracking()
                                .Where(s => s.Id == id)
                                .Select(s => s.Filter)
                                .SingleOrDefaultAsync(ct)
                            ?? throw new NotFoundException("Snapshot", id);
            long[] voteIds = SnapshotJson.Read<SnapshotFilter>(filter).VoteIds;

            PagedResult<Vote> page = await PagedResult<Vote>.From(
                db.Votes.AsNoTracking()
                    .Where(v => voteIds.Contains(v.Id))
                    .OrderByDescending(v => v.VoteDate)
                    .ThenBy(v => v.VoteRef),
                request,
                ct);

            // The position and weight it was calculated with, not the vote's current ones (SNP-04)
            long[] pageIds = page.Items.Select(v => v.Id).ToArray();
            Dictionary<long, FrozenInput> frozen = await db.SnapshotContributions.AsNoTracking()
                .Where(c => c.SnapshotId == id && pageIds.Contains(c.VoteId))
                .Select(c => new FrozenInput { VoteId = c.VoteId, Position = c.Position, Weight = c.Weight })
                .Distinct()
                .ToDictionaryAsync(f => f.VoteId, ct);

            return new PagedResult<SnapshotVoteDto>
            {
                Items = page.Items.Select(v => SnapshotVoteDto.From(v, frozen.GetValueOrDefault(v.Id))).ToList(),
                Page = page.Page,
                PageSize = page.PageSize,
                Total = page.Total,
            };
        }
    }
}

public class FrozenInput
{
    public required long VoteId { get; init; }
    public required string Position { get; init; }
    public required int Weight { get; init; }
}

public class SnapshotVoteDto
{
    public required long Id { get; init; }
    public required string VoteRef { get; init; }
    public required int Session { get; init; }
    public required DateOnly VoteDate { get; init; }
    public required string Title { get; init; }

    /// <summary>Null until the calculation has run, or if the vote has no ballots.</summary>
    public required string? Position { get; init; }

    public required int? Weight { get; init; }

    public static SnapshotVoteDto From(Vote vote, FrozenInput? frozen)
    {
        return new SnapshotVoteDto
        {
            Id = vote.Id,
            VoteRef = vote.VoteRef,
            Session = vote.Session,
            VoteDate = vote.VoteDate,
            Title = vote.Title,
            Position = frozen?.Position,
            Weight = frozen?.Weight,
        };
    }
}
