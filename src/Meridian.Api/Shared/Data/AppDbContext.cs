using Meridian.Api.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Meridian.Api.Shared.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<MemberState> MemberStates => Set<MemberState>();
    public DbSet<Import> Imports => Set<Import>();
    public DbSet<ImportRow> ImportRows => Set<ImportRow>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Vote> Votes => Set<Vote>();
    public DbSet<CountryVote> CountryVotes => Set<CountryVote>();
    public DbSet<Conflict> Conflicts => Set<Conflict>();
    public DbSet<VoteHistoryEntry> VoteHistory => Set<VoteHistoryEntry>();
    public DbSet<Snapshot> Snapshots => Set<Snapshot>();
    public DbSet<SnapshotScore> SnapshotScores => Set<SnapshotScore>();
    public DbSet<SnapshotContribution> SnapshotContributions => Set<SnapshotContribution>();
    public DbSet<SnapshotPublication> SnapshotPublications => Set<SnapshotPublication>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Indexes are chosen by hand (HLD): one per foreign key would slow the bulk inserts
        configurationBuilder.Conventions.Remove<ForeignKeyIndexConvention>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Nothing in the domain is deleted, so no foreign key may cascade
        foreach (IMutableForeignKey foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
    }
}
