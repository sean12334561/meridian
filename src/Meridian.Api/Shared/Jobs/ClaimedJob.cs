using System.Text.Json;
using Meridian.Api.Shared.Data.Entities;

namespace Meridian.Api.Shared.Jobs;

public class ClaimedJob
{
    public required long Id { get; init; }
    public required string Type { get; init; }
    public required string Payload { get; init; }
    public required int Attempts { get; init; }
    public required string? CorrelationId { get; init; }

    public T ReadPayload<T>()
    {
        return JsonSerializer.Deserialize<T>(Payload, JsonSerializerOptions.Web)!;
    }

    public static ClaimedJob From(Job job)
    {
        using JsonDocument payload = JsonDocument.Parse(job.Payload);

        return new ClaimedJob
        {
            Id = job.Id,
            Type = job.Type,
            Payload = job.Payload,
            Attempts = job.Attempts,
            CorrelationId = payload.RootElement.TryGetProperty("correlationId", out JsonElement id)
                ? id.GetString()
                : null,
        };
    }
}
