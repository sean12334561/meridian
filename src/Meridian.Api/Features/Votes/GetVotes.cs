using FluentValidation;
using Meridian.Api.Shared.Data;
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

            IQueryable<VoteDto> votes = VoteDto.Listing(db, request.Apply(db.Votes.AsNoTracking()));

            return await PagedResult<VoteDto>.From(votes, new PageRequest { Page = request.Page, PageSize = request.PageSize }, ct);
        }
    }
}
