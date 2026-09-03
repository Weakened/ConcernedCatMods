namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>RC15 lifecycle diagnostics: one monotonic generation counter
/// for the map session, advanced on every reconstruction boundary the
/// runtime observes (map available, vanilla map-data reload, world
/// unload). Support bundles then show WHICH transition preceded a fault
/// without logging any world, character, or pin data — the generation
/// number and the transition reason are the only payload. Bound flips
/// true only when a pin reconcile has completed inside the current
/// generation, which is the "stable, fully-bound map session" the
/// tombstone rule requires.</summary>
internal sealed class MapSessionTracker
{
    public int Generation { get; private set; }

    /// <summary>True between a completed reconcile and the next
    /// transition; the tombstone rule's session gate.</summary>
    public bool Bound { get; private set; }

    public string LastTransitionReason { get; private set; } = "none";

    /// <summary>A reconstruction boundary: new generation, not yet bound.
    /// Returns the new generation for the lifecycle log line.</summary>
    public int NoteTransition(string reason)
    {
        Generation++;
        Bound = false;
        LastTransitionReason = reason;
        return Generation;
    }

    /// <summary>A reconcile completed inside the current generation.</summary>
    public void NoteBound()
    {
        Bound = true;
    }
}
