using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Seeding;

namespace Meridian.UnitTests.Seeding;

public class MembershipOverlapTests
{
    [Fact]
    public void Separate_periods_do_not_overlap()
    {
        List<string> overlaps = Seeder.FindOverlaps(
        [
            Period("ISL", "1995-01-01", "2019-12-31"),
            Period("ISL", "2022-01-01", null),
        ]);

        Assert.Empty(overlaps);
    }

    [Fact]
    public void Period_starting_the_day_after_the_previous_ends_does_not_overlap()
    {
        List<string> overlaps = Seeder.FindOverlaps(
        [
            Period("ISL", "1995-01-01", "2019-12-31"),
            Period("ISL", "2020-01-01", null),
        ]);

        Assert.Empty(overlaps);
    }

    [Fact]
    public void Period_starting_on_the_previous_end_date_overlaps()
    {
        List<string> overlaps = Seeder.FindOverlaps(
        [
            Period("ISL", "1995-01-01", "2019-12-31"),
            Period("ISL", "2019-12-31", null),
        ]);

        Assert.Equal(["ISL"], overlaps);
    }

    [Fact]
    public void Open_ended_period_overlaps_any_later_period()
    {
        List<string> overlaps = Seeder.FindOverlaps(
        [
            Period("ISL", "2022-01-01", null),
            Period("ISL", "1995-01-01", null),
        ]);

        Assert.Equal(["ISL"], overlaps);
    }

    [Fact]
    public void Period_inside_a_longer_one_overlaps()
    {
        List<string> overlaps = Seeder.FindOverlaps(
        [
            Period("ISL", "1995-01-01", "2019-12-31"),
            Period("ISL", "2000-01-01", "2001-01-01"),
            Period("ISL", "2010-01-01", "2011-01-01"),
        ]);

        Assert.Equal(["ISL"], overlaps);
    }

    [Fact]
    public void Different_countries_never_overlap_each_other()
    {
        List<string> overlaps = Seeder.FindOverlaps(
        [
            Period("ISL", "1995-01-01", null),
            Period("FRA", "1995-01-01", null),
        ]);

        Assert.Empty(overlaps);
    }

    private static MemberState Period(string countryCode, string from, string? to) => new()
    {
        CountryCode = countryCode,
        CountryName = countryCode,
        MemberFrom = DateOnly.Parse(from),
        MemberTo = to is null ? null : DateOnly.Parse(to),
    };
}
