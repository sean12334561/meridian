using Meridian.Api.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Api.Shared.Data.Configurations;

public class SnapshotPublicationConfiguration : IEntityTypeConfiguration<SnapshotPublication>
{
    public void Configure(EntityTypeBuilder<SnapshotPublication> b)
    {
        b.Property(p => p.PublishedAt).HasDefaultValueSql("now()");
        b.HasIndex(p => p.Quarter).IsUnique().HasFilter("withdrawn_at IS NULL");
        b.HasOne<Snapshot>().WithMany().HasForeignKey(p => p.SnapshotId);
        b.HasOne<User>().WithMany().HasForeignKey(p => p.PublishedBy);
        b.HasOne<SnapshotPublication>().WithMany().HasForeignKey(p => p.SupersededBy);
    }
}
