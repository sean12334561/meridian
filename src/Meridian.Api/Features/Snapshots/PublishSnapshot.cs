using FluentValidation;
using FluentValidation.Results;
using Meridian.Api.Shared.Auth;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Errors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Meridian.Api.Features.Snapshots;

public static class PublishSnapshot
{
    public class Request
    {
        public string? Quarter { get; init; }
        public string? Reason { get; init; }
    }

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Quarter).NotEmpty().Matches(@"^\d{4}-Q[1-4]$").WithMessage("Quarter must look like 2024-Q1.");
            RuleFor(r => r.Reason).MaximumLength(1000);
        }
    }

    /// <summary>
    /// A new publication for a quarter withdraws the current one, which stays viewable. The partial
    /// unique index rejects a concurrent second publish, so the loser gets a 409.
    /// </summary>
    public class Handler(AppDbContext db, ICurrentUser user, IValidator<Request> validator)
    {
        public async Task<PublicationDto> Handle(long id, Request request, CancellationToken ct)
        {
            await validator.ValidateAndThrowAsync(request, ct);

            Snapshot snapshot = await db.Snapshots.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct)
                                ?? throw new NotFoundException("Snapshot", id);

            if (snapshot.Status != SnapshotStatus.Completed)
            {
                throw new ConcurrencyConflictException("Only a completed snapshot can be published.");
            }

            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);

            SnapshotPublication? current = await db.SnapshotPublications
                .SingleOrDefaultAsync(p => p.Quarter == request.Quarter && p.WithdrawnAt == null, ct);

            if (current is not null)
            {
                RequireNewSnapshotAndReason(current, id, request);
                current.WithdrawnAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            SnapshotPublication publication = new()
            {
                SnapshotId = id,
                Quarter = request.Quarter!,
                PublishedBy = user.Id,
                Reason = request.Reason,
            };
            db.SnapshotPublications.Add(publication);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                throw new ConcurrencyConflictException($"{request.Quarter} was published by someone else just now.");
            }

            if (current is not null)
            {
                current.SupersededBy = publication.Id;
                await db.SaveChangesAsync(ct);
            }

            await transaction.CommitAsync(ct);

            return await SnapshotQueries.Publications(db).SingleAsync(p => p.Id == publication.Id, ct);
        }

        private static void RequireNewSnapshotAndReason(SnapshotPublication current, long id, Request request)
        {
            if (current.SnapshotId == id)
            {
                throw new ConcurrencyConflictException($"This snapshot is already published for {request.Quarter}.");
            }

            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(Request.Reason), $"{request.Quarter} already has a publication. Give a reason for replacing it."),
                ]);
            }
        }
    }
}
