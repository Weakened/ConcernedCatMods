namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>What one attempt at a transfer came to.
///
/// <b>The value that matters is <see cref="Uncertain"/>.</b> Every other system
/// that moves things between two stores either retries or compensates, and both
/// are wrong here: a retry over an engine that may already have moved the units
/// duplicates them, and a compensation over one that did not destroys them.
/// When the measured counts cannot say which happened, this says so, nothing is
/// credited, and a person decides.</summary>
internal enum NpcTransferOutcome
{
    /// <summary>Nothing has been decided yet. Used by the ledger's own check to
    /// mean "the record permits this"; never a receipt.</summary>
    Unspecified = 0,

    /// <summary>Every unit arrived.</summary>
    Completed = 1,

    /// <summary>Some units arrived; the rest are where the receipt says.
    /// </summary>
    Partial = 2,

    /// <summary>Nothing moved, and that was established before anything was
    /// mutated. Safe to plan around.</summary>
    Refused = 3,

    /// <summary>A mutation may or may not have happened and the actual counts
    /// could not prove which. Nothing is replayed or compensated.</summary>
    Uncertain = 4,

    /// <summary>Planned against an older custody revision; nothing moved.
    /// </summary>
    Stale = 5,

    /// <summary>This exact transfer is already recorded as done. <b>What a
    /// retry after an interruption looks like</b>, and the reason a resumed job
    /// may walk its plan again from the start.</summary>
    AlreadySatisfied = 6,

    /// <summary>The same name carries a different transfer. A caller defect,
    /// refused.</summary>
    RejectedDifferentPayload = 7,
}
