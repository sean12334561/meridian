using Meridian.Api.Shared.Jobs;
using Meridian.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.IntegrationTests.Jobs;

public class WorkerRoleTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task Worker_enabled_decides_whether_the_app_runs_the_worker(string enabled, bool expected)
    {
        await using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Worker:Enabled", enabled);
                b.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);
            });

        IEnumerable<IHostedService> hostedServices = factory.Services.GetServices<IHostedService>();

        Assert.Equal(expected, hostedServices.OfType<JobWorker>().Any());
    }
}
