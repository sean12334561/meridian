using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Features.Conflicts;

public class ConflictDto
{
    public required long Id { get; init; }
    public required long VoteId { get; init; }
    public required string VoteRef { get; init; }
    public required string VoteTitle { get; init; }
    public required string Target { get; init; }
    public required ConflictStatus Status { get; init; }
    public required string ApprovedValue { get; init; }
    public required ConflictSourceDto ApprovedSource { get; init; }
    public required string ProposedValue { get; init; }
    public required ConflictSourceDto ProposedSource { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string? DecidedByName { get; init; }
    public required DateTimeOffset? DecidedAt { get; init; }
    public required string? Note { get; init; }

    public static IQueryable<ConflictDto> Query(AppDbContext db)
    {
        return
            from c in db.Conflicts.AsNoTracking()
            join v in db.Votes on c.VoteId equals v.Id
            join approvedImport in db.Imports on c.ApprovedSourceImportId equals approvedImport.Id
            join approvedUploader in db.Users on approvedImport.UploadedBy equals approvedUploader.Id
            join proposedImport in db.Imports on c.ProposedSourceImportId equals proposedImport.Id
            join proposedUploader in db.Users on proposedImport.UploadedBy equals proposedUploader.Id
            from decider in db.Users.Where(u => u.Id == c.DecidedBy).DefaultIfEmpty()
            from approvedRow in db.ImportRows
                .Where(r => r.ImportId == c.ApprovedSourceImportId && r.LineNo == c.ApprovedSourceLine)
                .DefaultIfEmpty()
            select new ConflictDto
            {
                Id = c.Id,
                VoteId = c.VoteId,
                VoteRef = v.VoteRef,
                VoteTitle = v.Title,
                Target = c.Target,
                Status = c.Status,
                ApprovedValue = c.ApprovedValue,
                ApprovedSource = new ConflictSourceDto
                {
                    ImportId = approvedImport.Id,
                    FileName = approvedImport.FileName,
                    UploadedByName = approvedUploader.Name,
                    UploadedAt = approvedImport.UploadedAt,
                    Line = c.ApprovedSourceLine,
                    PublishedAt = approvedRow == null ? null : approvedRow.PublishedAt,
                },
                ProposedValue = c.ProposedValue,
                ProposedSource = new ConflictSourceDto
                {
                    ImportId = proposedImport.Id,
                    FileName = proposedImport.FileName,
                    UploadedByName = proposedUploader.Name,
                    UploadedAt = proposedImport.UploadedAt,
                    Line = c.ProposedSourceLine,
                    PublishedAt = c.PublishedAt,
                },
                CreatedAt = c.CreatedAt,
                DecidedByName = decider == null ? null : decider.Name,
                DecidedAt = c.DecidedAt,
                Note = c.Note,
            };
    }
}

/// <summary>Where one side of a conflict came from (CON-02).</summary>
public class ConflictSourceDto
{
    public required Guid ImportId { get; init; }
    public required string FileName { get; init; }
    public required string UploadedByName { get; init; }
    public required DateTimeOffset UploadedAt { get; init; }
    public required int? Line { get; init; }
    public required DateTimeOffset? PublishedAt { get; init; }
}
