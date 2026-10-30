using Meridian.Api.Shared;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddShared(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

WebApplication app = builder.Build();

app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
