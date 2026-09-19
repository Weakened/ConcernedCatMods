namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>One row of the reconciliation matrix: what comparing the record
/// with the world showed.
///
/// <b>Five of the six need a person, and none of them is ever acted on
/// automatically.</b> That is the whole design. Every automatic response to a
/// discrepancy either conjures material or destroys it, and both are worse than
/// telling the player what was seen.</summary>
internal enum NpcReconciliationFindingKind
{
    Unspecified = 0,

    /// <summary>The place holds exactly what the record says.</summary>
    Matches = 1,

    /// <summary>An uncertain transfer whose effect the counts show: the source
    /// lost the units and the destination gained them. <b>A person confirms;
    /// it is never applied on the strength of the counts alone</b>, because
    /// the same counts are produced by the player moving the material
    /// themselves.</summary>
    EffectVisible = 2,

    /// <summary>An uncertain transfer the counts show did not happen. A person
    /// confirms.</summary>
    NoEffect = 3,

    /// <summary>Fewer units than the record expects. A person may record the
    /// difference as lost; nothing is refunded and nothing is replaced.
    /// </summary>
    BelowExpected = 4,

    /// <summary>More units than the record expects. <b>Never credited.</b>
    /// Material that appears from nowhere is the player's, or a bug, and
    /// crediting it to a job is how a ledger starts inventing.</summary>
    AboveExpected = 5,

    /// <summary>The place cannot be observed now, or several uncertain changes
    /// touch it at once, so the counts cannot say which happened.</summary>
    Unclear = 6,
}
