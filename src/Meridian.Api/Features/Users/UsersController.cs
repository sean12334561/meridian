using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Api.Features.Users;

[ApiController, Route("api"), Produces("application/json")]
public class UsersController : ControllerBase
{
    // Anonymous: the UI needs this list before anyone has been picked in the switcher
    [HttpGet("users", Name = nameof(GetUsers)), AllowAnonymous]
    public Task<List<UserDto>> GetUsers([FromServices] GetUsers.Handler handler, CancellationToken ct)
    {
        return handler.Handle(ct);
    }

    [HttpGet("me", Name = nameof(GetMe)), Authorize]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public Task<UserDto> GetMe([FromServices] GetMe.Handler handler, CancellationToken ct)
    {
        return handler.Handle(ct);
    }
}
