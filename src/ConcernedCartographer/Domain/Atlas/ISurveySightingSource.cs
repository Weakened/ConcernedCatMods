namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>An ordered, index-addressable snapshot of one loaded-world
/// surface the survey may examine.</summary>
/// <remarks>Reads are deliberately LAZY: <see cref="TryRead"/> is called
/// once per EXAMINED entry, never once per snapshot entry, so a surface
/// wrapping thousands of live objects (<c>ZNetScene.m_instances</c>) costs
/// the same bounded per-frame work it always has. Returning false skips an
/// entry that has since been destroyed or that the surface itself excludes
/// (characters, for example).</remarks>
internal interface ISurveySightingSource
{
    /// <summary>Entries in this snapshot. Read once per sweep.</summary>
    int Count { get; }

    /// <summary>Reads one entry, or returns false to skip it. The entry
    /// still counts as examined so the per-tick budget stays honest.</summary>
    bool TryRead(int index, out SurveySighting sighting);
}
