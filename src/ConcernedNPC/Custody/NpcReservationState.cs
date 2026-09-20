namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>Where one material reservation stands.
///
/// <b>Three of the four are terminal, and that is the exactly-once
/// guarantee.</b> A reservation leaves <see cref="Held"/> once and never
/// returns, so a refund cannot happen twice, a commit cannot happen twice, and
/// a commit cannot follow a refund. It is enforced by the transition rather
/// than asked of the caller, because the caller is a retry loop that does not
/// know what its previous attempt achieved.</summary>
internal enum NpcReservationState
{
    /// <summary>Set aside, or taken out and not yet spent. <b>The only state a
    /// refund is owed from.</b></summary>
    Held = 0,

    /// <summary>Spent on what it was for. Terminal.</summary>
    Committed = 1,

    /// <summary>Given back to where it came from. Terminal.</summary>
    Refunded = 2,

    /// <summary>A commit was started and its outcome is unknown. <b>Terminal
    /// for automatic handling</b>: neither committed nor refundable, because
    /// nothing here will guess which of the two happened. A person decides.
    /// </summary>
    Uncertain = 3,
}
