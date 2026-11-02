using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Seeding;
using Meridian.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.IntegrationTests.Seeding;

public class SeederTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private const string MemberStatesPath = "member_states.csv";
    private const string Header = "country_code,country_name,member_from,member_to";

    private readonly string _tempFile = Path.GetTempFileName();

    public void Dispose() => File.Delete(_tempFile);

    [Fact]
    public async Task Seeds_the_three_users_with_fixed_ids()
    {
        await Seed(MemberStatesPath);

        await using AppDbContext db = postgres.CreateDbContext();
        List<User> users = await db.Users.OrderBy(u => u.Id).ToListAsync();

        Assert.Equal(
            [(1, "Dana", Role.Analyst), (2, "Omer", Role.Analyst), (3, "Lena", Role.Admin)],
            users.Select(u => (u.Id, u.Name, u.Role)));
    }

    [Fact]
    public async Task Seeds_every_row_of_the_member_states_file()
    {
        await Seed(MemberStatesPath);

        await using AppDbContext db = postgres.CreateDbContext();
        Assert.Equal(151, await db.MemberStates.CountAsync());
    }

    [Fact]
    public async Task Iceland_keeps_both_periods_and_an_empty_end_date_stays_null()
    {
        await Seed(MemberStatesPath);

        await using AppDbContext db = postgres.CreateDbContext();
        List<MemberState> iceland = await db.MemberStates
            .Where(m => m.CountryCode == "ISL")
            .OrderBy(m => m.MemberFrom)
            .ToListAsync();

        Assert.Equal(
            [("Iceland", new DateOnly(1995, 1, 1), (DateOnly?)new DateOnly(2019, 12, 31)),
             ("Iceland", new DateOnly(2022, 1, 1), null)],
            iceland.Select(m => (m.CountryName, m.MemberFrom, m.MemberTo)));
    }

    [Fact]
    public async Task Seeding_twice_changes_nothing()
    {
        await Seed(MemberStatesPath);
        (int users, int memberStates) = await Counts();

        await Seed(MemberStatesPath);

        Assert.Equal((users, memberStates), await Counts());
    }

    [Fact]
    public async Task Existing_rows_are_not_overwritten()
    {
        await Seed(MemberStatesPath);
        await using AppDbContext db = postgres.CreateDbContext();
        MemberState france = await db.MemberStates.SingleAsync(m => m.CountryCode == "FRA");
        france.CountryName = "Edited";
        await db.SaveChangesAsync();

        await Seed(MemberStatesPath);

        await using AppDbContext after = postgres.CreateDbContext();
        Assert.Equal("Edited", await after.MemberStates
            .Where(m => m.CountryCode == "FRA").Select(m => m.CountryName).SingleAsync());
    }

    [Fact]
    public async Task Overlapping_periods_fail_and_write_nothing()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            Header,
            "ZZZ,Overlap,2000-01-01,2010-12-31",
            "ZZZ,Overlap,2010-12-31,",
        ]);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Seed(_tempFile));

        Assert.Contains("ZZZ", ex.Message);
        await using AppDbContext db = postgres.CreateDbContext();
        Assert.False(await db.MemberStates.AnyAsync(m => m.CountryCode == "ZZZ"));
    }

    [Fact]
    public async Task New_period_overlapping_one_already_stored_fails()
    {
        await Seed(MemberStatesPath);
        await File.WriteAllLinesAsync(_tempFile, [Header, "FRA,France,2000-01-01,"]);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Seed(_tempFile));

        Assert.Contains("FRA", ex.Message);
    }

    private async Task Seed(string path)
    {
        await using AppDbContext db = postgres.CreateDbContext();
        await new Seeder(db, NullLogger<Seeder>.Instance).SeedAsync(path);
    }

    private async Task<(int Users, int MemberStates)> Counts()
    {
        await using AppDbContext db = postgres.CreateDbContext();
        return (await db.Users.CountAsync(), await db.MemberStates.CountAsync());
    }
}
