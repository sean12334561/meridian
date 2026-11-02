namespace Meridian.Api.Shared.Data.Entities;

public class SnapshotPublication
{
    public long Id { get; set; }
    public long SnapshotId { get; set; }
    public string Quarter { get; set; } = null!;
    public int PublishedBy { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset? WithdrawnAt { get; set; }
    public long? SupersededBy { get; set; }
}
