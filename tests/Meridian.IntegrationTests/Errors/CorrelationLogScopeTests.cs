using System.Collections.Concurrent;
using Meridian.Api.Shared.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.IntegrationTests.Errors;

public class CorrelationLogScopeTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Log_lines_show_the_id_as_readable_text()
    {
        ScopeCapturingProvider logs = new();
        using HttpClient client = factory
            .WithWebHostBuilder(b => b.ConfigureLogging(l => l
                .AddProvider(logs)
                .AddFilter("Microsoft.AspNetCore", LogLevel.Information)))
            .CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, "/health");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "abc-123");

        await client.SendAsync(request);

        Assert.Contains("CorrelationId:abc-123", logs.Scopes);
    }

    private class ScopeCapturingProvider : ILoggerProvider, ISupportExternalScope, ILogger
    {
        private IExternalScopeProvider? _scopes;

        public ConcurrentBag<string?> Scopes { get; } = [];

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        public ILogger CreateLogger(string categoryName) => this;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _scopes?.ForEachScope((scope, captured) => captured.Add(scope?.ToString()), Scopes);

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public void Dispose()
        {
        }
    }
}
