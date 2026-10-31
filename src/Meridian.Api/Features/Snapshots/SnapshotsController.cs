using Meridian.Api.Shared.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Api.Features.Snapshots;

[ApiController, Route("api/snapshots"), Produces("application/json"), Authorize(Policy = Policies.Analyst)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public class SnapshotsController : ControllerBase
{
    [HttpPost(Name = nameof(CreateSnapshot))]
    [ProducesResponseType<SnapshotCreatedDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<SnapshotCreatedDto>> CreateSnapshot(CreateSnapshot.Request request, [FromServices] CreateSnapshot.Handler handler, CancellationToken ct)
    {
        SnapshotCreatedDto created = await handler.Handle(request, ct);

        return AcceptedAtRoute(nameof(GetSnapshot), new { id = created.SnapshotId }, created);
    }

    [HttpGet(Name = nameof(GetSnapshots))]
    [ProducesResponseType<List<SnapshotSummaryDto>>(StatusCodes.Status200OK)]
    public Task<List<SnapshotSummaryDto>> GetSnapshots([FromServices] GetSnapshots.Handler handler, CancellationToken ct)
    {
        return handler.Handle(ct);
    }

    [HttpGet("{id:long}", Name = nameof(GetSnapshot))]
    [ProducesResponseType<SnapshotDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<SnapshotDetailDto> GetSnapshot(long id, [FromServices] GetSnapshot.Handler handler, CancellationToken ct)
    {
        return handler.Handle(id, ct);
    }

    [HttpGet("{id:long}/countries/{code}", Name = nameof(GetCountryBreakdown))]
    [ProducesResponseType<CountryBreakdownDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<CountryBreakdownDto> GetCountryBreakdown(long id, string code, [FromServices] GetCountryBreakdown.Handler handler, CancellationToken ct)
    {
        return handler.Handle(id, code, ct);
    }

    [HttpPost("{id:long}/publish", Name = nameof(PublishSnapshot)), Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<PublicationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<PublicationDto> PublishSnapshot(long id, PublishSnapshot.Request request, [FromServices] PublishSnapshot.Handler handler, CancellationToken ct)
    {
        return handler.Handle(id, request, ct);
    }
}
