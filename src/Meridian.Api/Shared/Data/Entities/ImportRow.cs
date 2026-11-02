namespace Meridian.Api.Shared.Data.Entities;

public class ImportRow
{
    public Guid ImportId { get; set; }
    public int LineNo { get; set; }
    public string RawText { get; set; } = null!;

    // Parsed fields: null when a value failed to parse; RawText keeps the original
    public string? VoteRef { get; set; }
    public int? Session { get; set; }
    public DateOnly? VoteDate { get; set; }
    public string? Title { get; set; }
    public string? CountryCode { get; set; }
    public string? CountryName { get; set; }
    public char? Ballot { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }

    public string? Normalizations { get; set; }
    public RowOutcome? Outcome { get; set; }
    public string? Reason { get; set; }
    public long? ConflictId { get; set; }
}
