namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>RC14 final-smoke fix 4: when may a cached map-overlay handle
/// be trusted? Jötunn destroys every overlay texture on Minimap teardown
/// and clears its registry, so a handle cached in one game session paints
/// into a dead texture in the next — persisted roads loaded fine but the
/// minimap stayed blank ("Could not rebuild road map overlays" once per
/// redraw). The rule is deliberately a truth table the renderers share:
/// a handle is only used while it exists AND its texture is alive;
/// anything else must re-resolve against the current Minimap, which
/// re-creates the layer fresh. Presence alone (the RC13 behavior) is
/// exactly the relog bug.</summary>
internal static class OverlayHandleRule
{
    /// <summary>True when the cached handle must be dropped and freshly
    /// re-resolved before any pixel write or visibility toggle.</summary>
    public static bool MustReresolve(bool hasCachedHandle, bool cachedTextureAlive)
    {
        return !hasCachedHandle || !cachedTextureAlive;
    }
}
