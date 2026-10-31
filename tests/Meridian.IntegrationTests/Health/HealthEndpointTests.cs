using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Meridian.Api.Shared.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;

namespace Meridian.IntegrationTests.Health;

public class HealthEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_returns_200()
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_app_starts_with_telemetry_turned_on()
    {
        await using WebApplicationFactory<Program> withTelemetry = factory.WithWebHostBuilder(b => b.UseSetting(
            "APPLICATIONINSIGHTS_CONNECTION_STRING",
            "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=http://127.0.0.1:9/"));
        using HttpClient client = withTelemetry.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Telemetry_exports_log_scopes_so_the_correlation_id_reaches_application_insights()
    {
        await using WebApplicationFactory<Program> withTelemetry = factory.WithWebHostBuilder(b => b.UseSetting(
            "APPLICATIONINSIGHTS_CONNECTION_STRING",
            "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=http://127.0.0.1:9/"));

        OpenTelemetryLoggerOptions options = withTelemetry.Services.GetRequiredService<IOptionsMonitor<OpenTelemetryLoggerOptions>>().CurrentValue;

        Assert.True(options.IncludeScopes);
    }

    [Fact]
    public async Task The_request_activity_carries_the_correlation_id()
    {
        using Activity activity = new Activity("request").Start();
        DefaultHttpContext context = new();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "abc-123";
        CorrelationIdMiddleware middleware = new(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance);

        await middleware.InvokeAsync(context, new CorrelationContext());

        Assert.Equal("abc-123", activity.GetTagItem("correlation.id"));
    }

    [Theory]
    [InlineData("DecideConflictRequest", "decision")]
    [InlineData("SetPositionRequest", "desiredOutcome")]
    [InlineData("SetPositionsBulkRequest", "onlyMissing")]
    [InlineData("CreateSnapshotRequest", "minCountedVotes")]
    [InlineData("PublishSnapshotRequest", "quarter")]
    public async Task Each_use_case_has_its_own_request_schema_in_the_contract(string schema, string property)
    {
        using HttpClient client = factory.CreateClient();

        using JsonDocument contract = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));

        JsonElement schemas = contract.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.GetProperty(schema).GetProperty("properties").TryGetProperty(property, out _));
    }
}
