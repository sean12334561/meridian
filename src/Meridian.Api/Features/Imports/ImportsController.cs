using Meridian.Api.Shared.Auth;
using Meridian.Api.Shared.Paging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Api.Features.Imports;

[ApiController, Route("api/imports"), Produces("application/json"), Authorize(Policy = Policies.Analyst)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public class ImportsController : ControllerBase
{
    [HttpPost(Name = nameof(UploadImport)), Consumes("multipart/form-data")]
    [ServiceFilter(typeof(UploadLimitFilter))]
    [ProducesResponseType<ImportCreatedDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
    public async Task<ActionResult<ImportCreatedDto>> UploadImport(IFormFile? file, [FromServices] UploadImport.Handler handler, CancellationToken ct)
    {
        ImportCreatedDto created = await handler.Handle(new UploadImport.Request { File = file }, ct);

        return AcceptedAtRoute(nameof(GetImport), new { id = created.ImportId }, created);
    }

    [HttpGet(Name = nameof(GetImports))]
    [ProducesResponseType<List<ImportDto>>(StatusCodes.Status200OK)]
    public Task<List<ImportDto>> GetImports([FromServices] GetImports.Handler handler, CancellationToken ct)
    {
        return handler.Handle(ct);
    }

    [HttpGet("{id:guid}", Name = nameof(GetImport))]
    [ProducesResponseType<ImportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<ImportDto> GetImport(Guid id, [FromServices] GetImport.Handler handler, CancellationToken ct)
    {
        return handler.Handle(id, ct);
    }

    [HttpGet("{id:guid}/report", Name = nameof(GetImportReport))]
    [ProducesResponseType<ImportReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<ImportReportDto> GetImportReport(Guid id, [FromServices] GetImportReport.Handler handler, CancellationToken ct)
    {
        return handler.Handle(id, ct);
    }

    [HttpGet("{id:guid}/rows", Name = nameof(GetImportRows))]
    [ProducesResponseType<PagedResult<ImportRowDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<PagedResult<ImportRowDto>> GetImportRows(Guid id, [FromQuery] GetImportRows.Request request, [FromServices] GetImportRows.Handler handler, CancellationToken ct)
    {
        return handler.Handle(id, request, ct);
    }
}
