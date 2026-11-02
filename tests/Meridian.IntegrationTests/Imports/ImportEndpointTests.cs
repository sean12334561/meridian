using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Meridian.Api.Features.Imports;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Jobs;
using Meridian.Api.Shared.Seeding;
using Meridian.Api.Shared.Storage;
using Meridian.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.IntegrationTests.Imports;

public class ImportEndpointTests(PostgresFixture postgres, AzuriteFixture azurite)
    : IClassFixture<PostgresFixture>, IClassFixture<AzuriteFixture>, IAsyncLifetime
{
    private const string Dana = "1";
    private const string Lena = "3";

    private static readonly byte[] Csv = "assembly_vote_id,session\nMA/2024/001,79\n"u8.ToArray();

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await using AppDbContext db = postgres.CreateDbContext();
        await new Seeder(db, NullLogger<Seeder>.Instance).SeedAsync("member_states.csv");

        _factory = CreateFactory(maxBytes: null);
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Upload_returns_202_and_stores_the_file_the_import_and_its_job()
    {
        using HttpRequestMessage request = UploadRequest("votes.csv", Csv, Dana);
        request.Headers.Add("X-Correlation-ID", "upload-test");

        HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Guid id = (await response.Content.ReadFromJsonAsync<ImportCreatedDto>())!.ImportId;
        Assert.EndsWith($"/api/imports/{id}", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        await using AppDbContext db = postgres.CreateDbContext();
        Import import = await db.Imports.SingleAsync(i => i.Id == id);
        Assert.Equal("votes.csv", import.FileName);
        Assert.Equal(1, import.UploadedBy);
        Assert.Equal(ImportStatus.Pending, import.Status);
        Assert.Equal(ImportPhase.Queued, import.Phase);
        Assert.Equal(Csv.Length, import.SizeBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Csv)), import.Sha256);
        Assert.Equal("upload-test", import.CorrelationId);

        await using Stream stored = await Files().OpenReadAsync(import.BlobKey, default);
        using MemoryStream copy = new();
        await stored.CopyToAsync(copy);
        Assert.Equal(Csv, copy.ToArray());

        List<Job> jobs = await db.Jobs.Where(j => j.Type == ImportJobPayload.JobType).ToListAsync();
        Job job = Assert.Single(jobs, j => ClaimedJob.From(j).ReadPayload<ImportJobPayload>().ImportId == id);
        Assert.Equal(JobStatus.Pending, job.Status);
        Assert.Equal("upload-test", ClaimedJob.From(job).CorrelationId);
    }

    [Fact]
    public async Task An_admin_can_upload()
    {
        HttpResponseMessage response = await _client.SendAsync(UploadRequest("votes.csv", Csv, Lena));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task The_extension_check_ignores_case()
    {
        HttpResponseMessage response = await _client.SendAsync(UploadRequest("VOTES.CSV", Csv, Dana));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Theory]
    [InlineData("votes.txt", "a,b")]
    [InlineData("votes", "a,b")]
    [InlineData("votes.csv", "")]
    public async Task A_bad_file_gets_a_400_on_the_file_field_and_stores_nothing(string fileName, string content)
    {
        int before = await ImportCount();

        HttpResponseMessage response = await _client.SendAsync(
            UploadRequest(fileName, System.Text.Encoding.UTF8.GetBytes(content), Dana));

        await AssertFileError(response);
        Assert.Equal(before, await ImportCount());
    }

    [Fact]
    public async Task A_form_without_a_file_gets_a_400_on_the_file_field()
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/imports")
        {
            Content = new MultipartFormDataContent { { new StringContent("x"), "other" } },
        };
        request.Headers.Add("X-User-Id", Dana);

        await AssertFileError(await _client.SendAsync(request));
    }

    [Fact]
    public async Task An_upload_over_the_limit_gets_a_413_problem_and_stores_nothing()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(maxBytes: 10);
        using HttpClient client = factory.CreateClient();
        int before = await ImportCount();

        HttpResponseMessage response = await client.SendAsync(UploadRequest("votes.csv", Csv, Dana));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(before, await ImportCount());
    }

    [Fact]
    public async Task An_upload_over_kestrels_default_limit_works_on_a_real_server()
    {
        // TestServer enforces no body limit, so only Kestrel shows that the filter raises it (default 30 MB)
        await using WebApplicationFactory<Program> factory = CreateFactory(maxBytes: 40_000_000);
        factory.UseKestrel(0);
        factory.StartServer();
        using HttpClient client = factory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(2);

        HttpResponseMessage accepted = await client.SendAsync(
            UploadRequest("votes.csv", new byte[31_000_000], Dana));
        HttpResponseMessage rejected = await client.SendAsync(
            UploadRequest("votes.csv", new byte[41_000_000], Dana));

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rejected.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/api/imports")]
    [InlineData("GET", "/api/imports")]
    [InlineData("GET", "/api/imports/7f1a2c9e-0000-0000-0000-000000000000")]
    public async Task Every_import_route_needs_a_user(string method, string path)
    {
        HttpResponseMessage response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_is_newest_first_with_the_uploaders_name()
    {
        Guid first = await Upload();
        Guid second = await Upload();

        List<ImportDto> imports = (await Get<List<ImportDto>>("/api/imports"))!;

        Assert.True(
            imports.FindIndex(i => i.Id == second) < imports.FindIndex(i => i.Id == first));
        Assert.Equal("Dana", imports.Single(i => i.Id == first).UploadedByName);
    }

    [Fact]
    public async Task Status_shows_progress_saved_by_the_worker()
    {
        Guid id = await Upload();
        await using AppDbContext db = postgres.CreateDbContext();
        await db.Imports.Where(i => i.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(i => i.Status, ImportStatus.Running)
            .SetProperty(i => i.Phase, ImportPhase.Parsing)
            .SetProperty(i => i.ProcessedRows, 10)
            .SetProperty(i => i.TotalRows, 40));

        ImportDto import = (await Get<ImportDto>($"/api/imports/{id}"))!;

        Assert.Equal(
            (ImportStatus.Running, ImportPhase.Parsing, 10, (int?)40),
            (import.Status, import.Phase, import.ProcessedRows, import.TotalRows));
    }

    [Fact]
    public async Task The_worker_picks_up_an_uploaded_file_and_parses_it()
    {
        await using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);
            b.UseSetting("Storage:ConnectionString", azurite.ConnectionString);
            b.UseSetting("Worker:Enabled", "true");
            b.UseSetting("Worker:PollIntervalMs", "20");
        });
        using HttpClient client = factory.CreateClient();
        byte[] csv = "assembly_vote_id,session,vote_date,resolution_title,country_code,country_name,ballot,published_at\nMA/2024/001,79,2024-02-06,Title,BRA,Brazil,Y,2024-04-02T09:00:00Z\n"u8.ToArray();

        HttpResponseMessage response = await client.SendAsync(UploadRequest("votes.csv", csv, Dana));
        Guid id = (await response.Content.ReadFromJsonAsync<ImportCreatedDto>())!.ImportId;

        await using AppDbContext db = postgres.CreateDbContext();
        await Wait.Until(async () => await db.Imports.AsNoTracking().AnyAsync(i => i.Id == id && i.Phase == ImportPhase.Done));
        Assert.Equal(1, await db.ImportRows.CountAsync(r => r.ImportId == id));
    }

    [Fact]
    public async Task An_unknown_import_gets_a_404_problem()
    {
        using HttpRequestMessage request = new(HttpMethod.Get, $"/api/imports/{Guid.NewGuid()}");
        request.Headers.Add("X-User-Id", Dana);

        HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private WebApplicationFactory<Program> CreateFactory(long? maxBytes)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);
            b.UseSetting("Storage:ConnectionString", azurite.ConnectionString);
            if (maxBytes is not null)
            {
                b.UseSetting("Upload:MaxBytes", maxBytes.ToString());
            }
        });
    }

    private IFileStore Files()
    {
        return BlobFileStore.Create(new StorageOptions { ConnectionString = azurite.ConnectionString });
    }

    private static HttpRequestMessage UploadRequest(string fileName, byte[] content, string userId)
    {
        ByteArrayContent file = new(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        HttpRequestMessage request = new(HttpMethod.Post, "/api/imports")
        {
            Content = new MultipartFormDataContent { { file, "file", fileName } },
        };
        request.Headers.Add("X-User-Id", userId);
        return request;
    }

    private async Task<Guid> Upload()
    {
        HttpResponseMessage response = await _client.SendAsync(UploadRequest("votes.csv", Csv, Dana));
        return (await response.Content.ReadFromJsonAsync<ImportCreatedDto>())!.ImportId;
    }

    private async Task<T?> Get<T>(string path)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.Add("X-User-Id", Dana);
        HttpResponseMessage response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(TestJson);
    }

    private static readonly JsonSerializerOptions TestJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private async Task<int> ImportCount()
    {
        await using AppDbContext db = postgres.CreateDbContext();
        return await db.Imports.CountAsync();
    }

    private static async Task AssertFileError(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("file", out _));
    }
}
