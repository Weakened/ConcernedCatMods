namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>Where a replayed custody row stands against the world saves the
/// role's record knows about. <b>The world-save marker rule, as custody sees
/// it.</b>
///
/// <b>Why the rule is split, and where the halves live.</b> Working out which
/// rows a loaded save contains means reading save markers out of the role's own
/// journal file - generations, world times, a lineage - and that file's format
/// is a durable thing this library deliberately owns no part of. So the role's
/// replay computes the standing and hands it over with each row, and this
/// library owns what a standing <i>means</i> to custody, which is the half that
/// three products would otherwise each get slightly wrong.
///
/// <b>The failure it exists to prevent.</b> A player loads yesterday's save.
/// The world has rolled back four transfers, but the record - a separate file -
/// still has them. Replaying them credits material into chests that do not
/// contain it and reports shortfalls the player must record as lost. Voiding
/// them is not a correction applied to the world; it is the record agreeing
/// with a world that has already decided.</summary>
internal enum NpcRowStanding
{
    /// <summary>Nobody said. Treated as <see cref="Live"/>, because a role that
    /// does not implement the marker rule must still work - it simply gets no
    /// protection from a rollback.</summary>
    Unspecified = 0,

    /// <summary>Real, and either confirmed by a save or made in this session.
    /// Applied normally.</summary>
    Live = 1,

    /// <summary>The loaded save predates this row: the world rolled it back.
    /// The record is kept, so its name is still known and a retry under it is
    /// answered as stale rather than started, and nothing is applied.</summary>
    Voided = 2,

    /// <summary>Could not be placed before or after the loaded save. Kept as
    /// evidence, applied to nothing, and waits for a person.</summary>
    Ambiguous = 3,
}
