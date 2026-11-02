namespace Meridian.Api.Features.Snapshots;

public class SnapshotJobPayload
{
    public const string JobType = "snapshot";

    public required long SnapshotId { get; init; }
    public required string CorrelationId { get; init; }
}
