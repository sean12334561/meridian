using Meridian.Api.Shared.Auth;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Errors;
using Meridian.Api.Shared.History;
using Meridian.Api.Shared.Jobs;
using Meridian.Api.Shared.Observability;
using Meridian.Api.Shared.Seeding;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Shared;

public static class SharedServiceCollectionExtensions
{
    public static IServiceCollection AddShared(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextFactory<AppDbContext>(o => o
            .UseNpgsql(configuration.GetConnectionString("Default"))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<Seeder>();
        services.AddScoped<HistoryWriter>();

        services.Configure<WorkerOptions>(configuration.GetSection(WorkerOptions.SectionName));
        services.AddScoped<JobQueue>();

        services.AddScoped<CorrelationContext>();
        services.AddScoped<ICorrelationContext>(s => s.GetRequiredService<CorrelationContext>());

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
        services.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
            c.ProblemDetails.Extensions["correlationId"] =
                c.HttpContext.RequestServices.GetRequiredService<ICorrelationContext>().Id);

        return services;
    }
}
