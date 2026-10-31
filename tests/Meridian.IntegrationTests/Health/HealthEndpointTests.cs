using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

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
