using System.Net;
using System.Net.Http.Json;
using Meridian.Api.Features.Votes;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Meridian.IntegrationTests.Votes;

public class SetPositionTests(PostgresFixture postgres, AzuriteFixture azurite)
    : ApiTestBase(postgres, azurite), IClassFixture<PostgresFixture>, IClassFixture<AzuriteFixture>
{
    private readonly PostgresFixture _postgres = postgres;

    [Fact]
    public async Task The_admin_sets_a_position_and_it_is_recorded()
    {
        long id = await CreateVote("MA/5000/001");

        HttpResponseMessage response = await Put($"/api/votes/{id}/position", new { desiredOutcome = "Y", weight = 4 });

        VoteDto vote = (await response.Content.ReadFromJsonAsync<VoteDto>(Json))!;
        Assert.Equal(("Y", (int?)4, false), (vote.Position, vote.Weight, vote.MissingPosition));

        await Put($"/api/votes/{id}/position", new { desiredOutcome = "N", weight = 2 });

        await using AppDbContext db = _postgres.CreateDbContext();
        List<VoteHistoryEntry> history = await db.VoteHistory
            .Where(h => h.VoteId == id && h.Type == HistoryType.PositionChanged)
            .OrderBy(h => h.Id)
            .ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Equal(Lena, history[1].ActorId);
        Assert.Contains("\"Y\"", history[1].OldValue);
        Assert.Contains("\"N\"", history[1].NewValue);
    }

    [Fact]
    public async Task Setting_the_same_values_writes_no_history()
    {
        long id = await CreateVote("MA/5001/001");
        await Put($"/api/votes/{id}/position", new { desiredOutcome = "Y", weight = 4 });

        Assert.Equal(HttpStatusCode.OK, (await Put($"/api/votes/{id}/position", new { desiredOutcome = "Y", weight = 4 })).StatusCode);

        await using AppDbContext db = _postgres.CreateDbContext();
        Assert.Equal(1, await db.VoteHistory.CountAsync(h => h.VoteId == id && h.Type == HistoryType.PositionChanged));
    }

    [Fact]
    public async Task An_analyst_gets_a_403()
    {
        long id = await CreateVote("MA/5002/001");

        Assert.Equal(HttpStatusCode.Forbidden, (await Put($"/api/votes/{id}/position", new { desiredOutcome = "Y", weight = 4 }, Dana)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post("/api/votes/positions/bulk", new { desiredOutcome = "Y", weight = 4 }, Dana)).StatusCode);
    }

    [Theory]
    [InlineData("Y", 7, "weight")]
    [InlineData("Y", 6, "weight")]
    [InlineData("Y", 0, "weight")]
    [InlineData("A", 3, "desiredOutcome")]
    public async Task Bad_values_get_an_error_on_the_field(string outcome, int weight, string field)
    {
        long id = await CreateVote($"MA/5003/{weight:D2}{outcome.Length}");

        HttpResponseMessage response = await Put($"/api/votes/{id}/position", new { desiredOutcome = outcome, weight });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        HttpValidationProblemDetails problem = (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>())!;
        Assert.True(problem.Errors.ContainsKey(field));
    }

    [Fact]
    public async Task An_unknown_vote_gets_a_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Put("/api/votes/999999/position", new { desiredOutcome = "Y", weight = 4 })).StatusCode);
    }

    [Fact]
    public async Task Bulk_fills_only_missing_positions_by_default()
    {
        long byHand = await CreateVote("MA/5004/001");
        long missing = await CreateVote("MA/5004/002");
        long other = await CreateVote("MA/5005/001");
        await Put($"/api/votes/{byHand}/position", new { desiredOutcome = "N", weight = 5 });

        HttpResponseMessage response = await Post("/api/votes/positions/bulk", new { q = "MA/5004", desiredOutcome = "Y", weight = 1 }, Lena);

        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<BulkPositionResultDto>(Json))!.Updated);
        await using AppDbContext db = _postgres.CreateDbContext();
        Assert.Equal(("N", (int?)5), await Position(db, byHand));
        Assert.Equal(("Y", (int?)1), await Position(db, missing));
        Assert.Equal(((string?)null, (int?)null), await Position(db, other));
        Assert.Equal(1, await db.VoteHistory.CountAsync(h => h.VoteId == missing && h.Type == HistoryType.PositionChanged));
    }

    [Fact]
    public async Task Bulk_can_overwrite_when_asked()
    {
        long byHand = await CreateVote("MA/5006/001");
        await Put($"/api/votes/{byHand}/position", new { desiredOutcome = "N", weight = 5 });

        await Post("/api/votes/positions/bulk", new { q = "MA/5006", desiredOutcome = "Y", weight = 1, onlyMissing = false }, Lena);

        await using AppDbContext db = _postgres.CreateDbContext();
        Assert.Equal(("Y", (int?)1), await Position(db, byHand));
    }

    [Fact]
    public async Task Bulk_skips_votes_that_already_have_those_values()
    {
        long same = await CreateVote("MA/5007/001");
        long different = await CreateVote("MA/5007/002");
        await Put($"/api/votes/{same}/position", new { desiredOutcome = "Y", weight = 3 });
        await Put($"/api/votes/{different}/position", new { desiredOutcome = "Y", weight = 2 });

        HttpResponseMessage response = await Post("/api/votes/positions/bulk", new { q = "MA/5007", desiredOutcome = "Y", weight = 3, onlyMissing = false }, Lena);

        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<BulkPositionResultDto>(Json))!.Updated);
        await using AppDbContext db = _postgres.CreateDbContext();
        Assert.Equal(1, await db.VoteHistory.CountAsync(h => h.VoteId == same && h.Type == HistoryType.PositionChanged));
        Assert.Equal(2, await db.VoteHistory.CountAsync(h => h.VoteId == different && h.Type == HistoryType.PositionChanged));
    }

    [Fact]
    public async Task Bulk_validates_like_the_single_action()
    {
        HttpResponseMessage response = await Post("/api/votes/positions/bulk", new { desiredOutcome = "Y", weight = 9 }, Lena);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<long> CreateVote(string voteRef)
    {
        await RunFile($"{voteRef},79,2024-02-06,Title,BRA,Brazil,Y,2024-04-02T09:00:00Z");
        await using AppDbContext db = _postgres.CreateDbContext();
        return await db.Votes.Where(v => v.VoteRef == voteRef).Select(v => v.Id).SingleAsync();
    }

    private static async Task<(string?, int?)> Position(AppDbContext db, long id)
    {
        Vote vote = await db.Votes.AsNoTracking().SingleAsync(v => v.Id == id);
        return (vote.Position, vote.Weight);
    }
}
