using Meridian.Api.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Api.Shared.Data.Configurations;

public class SnapshotConfiguration : IEntityTypeConfiguration<Snapshot>
{
    public void Configure(EntityTypeBuilder<Snapshot> b)
    {
        b.Property(s => s.Filter).HasColumnType("jsonb");
        b.Property(s => s.Rules).HasColumnType("jsonb");
        b.Property(s => s.Status).HasConversion<string>();
        b.Property(s => s.CreatedAt).HasDefaultValueSql("now()");
        b.HasOne<User>().WithMany().HasForeignKey(s => s.CreatedBy);
    }
}
