using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Features.Snapshots;

/// <summary>Projections used by more than one snapshot read.</summary>
public static class SnapshotQueries
{
    public static IQueryable<SnapshotSummaryDto> Summaries(AppDbContext db)
    {
        return db.Snapshots
            .AsNoTracking()
            .Join(db.Users, s => s.CreatedBy, u => u.Id, (s, u) => new SnapshotSummaryDto
            {
                Id = s.Id,
                Name = s.Name,
                Status = s.Status,
                CreatedByName = u.Name,
                CreatedAt = s.CreatedAt,
                CompletedAt = s.CompletedAt,
                PublishedQuarter = db.SnapshotPublications
                    .Where(p => p.SnapshotId == s.Id && p.WithdrawnAt == null)
                    .OrderByDescending(p => p.PublishedAt)
                    .Select(p => p.Quarter)
                    .FirstOrDefault(),
            });
    }

    public static IQueryable<SnapshotScoreDto> Scores(AppDbContext db, long snapshotId)
    {
        return db.SnapshotScores
            .AsNoTracking()
            .Where(s => s.SnapshotId == snapshotId)
            .Select(s => new SnapshotScoreDto
            {
                CountryCode = s.CountryCode,
                CountryName = db.MemberStates
                    .Where(m => m.CountryCode == s.CountryCode)
                    .Select(m => m.CountryName)
                    .First(),
                Score = s.Score,
                Rank = s.Rank,
                CountedVotes = s.CountedVotes,
                CountedWeight = s.CountedWeight,
                EarnedWeight = s.EarnedWeight,
            });
    }

    public static IQueryable<PublicationDto> Publications(AppDbContext db)
    {
        return db.SnapshotPublications
            .AsNoTracking()
            .Join(db.Users, p => p.PublishedBy, u => u.Id, (p, u) => new PublicationDto
            {
                Id = p.Id,
                SnapshotId = p.SnapshotId,
                Quarter = p.Quarter,
                PublishedByName = u.Name,
                PublishedAt = p.PublishedAt,
                Reason = p.Reason,
                WithdrawnAt = p.WithdrawnAt,
                SupersededBy = p.SupersededBy,
            });
    }
}
