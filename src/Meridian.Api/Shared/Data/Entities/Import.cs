namespace Meridian.Api.Shared.Data.Entities;

public class Import
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = null!;
    public string BlobKey { get; set; } = null!;
    public string Sha256 { get; set; } = null!;
    public long SizeBytes { get; set; }
    public int UploadedBy { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public ImportStatus Status { get; set; }
    public ImportPhase Phase { get; set; }
    public int ProcessedRows { get; set; }
    public int? TotalRows { get; set; }
    public string? Report { get; set; }
    public string? Error { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
