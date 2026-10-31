using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Errors;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Features.Votes;

public static class GetVote
{
    public class Handler(AppDbContext db)
    {
        public async Task<VoteDetailDto> Handle(long id, CancellationToken ct)
        {
            Vote vote = await db.Votes.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id, ct)
                        ?? throw new NotFoundException("Vote", id);

            int openConflicts = await db.Conflicts.CountAsync(c => c.VoteId == id && c.Status == ConflictStatus.Pending, ct);

            List<CountryVoteDto> countryVotes = await db.CountryVotes
                .AsNoTracking()
                .Where(cv => cv.VoteId == id)
                .Join(db.Imports, cv => cv.SourceImportId, i => i.Id, (cv, i) => new CountryVoteDto
                {
                    CountryCode = cv.CountryCode,
                    CountryName = db.MemberStates
                        .Where(m => m.CountryCode == cv.CountryCode)
                        .Select(m => m.CountryName)
                        .First(),
                    Ballot = cv.Ballot,
                    SourceImportId = i.Id,
                    SourceFileName = i.FileName,
                    SourceLine = cv.SourceLine,
                    ChangedByDecision = db.Conflicts.Any(
                        c => c.VoteId == id && c.Target == cv.CountryCode && c.Status == ConflictStatus.Accepted),
                })
                .OrderBy(cv => cv.CountryCode)
                .ToListAsync(ct);

            return new VoteDetailDto
            {
                Vote = VoteDto.From(vote, openConflicts),
                TitleSourceImportId = vote.TitleSourceImportId,
                VoteDateSourceImportId = vote.VoteDateSourceImportId,
                SessionSourceImportId = vote.SessionSourceImportId,
                CountryVotes = countryVotes,
            };
        }
    }
}

public class VoteDetailDto
{
    public required VoteDto Vote { get; init; }
    public required Guid TitleSourceImportId { get; init; }
    public required Guid VoteDateSourceImportId { get; init; }
    public required Guid SessionSourceImportId { get; init; }
    public required List<CountryVoteDto> CountryVotes { get; init; }
}

public class CountryVoteDto
{
    public required string CountryCode { get; init; }
    public required string CountryName { get; init; }
    public required char Ballot { get; init; }
    public required Guid SourceImportId { get; init; }
    public required string SourceFileName { get; init; }
    public required int SourceLine { get; init; }
    public required bool ChangedByDecision { get; init; }
}
