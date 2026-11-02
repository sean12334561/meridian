namespace Meridian.Api.Shared.Storage;

public class StorageOptions
{
    public const string SectionName = "Storage";

    public string? ContainerSasUrl { get; set; }
    public string? ConnectionString { get; set; }
    public string Container { get; set; } = "imports";
}
