namespace Meridian.Api.Shared.Observability;

public interface ICorrelationContext
{
    string Id { get; }
}

public class CorrelationContext : ICorrelationContext
{
    public string Id { get; set; } = string.Empty;
}
