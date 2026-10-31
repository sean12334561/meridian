using Azure.Storage.Blobs;

namespace Meridian.Api.Shared.Storage;

/// <summary>Azure Blob Storage, reached with a container SAS URL, or Azurite with a connection string.</summary>
public class BlobFileStore(BlobContainerClient container, bool createContainer) : IFileStore
{
    private readonly BlobContainerClient container = container;
    private readonly bool createContainer = createContainer;

    public static BlobFileStore Create(StorageOptions options)
    {
        if (!string.IsNullOrEmpty(options.ContainerSasUrl))
        {
            return new BlobFileStore(new BlobContainerClient(new Uri(options.ContainerSasUrl)), createContainer: false);
        }

        if (!string.IsNullOrEmpty(options.ConnectionString))
        {
            // Azurite starts empty. A container SAS can't create a container, so on Azure it must already exist
            return new BlobFileStore(new BlobContainerClient(options.ConnectionString, options.Container), createContainer: true);
        }

        throw new InvalidOperationException("Set Storage__ContainerSasUrl or Storage__ConnectionString.");
    }

    public async Task SaveAsync(string key, Stream content, CancellationToken ct)
    {
        if (createContainer)
        {
            await container.CreateIfNotExistsAsync(cancellationToken: ct);
        }

        await container.GetBlobClient(key).UploadAsync(content, overwrite: false, ct);
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct)
    {
        return await container.GetBlobClient(key).OpenReadAsync(allowBlobModifications: false, cancellationToken: ct);
    }
}
