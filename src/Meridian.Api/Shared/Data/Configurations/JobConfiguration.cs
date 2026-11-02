using Meridian.Api.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Api.Shared.Data.Configurations;

public class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> b)
    {
        b.Property(j => j.Payload).HasColumnType("jsonb");
        b.Property(j => j.Status).HasConversion<string>();
        b.Property(j => j.RunAfter).HasDefaultValueSql("now()");
        b.Property(j => j.CreatedAt).HasDefaultValueSql("now()");
    }
}
