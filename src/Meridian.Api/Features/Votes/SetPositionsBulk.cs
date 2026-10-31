using FluentValidation;
using Meridian.Api.Shared.Auth;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.History;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Features.Votes;

public static class SetPositionsBulk
{
    public class Request : VoteFilter
    {
        public string? DesiredOutcome { get; init; }
        public int Weight { get; init; }

        /// <summary>Leaves positions alone that someone set by hand.</summary>
        public bool OnlyMissing { get; init; } = true;
    }

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.DesiredOutcome).Must(v => v is "Y" or "N")
                .WithMessage("Position must be For (Y) or Against (N).");
            RuleFor(r => r.Weight).InclusiveBetween(1, 5)
                .WithMessage("Weight must be between 1 (marginal) and 5 (critical).");
        }
    }

    public class Handler(AppDbContext db, HistoryWriter history, ICurrentUser user, IValidator<Request> validator)
    {
        /// <summary>One SaveChanges, so every vote and its history row commit together.</summary>
        public async Task<BulkPositionResultDto> Handle(Request request, CancellationToken ct)
        {
            await validator.ValidateAndThrowAsync(request, ct);

            IQueryable<Vote> matching = request.Apply(db.Votes);
            if (request.OnlyMissing)
            {
                matching = matching.Where(v => v.Position == null);
            }

            List<Vote> votes = await matching.ToListAsync(ct);
            foreach (Vote vote in votes)
            {
                history.PositionChanged(vote.Id, (vote.Position, vote.Weight), (request.DesiredOutcome, request.Weight), user.Id);
                vote.Position = request.DesiredOutcome;
                vote.Weight = request.Weight;
            }

            await db.SaveChangesAsync(ct);

            return new BulkPositionResultDto { Updated = votes.Count };
        }
    }
}

public class BulkPositionResultDto
{
    public required int Updated { get; init; }
}
