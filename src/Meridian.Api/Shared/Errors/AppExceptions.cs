namespace Meridian.Api.Shared.Errors;

public class NotFoundException(string resource, object id) : Exception($"{resource} {id} was not found.");

/// <summary>Another user got there first. <paramref name="current"/> tells the caller how things stand now.</summary>
public class ConcurrencyConflictException(string message, object? current = null, string? problemType = null)
    : Exception(message)
{
    public object? Current { get; } = current;
    public string? ProblemType { get; } = problemType;
}
