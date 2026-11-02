using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Meridian.Api.Shared.Errors;
using Meridian.Api.Shared.Observability;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.IntegrationTests.Errors;

public class ErrorResponseTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ProblemJson = "application/problem+json";

    private readonly HttpClient _client;

    public ErrorResponseTests(WebApplicationFactory<Program> factory)
    {
        _client = factory
            .WithWebHostBuilder(b => b.ConfigureTestServices(s =>
                s.AddControllers().AddApplicationPart(typeof(ErrorResponseTests).Assembly)))
            .CreateClient();
    }

    [Fact]
    public async Task Validation_failure_is_a_400_keyed_by_field()
    {
        (HttpResponseMessage response, JsonElement body) = await Get("validation");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Equal(
            ["Weight must be between 1 and 5.", "Weight is required."],
            body.GetProperty("errors").GetProperty("weight").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(
            ["Position is required."],
            body.GetProperty("errors").GetProperty("position").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Unreadable_request_body_uses_the_same_format()
    {
        HttpResponseMessage response = await _client.PostAsync(
            "/test/errors/body", new StringContent("{ not json", Encoding.UTF8, "application/json"));

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.True(body.TryGetProperty("errors", out _));
        Assert.False(string.IsNullOrEmpty(body.GetProperty("correlationId").GetString()));
    }

    [Fact]
    public async Task Missing_resource_is_a_404_naming_it()
    {
        (HttpResponseMessage response, JsonElement body) = await Get("not-found");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Vote 42 was not found.", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Unknown_route_is_a_404_in_the_same_format()
    {
        (HttpResponseMessage response, JsonElement body) = await Get("no-such-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.False(string.IsNullOrEmpty(body.GetProperty("correlationId").GetString()));
    }

    [Fact]
    public async Task Concurrency_conflict_is_a_409_carrying_the_current_state()
    {
        (HttpResponseMessage response, JsonElement body) = await Get("conflict");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("/problems/conflict-already-decided", body.GetProperty("type").GetString());
        Assert.Equal("Already decided.", body.GetProperty("detail").GetString());
        Assert.Equal("Dana", body.GetProperty("current").GetProperty("decidedBy").GetString());
    }

    [Fact]
    public async Task Unexpected_failure_is_a_500_that_hides_the_cause()
    {
        (HttpResponseMessage response, JsonElement body) = await Get("unexpected");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("secret detail", body.GetRawText());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("correlationId").GetString()));
    }

    [Theory]
    [InlineData("validation")]
    [InlineData("not-found")]
    [InlineData("conflict")]
    [InlineData("unexpected")]
    public async Task Every_error_carries_the_correlation_id_in_header_and_body(string route)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, $"/test/errors/{route}");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "abc-123");

        HttpResponseMessage response = await _client.SendAsync(request);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("abc-123", body.GetProperty("correlationId").GetString());
        Assert.Equal(["abc-123"], response.Headers.GetValues(CorrelationIdMiddleware.HeaderName));
    }

    [Fact]
    public async Task Handlers_see_the_id_of_the_request()
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "/test/errors/correlation");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "abc-123");

        HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal("abc-123", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Id_is_created_when_the_client_sends_none()
    {
        HttpResponseMessage response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrEmpty(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single()));
    }

    [Theory]
    [InlineData("has spaces")]
    [InlineData("<script>")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")]
    public async Task Unsafe_supplied_id_is_replaced(string supplied)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, supplied);

        HttpResponseMessage response = await _client.SendAsync(request);

        string id = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Assert.NotEqual(supplied, id);
        Assert.False(string.IsNullOrEmpty(id));
    }

    private async Task<(HttpResponseMessage Response, JsonElement Body)> Get(string route)
    {
        HttpResponseMessage response = await _client.GetAsync($"/test/errors/{route}");
        return (response, await response.Content.ReadFromJsonAsync<JsonElement>());
    }
}

[ApiController, Route("test/errors")]
public class ErrorTestController : ControllerBase
{
    public record Body(int Weight);

    [HttpGet("validation")]
    public IActionResult Validation() => throw new ValidationException(
    [
        new ValidationFailure("Weight", "Weight must be between 1 and 5."),
        new ValidationFailure("Weight", "Weight is required."),
        new ValidationFailure("Position", "Position is required."),
    ]);

    [HttpPost("body")]
    public IActionResult ReadBody(Body body) => Ok(body);

    [HttpGet("not-found")]
    public IActionResult NotFoundError() => throw new NotFoundException("Vote", 42);

    [HttpGet("conflict")]
    public IActionResult ConflictError() => throw new ConcurrencyConflictException(
        "Already decided.", new { DecidedBy = "Dana" }, "/problems/conflict-already-decided");

    [HttpGet("unexpected")]
    public IActionResult Unexpected() => throw new InvalidOperationException("secret detail");

    [HttpGet("correlation")]
    public string Correlation([FromServices] ICorrelationContext correlation) => correlation.Id;
}
