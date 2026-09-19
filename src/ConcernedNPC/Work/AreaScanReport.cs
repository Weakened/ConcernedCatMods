namespace TheConcernedCat.ConcernedNPC.Work;

/// <summary>Why a scan of a work area came back with nothing - or that it did
/// not.
///
/// <b>Five answers where a naive scan has one.</b> "Found nothing" is the single
/// most dangerous result an NPC can report, because every one of these reads as
/// an empty list and only one of them means the job is done. Telling a player
/// his settlement is out of wood when the truth is that he walked away from it,
/// or that the scan ran out of budget, is how an NPC loses trust it does not get
/// back.</summary>
internal enum AreaScanOutcome
{
    /// <summary>Nobody scanned. Never "there is nothing".</summary>
    Unspecified = 0,

    /// <summary>Something usable was found.</summary>
    Found = 1,

    /// <summary>Everything is loaded, everything was examined, and there is
    /// genuinely nothing of what was asked for. <b>The only outcome that means
    /// a job can be finished.</b></summary>
    Empty = 2,

    /// <summary>What was asked for is there and used up - picked, awaiting
    /// respawn, already taken. Distinct from <see cref="Empty"/> because it
    /// tells the player to wait rather than to go somewhere else.</summary>
    Exhausted = 3,

    /// <summary>Some of the area is not loaded, so part of it was never looked
    /// at. Ask again when the player is nearer. Never a finished job.</summary>
    NotLoaded = 4,

    /// <summary>The scan ran out of its budget before finishing. Incomplete, not
    /// empty; the next tick continues. A spent budget is never a reason to stop
    /// a job.</summary>
    Incomplete = 5,

    /// <summary>The area itself could not be read, so nothing was scanned at
    /// all. Fail closed.</summary>
    AreaInvalid = 6,
}

/// <summary>What one pass over a work area actually did, beside what it found.
///
/// <b>What it guarantees.</b> That the difference between "nothing is there" and
/// "I did not look" survives all the way to the sentence a player reads. The
/// counts are not diagnostics: they are the evidence
/// <see cref="AreaScanOutcome"/> is derived from, and a leaf that derives an
/// outcome without them is guessing.</summary>
internal readonly struct AreaScanReport
{
    internal AreaScanReport(
        AreaScanOutcome outcome, int examined, int notLoaded, int rejected, int exhausted, bool truncatedByBudget)
    {
        Outcome = outcome;
        Examined = examined;
        NotLoaded = notLoaded;
        Rejected = rejected;
        Exhausted = exhausted;
        TruncatedByBudget = truncatedByBudget;
    }

    /// <summary>The single answer, derived from everything below.</summary>
    internal AreaScanOutcome Outcome { get; }

    /// <summary>How many candidates were looked at at all.</summary>
    internal int Examined { get; }

    /// <summary>How many were in ground that is not loaded - unknown, never
    /// empty.</summary>
    internal int NotLoaded { get; }

    /// <summary>How many were looked at and refused for a reason the probe
    /// could name.</summary>
    internal int Rejected { get; }

    /// <summary>How many were the right thing, already used up.</summary>
    internal int Exhausted { get; }

    /// <summary>Whether the pass stopped because it ran out of budget rather
    /// than because it finished. The one flag that turns "nothing found" into
    /// "not finished looking".</summary>
    internal bool TruncatedByBudget { get; }

    /// <summary>Whether the counts are internally possible: every candidate
    /// accounted for was examined. If the counts are the evidence an outcome is
    /// derived from, a report whose counts cannot all be true is evidence of
    /// nothing, and the outcome resting on it certainly is not.</summary>
    internal bool CountsAgree =>
        Examined >= 0
        && NotLoaded >= 0
        && Rejected >= 0
        && Exhausted >= 0
        && Examined >= NotLoaded + Rejected + Exhausted;

    /// <summary>Whether this pass proves the area holds nothing more of what was
    /// asked for. True only for <see cref="AreaScanOutcome.Empty"/> and
    /// <see cref="AreaScanOutcome.Exhausted"/>, and false whenever anything was
    /// unloaded, the budget ran out, or the counts do not add up - whatever the
    /// outcome says. Belt and braces, because this is the property a finished
    /// job is claimed on, and "he says he is done" is the single most expensive
    /// thing an NPC can be wrong about.</summary>
    internal bool IsConclusive =>
        (Outcome == AreaScanOutcome.Empty || Outcome == AreaScanOutcome.Exhausted)
        && NotLoaded == 0
        && !TruncatedByBudget
        && CountsAgree;
}
