using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;

namespace Meridian.IntegrationTests.Health;

/// <summary>
/// Runs apart from every other test. Two Azure Monitor tracers can't share a process: a second one makes
/// each Azure SDK call throw, so these apps must not overlap with tests that use Blob Storage.
/// </summary>
[CollectionDefinition(nameof(TelemetryCollection), DisableParallelization = true)]
public class TelemetryCollection;

[Collection(nameof(TelemetryCollection))]
public class TelemetryTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
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
}
