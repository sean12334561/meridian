using Meridian.Api.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Api.Shared.Data.Configurations;

public class ImportRowConfiguration : IEntityTypeConfiguration<ImportRow>
{
    public void Configure(EntityTypeBuilder<ImportRow> b)
    {
        b.HasKey(r => new { r.ImportId, r.LineNo });
        b.HasIndex(r => new { r.ImportId, r.VoteRef });     // each apply batch reads one batch of votes
        b.Property(r => r.LineNo).ValueGeneratedNever();
        b.Property(r => r.Normalizations).HasColumnType("jsonb");
        b.Property(r => r.Outcome).HasConversion<string>();
        b.HasOne<Import>().WithMany().HasForeignKey(r => r.ImportId);
        b.HasOne<Conflict>().WithMany().HasForeignKey(r => r.ConflictId);
    }
}
