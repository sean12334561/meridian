namespace Meridian.Api.Shared.Storage;

/// <summary>Holds the original uploaded files. They are immutable: a key can be written only once.</summary>
public interface IFileStore
{
    Task SaveAsync(string key, Stream content, CancellationToken ct);

    Task<Stream> OpenReadAsync(string key, CancellationToken ct);
}
