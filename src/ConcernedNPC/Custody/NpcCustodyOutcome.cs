namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>What the ledger did with one request.</summary>
internal enum NpcCustodyOutcome
{
    /// <summary>The ledger changed.</summary>
    Applied = 0,

    /// <summary>The same request with the same payload was already recorded.
    /// Nothing changed and nothing is wrong: this is what a retry looks like,
    /// and answering it as success is what makes a resumed job safe.</summary>
    AlreadySatisfied = 1,

    /// <summary>The same name carries a different payload. <b>Never waved
    /// through as satisfied</b>: that told a caller reusing a name that the
    /// wrong thing had happened successfully, and silently kept the first
    /// payload.</summary>
    RejectedDifferentPayload = 2,

    /// <summary>Refused: unknown request, a place material may not leave, fewer
    /// units recorded than asked for, or a settled record being settled a
    /// second, different way.</summary>
    Rejected = 3,
}
