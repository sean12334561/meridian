using Meridian.Api.Features.Imports.Classification;
using Meridian.Api.Features.Imports.Parsing;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Features.Imports;

/// <summary>
/// Runs the import's phases. Each phase commits the move to the next, so a retry continues from
/// imports.phase: finished work can't run twice, because nothing may be deleted.
/// </summary>
public class ImportJobHandler(IDbContextFactory<AppDbContext> dbFactory, ImportParser parser, InFileChecks inFileChecks) : IJobHandler
{
    public async Task RunAsync(ClaimedJob job, CancellationToken ct)
    {
        Guid importId = job.ReadPayload<ImportJobPayload>().ImportId;

        await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
        Import import = await db.Imports.AsNoTracking().SingleAsync(i => i.Id == importId, ct);

        try
        {
            if (import.Phase <= ImportPhase.Parsing)
            {
                await parser.ParseAsync(import, ct);
            }

            if (import.Phase <= ImportPhase.Checking)
            {
                await inFileChecks.RunAsync(importId, ct);
            }
        }
        catch (ImportFileException ex)
        {
            // The job itself ended normally: a bad file fails the same way on every attempt
            await MarkFailedAsync(importId, ex.Message, ct);
        }
    }

    public Task OnFailedAsync(ClaimedJob job, string error, CancellationToken ct)
    {
        return MarkFailedAsync(job.ReadPayload<ImportJobPayload>().ImportId, error, ct);
    }

    private async Task MarkFailedAsync(Guid importId, string error, CancellationToken ct)
    {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
        await db.Imports
            .Where(i => i.Id == importId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, ImportStatus.Failed)
                .SetProperty(i => i.Error, error)
                .SetProperty(i => i.CompletedAt, DateTimeOffset.UtcNow), ct);
    }
}
