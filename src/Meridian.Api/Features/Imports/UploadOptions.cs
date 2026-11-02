namespace Meridian.Api.Features.Imports;

public class UploadOptions
{
    public const string SectionName = "Upload";

    public long MaxBytes { get; set; } = 200_000_000;
}
