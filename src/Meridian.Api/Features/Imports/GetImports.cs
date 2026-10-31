using Meridian.Api.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Features.Imports;

public static class GetImports
{
    public class Handler(AppDbContext db)
    {
        public Task<List<ImportDto>> Handle(CancellationToken ct)
        {
            return ImportDto.Query(db).OrderByDescending(i => i.UploadedAt).ToListAsync(ct);
        }
    }
}
