using Meridian.Api.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Api.Shared.Data.Configurations;

public class MemberStateConfiguration : IEntityTypeConfiguration<MemberState>
{
    public void Configure(EntityTypeBuilder<MemberState> b)
    {
        b.HasKey(m => new { m.CountryCode, m.MemberFrom });   // Iceland has two periods
        b.Property(m => m.CountryCode).HasMaxLength(3);
    }
}
