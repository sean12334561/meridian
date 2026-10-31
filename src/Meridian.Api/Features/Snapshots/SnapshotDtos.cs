using Meridian.Api.Shared.Data.Entities;

namespace Meridian.Api.Features.Snapshots;

public class SnapshotSummaryDto
{
    public required long Id { get; init; }
    public required string Name { get; init; }
    public required SnapshotStatus Status { get; init; }
    public required string CreatedByName { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset? CompletedAt { get; init; }

    /// <summary>The quarter this snapshot is the current publication for, if any.</summary>
    public required string? PublishedQuarter { get; init; }
}

public class SnapshotDetailDto
{
    public required SnapshotSummaryDto Snapshot { get; init; }
    public required string? Error { get; init; }
    public required SnapshotRules Rules { get; init; }
    public required int VoteCount { get; init; }

    /// <summary>Null until the calculation has run.</summary>
    public required int? PendingConflictsAtCreation { get; init; }

    public required List<SnapshotScoreDto> Ranking { get; init; }
    public required List<PublicationDto> Publications { get; init; }
}

public class SnapshotScoreDto
{
    public required string CountryCode { get; init; }
    public required string CountryName { get; init; }

    /// <summary>Null: too few counted votes, shown as insufficient data.</summary>
    public required decimal? Score { get; init; }

    public required int? Rank { get; init; }
    public required int CountedVotes { get; init; }
    public required decimal CountedWeight { get; init; }
    public required decimal EarnedWeight { get; init; }
}

public class PublicationDto
{
    public required long Id { get; init; }
    public required long SnapshotId { get; init; }
    public required string Quarter { get; init; }
    public required string PublishedByName { get; init; }
    public required DateTimeOffset PublishedAt { get; init; }
    public required string? Reason { get; init; }
    public required DateTimeOffset? WithdrawnAt { get; init; }
    public required long? SupersededBy { get; init; }
}

public class CountryBreakdownDto
{
    public required SnapshotScoreDto Score { get; init; }
    public required List<ContributionDto> Contributions { get; init; }
}

public class ContributionDto
{
    public required long VoteId { get; init; }
    public required string VoteRef { get; init; }
    public required string Title { get; init; }
    public required char Ballot { get; init; }
    public required string Position { get; init; }
    public required int Weight { get; init; }
    public required bool Counted { get; init; }
    public required decimal Points { get; init; }
    public required string Reason { get; init; }
    public required Guid SourceImportId { get; init; }
    public required string SourceFileName { get; init; }
}
