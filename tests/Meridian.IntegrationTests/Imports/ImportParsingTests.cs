using System.Text;
using System.Text.Json;
using Meridian.Api.Features.Imports;
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

public class ImportParsingTests(PostgresFixture postgres, AzuriteFixture azurite)
    : IClassFixture<PostgresFixture>, IClassFixture<AzuriteFixture>, IAsyncLifetime
{
    private const string Header = "assembly_vote_id,session,vote_date,resolution_title,country_code,country_name,ballot,published_at";
    private const string Good = "MA/2024/001,79,2024-02-06,\"Title, With Comma\",BRA,Brazil,Y,2024-04-02T09:00:00Z";

    public async Task InitializeAsync()
    {
        await using AppDbContext db = postgres.CreateDbContext();
        await new Seeder(db, NullLogger<Seeder>.Instance).SeedAsync("member_states.csv");
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Writes_every_row_with_its_parsed_values_and_moves_to_checking()
    {
        string csv = $"{Header}\n{Good}\n" +
                     "MA/2024/002,79,2024-02-07,Other,bra,Brazil,N,2024-04-02T09:00:00Z\n" +
                     "MA/2024/003,79,2024-02-08,Other,BRA,Brazil,Yes,2024-04-02T09:00:00Z\n" +
                     "too,few\n";
        Import import = await CreateImport(csv);

        await Run(import);

        Import stored = await Reload(import);
        Assert.Equal(ImportStatus.Running, stored.Status);
        Assert.Equal(ImportPhase.Checking, stored.Phase);
        Assert.Equal(4, stored.ProcessedRows);
        Assert.Equal(4, stored.TotalRows);

        List<ImportRow> rows = await Rows(import);
        Assert.Equal([2, 3, 4, 5], rows.Select(r => r.LineNo));

        ImportRow good = rows[0];
        Assert.Equal(Good, good.RawText);
        Assert.Equal("MA/2024/001", good.VoteRef);
        Assert.Equal(79, good.Session);
        Assert.Equal(new DateOnly(2024, 2, 6), good.VoteDate);
        Assert.Equal("Title, With Comma", good.Title);
        Assert.Equal("BRA", good.CountryCode);
        Assert.Equal("Brazil", good.CountryName);
        Assert.Equal('Y', good.Ballot);
        Assert.Equal(new DateTimeOffset(2024, 4, 2, 9, 0, 0, TimeSpan.Zero), good.PublishedAt);
        Assert.Null(good.Outcome);
        Assert.Null(good.Normalizations);

        Assert.Null(rows[1].Outcome);
        using JsonDocument normalizations = JsonDocument.Parse(rows[1].Normalizations!);
        Assert.Equal("bra", normalizations.RootElement[0].GetProperty("original").GetString());

        Assert.Equal((RowOutcome.Invalid, "Invalid ballot 'Yes'"), (rows[2].Outcome!.Value, rows[2].Reason!));
        Assert.Equal((RowOutcome.Invalid, "Expected 8 columns, found 2"), (rows[3].Outcome!.Value, rows[3].Reason!));
        Assert.Equal("too,few", rows[3].RawText);
    }

    [Fact]
    public async Task A_file_larger_than_a_batch_shows_progress_while_it_runs()
    {
        const int rowCount = 35_000;
        Import import = await CreateImport(ManyRows(rowCount));

        Task run = Run(import);
        List<int> seen = [];
        while (!run.IsCompleted)
        {
            seen.Add((await Reload(import)).ProcessedRows);
        }
        await run;

        Assert.Contains(seen, processed => processed is > 0 and < rowCount);
        Assert.Equal(rowCount, await CountRows(import));
        Assert.Equal(rowCount, (await Reload(import)).TotalRows);
    }

    [Theory]
    [InlineData("assembly_vote_id,session\nMA/2024/001,79\n", "The header is missing")]
    [InlineData(Header + "\n", "The file has no data rows.")]
    public async Task A_bad_file_fails_the_import_without_failing_the_job(string csv, string error)
    {
        Import import = await CreateImport(csv);

        await Run(import);

        Import stored = await Reload(import);
        Assert.Equal(ImportStatus.Failed, stored.Status);
        Assert.Contains(error, stored.Error);
        Assert.NotNull(stored.CompletedAt);
        Assert.Equal(0, await CountRows(import));
    }

    [Fact]
    public async Task Bad_bytes_after_a_full_batch_leave_no_rows_behind()
    {
        byte[] content = [.. Encoding.UTF8.GetBytes(ManyRows(12_000)), 0xFF, 0xFE, (byte)'\n'];
        Import import = await CreateImport(content);

        await Run(import);

        Import stored = await Reload(import);
        Assert.Equal(ImportStatus.Failed, stored.Status);
        Assert.Equal("The file is not valid UTF-8.", stored.Error);
        Assert.Equal(0, await CountRows(import));
    }

    [Fact]
    public async Task A_retry_after_a_failure_parses_cleanly()
    {
        Guid id = Guid.NewGuid();
        Import import = NewImport(id);
        await Save(import);

        await Assert.ThrowsAnyAsync<Exception>(() => Run(import));      // the blob isn't there yet
        await Upload(import, Encoding.UTF8.GetBytes($"{Header}\n{Good}\n"));
        await Run(import);

        Assert.Equal(ImportPhase.Checking, (await Reload(import)).Phase);
        Assert.Equal(1, await CountRows(import));
    }

    [Fact]
    public async Task A_finished_parsing_phase_is_skipped_on_retry()
    {
        Import import = await CreateImport($"{Header}\n{Good}\n");
        await Run(import);

        await Run(await Reload(import));

        Assert.Equal(1, await CountRows(import));
        Assert.Equal(ImportPhase.Checking, (await Reload(import)).Phase);
    }

    [Fact]
    public async Task Using_up_the_attempts_fails_the_import_with_the_error()
    {
        Import import = await CreateImport($"{Header}\n{Good}\n");

        await Handler().OnFailedAsync(Job(import), "boom", default);

        Import stored = await Reload(import);
        Assert.Equal((ImportStatus.Failed, "boom"), (stored.Status, stored.Error!));
    }

    private ImportJobHandler Handler()
    {
        FixtureDbContextFactory factory = new(postgres);

        return new ImportJobHandler(factory, new ImportParser(factory, Files()));
    }

    private IFileStore Files()
    {
        return BlobFileStore.Create(new StorageOptions { ConnectionString = azurite.ConnectionString });
    }

    private Task Run(Import import)
    {
        return Handler().RunAsync(Job(import), default);
    }

    private static ClaimedJob Job(Import import)
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

    private static string ManyRows(int count)
    {
        StringBuilder csv = new($"{Header}\n");
        for (int i = 0; i < count; i++)
        {
            csv.Append(Good).Append('\n');
        }
        return csv.ToString();
    }

    private static Import NewImport(Guid id)
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

    private Task<Import> CreateImport(string csv)
    {
        return CreateImport(Encoding.UTF8.GetBytes(csv));
    }

    private async Task<Import> CreateImport(byte[] content)
    {
        Import import = NewImport(Guid.NewGuid());
        await Upload(import, content);
        await Save(import);
        return import;
    }

    private Task Upload(Import import, byte[] content)
    {
        return Files().SaveAsync(import.BlobKey, new MemoryStream(content), default);
    }

    private async Task Save(Import import)
    {
        await using AppDbContext db = postgres.CreateDbContext();
        db.Imports.Add(import);
        await db.SaveChangesAsync();
    }

    private async Task<Import> Reload(Import import)
    {
        await using AppDbContext db = postgres.CreateDbContext();
        return await db.Imports.AsNoTracking().SingleAsync(i => i.Id == import.Id);
    }

    private async Task<List<ImportRow>> Rows(Import import)
    {
        await using AppDbContext db = postgres.CreateDbContext();
        return await db.ImportRows.Where(r => r.ImportId == import.Id).OrderBy(r => r.LineNo).ToListAsync();
    }

    private async Task<int> CountRows(Import import)
    {
        await using AppDbContext db = postgres.CreateDbContext();
        return await db.ImportRows.CountAsync(r => r.ImportId == import.Id);
    }
}
