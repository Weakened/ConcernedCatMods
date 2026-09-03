namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>RC13 polish 3: whether one vanilla large-map object may be
/// hidden as ORPHANED CHROME — a decorative backplate/container left
/// visibly empty after every control it framed was replaced by CC (the
/// RC11 per-group container hiding covers each button group's own
/// ancestor, but not a plate that frames a hidden group from outside,
/// like the visible-to-others toggle's backplate at the bottom-right).
/// The Unity side climbs parents from each already-hidden rail object
/// and gathers these facts per candidate; this pure rule is the single
/// verdict, so its truth table is testable: anything protected
/// (map image, hint bars, shared-map hint, pin roots, biome label), any
/// still-interactive control, or any text-bearing graphic keeps the
/// candidate visible — only pure decoration whose controls are all
/// already hidden may go. The climb is bounded and the large root itself
/// is never hideable. Restore is unconditional the moment ANY vanilla
/// fallback applies (ShowVanillaMapControls, palette shown, conflict,
/// CC UI failure, disable, teardown).</summary>
internal static class OrphanChromeRule
{
    /// <summary>Parents examined above each hidden rail object. The
    /// orphaned plate observed in practice is the direct parent; a small
    /// margin covers one wrapper level without letting the climb wander
    /// toward screen-level panels.</summary>
    public const int MaxClimbSteps = 3;

    /// <summary>Facts about one candidate object's subtree, gathered by
    /// the Unity adapter. "Live" means the element would be visible if
    /// the candidate itself were shown (its own activeSelf chain up to
    /// the candidate is on) — CC-hidden controls are not live, so a
    /// plate that only frames hidden controls reads as empty.</summary>
    public readonly struct CandidateFacts
    {
        public CandidateFacts(
            bool isLargeRootOrAbove,
            bool containsProtectedObject,
            bool hasLiveControl,
            bool hasLiveTextGraphic)
        {
            IsLargeRootOrAbove = isLargeRootOrAbove;
            ContainsProtectedObject = containsProtectedObject;
            HasLiveControl = hasLiveControl;
            HasLiveTextGraphic = hasLiveTextGraphic;
        }

        /// <summary>The candidate is the large map root itself (or
        /// escaped it): never hideable.</summary>
        public bool IsLargeRootOrAbove { get; }

        /// <summary>The subtree contains a protected object: the map
        /// image, a bottom hint bar, the shared-map hint, a pin root, or
        /// the biome label.</summary>
        public bool ContainsProtectedObject { get; }

        /// <summary>The subtree contains an interactive control that
        /// would still be visible.</summary>
        public bool HasLiveControl { get; }

        /// <summary>The subtree contains a would-be-visible graphic that
        /// is not plain decoration (any text-like element).</summary>
        public bool HasLiveTextGraphic { get; }
    }

    /// <summary>True when the candidate is hideable orphaned
    /// decoration.</summary>
    public static bool MayHide(in CandidateFacts facts)
    {
        return !facts.IsLargeRootOrAbove &&
            !facts.ContainsProtectedObject &&
            !facts.HasLiveControl &&
            !facts.HasLiveTextGraphic;
    }

    /// <summary>True when every chrome object this rule ever hid must be
    /// restored: any vanilla fallback (settings, conflict, failure,
    /// disable) means vanilla owns its UI again, exactly.</summary>
    public static bool MustRestore(bool anyVanillaControlsWanted)
    {
        return anyVanillaControlsWanted;
    }
}
