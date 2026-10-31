using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Api.Shared.Data.Entities;
using Meridian.IntegrationTests.Imports;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Meridian.IntegrationTests.Infrastructure;

/// <summary>Tests that load data through the import pipeline, then call the real API.</summary>
public abstract class ApiTestBase(PostgresFixture postgres, AzuriteFixture azurite) : ImportPipelineTestBase(postgres, azurite)
{
    protected const int Dana = 1;
    protected const int Omer = 2;
    protected const int Lena = 3;

    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly PostgresFixture _postgres = postgres;
    private readonly AzuriteFixture _azurite = azurite;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", _postgres.ConnectionString);
            b.UseSetting("Storage:ConnectionString", _azurite.ConnectionString);
        });
        _client = _factory.CreateClient();
    }

    public override async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    protected Task<HttpResponseMessage> Get(string path, int user = Dana)
    {
        return Send(HttpMethod.Get, path, null, user);
    }

    protected async Task<T> GetOk<T>(string path, int user = Dana)
    {
        HttpResponseMessage response = await Get(path, user);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    protected Task<HttpResponseMessage> Post(string path, object? body, int user = Dana)
    {
        return Send(HttpMethod.Post, path, body, user);
    }

    protected Task<HttpResponseMessage> Put(string path, object? body, int user = Lena)
    {
        return Send(HttpMethod.Put, path, body, user);
    }

    protected async Task<Import> RunFile(params string[] rows)
    {
        Import import = await CreateImport($"{Header}\n{string.Join('\n', rows)}\n");
        await Run(import);
        return import;
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body, int user)
    {
        HttpRequestMessage request = new(method, path);
        request.Headers.Add("X-User-Id", user.ToString());
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        return _client.SendAsync(request);
    }
}
