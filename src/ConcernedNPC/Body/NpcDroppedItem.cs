namespace TheConcernedCat.ConcernedNPC.Body;

/// <summary>One item a dying body put on the ground.
///
/// Both shipped bodies report this; one reported a name and a count and the
/// other reported the whole stack, and the runtime that only had the name had
/// to guess what kind of thing it had lost. The richer report is kept, because
/// the poorer one is derivable from it and the reverse is not, and because the
/// caller that classifies a tool needs the item itself rather than its
/// name.</summary>
internal readonly struct NpcDroppedItem
{
    internal NpcDroppedItem(
        string prefabName, string sharedName, int quality, int variant, int count, ItemDrop.ItemData? item)
    {
        PrefabName = prefabName ?? string.Empty;
        SharedName = sharedName ?? string.Empty;
        Quality = quality;
        Variant = variant;
        Count = count;
        Item = item;
    }

    /// <summary>The name of the prefab the stack drops as, or empty when the
    /// stack named none.</summary>
    internal string PrefabName { get; }

    /// <summary>The item's shared name, as the game would show it.</summary>
    internal string SharedName { get; }

    internal int Quality { get; }

    internal int Variant { get; }

    /// <summary>How many were on the ground, not how many were carried: a stack
    /// that failed to drop is not reported at all.</summary>
    internal int Count { get; }

    /// <summary>The item as it was in the body's inventory, for a caller that
    /// has to classify it. Null only when a caller constructed this without
    /// one.</summary>
    internal ItemDrop.ItemData? Item { get; }

    public override string ToString() =>
        (SharedName.Length == 0 ? PrefabName : SharedName) + " x" + Count;
}
