namespace Meridian.Api.Shared.Data.Entities;

public enum Role { Analyst, Admin }

public enum ImportStatus { Pending, Running, Completed, Failed }

public enum ImportPhase { Queued, Parsing, Checking, Applying, Done }

public enum RowOutcome { New, Unchanged, Duplicate, Invalid, Contradiction, Conflict, PreviouslyDecided }

public enum ConflictStatus { Pending, Accepted, Rejected, Superseded }

public enum HistoryType
{
    VoteCreated,
    BallotsImported,
    ConflictOpened,
    ConflictAccepted,
    ConflictRejected,
    ConflictSuperseded,
    ValueChanged,
    DetailsChanged,
    PositionChanged,
    DecisionNotApplied,
}

public enum SnapshotStatus { Pending, Running, Completed, Failed }

public enum JobStatus { Pending, Running, Succeeded, Failed }
