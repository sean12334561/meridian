using FluentValidation;
using Meridian.Api.Features.Votes;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Paging;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Features.Snapshots;

/// <summary>
/// Shows which votes a selection covers, before anything is created. Unlike Create, an unknown
/// reference is reported and not rejected, because the list may still be being typed.
/// </summary>
public static class PreviewSnapshot
{
    public class Request : SnapshotSelection
    {
        public int Page { get; init; } = 1;
        public int PageSize { get; init; } = 50;
    }

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            Include(new SnapshotSelectionValidator());
            RuleFor(r => r.Page).InclusiveBetween(1, PageRequestValidator.MaxPage);
            RuleFor(r => r.PageSize).InclusiveBetween(1, PageRequestValidator.MaxPageSize);
        }
    }

    public class Handler(AppDbContext db, IValidator<Request> validator)
    {
        public async Task<SnapshotPreviewDto> Handle(Request request, CancellationToken ct)
        {
            await validator.ValidateAndThrowAsync(request, ct);

            IQueryable<Vote> selected = request.Apply(db.Votes.AsNoTracking());
            string[] listedRefs = request.VoteRefs ?? [];
            List<string> foundRefs = await selected
                .Where(v => listedRefs.Contains(v.VoteRef))
                .Select(v => v.VoteRef)
                .ToListAsync(ct);

            return new SnapshotPreviewDto
            {
                Votes = await PagedResult<VoteDto>.From(
                    VoteDto.Listing(db, selected),
                    new PageRequest { Page = request.Page, PageSize = request.PageSize },
                    ct),
                MissingPositions = await selected.CountAsync(v => v.Position == null, ct),
                UnknownVoteRefs = listedRefs.Except(foundRefs).ToList(),
            };
        }
    }
}

public class SnapshotPreviewDto
{
    public required PagedResult<VoteDto> Votes { get; init; }
    public required int MissingPositions { get; init; }
    public required List<string> UnknownVoteRefs { get; init; }
}
