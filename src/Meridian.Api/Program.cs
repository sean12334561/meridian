using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using FluentValidation;
using Meridian.Api.Features.Conflicts;
using Meridian.Api.Features.Imports;
using Meridian.Api.Features.Snapshots;
using Meridian.Api.Features.Users;
using Meridian.Api.Features.Votes;
using Meridian.Api.Shared;
using Meridian.Api.Shared.Auth;
using Meridian.Api.Shared.Jobs;
using Meridian.Api.Shared.Observability;
using Meridian.Api.Shared.Seeding;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OpenApi;
using OpenTelemetry.Trace;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddShared(builder.Configuration)
    .AddUsersFeature()
    .AddImportsFeature()
    .AddConflictsFeature()
    .AddVotesFeature()
    .AddSnapshotsFeature()
    .AddValidatorsFromAssemblyContaining<Program>();
builder.Services
    .AddAuthentication(HeaderAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(HeaderAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Analyst, p => p.RequireRole("Analyst", "Admin"))
    .AddPolicy(Policies.Admin, p => p.RequireRole("Admin"));
builder.Services.AddControllers().AddJsonOptions(o => ConfigureJson(o.JsonSerializerOptions));
builder.Services.ConfigureHttpJsonOptions(o => ConfigureJson(o.SerializerOptions));    // read by OpenAPI
builder.Services.AddOpenApi(o => o.CreateSchemaReferenceId = SchemaId);
builder.Services.AddHealthChecks();
if (builder.Configuration.GetValue<bool>("Worker:Enabled"))
    builder.Services.AddHostedService<JobWorker>();
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry()
        .UseAzureMonitor()
        .WithTracing(t => t.AddSource(JobWorker.ActivitySourceName));
}

WebApplication app = builder.Build();

if (args.Contains("--migrate-and-seed"))
{
    await app.MigrateAndSeedAsync();
    return;
}

app.UseMiddleware<CorrelationIdMiddleware>();   // first: every log line and error carries the ID
app.UseExceptionHandler();
app.UseStatusCodePages();                       // bodyless 401, 403 and 404 become ProblemDetails too
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

static void ConfigureJson(JsonSerializerOptions options)
{
    options.Converters.Add(new JsonStringEnumConverter());
    options.NumberHandling = JsonNumberHandling.Strict;
}

// Every use case nests a class called Request: without the outer name they share one schema
static string? SchemaId(JsonTypeInfo type)
{
    string? id = OpenApiOptions.CreateDefaultSchemaReferenceId(type);

    return id is not null && type.Type.DeclaringType is { } useCase ? useCase.Name + id : id;
}
