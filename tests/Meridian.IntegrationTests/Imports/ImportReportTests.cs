using System.Net;
using Meridian.Api.Features.Imports;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Paging;
using Meridian.IntegrationTests.Infrastructure;

namespace Meridian.IntegrationTests.Imports;

public class ImportReportTests(PostgresFixture postgres, AzuriteFixture azurite)
    : ApiTestBase(postgres, azurite), IClassFixture<PostgresFixture>, IClassFixture<AzuriteFixture>
{
    private const string Published = "2024-04-02T09:00:00Z";

    [Fact]
    public async Task The_report_has_totals_and_a_result_per_vote()
    {
        Import import = await RunFile(
            Row("MA/2300/001", "BRA", "Y"),
            Row("MA/2300/001", "bra", "Y"),
            Row("MA/2300/001", "FRA", "Y"),
            Row("MA/2300/001", "FRA", "N"),
            Row("MA/2300/001", "FRN", "Y"),
            Row("MA/2300/002", "FRN", "Y"),
            "too,few");

        ImportReportDto report = await GetOk<ImportReportDto>($"/api/imports/{import.Id}/report");

        Assert.Equal(7, report.Totals.Rows);
        Assert.Equal(
            (1, 1, 2, 3, 1),
            (report.Totals.New, report.Totals.Duplicate, report.Totals.Contradiction, report.Totals.Invalid, report.Totals.Normalized));
        Assert.Equal(["MA/2300/001", "MA/2300/002"], report.Votes.Select(v => v.VoteRef));
        Assert.Equal(
            (1, 1, 2, 1, VoteDetailsStatus.Same),
            (report.Votes[0].New, report.Votes[0].Duplicate, report.Votes[0].Contradiction, report.Votes[0].Invalid, report.Votes[0].Details));
        Assert.Equal(VoteDetailsStatus.NotStored, report.Votes[1].Details);
    }

    [Fact]
    public async Task The_report_counts_conflicts_opened_and_already_pending()
    {
        await RunFile(Row("MA/2301/001", "BRA", "Y"));
        string changed = Row("MA/2301/001", "BRA", "A", title: "Other");

        Import first = await RunFile(changed);
        Import second = await RunFile(changed);

        ImportReportDto firstReport = await GetOk<ImportReportDto>($"/api/imports/{first.Id}/report");
        Assert.Equal((2, 1, 0), (firstReport.ConflictsOpened, firstReport.Totals.Conflict, firstReport.Totals.AlreadyPending));
        Assert.Equal(VoteDetailsStatus.Conflict, firstReport.Votes.Single().Details);

        ImportReportDto secondReport = await GetOk<ImportReportDto>($"/api/imports/{second.Id}/report");
        Assert.Equal((0, 1, 1), (secondReport.ConflictsOpened, secondReport.Totals.Conflict, secondReport.Totals.AlreadyPending));
    }

    [Fact]
    public async Task Details_that_differ_inside_the_file_are_reported()
    {
        await RunFile(Row("MA/2302/001", "BRA", "Y"));

        Import import = await RunFile(Row("MA/2302/001", "BRA", "Y"), Row("MA/2302/001", "IND", "Y", title: "Other"));

        ImportReportDto report = await GetOk<ImportReportDto>($"/api/imports/{import.Id}/report");
        Assert.Equal(VoteDetailsStatus.DiffersInFile, report.Votes.Single().Details);
    }

    [Fact]
    public async Task An_unfinished_import_has_no_report()
    {
        Import import = await CreateImport($"{Header}\n");

        Assert.Equal(HttpStatusCode.NotFound, (await Get($"/api/imports/{import.Id}/report")).StatusCode);
    }

    [Fact]
    public async Task Rows_can_be_filtered_and_paged()
    {
        Import import = await RunFile(
            Row("MA/2303/001", "BRA", "Y"),
            Row("MA/2303/001", "FRN", "Y"),
            Row("MA/2303/001", "XXX", "Y"),
            Row("MA/2303/001", " ind", "Y"));

        PagedResult<ImportRowDto> invalid = await GetOk<PagedResult<ImportRowDto>>(
            $"/api/imports/{import.Id}/rows?outcome=Invalid&page=2&pageSize=1");

        Assert.Equal((2, 2, 1), (invalid.Total, invalid.Page, invalid.PageSize));
        ImportRowDto row = Assert.Single(invalid.Items);
        Assert.Equal(4, row.LineNo);
        Assert.Contains("XXX", row.RawText);
        Assert.NotNull(row.Reason);

        PagedResult<ImportRowDto> normalized = await GetOk<PagedResult<ImportRowDto>>($"/api/imports/{import.Id}/rows?normalized=true");
        Assert.Equal(5, Assert.Single(normalized.Items).LineNo);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task Bad_paging_gets_a_400(string query)
    {
        Import import = await CreateImport($"{Header}\n");

        Assert.Equal(HttpStatusCode.BadRequest, (await Get($"/api/imports/{import.Id}/rows?{query}")).StatusCode);
    }

    [Fact]
    public async Task Rows_of_an_unknown_import_get_a_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Get($"/api/imports/{Guid.NewGuid()}/rows")).StatusCode);
    }

    private static string Row(string voteRef, string country, string ballot, string title = "Title")
    {
        return $"{voteRef},79,2024-02-06,{title},{country},Name,{ballot},{Published}";
    }
}
