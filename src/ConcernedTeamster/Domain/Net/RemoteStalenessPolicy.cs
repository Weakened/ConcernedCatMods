namespace TheConcernedCat.ConcernedTeamster.Domain.Net;

/// <summary>Decides when a remote-derived reading is too old to present as
/// current (CT-029). A cart owned by another client updates only as fast as
/// the game replicates it, and stops entirely if that client disconnects, so
/// any observed value carries an age; past the threshold the display must
/// mark itself stale rather than show a frozen number as live. Pure: the
/// caller supplies both timestamps (no clock here), so it is deterministic
/// and testable.</summary>
public static class RemoteStalenessPolicy
{
    /// <summary>Remote readings older than this (seconds) are stale. Chosen
    /// above the game's own replication cadence so normal jitter never trips
    /// it, low enough that a disconnected owner's frozen cart is flagged
    /// within a few seconds.</summary>
    public const double DefaultStaleAfterSeconds = 5.0;

    /// <summary>True when a reading sampled at <paramref name="sampledAt"/>
    /// is stale as of <paramref name="now"/> (age &gt;= the threshold). A
    /// backwards age is treated as fresh (local clock skew is not staleness);
    /// an infinite/NaN age is stale (unknown age fails closed). Callers must
    /// stamp <paramref name="sampledAt"/> from the LOCAL clock at read time —
    /// never from a remote-supplied timestamp — so a future-dated remote value
    /// cannot mask its own staleness.</summary>
    public static bool IsStale(double sampledAt, double now, double staleAfterSeconds = DefaultStaleAfterSeconds)
    {
        double age = now - sampledAt;
        if (double.IsNaN(age) || double.IsInfinity(age))
        {
            return true;
        }

        if (age <= 0d)
        {
            return false;
        }

        return age >= staleAfterSeconds;
    }

    /// <summary>Local-authority readings are never stale by this policy —
    /// the local client samples them live — so callers can short-circuit
    /// with this for readability.</summary>
    public static bool IsStaleForRemote(bool isLocalAuthority, double sampledAt, double now,
        double staleAfterSeconds = DefaultStaleAfterSeconds)
    {
        return !isLocalAuthority && IsStale(sampledAt, now, staleAfterSeconds);
    }
}
