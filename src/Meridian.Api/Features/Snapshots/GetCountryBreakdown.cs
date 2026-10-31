using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Errors;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Features.Snapshots;

public static class GetCountryBreakdown
{
    public class Handler(AppDbContext db)
    {
        public async Task<CountryBreakdownDto> Handle(long id, string code, CancellationToken ct)
        {
            string countryCode = code.ToUpperInvariant();

            SnapshotScoreDto score = await SnapshotQueries.Scores(db, id).SingleOrDefaultAsync(s => s.CountryCode == countryCode, ct)
                                     ?? throw new NotFoundException("Snapshot country", $"{id}/{countryCode}");

            List<ContributionDto> contributions = await (
                    from c in db.SnapshotContributions.AsNoTracking()
                    join v in db.Votes on c.VoteId equals v.Id
                    join i in db.Imports on c.SourceImportId equals i.Id
                    where c.SnapshotId == id && c.CountryCode == countryCode
                    orderby v.VoteDate descending, v.VoteRef
                    select new ContributionDto
                    {
                        VoteId = c.VoteId,
                        VoteRef = v.VoteRef,
                        Title = v.Title,
                        Ballot = c.Ballot,
                        Position = c.Position,
                        Weight = c.Weight,
                        Counted = c.Counted,
                        Points = c.Points,
                        Reason = c.Reason,
                        SourceImportId = c.SourceImportId,
                        SourceFileName = i.FileName,
                    })
                .ToListAsync(ct);

            return new CountryBreakdownDto { Score = score, Contributions = contributions };
        }
    }
}
