namespace Meridian.Api.Shared.Data.Entities;

public class Conflict
{
    public long Id { get; set; }
    public long VoteId { get; set; }
    public string Target { get; set; } = null!;

    public string ApprovedValue { get; set; } = null!;
    public Guid ApprovedSourceImportId { get; set; }
    public int? ApprovedSourceLine { get; set; }

    public string ProposedValue { get; set; } = null!;
    public Guid ProposedSourceImportId { get; set; }
    public int ProposedSourceLine { get; set; }
    public DateTimeOffset PublishedAt { get; set; }

    public ConflictStatus Status { get; set; }
    public int? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
