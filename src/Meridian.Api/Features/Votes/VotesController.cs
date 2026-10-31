using Meridian.Api.Shared.Auth;
using Meridian.Api.Shared.Paging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Api.Features.Votes;

[ApiController, Route("api/votes"), Produces("application/json"), Authorize(Policy = Policies.Analyst)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public class VotesController : ControllerBase
{
    [HttpGet(Name = nameof(GetVotes))]
    [ProducesResponseType<PagedResult<VoteDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public Task<PagedResult<VoteDto>> GetVotes([FromQuery] GetVotes.Request request, [FromServices] GetVotes.Handler handler, CancellationToken ct)
    {
        return handler.Handle(request, ct);
    }

    [HttpGet("{id:long}", Name = nameof(GetVote))]
    [ProducesResponseType<VoteDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<VoteDetailDto> GetVote(long id, [FromServices] GetVote.Handler handler, CancellationToken ct)
    {
        return handler.Handle(id, ct);
    }

    [HttpGet("{id:long}/timeline", Name = nameof(GetVoteTimeline))]
    [ProducesResponseType<List<TimelineEntryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<List<TimelineEntryDto>> GetVoteTimeline(long id, [FromServices] GetVoteTimeline.Handler handler, CancellationToken ct)
    {
        return handler.Handle(id, ct);
    }
}
