using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Features.Imports;

public class ImportDto
{
    public required Guid Id { get; init; }
    public required string FileName { get; init; }
    public required string UploadedByName { get; init; }
    public required DateTimeOffset UploadedAt { get; init; }
    public required ImportStatus Status { get; init; }
    public required ImportPhase Phase { get; init; }
    public required int ProcessedRows { get; init; }
    public required int? TotalRows { get; init; }
    public required string? Error { get; init; }
    public required DateTimeOffset? CompletedAt { get; init; }

    // A projection, not a From: the report column can be large and neither endpoint returns it
    public static IQueryable<ImportDto> Query(AppDbContext db)
    {
        return db.Imports
            .AsNoTracking()
            .Join(db.Users, i => i.UploadedBy, u => u.Id, (i, u) => new ImportDto
            {
                Id = i.Id,
                FileName = i.FileName,
                UploadedByName = u.Name,
                UploadedAt = i.UploadedAt,
                Status = i.Status,
                Phase = i.Phase,
                ProcessedRows = i.ProcessedRows,
                TotalRows = i.TotalRows,
                Error = i.Error,
                CompletedAt = i.CompletedAt,
            });
    }
}
