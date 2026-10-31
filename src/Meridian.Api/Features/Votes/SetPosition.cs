using FluentValidation;
using Meridian.Api.Shared.Auth;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Errors;
using Meridian.Api.Shared.History;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Meridian.Api.Features.Votes;

public static class SetPosition
{
    public class Request
    {
        public string? DesiredOutcome { get; init; }
        public int Weight { get; init; }
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
        public async Task<VoteDto> Handle(long voteId, Request request, CancellationToken ct)
        {
            await validator.ValidateAndThrowAsync(request, ct);

            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({Locks.Positions})", ct);

            Vote vote = await db.Votes.SingleOrDefaultAsync(v => v.Id == voteId, ct)
                        ?? throw new NotFoundException("Vote", voteId);

            (string? Position, int? Weight) old = (vote.Position, vote.Weight);
            if (old != (request.DesiredOutcome, request.Weight))
            {
                vote.Position = request.DesiredOutcome;
                vote.Weight = request.Weight;
                history.PositionChanged(vote.Id, old, (request.DesiredOutcome, request.Weight), user.Id);
                await db.SaveChangesAsync(ct);
            }

            await transaction.CommitAsync(ct);

            int openConflicts = await db.Conflicts.CountAsync(c => c.VoteId == voteId && c.Status == ConflictStatus.Pending, ct);

            return VoteDto.From(vote, openConflicts);
        }
    }
}
