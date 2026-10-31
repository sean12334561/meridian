using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Api.Shared.Auth;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Seeding;
using Meridian.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.IntegrationTests.Auth;

public class AuthorizationTests(PostgresFixture postgres)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string Dana = "1";
    private const string Omer = "2";
    private const string Lena = "3";
    private const string ProblemJson = "application/problem+json";

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await using AppDbContext db = postgres.CreateDbContext();
        await new Seeder(db, NullLogger<Seeder>.Instance).SeedAsync("member_states.csv");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);
            b.ConfigureTestServices(s =>
                s.AddControllers().AddApplicationPart(typeof(AuthorizationTests).Assembly));
        });
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-number")]
    [InlineData("99999999999999")]
    [InlineData("999")]
    public async Task Missing_or_unknown_user_gets_a_401_problem(string? userId)
    {
        HttpResponseMessage response = await Send("/test/auth/analyst", userId);

        await AssertProblem(response, HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(Dana)]
    [InlineData(Omer)]
    public async Task Analyst_gets_a_403_problem_on_admin_routes(string userId)
    {
        HttpResponseMessage response = await Send("/test/auth/admin", userId);

        await AssertProblem(response, HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("/test/auth/analyst", Dana)]
    [InlineData("/test/auth/analyst", Omer)]
    [InlineData("/test/auth/analyst", Lena)]
    [InlineData("/test/auth/admin", Lena)]
    public async Task Allowed_roles_get_through(string route, string userId)
    {
        HttpResponseMessage response = await Send(route, userId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(Dana, 1, Role.Analyst)]
    [InlineData(Lena, 3, Role.Admin)]
    public async Task Handlers_see_the_acting_user(string userId, int id, Role role)
    {
        HttpResponseMessage response = await Send("/test/auth/analyst", userId);

        Assert.Equal($"{id}:{role}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Users_list_is_open_so_the_switcher_can_load()
    {
        HttpResponseMessage response = await Send("/api/users", null);

        JsonElement[] users = await response.Content.ReadFromJsonAsync<JsonElement[]>() ?? [];
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [(1, "Dana", "Analyst"), (2, "Omer", "Analyst"), (3, "Lena", "Admin")],
            users.Select(u => (
                u.GetProperty("id").GetInt32(),
                u.GetProperty("name").GetString(),
                u.GetProperty("role").GetString())));
    }

    [Fact]
    public async Task Me_returns_the_current_identity()
    {
        HttpResponseMessage response = await Send("/api/me", Lena);

        JsonElement me = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, me.GetProperty("id").GetInt32());
        Assert.Equal("Lena", me.GetProperty("name").GetString());
        Assert.Equal("Admin", me.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Me_without_a_user_is_a_401_problem()
    {
        HttpResponseMessage response = await Send("/api/me", null);

        await AssertProblem(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Health_stays_open()
    {
        HttpResponseMessage response = await Send("/health", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Contract_documents_roles_as_text_and_the_error_format()
    {
        HttpResponseMessage response = await Send("/openapi/v1.json", null);

        JsonElement contract = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement me = contract.GetProperty("paths").GetProperty("/api/me").GetProperty("get").GetProperty("responses");
        JsonElement role = contract.GetProperty("components").GetProperty("schemas").GetProperty("Role");
        Assert.Equal(["Analyst", "Admin"], role.GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.True(me.GetProperty("200").GetProperty("content").TryGetProperty("application/json", out _));
        Assert.True(me.GetProperty("401").GetProperty("content").TryGetProperty(ProblemJson, out _));
    }

    private async Task<HttpResponseMessage> Send(string route, string? userId)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, route);
        if (userId is not null)
        {
            request.Headers.Add(HeaderAuthenticationHandler.HeaderName, userId);
        }

        return await _client.SendAsync(request);
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)status, body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("correlationId").GetString()));
    }
}

[ApiController, Route("test/auth")]
public class AuthTestController(ICurrentUser user) : ControllerBase
{
    [HttpGet("analyst"), Authorize(Policy = Policies.Analyst)]
    public string Analyst() => $"{user.Id}:{user.Role}";

    [HttpGet("admin"), Authorize(Policy = Policies.Admin)]
    public string Admin() => $"{user.Id}:{user.Role}";
}
