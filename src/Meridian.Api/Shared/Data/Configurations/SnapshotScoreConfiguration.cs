using Meridian.Api.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Api.Shared.Data.Configurations;

public class SnapshotScoreConfiguration : IEntityTypeConfiguration<SnapshotScore>
{
    public void Configure(EntityTypeBuilder<SnapshotScore> b)
    {
        b.HasKey(s => new { s.SnapshotId, s.CountryCode });
        b.Property(s => s.CountryCode).HasMaxLength(3);
        b.Property(s => s.CountedWeight).HasPrecision(14, 4);
        b.Property(s => s.EarnedWeight).HasPrecision(14, 4);
        b.Property(s => s.Score).HasPrecision(7, 4);
        b.HasOne<Snapshot>().WithMany().HasForeignKey(s => s.SnapshotId);
    }
}
