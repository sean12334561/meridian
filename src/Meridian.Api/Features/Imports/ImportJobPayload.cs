namespace Meridian.Api.Features.Imports;

public class ImportJobPayload
{
    public const string JobType = "import";

    public required Guid ImportId { get; init; }
    public required string CorrelationId { get; init; }
}
