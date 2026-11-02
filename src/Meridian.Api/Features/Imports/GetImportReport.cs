using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Errors;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Features.Imports;

public static class GetImportReport
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public class Handler(AppDbContext db)
    {
        /// <summary>An import has no report until it completes, so asking early is a 404.</summary>
        public async Task<ImportReportDto> Handle(Guid id, CancellationToken ct)
        {
            string report = await db.Imports.Where(i => i.Id == id).Select(i => i.Report).SingleOrDefaultAsync(ct)
                            ?? throw new NotFoundException("Import report", id);

            return JsonSerializer.Deserialize<ImportReportDto>(report, Json)!;
        }
    }
}
