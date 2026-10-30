namespace Meridian.Api.Shared.Data.Entities;

public class SnapshotScore
{
    public long SnapshotId { get; set; }
    public string CountryCode { get; set; } = null!;
    public int CountedVotes { get; set; }
    public decimal CountedWeight { get; set; }
    public decimal EarnedWeight { get; set; }
    public decimal? Score { get; set; }
    public int? Rank { get; set; }
}
