using Meridian.Api.Shared.Auth;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Features.Users;

public static class GetMe
{
    public class Handler(AppDbContext db, ICurrentUser currentUser)
    {
        public async Task<UserDto> Handle(CancellationToken ct)
        {
            User user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == currentUser.Id, ct);

            return UserDto.From(user);
        }
    }
}
