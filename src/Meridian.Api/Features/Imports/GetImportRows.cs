using FluentValidation;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Errors;
using Meridian.Api.Shared.Paging;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Features.Imports;

public static class GetImportRows
{
    public class Request : PageRequest
    {
        public RowOutcome? Outcome { get; init; }

        /// <summary>Only rows whose values the import normalized (ING-13).</summary>
        public bool? Normalized { get; init; }
    }

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            Include(new PageRequestValidator());
        }
    }

    public class Handler(AppDbContext db, IValidator<Request> validator)
    {
        public async Task<PagedResult<ImportRowDto>> Handle(Guid id, Request request, CancellationToken ct)
        {
            await validator.ValidateAndThrowAsync(request, ct);

            if (!await db.Imports.AnyAsync(i => i.Id == id, ct))
            {
                throw new NotFoundException("Import", id);
            }

            IQueryable<ImportRow> rows = db.ImportRows.AsNoTracking().Where(r => r.ImportId == id);
            if (request.Outcome is not null)
            {
                rows = rows.Where(r => r.Outcome == request.Outcome);
            }

            if (request.Normalized == true)
            {
                rows = rows.Where(r => r.Normalizations != null);
            }

            return await PagedResult<ImportRowDto>.From(
                rows.OrderBy(r => r.LineNo).Select(r => new ImportRowDto
                {
                    LineNo = r.LineNo,
                    RawText = r.RawText,
                    VoteRef = r.VoteRef,
                    CountryCode = r.CountryCode,
                    Ballot = r.Ballot,
                    Outcome = r.Outcome,
                    Reason = r.Reason,
                    Normalizations = r.Normalizations,
                    ConflictId = r.ConflictId,
                }),
                request,
                ct);
        }
    }
}

public class ImportRowDto
{
    public required int LineNo { get; init; }
    public required string RawText { get; init; }
    public required string? VoteRef { get; init; }
    public required string? CountryCode { get; init; }
    public required char? Ballot { get; init; }
    public required RowOutcome? Outcome { get; init; }
    public required string? Reason { get; init; }

    /// <summary>JSON array of what was normalized, with the original values.</summary>
    public required string? Normalizations { get; init; }

    public required long? ConflictId { get; init; }
}
