using Meridian.Api.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Api.Shared.Data.Configurations;

public class ImportConfiguration : IEntityTypeConfiguration<Import>
{
    public void Configure(EntityTypeBuilder<Import> b)
    {
        b.Property(i => i.Id).ValueGeneratedNever();          // the blob key needs it before the insert
        b.Property(i => i.Status).HasConversion<string>();
        b.Property(i => i.Phase).HasConversion<string>();
        b.Property(i => i.Report).HasColumnType("jsonb");
        b.Property(i => i.UploadedAt).HasDefaultValueSql("now()");
        b.HasOne<User>().WithMany().HasForeignKey(i => i.UploadedBy);
    }
}
