using Meridian.Api.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Api.Shared.Data.Configurations;

public class ConflictConfiguration : IEntityTypeConfiguration<Conflict>
{
    public void Configure(EntityTypeBuilder<Conflict> b)
    {
        b.Property(c => c.Status).HasConversion<string>();
        b.Property(c => c.Note).HasMaxLength(1000);
        b.Property(c => c.CreatedAt).HasDefaultValueSql("now()");
        b.HasIndex(c => new { c.VoteId, c.Target, c.ProposedValue })
         .IsUnique().HasFilter("status = 'Pending'");    // ING-12
        b.HasOne<Vote>().WithMany().HasForeignKey(c => c.VoteId);
        b.HasOne<Import>().WithMany().HasForeignKey(c => c.ApprovedSourceImportId);
        b.HasOne<Import>().WithMany().HasForeignKey(c => c.ProposedSourceImportId);
        b.HasOne<User>().WithMany().HasForeignKey(c => c.DecidedBy);
    }
}
