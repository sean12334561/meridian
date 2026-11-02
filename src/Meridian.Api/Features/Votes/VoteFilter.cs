using Meridian.Api.Shared.Data.Entities;

namespace Meridian.Api.Features.Votes;

/// <summary>Shared by the vote list and the bulk position action, so both mean the same votes.</summary>
public class VoteFilter
{
    public int? Session { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    /// <summary>Part of the Assembly reference or the title.</summary>
    public string? Q { get; init; }

    public bool? HasPosition { get; init; }

    public IQueryable<Vote> Apply(IQueryable<Vote> votes)
    {
        if (Session is not null)
        {
            votes = votes.Where(v => v.Session == Session);
        }

        if (From is not null)
        {
            votes = votes.Where(v => v.VoteDate >= From);
        }

        if (To is not null)
        {
            votes = votes.Where(v => v.VoteDate <= To);
        }

        if (!string.IsNullOrWhiteSpace(Q))
        {
            string text = Q.Trim().ToLower();
            votes = votes.Where(v => v.VoteRef.ToLower().Contains(text) || v.Title.ToLower().Contains(text));
        }

        if (HasPosition is not null)
        {
            votes = votes.Where(v => (v.Position != null) == HasPosition);
        }

        return votes;
    }
}
