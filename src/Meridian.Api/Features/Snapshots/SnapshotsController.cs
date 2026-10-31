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

        return Accepted(created);
    }
}
