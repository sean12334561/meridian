using Meridian.Api.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Api.Shared.Data.Configurations;

public class SnapshotContributionConfiguration : IEntityTypeConfiguration<SnapshotContribution>
{
    public void Configure(EntityTypeBuilder<SnapshotContribution> b)
    {
        b.HasKey(c => new { c.SnapshotId, c.CountryCode, c.VoteId });
        b.Property(c => c.CountryCode).HasMaxLength(3);
        b.Property(c => c.Position).HasMaxLength(1);
        b.Property(c => c.Points).HasPrecision(8, 4);
        b.HasOne<Snapshot>().WithMany().HasForeignKey(c => c.SnapshotId);
        b.HasOne<Vote>().WithMany().HasForeignKey(c => c.VoteId);
        b.HasOne<Import>().WithMany().HasForeignKey(c => c.SourceImportId);
        b.ToTable(t => t.HasCheckConstraint("ck_ballot", "ballot IN ('Y','N','A','X')"));
    }
}
