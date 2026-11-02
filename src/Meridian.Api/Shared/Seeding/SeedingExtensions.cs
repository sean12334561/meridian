using Meridian.Api.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Shared.Seeding;

public static class SeedingExtensions
{
    public static async Task MigrateAndSeedAsync(this IHost app)
    {
        IConfiguration configuration = app.Services.GetRequiredService<IConfiguration>();
        string memberStatesPath = configuration["Seed:MemberStatesPath"]
            ?? throw new InvalidOperationException("Seed:MemberStatesPath is not set");

        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        Seeder seeder = scope.ServiceProvider.GetRequiredService<Seeder>();
        await seeder.SeedAsync(memberStatesPath);
    }
}
