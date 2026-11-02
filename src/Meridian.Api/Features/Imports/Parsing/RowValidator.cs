using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meridian.Api.Shared.Data.Entities;

namespace Meridian.Api.Features.Imports.Parsing;

/// <summary>
/// Turns a CSV row into an import row: parsed values, plus Invalid and a reason if a check failed. The
/// first failed check is the reason. Fields that did parse are kept, so the report can place the row.
/// </summary>
public partial class RowValidator(IEnumerable<MemberState> memberStates)
{
    private const string TimestampUtc = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";
    private const string TimestampWithOffset = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz";

    private static readonly char[] Ballots = ['Y', 'N', 'A', 'X'];

    private readonly ILookup<string, MemberState> _periods = memberStates.ToLookup(m => m.CountryCode);

    public ImportRow Validate(Guid importId, CsvRow csvRow, CsvColumns columns)
    {
        ImportRow row = new() { ImportId = importId, LineNo = csvRow.LineNo, RawText = csvRow.RawText };

        if (csvRow.Fields is null)
        {
            return Invalid(row, "Broken quotes");
        }

        if (csvRow.Fields.Length != CsvColumns.Count)
        {
            return Invalid(row, $"Expected {CsvColumns.Count} columns, found {csvRow.Fields.Length}");
        }

        List<Normalization> normalizations = [];
        string voteRef = Trimmed("assembly_vote_id", csvRow.Fields[columns.VoteRef], normalizations);
        string session = Trimmed("session", csvRow.Fields[columns.Session], normalizations);
        string voteDate = Trimmed("vote_date", csvRow.Fields[columns.VoteDate], normalizations);
        string title = Trimmed("resolution_title", csvRow.Fields[columns.Title], normalizations);
        string countryCode = Uppercased(Trimmed("country_code", csvRow.Fields[columns.CountryCode], normalizations), normalizations);
        string countryName = Trimmed("country_name", csvRow.Fields[columns.CountryName], normalizations);
        string ballot = Trimmed("ballot", csvRow.Fields[columns.Ballot], normalizations);
        string publishedAt = Trimmed("published_at", csvRow.Fields[columns.PublishedAt], normalizations);

        row.Title = NullIfEmpty(title);
        row.CountryName = NullIfEmpty(countryName);

        if (VoteRefPattern().IsMatch(voteRef))
        {
            row.VoteRef = voteRef;
        }

        if (int.TryParse(session, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedSession))
        {
            row.Session = parsedSession;
        }

        if (DateOnly.TryParseExact(voteDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsedDate))
        {
            row.VoteDate = parsedDate;
        }

        bool knownCountry = _periods.Contains(countryCode);
        if (knownCountry)
        {
            row.CountryCode = countryCode;
        }

        bool validBallot = ballot.Length == 1 && Ballots.Contains(ballot[0]);
        if (validBallot)
        {
            row.Ballot = ballot[0];
        }

        if (DateTimeOffset.TryParseExact(
                publishedAt, [TimestampUtc, TimestampWithOffset], CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsedPublishedAt))
        {
            row.PublishedAt = parsedPublishedAt;
        }

        string? reason = FirstMissingField(voteRef, session, voteDate, title, countryCode, countryName, ballot, publishedAt);
        reason ??= row.VoteRef is null ? $"Malformed vote id '{voteRef}'" : null;
        reason ??= row.Session is null ? $"Session '{session}' is not a number" : null;
        reason ??= row.VoteDate is null ? $"Date '{voteDate}' is not yyyy-mm-dd" : null;
        reason ??= knownCountry ? null : $"Unknown country code '{countryCode}'";
        reason ??= IsMember(countryCode, row.VoteDate!.Value) ? null : $"{countryCode} was not a member on {voteDate}";
        reason ??= validBallot ? null : $"Invalid ballot '{ballot}'";
        reason ??= row.PublishedAt is null ? $"Invalid published_at '{publishedAt}'" : null;

        if (reason is not null)
        {
            return Invalid(row, reason);
        }

        if (normalizations.Count > 0)
        {
            row.Normalizations = JsonSerializer.Serialize(normalizations, JsonSerializerOptions.Web);
        }

        return row;
    }

    [GeneratedRegex("^MA/[0-9]{4}/[0-9]{3}$")]
    private static partial Regex VoteRefPattern();

    private static string Trimmed(string field, string original, List<Normalization> normalizations)
    {
        string value = original.Trim();
        if (value != original)
        {
            normalizations.Add(new Normalization { Field = field, Original = original, Value = value });
        }

        return value;
    }

    private static string Uppercased(string countryCode, List<Normalization> normalizations)
    {
        string value = countryCode.ToUpperInvariant();
        if (value != countryCode)
        {
            normalizations.Add(new Normalization { Field = "country_code", Original = countryCode, Value = value });
        }

        return value;
    }

    private static string? NullIfEmpty(string value)
    {
        return value.Length == 0 ? null : value;
    }

    private static string? FirstMissingField(
        string voteRef, string session, string voteDate, string title,
        string countryCode, string countryName, string ballot, string publishedAt)
    {
        (string Value, string Name)[] fields =
        [
            (voteRef, "vote id"), (session, "session"), (voteDate, "vote date"), (title, "title"),
            (countryCode, "country code"), (countryName, "country name"), (ballot, "ballot"), (publishedAt, "published_at"),
        ];

        foreach ((string value, string name) in fields)
        {
            if (value.Length == 0)
            {
                return $"Missing {name}";
            }
        }

        return null;
    }

    // Both ends are inclusive, and no end date means still a member
    private bool IsMember(string countryCode, DateOnly date)
    {
        return _periods[countryCode].Any(p => p.MemberFrom <= date && (p.MemberTo is null || date <= p.MemberTo));
    }

    private static ImportRow Invalid(ImportRow row, string reason)
    {
        row.Outcome = RowOutcome.Invalid;
        row.Reason = reason;

        return row;
    }

    private class Normalization
    {
        public required string Field { get; init; }
        public required string Original { get; init; }
        public required string Value { get; init; }
    }
}
