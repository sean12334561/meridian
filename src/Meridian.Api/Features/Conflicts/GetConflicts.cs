using FluentValidation;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Paging;

namespace Meridian.Api.Features.Conflicts;

public static class GetConflicts
{
    public class Request : PageRequest
    {
        public ConflictStatus Status { get; init; } = ConflictStatus.Pending;
        public long? VoteId { get; init; }
    }

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            Include(new PageRequestValidator());
        }
    }

    public class Handler(AppDbContext db, IValidator<Request> validator)
    {
        public async Task<PagedResult<ConflictDto>> Handle(Request request, CancellationToken ct)
        {
            await validator.ValidateAndThrowAsync(request, ct);

            IQueryable<ConflictDto> conflicts = ConflictDto.Query(db).Where(c => c.Status == request.Status);
            if (request.VoteId is not null)
            {
                conflicts = conflicts.Where(c => c.VoteId == request.VoteId);
            }

            return await PagedResult<ConflictDto>.From(conflicts.OrderBy(c => c.Id), request, ct);
        }
    }
}
