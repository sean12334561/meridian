using System.Security.Claims;
using System.Text.Encodings.Web;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Meridian.Api.Shared.Auth;

/// <summary>Trusts the X-User-Id header. Spoofable by design: production auth is out of scope.</summary>
public class HeaderAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AppDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Header";
    public const string HeaderName = "X-User-Id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey(HeaderName))
        {
            return AuthenticateResult.NoResult();
        }

        if (!int.TryParse(Request.Headers[HeaderName].ToString(), out int userId))
        {
            return AuthenticateResult.Fail($"{HeaderName} is not a user ID");
        }

        User? user = await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId, Context.RequestAborted);
        if (user is null)
        {
            return AuthenticateResult.Fail($"{HeaderName} does not match a user");
        }

        ClaimsIdentity identity = new(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Name),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
            ],
            SchemeName);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
