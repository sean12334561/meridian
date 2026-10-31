namespace Meridian.Api.Features.Imports.Parsing;

/// <summary>Where each required column is in this file. Columns are found by name, so their order doesn't matter.</summary>
public class CsvColumns
{
    public const int Count = 8;

    private static readonly string[] Required =
    [
        "assembly_vote_id", "session", "vote_date", "resolution_title",
        "country_code", "country_name", "ballot", "published_at",
    ];

    public required int VoteRef { get; init; }
    public required int Session { get; init; }
    public required int VoteDate { get; init; }
    public required int Title { get; init; }
    public required int CountryCode { get; init; }
    public required int CountryName { get; init; }
    public required int Ballot { get; init; }
    public required int PublishedAt { get; init; }

    public static CsvColumns FromHeader(string[] header)
    {
        Dictionary<string, int> positions = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Length; i++)
        {
            positions.TryAdd(header[i].Trim(), i);
        }

        List<string> missing = Required.Where(name => !positions.ContainsKey(name)).ToList();
        if (missing.Count > 0)
        {
            throw new ImportFileException($"The header is missing: {string.Join(", ", missing)}.");
        }

        return new CsvColumns
        {
            VoteRef = positions["assembly_vote_id"],
            Session = positions["session"],
            VoteDate = positions["vote_date"],
            Title = positions["resolution_title"],
            CountryCode = positions["country_code"],
            CountryName = positions["country_name"],
            Ballot = positions["ballot"],
            PublishedAt = positions["published_at"],
        };
    }
}
