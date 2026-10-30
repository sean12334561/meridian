namespace Meridian.Api.Shared.Data.Entities;

public class CountryVote
{
    public long Id { get; set; }
    public long VoteId { get; set; }
    public string CountryCode { get; set; } = null!;
    public char Ballot { get; set; }
    public Guid SourceImportId { get; set; }
    public int SourceLine { get; set; }
}
