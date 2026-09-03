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

    /// <summary>RC15 item 8: may a full-texture write (SetPixels32/Apply)
    /// proceed? A redraw resolves its handles first, spends CPU time
    /// building pixel buffers, and only then writes — map teardown in that
    /// window destroys the texture between resolve and write (the RC13
    /// Sentry NullReferenceException at Texture2D.SetPixels32 during
    /// "rebuild road map"). Liveness must therefore hold at BOTH ends:
    /// anything else aborts the write, resets the cached handles, and
    /// retries on the next valid map session instead of throwing.</summary>
    public static bool MayWrite(bool textureAliveAtResolve, bool textureAliveAtWrite)
    {
        return textureAliveAtResolve && textureAliveAtWrite;
    }
}
