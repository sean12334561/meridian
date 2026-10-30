using Meridian.Api.Shared;
using Meridian.Api.Shared.Seeding;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddShared(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

WebApplication app = builder.Build();

if (args.Contains("--migrate-and-seed"))
{
    await app.MigrateAndSeedAsync();
    return;
}

app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
