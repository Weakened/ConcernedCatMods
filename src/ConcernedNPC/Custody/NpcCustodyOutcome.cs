namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>What the ledger did with one request.
///
/// <b>Zero is no answer at all</b> (#376). It was <c>Applied</c> while the type
/// was internal; the day a role could hold one, a defaulted result would have
/// read as a recorded success. Nothing persists these values.</summary>
public enum NpcCustodyOutcome
{
    /// <summary>Nothing was asked, or nothing answered. Never a success.
    /// </summary>
    Unspecified = 0,

    /// <summary>The ledger changed.</summary>
    Applied = 1,

    /// <summary>The same request with the same payload was already recorded.
    /// Nothing changed and nothing is wrong: this is what a retry looks like,
    /// and answering it as success is what makes a resumed job safe.</summary>
    AlreadySatisfied = 2,

    /// <summary>The same name carries a different payload. <b>Never waved
    /// through as satisfied</b>: that told a caller reusing a name that the
    /// wrong thing had happened successfully, and silently kept the first
    /// payload.</summary>
    RejectedDifferentPayload = 3,

    /// <summary>Refused: unknown request, a place material may not leave, fewer
    /// units recorded than asked for, or a settled record being settled a
    /// second, different way.</summary>
    Rejected = 4,

    /// <summary><b>That name belongs to work the world rolled back, or to a
    /// claim already given up.</b> Nothing was recorded and nothing was
    /// applied; the job needs a new name for this step.
    ///
    /// This is the value that stops the worst thing in this file from
    /// happening. Without it, a live retry under a name the marker rule had
    /// already voided was answered
    /// <see cref="AlreadySatisfied"/> - a success that records nothing - and
    /// material physically out of a chest existed in no holding at all, while
    /// the conservation invariant stayed true because nothing had ever been
    /// recorded to conserve. The transfer half of the marker rule has always
    /// refused this case; the acquisition half now does too, and the two speak
    /// the same word for it.</summary>
    Stale = 5,
}
