using System.Net;
using System.Net.Http.Json;
using Meridian.Api.Features.Snapshots;
using Meridian.Api.Shared.Paging;
using Meridian.IntegrationTests.Infrastructure;

namespace Meridian.IntegrationTests.Snapshots;

public class SnapshotVoteListTests(PostgresFixture postgres, AzuriteFixture azurite)
    : SnapshotTestBase(postgres, azurite), IClassFixture<PostgresFixture>, IClassFixture<AzuriteFixture>
{
    private async Task<SnapshotPreviewDto> Preview(object request)
    {
        HttpResponseMessage response = await Post("/api/snapshots/preview", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SnapshotPreviewDto>(Json))!;
    }

    private static string[] Refs(SnapshotPreviewDto preview)
    {
        return preview.Votes.Items.Select(v => v.VoteRef).ToArray();
    }

    [Fact]
    public async Task The_preview_covers_the_same_votes_as_creating_the_snapshot()
    {
        await RunFile(
            "MA/6200/001,6200,2024-02-06,Title,BRA,Name,Y," + Published,
            "MA/6200/002,6201,2024-02-07,Title,BRA,Name,Y," + Published,
            "MA/6200/003,6202,2024-02-08,Title,BRA,Name,Y," + Published);
        foreach (string voteRef in new[] { "MA/6200/001", "MA/6200/002", "MA/6200/003" })
        {
            (await Put($"/api/votes/{await VoteId(voteRef)}/position", new { desiredOutcome = "Y", weight = 1 })).EnsureSuccessStatusCode();
        }

        // The session matches one vote and the list adds another: the selection is the two together
        object selection = new { session = 6200, voteRefs = new[] { "MA/6200/003" } };
        SnapshotPreviewDto preview = await Preview(selection);

        long id = await CreateSnapshot(new { name = "Same", session = 6200, voteRefs = new[] { "MA/6200/003" } });
        await RunSnapshotJob(id);
        PagedResult<SnapshotVoteDto> created = await GetOk<PagedResult<SnapshotVoteDto>>($"/api/snapshots/{id}/votes");

        Assert.Equal(["MA/6200/003", "MA/6200/001"], Refs(preview));
        Assert.Equal(Refs(preview), created.Items.Select(v => v.VoteRef));
        Assert.Equal(0, preview.MissingPositions);
    }

    [Fact]
    public async Task The_preview_counts_missing_positions_and_reports_unknown_references_without_failing()
    {
        await LoadVote("MA/6201/001", "Y", 4, ("BRA", "Y"));
        await RunFile(Row("MA/6201/002", "BRA", "Y"));

        SnapshotPreviewDto preview = await Preview(new { voteRefs = new[] { "MA/6201/001", "MA/6201/002", "MA/6201/999" } });

        Assert.Equal(2, preview.Votes.Total);
        Assert.Equal(1, preview.MissingPositions);
        Assert.Equal(["MA/6201/999"], preview.UnknownVoteRefs);
        Assert.True(preview.Votes.Items.Single(v => v.VoteRef == "MA/6201/002").MissingPosition);
    }

    [Fact]
    public async Task The_preview_pages_and_counts_the_whole_selection()
    {
        await RunFile(Row("MA/6202/001", "BRA", "Y"), Row("MA/6202/002", "BRA", "Y"), Row("MA/6202/003", "BRA", "Y"));
        string[] refs = ["MA/6202/001", "MA/6202/002", "MA/6202/003"];

        SnapshotPreviewDto preview = await Preview(new { voteRefs = refs, page = 2, pageSize = 2 });

        Assert.Equal(3, preview.Votes.Total);
        Assert.Equal(3, preview.MissingPositions);
        Assert.Equal(["MA/6202/003"], Refs(preview));
    }

    [Fact]
    public async Task A_preview_needs_a_selection_and_a_valid_range()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("/api/snapshots/preview", new { })).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await Post("/api/snapshots/preview", new { from = "2024-03-01", to = "2024-02-01" })).StatusCode);
    }

    [Fact]
    public async Task A_snapshot_lists_the_position_and_weight_it_was_calculated_with()
    {
        long vote = await LoadVote("MA/6203/001", "Y", 4, ("BRA", "Y"));
        long id = await Compute(vote);

        (await Put($"/api/votes/{vote}/position", new { desiredOutcome = "N", weight = 5 })).EnsureSuccessStatusCode();

        SnapshotVoteDto listed = (await GetOk<PagedResult<SnapshotVoteDto>>($"/api/snapshots/{id}/votes")).Items.Single();
        Assert.Equal(("MA/6203/001", "Y", (int?)4), (listed.VoteRef, listed.Position, listed.Weight));
    }

    [Fact]
    public async Task The_snapshot_votes_are_paged_and_a_left_out_vote_is_not_listed()
    {
        long a = await LoadVote("MA/6204/001", "Y", 4, ("BRA", "Y"));
        long b = await LoadVote("MA/6204/002", "N", 2, ("BRA", "Y"));
        await RunFile(Row("MA/6204/003", "BRA", "Y"));

        long id = await CreateSnapshot(new
        {
            name = "Left out",
            voteRefs = new[] { "MA/6204/001", "MA/6204/002", "MA/6204/003" },
            excludeMissingPositions = true,
        });
        await RunSnapshotJob(id);

        PagedResult<SnapshotVoteDto> page = await GetOk<PagedResult<SnapshotVoteDto>>($"/api/snapshots/{id}/votes?page=2&pageSize=1");

        Assert.Equal(2, page.Total);
        Assert.Equal([b], page.Items.Select(v => v.Id));
        Assert.NotEqual(a, page.Items[0].Id);
    }

    [Fact]
    public async Task A_pending_snapshot_lists_its_votes_with_no_inputs_yet()
    {
        long vote = await LoadVote("MA/6205/001", "Y", 4, ("BRA", "Y"));
        long id = await CreateSnapshot(new { name = "Waiting", voteIds = new[] { vote } });

        SnapshotVoteDto listed = (await GetOk<PagedResult<SnapshotVoteDto>>($"/api/snapshots/{id}/votes")).Items.Single();

        Assert.Equal((vote, null, null), (listed.Id, listed.Position, listed.Weight));
    }

    [Fact]
    public async Task Snapshot_votes_of_an_unknown_snapshot_get_a_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Get("/api/snapshots/999999/votes")).StatusCode);
    }
}
