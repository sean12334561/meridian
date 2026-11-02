using Meridian.Api.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Api.Shared.Data.Configurations;

public class VoteHistoryEntryConfiguration : IEntityTypeConfiguration<VoteHistoryEntry>
{
    public void Configure(EntityTypeBuilder<VoteHistoryEntry> b)
    {
        b.Property(h => h.Type).HasConversion<string>();
        b.Property(h => h.OldValue).HasColumnType("jsonb");
        b.Property(h => h.NewValue).HasColumnType("jsonb");
        b.Property(h => h.CreatedAt).HasDefaultValueSql("now()");
        b.HasIndex(h => h.VoteId);                       // timeline lookup (HIS-04)
        b.HasOne<Vote>().WithMany().HasForeignKey(h => h.VoteId);
        b.HasOne<User>().WithMany().HasForeignKey(h => h.ActorId);
        b.HasOne<Import>().WithMany().HasForeignKey(h => h.ImportId);
        b.HasOne<Conflict>().WithMany().HasForeignKey(h => h.ConflictId);
    }
}
