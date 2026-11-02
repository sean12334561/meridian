namespace Meridian.Api.Shared.Data.Entities;

public class Snapshot
{
    public long Id { get; set; }
    public string Name { get; set; } = null!;
    public string Filter { get; set; } = null!;
    public string Rules { get; set; } = null!;
    public SnapshotStatus Status { get; set; }
    public int CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? PendingConflictsAtCreation { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
