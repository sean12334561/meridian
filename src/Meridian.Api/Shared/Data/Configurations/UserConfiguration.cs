using Meridian.Api.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Api.Shared.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.Property(u => u.Id).ValueGeneratedNever();   // seeded with fixed IDs
        b.Property(u => u.Role).HasConversion<string>();
    }
}
