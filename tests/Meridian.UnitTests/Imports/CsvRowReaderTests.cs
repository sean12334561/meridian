using System.Text;
using Meridian.Api.Features.Imports.Parsing;

namespace Meridian.UnitTests.Imports;

public class CsvRowReaderTests
{
    private const string Header = "assembly_vote_id,session,vote_date,resolution_title,country_code,country_name,ballot,published_at";
    private const string Row = "MA/2024/001,79,2024-01-12,Title,AFG,Afghanistan,N,2024-04-02T09:00:00Z";

    [Fact]
    public async Task Reads_quoted_commas_as_one_field_and_keeps_the_raw_text_and_line()
    {
        string quoted = "MA/2024/001,79,2024-01-12,\"Title, With Comma\",AFG,Afghanistan,N,2024-04-02T09:00:00Z";

        List<CsvRow> rows = await ReadAll($"{Header}\n{quoted}\n");

        CsvRow row = Assert.Single(rows);
        Assert.Equal(2, row.LineNo);
        Assert.Equal(quoted, row.RawText);
        Assert.Equal(8, row.Fields.Length);
        Assert.Equal("Title, With Comma", row.Fields[3]);
    }

    [Fact]
    public async Task Skips_a_utf8_bom()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes($"{Header}\n{Row}\n")];

        using CsvRowReader reader = new(new MemoryStream(bytes));
        CsvColumns columns = await reader.ReadColumnsAsync();

        Assert.Equal(0, columns.VoteRef);
    }

    [Fact]
    public async Task Handles_windows_line_endings_and_a_missing_last_newline()
    {
        List<CsvRow> rows = await ReadAll($"{Header}\r\n{Row}\r\n{Row}");

        Assert.Equal([2, 3], rows.Select(r => r.LineNo));
        Assert.All(rows, r => Assert.Equal(Row, r.RawText));
    }

    [Fact]
    public async Task Line_numbers_stay_true_after_a_blank_line()
    {
        List<CsvRow> rows = await ReadAll($"{Header}\n{Row}\n\n{Row}\n");

        Assert.Equal([2, 4], rows.Select(r => r.LineNo));
    }

    [Fact]
    public async Task A_stray_quote_does_not_lose_the_rows_after_it()
    {
        string broken = "MA/2024/001,79,2024-01-12,\"Title,AFG,Afghanistan,N,2024-04-02T09:00:00Z";

        List<CsvRow> rows = await ReadAll($"{Header}\n{broken}\n{Row}\n{Row}\n");

        Assert.Equal([2, 3, 4], rows.Select(r => r.LineNo));
        Assert.Equal(broken, rows[0].RawText);
        Assert.Null(rows[0].Fields);
        Assert.Equal(8, rows[2].Fields?.Length);
    }

    [Theory]
    [InlineData("a,b\"c,d")]
    [InlineData("a,\"b\"c,d")]
    [InlineData("a, \"b\",c")]
    public async Task Misplaced_quotes_break_only_their_own_row(string broken)
    {
        List<CsvRow> rows = await ReadAll($"{Header}\n{broken}\n{Row}\n");

        Assert.Null(rows[0].Fields);
        Assert.Equal(8, rows[1].Fields?.Length);
    }

    [Theory]
    [InlineData("a,\"b \"\"quoted\"\" c\",d", new[] { "a", "b \"quoted\" c", "d" })]
    [InlineData("\"a\",\"\",\"c\"", new[] { "a", "", "c" })]
    [InlineData("a,,", new[] { "a", "", "" })]
    public async Task Splits_quoted_and_empty_fields(string line, string[] expected)
    {
        List<CsvRow> rows = await ReadAll($"{Header}\n{line}\n");

        Assert.Equal(expected, Assert.Single(rows).Fields);
    }

    [Fact]
    public async Task A_row_with_too_few_columns_is_data()
    {
        List<CsvRow> rows = await ReadAll($"{Header}\nMA/2024/001,79\n{Row}\n");

        Assert.Equal([2, 8], rows.Select(r => r.Fields?.Length));
    }

    [Fact]
    public async Task Invalid_utf8_is_a_file_error()
    {
        byte[] bytes = [.. Encoding.UTF8.GetBytes($"{Header}\n"), 0xFF, 0xFE, (byte)'\n'];

        using CsvRowReader reader = new(new MemoryStream(bytes));

        await Assert.ThrowsAsync<ImportFileException>(async () =>
        {
            while (await reader.ReadRowAsync() is not null)
            {
            }
        });
    }

    [Fact]
    public async Task A_nul_character_is_a_file_error()
    {
        await Assert.ThrowsAsync<ImportFileException>(() => ReadAll($"{Header}\nMA\0,1\n"));
    }

    [Theory]
    [InlineData("assembly_vote_id,session")]
    [InlineData("")]
    public async Task A_header_missing_columns_is_a_file_error(string header)
    {
        using CsvRowReader reader = new(new MemoryStream(Encoding.UTF8.GetBytes(header)));

        ImportFileException exception = await Assert.ThrowsAsync<ImportFileException>(() => reader.ReadColumnsAsync());

        Assert.Contains("published_at", exception.Message);
    }

    [Fact]
    public async Task Header_matching_ignores_case_and_order()
    {
        string header = "BALLOT,Session,vote_date,resolution_title,country_code,country_name,assembly_vote_id,published_at";

        using CsvRowReader reader = new(new MemoryStream(Encoding.UTF8.GetBytes(header)));
        CsvColumns columns = await reader.ReadColumnsAsync();

        Assert.Equal((0, 1, 6), (columns.Ballot, columns.Session, columns.VoteRef));
    }

    private static async Task<List<CsvRow>> ReadAll(string csv)
    {
        using CsvRowReader reader = new(new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        await reader.ReadColumnsAsync();

        List<CsvRow> rows = [];
        while (await reader.ReadRowAsync() is { } row)
        {
            rows.Add(row);
        }
        return rows;
    }
}
