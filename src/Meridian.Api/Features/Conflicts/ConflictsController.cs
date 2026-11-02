using Meridian.Api.Shared.Auth;
using Meridian.Api.Shared.Paging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Api.Features.Conflicts;

[ApiController, Route("api/conflicts"), Produces("application/json"), Authorize(Policy = Policies.Analyst)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public class ConflictsController : ControllerBase
{
    [HttpGet(Name = nameof(GetConflicts))]
    [ProducesResponseType<PagedResult<ConflictDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public Task<PagedResult<ConflictDto>> GetConflicts([FromQuery] GetConflicts.Request request, [FromServices] GetConflicts.Handler handler, CancellationToken ct)
    {
        return handler.Handle(request, ct);
    }

    [HttpGet("{id:long}", Name = nameof(GetConflict))]
    [ProducesResponseType<ConflictDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<ConflictDto> GetConflict(long id, [FromServices] GetConflict.Handler handler, CancellationToken ct)
    {
        return handler.Handle(id, ct);
    }

    [HttpPost("{id:long}/decision", Name = nameof(DecideConflict))]
    [ProducesResponseType<ConflictDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<ConflictDto> DecideConflict(long id, DecideConflict.Request request, [FromServices] DecideConflict.Handler handler, CancellationToken ct)
    {
        return handler.Handle(id, request, ct);
    }
}
