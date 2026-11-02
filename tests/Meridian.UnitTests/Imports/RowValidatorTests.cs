using System.Text.Json;
using Meridian.Api.Features.Imports.Parsing;
using Meridian.Api.Shared.Data.Entities;

namespace Meridian.UnitTests.Imports;

public class RowValidatorTests
{
    private static readonly Guid ImportId = Guid.NewGuid();

    private static readonly CsvColumns Columns = new()
    {
        VoteRef = 0, Session = 1, VoteDate = 2, Title = 3, CountryCode = 4, CountryName = 5, Ballot = 6, PublishedAt = 7,
    };

    private static readonly RowValidator Validator = new(
    [
        new MemberState { CountryCode = "BRA", CountryName = "Brazil", MemberFrom = new DateOnly(1995, 1, 1) },
        new MemberState { CountryCode = "ISL", CountryName = "Iceland", MemberFrom = new DateOnly(1995, 1, 1), MemberTo = new DateOnly(2019, 12, 31) },
        new MemberState { CountryCode = "ISL", CountryName = "Iceland", MemberFrom = new DateOnly(2022, 1, 1) },
    ]);

    [Fact]
    public void A_good_row_is_parsed_and_has_no_outcome_yet()
    {
        ImportRow row = Validate();

        Assert.Null(row.Outcome);
        Assert.Null(row.Reason);
        Assert.Null(row.Normalizations);
        Assert.Equal(ImportId, row.ImportId);
        Assert.Equal(5, row.LineNo);
        Assert.Equal("MA/2024/001", row.VoteRef);
        Assert.Equal(79, row.Session);
        Assert.Equal(new DateOnly(2024, 1, 12), row.VoteDate);
        Assert.Equal("Title", row.Title);
        Assert.Equal("BRA", row.CountryCode);
        Assert.Equal("Brazil", row.CountryName);
        Assert.Equal('Y', row.Ballot);
        Assert.Equal(new DateTimeOffset(2024, 4, 2, 9, 0, 0, TimeSpan.Zero), row.PublishedAt);
    }

    [Theory]
    [InlineData("MA/2024/3", null, null, null, null, null, null, null, "Malformed vote id 'MA/2024/3'")]
    [InlineData(null, "abc", null, null, null, null, null, null, "Session 'abc' is not a number")]
    [InlineData(null, "-1", null, null, null, null, null, null, "Session '-1' is not a number")]
    [InlineData(null, null, "21/02/2024", null, null, null, null, null, "Date '21/02/2024' is not yyyy-mm-dd")]
    [InlineData(null, null, "2024-02-30", null, null, null, null, null, "Date '2024-02-30' is not yyyy-mm-dd")]
    [InlineData(null, null, null, null, "FRN", null, null, null, "Unknown country code 'FRN'")]
    [InlineData(null, null, null, null, null, null, "Yes", null, "Invalid ballot 'Yes'")]
    [InlineData(null, null, null, null, null, null, "y", null, "Invalid ballot 'y'")]
    [InlineData(null, null, null, null, null, null, null, "soon", "Invalid published_at 'soon'")]
    [InlineData(null, null, null, null, null, null, null, "2024-04-02T09:00:00", "Invalid published_at '2024-04-02T09:00:00'")]
    public void A_bad_value_is_invalid_with_its_reason(
        string? voteRef, string? session, string? date, string? title, string? code, string? name, string? ballot, string? publishedAt,
        string reason)
    {
        ImportRow row = Validate(voteRef, session, date, title, code, name, ballot, publishedAt);

        Assert.Equal(RowOutcome.Invalid, row.Outcome);
        Assert.Equal(reason, row.Reason);
    }

    [Theory]
    [InlineData(0, "vote id")]
    [InlineData(1, "session")]
    [InlineData(2, "vote date")]
    [InlineData(3, "title")]
    [InlineData(4, "country code")]
    [InlineData(5, "country name")]
    [InlineData(6, "ballot")]
    [InlineData(7, "published_at")]
    public void An_empty_field_is_reported_as_missing(int column, string name)
    {
        string[] fields = GoodFields();
        fields[column] = "   ";

        ImportRow row = Validator.Validate(ImportId, Row(fields), Columns);

        Assert.Equal(RowOutcome.Invalid, row.Outcome);
        Assert.Equal($"Missing {name}", row.Reason);
    }

    [Fact]
    public void Wrong_column_count_and_broken_quotes_keep_only_the_raw_text()
    {
        ImportRow shortRow = Validator.Validate(ImportId, Row(["a", "b"]), Columns);
        ImportRow broken = Validator.Validate(ImportId, new CsvRow { LineNo = 2, RawText = "a\"b", Fields = null }, Columns);

        Assert.Equal("Expected 8 columns, found 2", shortRow.Reason);
        Assert.Equal("Broken quotes", broken.Reason);
        Assert.Equal(RowOutcome.Invalid, broken.Outcome);
        Assert.Equal("a\"b", broken.RawText);
    }

    [Theory]
    [InlineData("ISL", "2019-12-31", true)]     // last day of the first period
    [InlineData("ISL", "2020-01-01", false)]    // the day after it
    [InlineData("ISL", "2021-12-31", false)]    // the day before the second
    [InlineData("ISL", "2022-01-01", true)]     // its first day
    [InlineData("ISL", "1994-12-31", false)]    // before ever joining
    [InlineData("BRA", "2030-01-01", true)]     // no end date
    public void Membership_is_inclusive_on_both_ends(string code, string date, bool member)
    {
        ImportRow row = Validate(code: code, date: date);

        Assert.Equal(member, row.Outcome is null);
        if (!member)
        {
            Assert.Equal($"{code} was not a member on {date}", row.Reason);
        }
    }

    [Fact]
    public void Lowercase_code_is_normalized_and_flagged_with_the_original()
    {
        ImportRow row = Validate(code: "bra");

        Assert.Null(row.Outcome);
        Assert.Equal("BRA", row.CountryCode);
        Assert.Equal([("country_code", "bra", "BRA")], Normalizations(row));
    }

    [Fact]
    public void Surrounding_spaces_are_trimmed_and_flagged()
    {
        ImportRow row = Validate(title: " Title ", code: " BRA");

        Assert.Null(row.Outcome);
        Assert.Equal("Title", row.Title);
        Assert.Equal("BRA", row.CountryCode);
        Assert.Equal(
            [("resolution_title", " Title ", "Title"), ("country_code", " BRA", "BRA")],
            Normalizations(row));
    }

    [Fact]
    public void An_invalid_row_keeps_the_values_that_did_parse()
    {
        ImportRow row = Validate(ballot: "Yes");

        Assert.Equal(RowOutcome.Invalid, row.Outcome);
        Assert.Equal("MA/2024/001", row.VoteRef);
        Assert.Equal("BRA", row.CountryCode);
        Assert.Null(row.Ballot);
    }

    [Fact]
    public void A_rejected_row_records_no_normalization()
    {
        ImportRow row = Validate(code: "bra", ballot: "Yes");

        Assert.Equal(RowOutcome.Invalid, row.Outcome);
        Assert.Null(row.Normalizations);
    }

    [Fact]
    public void An_offset_timestamp_is_stored_as_utc()
    {
        ImportRow row = Validate(publishedAt: "2024-04-02T11:00:00+02:00");

        Assert.Null(row.Outcome);
        Assert.Equal(TimeSpan.Zero, row.PublishedAt!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2024, 4, 2, 9, 0, 0, TimeSpan.Zero), row.PublishedAt);
    }

    private static string[] GoodFields()
    {
        return ["MA/2024/001", "79", "2024-01-12", "Title", "BRA", "Brazil", "Y", "2024-04-02T09:00:00Z"];
    }

    private static CsvRow Row(string[] fields)
    {
        return new CsvRow { LineNo = 5, RawText = "raw", Fields = fields };
    }

    private static ImportRow Validate(
        string? voteRef = null, string? session = null, string? date = null, string? title = null,
        string? code = null, string? name = null, string? ballot = null, string? publishedAt = null)
    {
        string[] good = GoodFields();
        string[] fields =
        [
            voteRef ?? good[0], session ?? good[1], date ?? good[2], title ?? good[3],
            code ?? good[4], name ?? good[5], ballot ?? good[6], publishedAt ?? good[7],
        ];

        return Validator.Validate(ImportId, Row(fields), Columns);
    }

    private static IEnumerable<(string, string, string)> Normalizations(ImportRow row)
    {
        using JsonDocument document = JsonDocument.Parse(row.Normalizations!);

        return document.RootElement.EnumerateArray()
            .Select(e => (e.GetProperty("field").GetString()!, e.GetProperty("original").GetString()!, e.GetProperty("value").GetString()!))
            .ToList();
    }
}
