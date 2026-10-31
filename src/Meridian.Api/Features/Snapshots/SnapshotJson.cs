using System.Text.Json;

namespace Meridian.Api.Features.Snapshots;

/// <summary>Stored in snapshots.filter. VoteIds is the resolved selection, excluded votes removed.</summary>
public class SnapshotFilter
{
    public required DateOnly? From { get; init; }
    public required DateOnly? To { get; init; }
    public required int? Session { get; init; }
    public required long[]? RequestedVoteIds { get; init; }
    public string[]? RequestedVoteRefs { get; init; }
    public required long[] VoteIds { get; init; }
}

/// <summary>Stored in snapshots.rules, and shown with the ranking.</summary>
public class SnapshotRules
{
    public required decimal AbstentionFactor { get; init; }
    public required int MinCountedVotes { get; init; }
    public required long[] ExcludedVotes { get; init; }
}

public static class SnapshotJson
{
    public static string Write<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
    }

    public static T Read<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web)!;
    }
}
