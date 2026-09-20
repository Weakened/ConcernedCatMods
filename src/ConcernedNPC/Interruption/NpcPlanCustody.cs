namespace TheConcernedCat.ConcernedNPC.Interruption;

/// <summary>Whether this plan has a movement of a player's material in flight.
///
/// <b>This is the field the whole leaf turns on.</b> A plan can be killed at any
/// instant, and the only instants that can duplicate or lose material are the
/// ones where the world has been told to move something and the record does not
/// yet say what happened. So the record says <i>that a move was about to
/// happen</i> before it happens, and says what happened after - the same
/// write-ahead order the transfer executor already uses for one transfer, raised
/// to the plan.
///
/// <b>What each value costs a resumed plan.</b> <see cref="Clear"/> is
/// resumable. <see cref="Pending"/> never survives a reload as itself: an intent
/// with no recorded outcome is exactly the shape of an interruption, so
/// reconstruction turns it into <see cref="Uncertain"/>, which is the one state
/// nothing resolves automatically. That is the trade this program has already
/// made twice, in the ledger and in the executor: a person looking at a stopped
/// NPC is cheap, and a quietly duplicated or quietly deleted stack of a player's
/// material is not.</summary>
internal enum NpcPlanCustody
{
    /// <summary>Nobody said. <b>Never treated as clear.</b> A record written
    /// without this field set is a record whose author did not think about the
    /// question, and reconstruction answers it the same way it answers a
    /// pending one.</summary>
    Unspecified = 0,

    /// <summary>Nothing is in flight. Everything this plan has moved has been
    /// recorded as moved, and everything it has not moved is where the record
    /// says it is.</summary>
    Clear = 1,

    /// <summary>A move has been written down and its outcome has not. Live and
    /// ordinary within a session - it is what the instant before a transfer
    /// looks like - and never a state a plan is read back from disk in.
    /// </summary>
    Pending = 2,

    /// <summary>A move whose outcome nobody can establish from the record.
    /// Terminal for automatic handling: the plan stops, keeps the evidence, and
    /// waits for a person.</summary>
    Uncertain = 3,
}
