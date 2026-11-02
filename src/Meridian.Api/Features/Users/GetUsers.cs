using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Features.Users;

public static class GetUsers
{
    public class Handler(AppDbContext db)
    {
        public async Task<List<UserDto>> Handle(CancellationToken ct)
        {
            List<User> users = await db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync(ct);

            return users.Select(UserDto.From).ToList();
        }
    }
}
