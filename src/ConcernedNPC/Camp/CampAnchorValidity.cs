namespace TheConcernedCat.ConcernedNPC.Camp;

/// <summary>Whether an anchor offer can be believed right now. <b>Three-way on
/// purpose.</b>
///
/// The two shipped anchor readers in this repository disagree here, and the
/// disagreement is semantic rather than stylistic. One reads a bed and answers
/// yes or no; the other carries a "verified" flag and can say it does not know.
/// Collapsing the third value into <see cref="Gone"/> is the tempting
/// simplification and it is wrong: a bed in an unloaded zone reads as absent
/// through no fault of the bed, and an NPC that treats "I cannot see it" as "it
/// is not there" walks to the world's start every time the player travels.
///
/// So an offer this library cannot verify keeps whatever the resolver already
/// had, and only a positively absent one gives up.</summary>
internal enum CampAnchorValidity
{
    /// <summary>The source could not tell. <b>Keeps the anchor that is already
    /// held</b> when the kinds match; never promotes, never demotes.</summary>
    Unknown = 0,

    /// <summary>Read and believed: it is there, now.</summary>
    Valid = 1,

    /// <summary>Positively not there any more - destroyed, unclaimed, replaced.
    /// The only answer that gives an anchor up.</summary>
    Gone = 2,
}
