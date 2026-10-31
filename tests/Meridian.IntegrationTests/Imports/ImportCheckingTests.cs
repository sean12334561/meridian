using Meridian.Api.Features.Imports.Classification;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Meridian.IntegrationTests.Imports;

public class ImportCheckingTests(PostgresFixture postgres, AzuriteFixture azurite)
    : ImportPipelineTestBase(postgres, azurite), IClassFixture<PostgresFixture>, IClassFixture<AzuriteFixture>
{
    private const string Details = "79,2024-02-06,Title";
    private const string Published = "2024-04-02T09:00:00Z";

    [Fact]
    public async Task Exact_duplicates_keep_the_first_line_and_the_phase_moves_on()
    {
        Import import = await RunFile(
            Row("MA/2024/001", "BRA", "Y"),
            Row("MA/2024/001", "IND", "N"),
            Row("MA/2024/001", "BRA", "Y"),
            Row("MA/2024/001", "BRA", "Y"));

        Assert.Equal(ImportPhase.Applying, (await Reload(import)).Phase);
        Assert.Equal(
            [(2, null, null), (3, null, null), (4, RowOutcome.Duplicate, "Same as line 2"), (5, RowOutcome.Duplicate, "Same as line 2")],
            await Outcomes(import));
    }

    [Fact]
    public async Task Rows_that_differ_only_in_name_or_publication_count_as_duplicates()
    {
        // Same vote, country and ballot: one value either way, and ING-11 says a name is not a difference
        Import import = await RunFile(
            "MA/2024/001,79,2024-02-06,Title,TUR,Turkey,Y,2024-04-02T09:00:00Z",
            "MA/2024/001,79,2024-02-06,Title,TUR,Türkiye,Y,2024-04-02T09:00:00Z",
            "MA/2024/001,79,2024-02-06,Title,TUR,Turkey,Y,2024-04-20T09:00:00Z");

        Assert.Equal(
            [(2, null, null), (3, RowOutcome.Duplicate, "Same as line 2"), (4, RowOutcome.Duplicate, "Same as line 2")],
            await Outcomes(import));
    }

    [Fact]
    public async Task Different_ballots_for_one_country_store_neither()
    {
        Import import = await RunFile(
            Row("MA/2024/001", "FRA", "Y"),
            Row("MA/2024/001", "FRA", "N"),
            Row("MA/2024/001", "BRA", "Y"));

        const string reason = "Different ballots for this country in the file";
        Assert.Equal(
            [(2, RowOutcome.Contradiction, reason), (3, RowOutcome.Contradiction, reason), (4, null, null)],
            await Outcomes(import));
    }

    [Fact]
    public async Task A_repeated_row_inside_a_contradiction_stays_a_duplicate()
    {
        Import import = await RunFile(
            Row("MA/2024/001", "FRA", "Y"),
            Row("MA/2024/001", "FRA", "Y"),
            Row("MA/2024/001", "FRA", "N"));

        Assert.Equal(
            [RowOutcome.Contradiction, RowOutcome.Duplicate, RowOutcome.Contradiction],
            (await Rows(import)).Select(r => r.Outcome!.Value));
    }

    [Fact]
    public async Task The_same_country_in_two_votes_is_neither_duplicate_nor_contradiction()
    {
        Import import = await RunFile(
            Row("MA/2024/001", "FRA", "Y"),
            Row("MA/2024/002", "FRA", "Y"),
            Row("MA/2024/003", "FRA", "N"));

        Assert.All(await Rows(import), r => Assert.Null(r.Outcome));
    }

    [Fact]
    public async Task Invalid_rows_keep_their_outcome_and_are_not_compared()
    {
        Import import = await RunFile(
            Row("MA/2024/001", "FRA", "Y"),
            Row("MA/2024/001", "FRA", "Yes"),
            Row("MA/2024/001", "FRA", "Yes"));

        Assert.Equal(
            [(2, null, null), (3, RowOutcome.Invalid, "Invalid ballot 'Yes'"), (4, RowOutcome.Invalid, "Invalid ballot 'Yes'")],
            await Outcomes(import));
    }

    [Theory]
    [InlineData("80,2024-02-06,Title")]
    [InlineData("79,2024-02-07,Title")]
    [InlineData("79,2024-02-06,Other")]
    public async Task A_new_vote_with_differing_details_is_a_contradiction_on_all_its_rows(string otherDetails)
    {
        Import import = await RunFile(
            Row("MA/2024/001", "BRA", "Y"),
            Row("MA/2024/001", "IND", "N", otherDetails),
            Row("MA/2024/001", "BRA", "Y"),
            Row("MA/2024/002", "BRA", "Y"));

        const string reason = "Vote details differ between rows of this file";
        Assert.Equal(
            [
                (2, RowOutcome.Contradiction, reason), (3, RowOutcome.Contradiction, reason),
                (4, RowOutcome.Contradiction, reason), (5, null, null),
            ],
            await Outcomes(import));
    }

    [Fact]
    public async Task A_duplicate_differing_only_in_title_still_reveals_the_contradiction()
    {
        Import import = await RunFile(
            Row("MA/2024/001", "BRA", "Y"),
            Row("MA/2024/001", "BRA", "Y", "79,2024-02-06,Other"));

        Assert.All(await Rows(import), r => Assert.Equal(RowOutcome.Contradiction, r.Outcome));
    }

    [Fact]
    public async Task An_existing_vote_with_differing_details_keeps_its_ballot_rows()
    {
        Import earlier = await RunFile(Row("MA/2024/050", "BRA", "Y"));
        await using (AppDbContext db = postgres.CreateDbContext())
        {
            db.Votes.Add(new Vote
            {
                VoteRef = "MA/2024/050",
                Session = 79,
                VoteDate = new DateOnly(2024, 2, 6),
                Title = "Title",
                DetailsSourceImportId = earlier.Id,
            });
            await db.SaveChangesAsync();
        }

        Import import = await RunFile(
            Row("MA/2024/050", "BRA", "Y"),
            Row("MA/2024/050", "IND", "N", "79,2024-02-06,Other"));

        Assert.All(await Rows(import), r => Assert.Null(r.Outcome));
    }

    [Fact]
    public async Task Running_the_checks_twice_changes_nothing()
    {
        Import import = await RunFile(
            Row("MA/2024/001", "BRA", "Y"),
            Row("MA/2024/001", "BRA", "Y"),
            Row("MA/2024/001", "FRA", "Y"),
            Row("MA/2024/001", "FRA", "N"));
        List<(int, RowOutcome?, string?)> first = await Outcomes(import);

        await new InFileChecks(new FixtureDbContextFactory(postgres)).RunAsync(import.Id, default);

        Assert.Equal(first, await Outcomes(import));
    }

    [Fact]
    public async Task A_retry_at_the_checking_phase_does_not_parse_again()
    {
        Import import = await RunFile(Row("MA/2024/099", "BRA", "Y"));
        await using (AppDbContext db = postgres.CreateDbContext())
        {
            await db.Imports.Where(i => i.Id == import.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.Phase, ImportPhase.Checking));
        }

        await Run(await Reload(import));

        Assert.Equal(ImportPhase.Done, (await Reload(import)).Phase);
        Assert.Equal(1, await CountRows(import));
    }

    private static string Row(string voteRef, string countryCode, string ballot, string details = Details)
    {
        return $"{voteRef},{details},{countryCode},Name,{ballot},{Published}";
    }

    private async Task<Import> RunFile(params string[] rows)
    {
        Import import = await CreateImport($"{Header}\n{string.Join('\n', rows)}\n");
        await RunChecks(import);
        return import;
    }

    private async Task<List<(int, RowOutcome?, string?)>> Outcomes(Import import)
    {
        return (await Rows(import)).Select(r => (r.LineNo, r.Outcome, r.Reason)).ToList();
    }
}
