using FluentValidation;
using FluentValidation.Results;
using Meridian.Api.Shared.Auth;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Errors;
using Meridian.Api.Shared.Jobs;
using Meridian.Api.Shared.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Meridian.Api.Features.Snapshots;

public static class CreateSnapshot
{
    private const decimal AbstentionFactor = 0.5m;

    public class Request : SnapshotSelection
    {
        public string? Name { get; init; }
        public bool ExcludeMissingPositions { get; init; }
        public int MinCountedVotes { get; init; } = 1;
    }

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
            RuleFor(r => r.MinCountedVotes).GreaterThanOrEqualTo(1);
            Include(new SnapshotSelectionValidator());
        }
    }

    public class Handler(
        AppDbContext db,
        JobQueue jobs,
        ICurrentUser user,
        ICorrelationContext correlation,
        IValidator<Request> validator)
    {
        public async Task<SnapshotCreatedDto> Handle(Request request, CancellationToken ct)
        {
            await validator.ValidateAndThrowAsync(request, ct);

            List<SelectedVote> selected = await SelectVotesAsync(request, ct);
            if (selected.Count == 0)
            {
                throw new ValidationException([new ValidationFailure(nameof(Request.VoteRefs), "No votes match this selection.")]);
            }

            List<SelectedVote> missing = selected.Where(v => !v.HasPosition).ToList();
            if (missing.Count > 0 && !request.ExcludeMissingPositions)
            {
                throw new MissingPositionsException(missing.Select(v => new MissingPositionDto { Id = v.Id, VoteRef = v.VoteRef }).ToList());
            }

            long[] voteIds = selected.Where(v => v.HasPosition).Select(v => v.Id).ToArray();
            if (voteIds.Length == 0)
            {
                throw new ValidationException([new ValidationFailure(nameof(Request.VoteRefs), "None of these votes has a position.")]);
            }

            Snapshot snapshot = new()
            {
                Name = request.Name!,
                Filter = SnapshotJson.Write(new SnapshotFilter
                {
                    From = request.From,
                    To = request.To,
                    Session = request.Session,
                    RequestedVoteIds = request.VoteIds,
                    RequestedVoteRefs = request.VoteRefs,
                    VoteIds = voteIds,
                }),
                Rules = SnapshotJson.Write(new SnapshotRules
                {
                    AbstentionFactor = AbstentionFactor,
                    MinCountedVotes = request.MinCountedVotes,
                    ExcludedVotes = missing.Select(v => v.Id).ToArray(),
                }),
                Status = SnapshotStatus.Pending,
                CreatedBy = user.Id,
            };

            // Saved first for its ID, in one transaction with the job that refers to it
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
            db.Snapshots.Add(snapshot);
            await db.SaveChangesAsync(ct);
            jobs.Enqueue(SnapshotJobPayload.JobType, new SnapshotJobPayload { SnapshotId = snapshot.Id, CorrelationId = correlation.Id });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return new SnapshotCreatedDto { SnapshotId = snapshot.Id };
        }

        private async Task<List<SelectedVote>> SelectVotesAsync(Request request, CancellationToken ct)
        {
            long[] listed = request.VoteIds ?? [];
            string[] listedRefs = request.VoteRefs ?? [];

            List<SelectedVote> selected = await request.Apply(db.Votes.AsNoTracking())
                .Select(v => new SelectedVote { Id = v.Id, VoteRef = v.VoteRef, HasPosition = v.Position != null })
                .ToListAsync(ct);

            long[] unknown = listed.Except(selected.Select(v => v.Id)).ToArray();
            if (unknown.Length > 0)
            {
                throw new ValidationException([new ValidationFailure(nameof(Request.VoteIds), $"Unknown votes: {string.Join(", ", unknown)}.")]);
            }

            string[] unknownRefs = listedRefs.Except(selected.Select(v => v.VoteRef)).ToArray();
            if (unknownRefs.Length > 0)
            {
                throw new ValidationException([new ValidationFailure(nameof(Request.VoteRefs), $"Unknown votes: {string.Join(", ", unknownRefs)}.")]);
            }

            return selected;
        }
    }

    private class SelectedVote
    {
        public required long Id { get; init; }
        public required string VoteRef { get; init; }
        public required bool HasPosition { get; init; }
    }
}

public class SnapshotCreatedDto
{
    public required long SnapshotId { get; init; }
}

public class MissingPositionDto
{
    public required long Id { get; init; }
    public required string VoteRef { get; init; }
}

/// <summary>Votes without a position block a snapshot until the user excludes them (POS-04).</summary>
public class MissingPositionsException(List<MissingPositionDto> missing)
    : Exception($"{missing.Count} selected votes have no position.")
{
    public List<MissingPositionDto> Missing { get; } = missing;
}
