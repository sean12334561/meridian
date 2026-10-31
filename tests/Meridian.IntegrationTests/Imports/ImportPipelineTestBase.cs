using System.Text;
using System.Text.Json;
using Meridian.Api.Features.Imports;
using Meridian.Api.Features.Imports.Classification;
using Meridian.Api.Features.Imports.Parsing;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Jobs;
using Meridian.Api.Shared.Seeding;
using Meridian.Api.Shared.Storage;
using Meridian.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.IntegrationTests.Imports;

/// <summary>Runs ImportJobHandler directly against real PostgreSQL and Azurite.</summary>
public abstract class ImportPipelineTestBase(PostgresFixture postgres, AzuriteFixture azurite) : IAsyncLifetime
{
    protected const string Header = "assembly_vote_id,session,vote_date,resolution_title,country_code,country_name,ballot,published_at";

    public virtual async Task InitializeAsync()
    {
        await using AppDbContext db = postgres.CreateDbContext();
        await new Seeder(db, NullLogger<Seeder>.Instance).SeedAsync("member_states.csv");
    }

    public virtual Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    protected ImportJobHandler Handler()
    {
        FixtureDbContextFactory factory = new(postgres);

        return new ImportJobHandler(factory, new ImportParser(factory, Files()), new InFileChecks(factory), new ApprovedDataClassifier(factory), new ImportReportWriter(factory));
    }

    protected Task Run(Import import)
    {
        return Handler().RunAsync(Job(import), default);
    }

    // The phases that only look at the file: their tests can reuse vote references
    protected async Task RunChecks(Import import)
    {
        FixtureDbContextFactory factory = new(postgres);
        await new ImportParser(factory, Files()).ParseAsync(import, default);
        await new InFileChecks(factory).RunAsync(import.Id, default);
    }

    protected static ClaimedJob Job(Import import)
    {
        return new ClaimedJob
        {
            Id = 1,
            Type = ImportJobPayload.JobType,
            Payload = JsonSerializer.Serialize(new { importId = import.Id, correlationId = "test" }),
            Attempts = 1,
            CorrelationId = null,
        };
    }

    protected static Import NewImport(Guid id)
    {
        return new Import
        {
            Id = id,
            FileName = "votes.csv",
            BlobKey = $"imports/{id}.csv",
            Sha256 = new string('a', 64),
            UploadedBy = 1,
            Status = ImportStatus.Pending,
            Phase = ImportPhase.Queued,
        };
    }

    protected Task<Import> CreateImport(string csv)
    {
        return CreateImport(Encoding.UTF8.GetBytes(csv));
    }

    protected async Task<Import> CreateImport(byte[] content)
    {
        Import import = NewImport(Guid.NewGuid());
        await Upload(import, content);
        await Save(import);
        return import;
    }

    protected Task Upload(Import import, byte[] content)
    {
        return Files().SaveAsync(import.BlobKey, new MemoryStream(content), default);
    }

    protected async Task Save(Import import)
    {
        await using AppDbContext db = postgres.CreateDbContext();
        db.Imports.Add(import);
        await db.SaveChangesAsync();
    }

    protected async Task<Import> Reload(Import import)
    {
        await using AppDbContext db = postgres.CreateDbContext();
        return await db.Imports.AsNoTracking().SingleAsync(i => i.Id == import.Id);
    }

    protected async Task<List<ImportRow>> Rows(Import import)
    {
        await using AppDbContext db = postgres.CreateDbContext();
        return await db.ImportRows.Where(r => r.ImportId == import.Id).OrderBy(r => r.LineNo).ToListAsync();
    }

    protected async Task<int> CountRows(Import import)
    {
        await using AppDbContext db = postgres.CreateDbContext();
        return await db.ImportRows.CountAsync(r => r.ImportId == import.Id);
    }

    private IFileStore Files()
    {
        return BlobFileStore.Create(new StorageOptions { ConnectionString = azurite.ConnectionString });
    }
}
