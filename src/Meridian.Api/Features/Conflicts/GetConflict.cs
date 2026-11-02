using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Errors;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Features.Conflicts;

public static class GetConflict
{
    public class Handler(AppDbContext db)
    {
        public async Task<ConflictDto> Handle(long id, CancellationToken ct)
        {
            return await ConflictDto.Query(db).SingleOrDefaultAsync(c => c.Id == id, ct)
                   ?? throw new NotFoundException("Conflict", id);
        }
    }
}
