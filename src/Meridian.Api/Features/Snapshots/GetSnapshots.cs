using Meridian.Api.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Features.Snapshots;

public static class GetSnapshots
{
    public class Handler(AppDbContext db)
    {
        public Task<List<SnapshotSummaryDto>> Handle(CancellationToken ct)
        {
            return SnapshotQueries.Summaries(db).OrderByDescending(s => s.CreatedAt).ToListAsync(ct);
        }
    }
}
