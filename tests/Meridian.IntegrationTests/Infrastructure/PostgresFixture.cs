using Meridian.Api.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Meridian.IntegrationTests.Infrastructure;

/// <summary>A throwaway, migrated PostgreSQL shared by the tests of one class.</summary>
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public string ConnectionString => _container.GetConnectionString();

    public AppDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using AppDbContext db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
