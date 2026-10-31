using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Errors;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Features.Imports;

public static class GetImport
{
    public class Handler(AppDbContext db)
    {
        public async Task<ImportDto> Handle(Guid id, CancellationToken ct)
        {
            return await ImportDto.Query(db).SingleOrDefaultAsync(i => i.Id == id, ct)
                   ?? throw new NotFoundException("Import", id);
        }
    }
}
