using Meridian.Api.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace Meridian.IntegrationTests.Infrastructure;

public class FixtureDbContextFactory(PostgresFixture postgres) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => postgres.CreateDbContext();
}
