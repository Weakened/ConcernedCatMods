namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>An engine inventory behind a seam, so that the add-then-remove
/// ordering of every transfer can be tested and fault-injected without the
/// game running.
///
/// <b>The rule every implementation obeys: answer, never throw for a game
/// reason.</b> A chest that is gone, unloaded, warded or open in somebody's
/// hands is not an exception - it is <see cref="IsAvailable"/> false, or a
/// count of zero. The executor treats a throw as evidence of an uncertain
/// transfer, which is a heavy outcome to land a player in for an ordinary
/// state of the world.
///
/// <b><see cref="Add"/> may never mint.</b> An implementation that created
/// items from a name would conjure material on a retry, and the whole ledger
/// exists so that nothing does. Every shipped port returns zero from
/// <see cref="Add"/> unless it is moving items that already exist - which is
/// what <see cref="INpcInventoryMoveTarget"/> is for.</summary>
internal interface INpcInventoryPort
{
    /// <summary>What to call it in a sentence: "the chest by the workbench".
    /// A phrase, not a noun, because it is concatenated into evidence.
    /// </summary>
    string Describe { get; }

    /// <summary>Whether the inventory exists, is loaded, and may be written by
    /// this process now. Re-read before every use.</summary>
    bool IsAvailable { get; }

    /// <summary>How many units of <paramref name="material"/> are in it now.
    /// Negative is treated as "could not be counted".</summary>
    int Count(NpcMaterial material);

    /// <summary>How many of <paramref name="count"/> would fit now - counting
    /// stack limits, free slots, weight and what is already in there.</summary>
    int CanAccept(NpcMaterial material, int count);

    /// <summary>Adds up to <paramref name="count"/>; returns how many were
    /// added. <b>The executor does not believe this number</b> - it measures
    /// both inventories - and it is returned only so it can appear in the
    /// evidence when the two disagree.</summary>
    int Add(NpcMaterial material, int count);

    /// <summary>Removes up to <paramref name="count"/>; returns how many were
    /// removed. Believed no more than <see cref="Add"/> is.</summary>
    int Remove(NpcMaterial material, int count);
}
