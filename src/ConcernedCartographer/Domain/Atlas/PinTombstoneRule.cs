namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>RC15 final beta blocker: when may a managed pin be tombstoned
/// as "the player deleted it in vanilla"? The RC14 absorber inferred
/// deletion whenever a tracked rendering was missing from the live pin
/// list — but vanilla rebuilds that list wholesale during login
/// (Minimap.LoadMapData → SetMapData → ClearPins + re-AddPin, verified by
/// decompile), so every relog could rewrite live cc:* pins Deleted=1 while
/// their save-file copies stayed behind as plain Fire/Portal markers.
///
/// The rule inverts the burden of proof: absence is NEVER deletion
/// evidence. Only an explicit vanilla deletion event (the user-facing
/// Minimap.RemovePin path, captured at its choke point) that occurs during
/// a stable, fully-bound map session — reconcile completed for the current
/// map generation — may tombstone, and only once (an already-deleted
/// entity is never re-tombstoned, so revisions stay honest). Everything
/// else is map reconstruction and must resolve to a rebind.</summary>
internal static class PinTombstoneRule
{
    public enum Verdict
    {
        /// <summary>Write the tombstone (store-level durable deletion).</summary>
        Tombstone,

        /// <summary>Keep the entity; the rendering is reconstruction
        /// fallout and must be re-linked by the next reconcile.</summary>
        KeepAndRebind,
    }

    /// <summary>The single decision point for every "rendering is gone"
    /// observation. explicitVanillaDelete is true only when the removal
    /// was captured at the vanilla RemovePin choke point (never inferred
    /// from list absence); sessionBound is true only between a completed
    /// reconcile and the next map/world transition; alreadyDeleted guards
    /// the exactly-once contract.</summary>
    public static Verdict Decide(bool explicitVanillaDelete, bool sessionBound, bool alreadyDeleted)
    {
        if (explicitVanillaDelete && sessionBound && !alreadyDeleted)
        {
            return Verdict.Tombstone;
        }

        return Verdict.KeepAndRebind;
    }
}
