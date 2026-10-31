namespace Meridian.Api.Shared.Data;

public static class Locks
{
    /// <summary>Taken with pg_advisory_xact_lock by import apply batches and conflict decisions.</summary>
    public const long ApprovedData = 1001;
}
