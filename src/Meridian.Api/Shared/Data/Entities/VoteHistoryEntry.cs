namespace Meridian.Api.Shared.Data.Entities;

public class VoteHistoryEntry
{
    public long Id { get; set; }
    public long VoteId { get; set; }
    public HistoryType Type { get; set; }
    public string? Target { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public int ActorId { get; set; }
    public Guid? ImportId { get; set; }
    public long? ConflictId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
