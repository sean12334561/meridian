using FluentValidation;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Paging;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Features.Votes;

public static class GetVotes
{
    public class Request : VoteFilter
    {
        public int Page { get; init; } = 1;
        public int PageSize { get; init; } = 50;
    }

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Page).InclusiveBetween(1, PageRequestValidator.MaxPage);
            RuleFor(r => r.PageSize).InclusiveBetween(1, PageRequestValidator.MaxPageSize);
        }
    }

    public class Handler(AppDbContext db, IValidator<Request> validator)
    {
        public async Task<PagedResult<VoteDto>> Handle(Request request, CancellationToken ct)
        {
            await validator.ValidateAndThrowAsync(request, ct);

            IQueryable<VoteDto> votes = request.Apply(db.Votes.AsNoTracking())
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

            return await PagedResult<VoteDto>.From(votes, new PageRequest { Page = request.Page, PageSize = request.PageSize }, ct);
        }
    }
}
