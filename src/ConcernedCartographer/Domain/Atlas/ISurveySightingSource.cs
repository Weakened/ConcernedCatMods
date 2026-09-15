using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>An ordered, index-addressable snapshot of one loaded-world
/// surface the survey may examine.</summary>
/// <remarks>Reads are deliberately SPLIT and LAZY so the sweep keeps the
/// exact cost profile it has always had: <see cref="TryReadPlacement"/> is
/// the cheap positional read done for every examined entry, and
/// <see cref="TryReadName"/> — which is where a surface pays for component
/// lookups and for <c>UnityEngine.Object.name</c>, an interop call that
/// allocates a fresh string every time — runs ONLY for an entry the sweep
/// has already accepted as in range. Returning false from either step skips
/// the entry (destroyed object, or one the surface itself excludes, such as
/// a character); it still counts as examined so the per-tick budget stays
/// honest.</remarks>
internal interface ISurveySightingSource
{
    /// <summary>Entries in this snapshot. Read once per sweep.</summary>
    int Count { get; }

    /// <summary>The cheap read: where the entry is, and how large its own
    /// declared footprint is (0 when it has none).</summary>
    bool TryReadPlacement(int index, out RoadPoint position, out float footprintRadiusMeters);

    /// <summary>The expensive read, taken only for an in-range entry.</summary>
    bool TryReadName(int index, out string name);
}
