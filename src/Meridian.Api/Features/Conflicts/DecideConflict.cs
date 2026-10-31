using FluentValidation;
using Meridian.Api.Shared.Auth;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Errors;
using Meridian.Api.Shared.History;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Meridian.Api.Features.Conflicts;

public enum Decision { Accept, Reject }

public static class DecideConflict
{
    public class Request
    {
        public Decision? Decision { get; init; }
        public string? Note { get; init; }
    }

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Decision).NotNull().IsInEnum().WithMessage("Choose Accept or Reject.");
            RuleFor(r => r.Note).MaximumLength(1000);
        }
    }

    /// <summary>
    /// The conditional update decides who wins: only one caller can move the conflict from Pending.
    /// The lock keeps the decision from running between an import's classification and its commit.
    /// </summary>
    public class Handler(AppDbContext db, HistoryWriter history, ICurrentUser user, IValidator<Request> validator)
    {
        public async Task<ConflictDto> Handle(long id, Request request, CancellationToken ct)
        {
            await validator.ValidateAndThrowAsync(request, ct);

            ConflictStatus status = request.Decision == Decision.Accept ? ConflictStatus.Accepted : ConflictStatus.Rejected;

            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({Locks.ApprovedData})", ct);

            int won = await db.Conflicts
                .Where(c => c.Id == id && c.Status == ConflictStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, status)
                    .SetProperty(c => c.DecidedBy, user.Id)
                    .SetProperty(c => c.DecidedAt, DateTimeOffset.UtcNow)
                    .SetProperty(c => c.Note, request.Note), ct);

            if (won == 0)
            {
                await transaction.RollbackAsync(ct);
                await RejectLosingAttemptAsync(id, status, ct);
            }

            Conflict conflict = await db.Conflicts.AsNoTracking().SingleAsync(c => c.Id == id, ct);
            if (status == ConflictStatus.Accepted)
            {
                await ApplyProposedValueAsync(conflict, ct);
                await SupersedeOtherPendingAsync(conflict, ct);
            }

            history.ConflictDecided(conflict, user.Id);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return await ConflictDto.Query(db).SingleAsync(c => c.Id == id, ct);
        }

        // Runs after the rollback, so the attempt is recorded in its own short transaction
        private async Task RejectLosingAttemptAsync(long id, ConflictStatus attempted, CancellationToken ct)
        {
            Conflict conflict = await db.Conflicts.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct)
                                ?? throw new NotFoundException("Conflict", id);

            history.DecisionNotApplied(conflict, attempted, user.Id);
            await db.SaveChangesAsync(ct);

            ConflictDto current = await ConflictDto.Query(db).SingleAsync(c => c.Id == id, ct);
            throw new ConcurrencyConflictException(
                "This conflict was already decided.", current, "/problems/conflict-already-decided");
        }

        private async Task ApplyProposedValueAsync(Conflict conflict, CancellationToken ct)
        {
            switch (conflict.Target)
            {
                case "title":
                    await db.Votes.Where(v => v.Id == conflict.VoteId).ExecuteUpdateAsync(s => s
                        .SetProperty(v => v.Title, conflict.ProposedValue)
                        .SetProperty(v => v.TitleSourceImportId, conflict.ProposedSourceImportId), ct);
                    break;
                case "vote_date":
                    await db.Votes.Where(v => v.Id == conflict.VoteId).ExecuteUpdateAsync(s => s
                        .SetProperty(v => v.VoteDate, DateOnly.Parse(conflict.ProposedValue))
                        .SetProperty(v => v.VoteDateSourceImportId, conflict.ProposedSourceImportId), ct);
                    break;
                case "session":
                    await db.Votes.Where(v => v.Id == conflict.VoteId).ExecuteUpdateAsync(s => s
                        .SetProperty(v => v.Session, int.Parse(conflict.ProposedValue))
                        .SetProperty(v => v.SessionSourceImportId, conflict.ProposedSourceImportId), ct);
                    break;
                default:
                    await db.CountryVotes
                        .Where(cv => cv.VoteId == conflict.VoteId && cv.CountryCode == conflict.Target)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(cv => cv.Ballot, conflict.ProposedValue[0])
                            .SetProperty(cv => cv.SourceImportId, conflict.ProposedSourceImportId)
                            .SetProperty(cv => cv.SourceLine, conflict.ProposedSourceLine), ct);
                    break;
            }
        }

        private async Task SupersedeOtherPendingAsync(Conflict conflict, CancellationToken ct)
        {
            long[] superseded = await db.Database.SqlQuery<long>($"""
                UPDATE conflicts
                SET status = 'Superseded', decided_by = {user.Id}, decided_at = now()
                WHERE vote_id = {conflict.VoteId} AND target = {conflict.Target} AND status = 'Pending'
                RETURNING id AS "Value"
                """).ToArrayAsync(ct);

            await history.ConflictsSupersededAsync(superseded, user.Id, ct);
        }
    }
}
