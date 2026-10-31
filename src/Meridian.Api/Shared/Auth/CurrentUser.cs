using System.Security.Claims;
using Meridian.Api.Shared.Data.Entities;

namespace Meridian.Api.Shared.Auth;

/// <summary>The acting user. Handlers read this instead of taking a user from the request body.</summary>
public interface ICurrentUser
{
    int Id { get; }
    Role Role { get; }
}

public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public int Id => int.Parse(Claim(ClaimTypes.NameIdentifier));

    public Role Role => Enum.Parse<Role>(Claim(ClaimTypes.Role));

    private string Claim(string type) =>
        accessor.HttpContext?.User.FindFirstValue(type)
        ?? throw new InvalidOperationException("No authenticated user in this context");
}
