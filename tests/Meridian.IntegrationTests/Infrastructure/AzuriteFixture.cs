using Testcontainers.Azurite;

namespace Meridian.IntegrationTests.Infrastructure;

/// <summary>A throwaway Azurite shared by the tests of one class.</summary>
public class AzuriteFixture : IAsyncLifetime
{
    private readonly AzuriteContainer _container = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:latest")
        .WithCommand("--skipApiVersionCheck")
        .Build();

    public string ConnectionString
    {
        get { return _container.GetConnectionString(); }
    }

    public Task InitializeAsync()
    {
        return _container.StartAsync();
    }

    public Task DisposeAsync()
    {
        return _container.DisposeAsync().AsTask();
    }
}
