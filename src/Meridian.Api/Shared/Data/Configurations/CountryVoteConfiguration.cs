using Meridian.Api.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Api.Shared.Data.Configurations;

public class CountryVoteConfiguration : IEntityTypeConfiguration<CountryVote>
{
    public void Configure(EntityTypeBuilder<CountryVote> b)
    {
        b.HasIndex(cv => new { cv.VoteId, cv.CountryCode }).IsUnique();
        b.Property(cv => cv.CountryCode).HasMaxLength(3);
        b.HasOne<Vote>().WithMany().HasForeignKey(cv => cv.VoteId);
        b.HasOne<Import>().WithMany().HasForeignKey(cv => cv.SourceImportId);
        b.ToTable(t => t.HasCheckConstraint("ck_ballot", "ballot IN ('Y','N','A','X')"));
    }
}
