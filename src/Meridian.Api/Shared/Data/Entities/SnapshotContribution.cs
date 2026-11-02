namespace Meridian.Api.Shared.Data.Entities;

public class SnapshotContribution
{
    public long SnapshotId { get; set; }
    public string CountryCode { get; set; } = null!;
    public long VoteId { get; set; }
    public char Ballot { get; set; }
    public string Position { get; set; } = null!;
    public int Weight { get; set; }
    public Guid SourceImportId { get; set; }
    public bool Counted { get; set; }
    public decimal Points { get; set; }
    public string Reason { get; set; } = null!;
}
