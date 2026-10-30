using FluentValidation;
using Meridian.Api.Shared;
using Meridian.Api.Shared.Observability;
using Meridian.Api.Shared.Seeding;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddShared(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

WebApplication app = builder.Build();

if (args.Contains("--migrate-and-seed"))
{
    await app.MigrateAndSeedAsync();
    return;
}

app.UseMiddleware<CorrelationIdMiddleware>();   // first: every log line and error carries the ID
app.UseExceptionHandler();
app.UseStatusCodePages();                       // bodyless 401, 403 and 404 become ProblemDetails too

app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
