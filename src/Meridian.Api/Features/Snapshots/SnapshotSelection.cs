using FluentValidation;
using Meridian.Api.Shared.Data.Entities;

namespace Meridian.Api.Features.Snapshots;

/// <summary>Shared by creating and previewing a snapshot, so the preview shows the votes Create will use.</summary>
public class SnapshotSelection
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public int? Session { get; init; }
    public long[]? VoteIds { get; init; }

    /// <summary>Assembly references such as MA/2024/117, the identifier people see.</summary>
    public string[]? VoteRefs { get; init; }

    /// <summary>The filter's votes plus the listed ones: a list can add votes the filter misses.</summary>
    public IQueryable<Vote> Apply(IQueryable<Vote> votes)
    {
        bool hasFilter = From is not null || To is not null || Session is not null;
        long[] listed = VoteIds ?? [];
        string[] listedRefs = VoteRefs ?? [];

        return votes.Where(v => listed.Contains(v.Id)
                                || listedRefs.Contains(v.VoteRef)
                                || (hasFilter
                                    && (From == null || v.VoteDate >= From)
                                    && (To == null || v.VoteDate <= To)
                                    && (Session == null || v.Session == Session)));
    }
}

public class SnapshotSelectionValidator : AbstractValidator<SnapshotSelection>
{
    public SnapshotSelectionValidator()
    {
        RuleFor(r => r.To).GreaterThanOrEqualTo(r => r.From).When(r => r.From is not null && r.To is not null);
        RuleFor(r => r.VoteRefs)
            .Must((request, _) => request.From is not null || request.To is not null || request.Session is not null
                                  || request.VoteIds is { Length: > 0 } || request.VoteRefs is { Length: > 0 })
            .WithMessage("Choose a date range, a session or a list of votes.");
    }
}
