using System.Net;
using System.Net.Http.Json;
using Meridian.Api.Features.Snapshots;
using Meridian.Api.Features.Votes;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Meridian.IntegrationTests.Snapshots;

public class PublishSnapshotTests(PostgresFixture postgres, AzuriteFixture azurite)
    : SnapshotTestBase(postgres, azurite), IClassFixture<PostgresFixture>, IClassFixture<AzuriteFixture>
{
    private readonly PostgresFixture _postgres = postgres;

    [Fact]
    public async Task The_admin_publishes_a_completed_snapshot_for_a_quarter()
    {
        long id = await Compute(await LoadVote("MA/6200/001", "Y", 4, ("BRA", "Y")));

        HttpResponseMessage response = await Publish(id, "2030-Q1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PublicationDto publication = (await response.Content.ReadFromJsonAsync<PublicationDto>(Json))!;
        Assert.Equal(("2030-Q1", "Lena", id), (publication.Quarter, publication.PublishedByName, publication.SnapshotId));
        Assert.Equal("2030-Q1", (await GetOk<SnapshotDetailDto>($"/api/snapshots/{id}")).Snapshot.PublishedQuarter);
    }

    [Fact]
    public async Task Publishing_twice_needs_a_reason_and_supersedes_the_first_which_stays_readable()
    {
        long vote = await LoadVote("MA/6201/001", "Y", 4, ("BRA", "Y"));
        long first = await Compute(vote);
        long second = await Compute(vote);
        await Publish(first, "2030-Q2");

        HttpResponseMessage noReason = await Publish(second, "2030-Q2");
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.True((await noReason.Content.ReadFromJsonAsync<HttpValidationProblemDetails>())!.Errors.ContainsKey("reason"));

        HttpResponseMessage response = await Publish(second, "2030-Q2", "Corrected data");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PublicationDto newer = (await response.Content.ReadFromJsonAsync<PublicationDto>(Json))!;

        SnapshotDetailDto old = await GetOk<SnapshotDetailDto>($"/api/snapshots/{first}");
        PublicationDto withdrawn = Assert.Single(old.Publications);
        Assert.NotNull(withdrawn.WithdrawnAt);
        Assert.Equal(newer.Id, withdrawn.SupersededBy);
        Assert.Null(old.Snapshot.PublishedQuarter);
        Assert.NotEmpty(old.Ranking);
        Assert.Equal("2030-Q2", (await GetOk<SnapshotDetailDto>($"/api/snapshots/{second}")).Snapshot.PublishedQuarter);
    }

    [Fact]
    public async Task Publishing_the_same_snapshot_for_its_quarter_again_is_a_409()
    {
        long id = await Compute(await LoadVote("MA/6202/001", "Y", 4, ("BRA", "Y")));
        await Publish(id, "2030-Q3");

        Assert.Equal(HttpStatusCode.Conflict, (await Publish(id, "2030-Q3", "Again")).StatusCode);
    }

    [Fact]
    public async Task Two_publishes_at_once_leave_one_current_publication()
    {
        long vote = await LoadVote("MA/6203/001", "Y", 4, ("BRA", "Y"));
        long first = await Compute(vote);
        long second = await Compute(vote);

        HttpResponseMessage[] responses = await Task.WhenAll(Publish(first, "2030-Q4", "A"), Publish(second, "2030-Q4", "B"));

        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        await using AppDbContext db = _postgres.CreateDbContext();
        Assert.Equal(1, await db.SnapshotPublications.CountAsync(p => p.Quarter == "2030-Q4" && p.WithdrawnAt == null));
    }

    [Fact]
    public async Task A_snapshot_that_has_not_finished_cannot_be_published()
    {
        long vote = await LoadVote("MA/6204/001", "Y", 4, ("BRA", "Y"));
        long id = await CreateSnapshot(new { name = "Waiting", voteIds = new[] { vote } });

        Assert.Equal(HttpStatusCode.Conflict, (await Publish(id, "2031-Q1")).StatusCode);
    }

    [Fact]
    public async Task An_analyst_gets_a_403()
    {
        long id = await Compute(await LoadVote("MA/6205/001", "Y", 4, ("BRA", "Y")));

        Assert.Equal(HttpStatusCode.Forbidden, (await Post($"/api/snapshots/{id}/publish", new { quarter = "2031-Q2" }, Dana)).StatusCode);
    }

    [Theory]
    [InlineData("2024")]
    [InlineData("2024-Q5")]
    [InlineData("Q1-2024")]
    [InlineData("2024-Q1\n")]
    public async Task A_bad_quarter_gets_a_400(string quarter)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Publish(1, quarter)).StatusCode);
    }

    [Fact]
    public async Task An_unknown_snapshot_gets_a_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Publish(999999, "2031-Q3")).StatusCode);
    }

    [Fact]
    public async Task The_timeline_shows_publications_of_snapshots_that_include_the_vote()
    {
        long included = await LoadVote("MA/6206/001", "Y", 4, ("BRA", "Y"));
        long other = await LoadVote("MA/6206/002", "Y", 4, ("BRA", "Y"));
        long id = await Compute(included);
        await Publish(id, "2031-Q4");

        List<TimelineEntryDto> timeline = await GetOk<List<TimelineEntryDto>>($"/api/votes/{included}/timeline");

        TimelineEntryDto published = timeline[0];
        Assert.Equal(("Published", "2031-Q4", "Lena", id), (published.Type, published.Target, published.ActorName, published.SnapshotId!.Value));
        Assert.DoesNotContain(await GetOk<List<TimelineEntryDto>>($"/api/votes/{other}/timeline"), t => t.Type == "Published");
    }

    [Fact]
    public async Task The_timeline_shows_when_a_publication_was_withdrawn()
    {
        long replaced = await LoadVote("MA/6207/001", "Y", 4, ("BRA", "Y"));
        long replacing = await LoadVote("MA/6207/002", "Y", 4, ("BRA", "Y"));
        long first = await Compute(replaced);
        long second = await Compute(replacing);
        await Publish(first, "2032-Q1");
        await Publish(second, "2032-Q1", "Corrected weights");

        List<TimelineEntryDto> timeline = await GetOk<List<TimelineEntryDto>>($"/api/votes/{replaced}/timeline");

        Assert.Equal(["Withdrawn", "Published"], timeline.Take(2).Select(t => t.Type));
        Assert.Equal(("2032-Q1", "Lena", first), (timeline[0].Target, timeline[0].ActorName, timeline[0].SnapshotId!.Value));
        Assert.DoesNotContain(await GetOk<List<TimelineEntryDto>>($"/api/votes/{replacing}/timeline"), t => t.Type == "Withdrawn");
    }

    [Fact]
    public async Task A_snapshot_published_for_two_quarters_lists_the_latest()
    {
        long id = await Compute(await LoadVote("MA/6208/001", "Y", 4, ("BRA", "Y")));
        await Publish(id, "2032-Q3");
        await Publish(id, "2032-Q2");

        List<SnapshotSummaryDto> snapshots = await GetOk<List<SnapshotSummaryDto>>("/api/snapshots");

        Assert.Equal("2032-Q2", snapshots.Single(s => s.Id == id).PublishedQuarter);
    }

    private Task<HttpResponseMessage> Publish(long id, string quarter, string? reason = null)
    {
        return Post($"/api/snapshots/{id}/publish", new { quarter, reason }, Lena);
    }
}
