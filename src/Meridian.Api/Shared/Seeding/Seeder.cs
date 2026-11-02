using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Shared.Seeding;

/// <summary>Adds the users and member states that are missing; existing rows are left as they are.</summary>
public class Seeder(AppDbContext db, ILogger<Seeder> logger)
{
    public async Task SeedAsync(string memberStatesPath)
    {
        List<User> users = await MissingUsers();
        List<MemberState> memberStates = await MissingMemberStates(memberStatesPath);

        db.Users.AddRange(users);
        db.MemberStates.AddRange(memberStates);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Seeding added {UserCount} users and {MemberStateCount} member state rows",
            users.Count, memberStates.Count);
    }

    /// <summary>Countries with two periods sharing a day. Dates are inclusive and no end date means still a member.</summary>
    public static List<string> FindOverlaps(IEnumerable<MemberState> periods) => periods
        .GroupBy(p => p.CountryCode)
        .Where(country => HasOverlap(country))
        .Select(country => country.Key)
        .Order()
        .ToList();

    private static bool HasOverlap(IEnumerable<MemberState> periods)
    {
        List<MemberState> ordered = periods.OrderBy(p => p.MemberFrom).ToList();

        return ordered.Zip(ordered.Skip(1)).Any(pair =>
            pair.First.MemberTo is not DateOnly end || end >= pair.Second.MemberFrom);
    }

    private async Task<List<User>> MissingUsers()
    {
        List<User> users =
        [
            new() { Id = 1, Name = "Dana", Role = Role.Analyst },
            new() { Id = 2, Name = "Omer", Role = Role.Analyst },
            new() { Id = 3, Name = "Lena", Role = Role.Admin },
        ];
        List<int> existing = await db.Users.Select(u => u.Id).ToListAsync();

        return users.Where(u => !existing.Contains(u.Id)).ToList();
    }

    private async Task<List<MemberState>> MissingMemberStates(string path)
    {
        List<MemberState> existing = await db.MemberStates.AsNoTracking().ToListAsync();
        HashSet<(string, DateOnly)> existingKeys = existing.Select(m => (m.CountryCode, m.MemberFrom)).ToHashSet();

        List<MemberState> missing = ReadMemberStates(path)
            .Where(m => !existingKeys.Contains((m.CountryCode, m.MemberFrom)))
            .ToList();

        List<string> overlaps = FindOverlaps(existing.Concat(missing));
        if (overlaps.Count > 0)
        {
            throw new InvalidOperationException(
                $"Overlapping membership periods for: {string.Join(", ", overlaps)}");
        }

        return missing;
    }

    private static List<MemberState> ReadMemberStates(string path)
    {
        using StreamReader reader = new(path);
        using CsvReader csv = new(reader, CultureInfo.InvariantCulture);
        csv.Context.RegisterClassMap<MemberStateMap>();

        return csv.GetRecords<MemberState>().ToList();
    }

    private class MemberStateMap : ClassMap<MemberState>
    {
        public MemberStateMap()
        {
            Map(m => m.CountryCode).Name("country_code");
            Map(m => m.CountryName).Name("country_name");
            Map(m => m.MemberFrom).Name("member_from");
            Map(m => m.MemberTo).Name("member_to");
        }
    }
}
