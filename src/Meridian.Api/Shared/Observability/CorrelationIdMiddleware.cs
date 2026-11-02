using System.Diagnostics;

namespace Meridian.Api.Shared.Observability;

public class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context, CorrelationContext correlation)
    {
        string id = ResolveId(context);
        correlation.Id = id;
        Activity.Current?.SetTag("correlation.id", id);

        // OnStarting, because the exception handler clears headers set before it runs
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = id;
            return Task.CompletedTask;
        });

        using (logger.BeginScope("CorrelationId:{CorrelationId}", id))
        {
            await next(context);
        }
    }

    private static string ResolveId(HttpContext context)
    {
        string supplied = context.Request.Headers[HeaderName].ToString();

        return IsSafe(supplied)
            ? supplied
            : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
    }

    // The value comes from the client and ends up in logs and job payloads
    private static bool IsSafe(string id) =>
        id.Length is > 0 and <= MaxLength
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
