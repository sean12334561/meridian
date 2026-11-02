using System.Net;
using System.Security.Cryptography;
using System.Text;
using Meridian.Api.Features.Conflicts;
using Meridian.Api.Features.Imports;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Paging;
using Meridian.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Meridian.IntegrationTests.Imports;

public class ImportReportTests(PostgresFixture postgres, AzuriteFixture azurite)
    : ApiTestBase(postgres, azurite), IClassFixture<PostgresFixture>, IClassFixture<AzuriteFixture>
{
    private const string Published = "2024-04-02T09:00:00Z";

    private readonly PostgresFixture _postgres = postgres;

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
    public async Task Details_already_decided_are_reported_as_decided()
    {
        await RunFile(Row("MA/2304/001", "BRA", "Y"));
        string changed = Row("MA/2304/001", "BRA", "Y", title: "Other");
        await RunFile(changed);
        await Reject("MA/2304/001");

        Import reupload = await RunFile(changed);

        ImportReportDto report = await GetOk<ImportReportDto>($"/api/imports/{reupload.Id}/report");
        Assert.Equal(VoteDetailsStatus.PreviouslyDecided, report.Votes.Single().Details);
        Assert.Equal(0, report.ConflictsOpened);
    }

    [Fact]
    public async Task Changed_details_open_a_conflict_even_if_every_ballot_contradicts_itself()
    {
        await RunFile(Row("MA/2305/001", "BRA", "Y"));

        Import import = await RunFile(Row("MA/2305/001", "BRA", "Y", title: "Other"), Row("MA/2305/001", "BRA", "N", title: "Other"));

        ImportReportDto report = await GetOk<ImportReportDto>($"/api/imports/{import.Id}/report");
        Assert.Equal((VoteDetailsStatus.Conflict, 1, 2), (report.Votes.Single().Details, report.ConflictsOpened, report.Totals.Contradiction));
    }

    [Fact]
    public async Task A_report_names_the_first_earlier_import_of_the_same_file()
    {
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())));
        Import first = await RunWithHash(sha256, Row("MA/2306/001", "BRA", "Y"));
        await RunWithHash(sha256, Row("MA/2306/001", "BRA", "Y"));
        Import third = await RunWithHash(sha256, Row("MA/2306/001", "BRA", "Y"));

        ImportReportDto firstReport = await GetOk<ImportReportDto>($"/api/imports/{first.Id}/report");
        ImportReportDto thirdReport = await GetOk<ImportReportDto>($"/api/imports/{third.Id}/report");

        Assert.Null(firstReport.IdenticalTo);
        Assert.Equal((first.Id, "votes.csv"), (thirdReport.IdenticalTo!.ImportId, thirdReport.IdenticalTo.FileName));
    }

    [Fact]
    public async Task A_retry_after_completion_keeps_the_stored_report()
    {
        Import import = await RunFile(Row("MA/2307/001", "BRA", "Y"));
        DateTimeOffset? completedAt = (await Reload(import)).CompletedAt;

        await Run(import);

        Assert.Equal(completedAt, (await Reload(import)).CompletedAt);
    }

    [Fact]
    public async Task A_failure_after_completion_leaves_the_import_completed()
    {
        Import import = await RunFile(Row("MA/2308/001", "BRA", "Y"));

        await Handler().OnFailedAsync(Job(import), "Worker lost", default);

        Import stored = await Reload(import);
        Assert.Equal((ImportStatus.Completed, null), (stored.Status, stored.Error));
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
    [InlineData("page=2147483647&pageSize=100")]
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

    private async Task<Import> RunWithHash(string sha256, params string[] rows)
    {
        Import import = NewImport(Guid.NewGuid());
        import.Sha256 = sha256;
        await Upload(import, Encoding.UTF8.GetBytes($"{Header}\n{string.Join('\n', rows)}\n"));
        await Save(import);
        await Run(import);
        return import;
    }

    private async Task Reject(string voteRef)
    {
        await using AppDbContext db = _postgres.CreateDbContext();
        long conflictId = await db.Conflicts
            .Where(c => c.Status == ConflictStatus.Pending && db.Votes.Any(v => v.Id == c.VoteId && v.VoteRef == voteRef))
            .Select(c => c.Id)
            .SingleAsync();

        HttpResponseMessage response = await Post($"/api/conflicts/{conflictId}/decision", new DecideConflict.Request { Decision = Decision.Reject });
        response.EnsureSuccessStatusCode();
    }

    private static string Row(string voteRef, string country, string ballot, string title = "Title")
    {
        return $"{voteRef},79,2024-02-06,{title},{country},Name,{ballot},{Published}";
    }
}
