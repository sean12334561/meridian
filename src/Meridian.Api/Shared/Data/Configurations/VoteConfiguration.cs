using Meridian.Api.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Api.Shared.Data.Configurations;

public class VoteConfiguration : IEntityTypeConfiguration<Vote>
{
    public void Configure(EntityTypeBuilder<Vote> b)
    {
        b.HasIndex(v => v.VoteRef).IsUnique();
        b.Property(v => v.Position).HasMaxLength(1);
        b.HasOne<Import>().WithMany().HasForeignKey(v => v.DetailsSourceImportId);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_weight", "weight IS NULL OR weight BETWEEN 1 AND 5");
            t.HasCheckConstraint("ck_position", "position IS NULL OR position IN ('Y','N')");
        });
    }
}
