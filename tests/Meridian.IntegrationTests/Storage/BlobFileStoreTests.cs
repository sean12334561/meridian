using System.Security.Cryptography;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Meridian.Api.Shared;
using Meridian.Api.Shared.Storage;
using Meridian.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.IntegrationTests.Storage;

public class BlobFileStoreTests(AzuriteFixture azurite) : IClassFixture<AzuriteFixture>
{
    [Fact]
    public async Task Connection_string_creates_the_container_and_reads_back_what_was_saved()
    {
        IFileStore store = ConnectionStringStore(NewContainerName());

        await store.SaveAsync("imports/a.csv", new MemoryStream("a,b\n1,2\n"u8.ToArray()), default);

        Assert.Equal("a,b\n1,2\n"u8.ToArray(), await Read(store, "imports/a.csv"));
    }

    [Fact]
    public async Task Saves_a_large_stream_that_cannot_seek()
    {
        // The upload hashes while streaming, so the store never sees a seekable stream or a length
        byte[] content = RandomNumberGenerator.GetBytes(9 * 1024 * 1024);
        IFileStore store = ConnectionStringStore(NewContainerName());

        await using (CryptoStream hashing = new(new MemoryStream(content), SHA256.Create(), CryptoStreamMode.Read))
        {
            await store.SaveAsync("large.csv", hashing, default);
        }

        Assert.Equal(content, await Read(store, "large.csv"));
    }

    [Fact]
    public async Task A_saved_file_cannot_be_overwritten()
    {
        IFileStore store = ConnectionStringStore(NewContainerName());
        await store.SaveAsync("a.csv", new MemoryStream("original"u8.ToArray()), default);

        RequestFailedException exception = await Assert.ThrowsAsync<RequestFailedException>(() =>
            store.SaveAsync("a.csv", new MemoryStream("changed"u8.ToArray()), default));

        Assert.Equal(409, exception.Status);
        Assert.Equal("original"u8.ToArray(), await Read(store, "a.csv"));
    }

    [Fact]
    public async Task Opening_a_missing_file_fails_with_not_found()
    {
        string name = NewContainerName();
        IFileStore store = ConnectionStringStore(name);
        await store.SaveAsync("a.csv", new MemoryStream("a"u8.ToArray()), default);

        RequestFailedException exception = await Assert.ThrowsAsync<RequestFailedException>(() =>
            store.OpenReadAsync("missing.csv", default));

        Assert.Equal(404, exception.Status);
    }

    [Fact]
    public async Task Container_sas_url_saves_and_reads_in_an_existing_container()
    {
        string name = NewContainerName();
        await new BlobContainerClient(azurite.ConnectionString, name).CreateAsync();
        IFileStore store = BlobFileStore.Create(new StorageOptions { ContainerSasUrl = SasUrl(name) });

        await store.SaveAsync("imports/a.csv", new MemoryStream("a"u8.ToArray()), default);

        Assert.Equal("a"u8.ToArray(), await Read(store, "imports/a.csv"));
    }

    [Fact]
    public async Task Container_sas_url_does_not_try_to_create_the_container()
    {
        IFileStore store = BlobFileStore.Create(new StorageOptions { ContainerSasUrl = SasUrl(NewContainerName()) });

        RequestFailedException exception = await Assert.ThrowsAsync<RequestFailedException>(() =>
            store.SaveAsync("a.csv", new MemoryStream("a"u8.ToArray()), default));

        Assert.Equal(404, exception.Status);
    }

    [Fact]
    public async Task Container_sas_url_wins_over_the_connection_string()
    {
        string sasContainer = NewContainerName();
        string otherContainer = NewContainerName();
        BlobContainerClient sasClient = new(azurite.ConnectionString, sasContainer);
        await sasClient.CreateAsync();
        IFileStore store = BlobFileStore.Create(new StorageOptions
        {
            ContainerSasUrl = SasUrl(sasContainer),
            ConnectionString = azurite.ConnectionString,
            Container = otherContainer,
        });

        await store.SaveAsync("a.csv", new MemoryStream("a"u8.ToArray()), default);

        Assert.True(await sasClient.GetBlobClient("a.csv").ExistsAsync());
        Assert.False(await new BlobContainerClient(azurite.ConnectionString, otherContainer).ExistsAsync());
    }

    [Fact]
    public async Task Is_registered_from_the_storage_settings()
    {
        string name = NewContainerName();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=unused",
                ["Storage:ConnectionString"] = azurite.ConnectionString,
                ["Storage:Container"] = name,
            })
            .Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddShared(configuration);
        await using ServiceProvider provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IFileStore>().SaveAsync("a.csv", new MemoryStream("a"u8.ToArray()), default);

        Assert.True(await new BlobContainerClient(azurite.ConnectionString, name).GetBlobClient("a.csv").ExistsAsync());
    }

    [Fact]
    public void Missing_settings_are_refused_with_a_clear_message()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            BlobFileStore.Create(new StorageOptions()));

        Assert.Contains("Storage__ContainerSasUrl", exception.Message);
    }

    private static string NewContainerName()
    {
        return $"test-{Guid.NewGuid():N}";
    }

    private IFileStore ConnectionStringStore(string container)
    {
        return BlobFileStore.Create(new StorageOptions { ConnectionString = azurite.ConnectionString, Container = container });
    }

    private string SasUrl(string container)
    {
        // The same permissions .env.example asks of the real SAS
        return new BlobContainerClient(azurite.ConnectionString, container).GenerateSasUri(
            BlobContainerSasPermissions.Read | BlobContainerSasPermissions.Add | BlobContainerSasPermissions.Create
                | BlobContainerSasPermissions.Write | BlobContainerSasPermissions.List,
            DateTimeOffset.UtcNow.AddHours(1)).ToString();
    }

    private static async Task<byte[]> Read(IFileStore store, string key)
    {
        await using Stream stream = await store.OpenReadAsync(key, default);
        using MemoryStream copy = new();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }
}
