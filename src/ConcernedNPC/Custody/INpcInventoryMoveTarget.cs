namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>An inventory that can take units straight out of another engine
/// inventory in one engine call, keeping the moved item instances.
///
/// <b>Why this exists beside <see cref="INpcInventoryPort.Add"/>.</b> A port's
/// <c>Add</c> has no item to add: an adapter implementing it could only create
/// a new stack from the name, and a created stack loses what the moved one
/// carried - a tool's wear, a crafter's name, a world level. The contract is
/// that an item's identity survives a transfer, so an engine that offers a
/// move-in-one-call is used through this instead, and only the measurement
/// changes nothing.
///
/// <b>It still returns nothing the executor trusts.</b> The executor counts
/// both inventories before and after and classifies from those deltas alone,
/// exactly as it does for the two-step path. A move that half worked is
/// uncertain, not "the number the engine returned".</summary>
internal interface INpcInventoryMoveTarget
{
    /// <summary>True when <paramref name="source"/> is an engine inventory this
    /// target can move items out of. False - for any reason at all, including
    /// not knowing - sends the executor down the ordinary add-then-remove path,
    /// which is always correct and merely loses the metadata.</summary>
    bool CanMoveFrom(INpcInventoryPort source);

    /// <summary>Moves up to <paramref name="count"/> units of
    /// <paramref name="material"/> from <paramref name="source"/>, adding to
    /// this inventory before removing from the source for every stack.</summary>
    void MoveFrom(INpcInventoryPort source, NpcMaterial material, int count);
}
