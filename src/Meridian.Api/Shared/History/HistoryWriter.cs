using System.Text.Json;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Shared.History;

/// <summary>
/// The only way to record changes to approved data. Per-row methods add to the context, so the
/// caller's SaveChanges commits the change and its trail together. Bulk methods run in the
/// caller's open transaction.
/// </summary>
public class HistoryWriter(AppDbContext db)
{
    private static readonly string[] DetailTargets = ["title", "vote_date", "session"];

    public void PositionChanged(
        long voteId, (string? Position, int? Weight) oldValue, (string? Position, int? Weight) newValue, int actorId)
    {
        db.VoteHistory.Add(new VoteHistoryEntry
        {
            VoteId = voteId,
            Type = HistoryType.PositionChanged,
            Target = "position",
            OldValue = JsonSerializer.Serialize(new { position = oldValue.Position, weight = oldValue.Weight }),
            NewValue = JsonSerializer.Serialize(new { position = newValue.Position, weight = newValue.Weight }),
            ActorId = actorId,
        });
    }

    /// <summary>Records the decision; an accepted one also records the change to the approved value.</summary>
    public void ConflictDecided(Conflict conflict, int actorId)
    {
        switch (conflict.Status)
        {
            case ConflictStatus.Accepted:
                AddConflictEntry(HistoryType.ConflictAccepted, conflict, actorId);
                AddConflictEntry(
                    DetailTargets.Contains(conflict.Target) ? HistoryType.DetailsChanged : HistoryType.ValueChanged,
                    conflict, actorId);
                break;
            case ConflictStatus.Rejected:
                AddConflictEntry(HistoryType.ConflictRejected, conflict, actorId);
                break;
            default:
                throw new InvalidOperationException($"Conflict {conflict.Id} has no decision to record.");
        }
    }

    public void DecisionNotApplied(Conflict conflict, ConflictStatus attempted, int actorId)
    {
        db.VoteHistory.Add(new VoteHistoryEntry
        {
            VoteId = conflict.VoteId,
            Type = HistoryType.DecisionNotApplied,
            Target = conflict.Target,
            NewValue = JsonSerializer.Serialize(new { attempted = attempted.ToString() }),
            ActorId = actorId,
            ImportId = conflict.ProposedSourceImportId,
            ConflictId = conflict.Id,
        });
    }

    public async Task VotesCreatedAsync(long[] voteIds, Guid importId, int actorId, CancellationToken ct)
    {
        RequireTransaction();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO vote_history (vote_id, type, new_value, actor_id, import_id)
            SELECT v.id, {nameof(HistoryType.VoteCreated)},
                   jsonb_build_object('session', v.session, 'voteDate', v.vote_date, 'title', v.title),
                   {actorId}, {importId}
            FROM votes v
            WHERE v.id = ANY({voteIds})
            """, ct);
    }

    /// <summary>Writes one row per vote that gained ballots from this import, with the count.</summary>
    public async Task BallotsImportedAsync(long[] voteIds, Guid importId, int actorId, CancellationToken ct)
    {
        RequireTransaction();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO vote_history (vote_id, type, new_value, actor_id, import_id)
            SELECT cv.vote_id, {nameof(HistoryType.BallotsImported)},
                   jsonb_build_object('count', COUNT(*)),
                   {actorId}, {importId}
            FROM country_votes cv
            WHERE cv.vote_id = ANY({voteIds}) AND cv.source_import_id = {importId}
            GROUP BY cv.vote_id
            """, ct);
    }

    public async Task ConflictsOpenedAsync(long[] conflictIds, int actorId, CancellationToken ct)
    {
        RequireTransaction();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO vote_history (vote_id, type, target, old_value, new_value, actor_id, import_id, conflict_id)
            SELECT c.vote_id, {nameof(HistoryType.ConflictOpened)}, c.target,
                   to_jsonb(c.approved_value), to_jsonb(c.proposed_value),
                   {actorId}, c.proposed_source_import_id, c.id
            FROM conflicts c
            WHERE c.id = ANY({conflictIds})
            """, ct);
    }

    public async Task ConflictsSupersededAsync(long[] conflictIds, int actorId, CancellationToken ct)
    {
        RequireTransaction();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO vote_history (vote_id, type, target, old_value, new_value, actor_id, import_id, conflict_id)
            SELECT c.vote_id, {nameof(HistoryType.ConflictSuperseded)}, c.target,
                   to_jsonb(c.approved_value), to_jsonb(c.proposed_value),
                   {actorId}, c.proposed_source_import_id, c.id
            FROM conflicts c
            WHERE c.id = ANY({conflictIds})
            """, ct);
    }

    private void AddConflictEntry(HistoryType type, Conflict conflict, int actorId)
    {
        db.VoteHistory.Add(new VoteHistoryEntry
        {
            VoteId = conflict.VoteId,
            Type = type,
            Target = conflict.Target,
            OldValue = JsonSerializer.Serialize(conflict.ApprovedValue),
            NewValue = JsonSerializer.Serialize(conflict.ProposedValue),
            ActorId = actorId,
            ImportId = conflict.ProposedSourceImportId,
            ConflictId = conflict.Id,
        });
    }

    // Outside a transaction a bulk insert would commit on its own, apart from the change it records
    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Bulk history must be written inside the caller's transaction.");
    }
}
