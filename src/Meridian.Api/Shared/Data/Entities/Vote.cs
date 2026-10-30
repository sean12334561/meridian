namespace Meridian.Api.Shared.Data.Entities;

public class Vote
{
    public long Id { get; set; }
    public string VoteRef { get; set; } = null!;
    public int Session { get; set; }
    public DateOnly VoteDate { get; set; }
    public string Title { get; set; } = null!;
    public Guid DetailsSourceImportId { get; set; }
    public string? Position { get; set; }
    public int? Weight { get; set; }
}
