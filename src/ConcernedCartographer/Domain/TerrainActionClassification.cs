namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>The verdict of <see cref="TerrainActionClassifier"/> for one
/// captured terrain operation: which player action it was, whether it is
/// authorized to create road data, and a human-readable description for the
/// always-on diagnostic log (so a future authority regression is visible in
/// LogOutput.log without rebuilding).</summary>
internal readonly struct TerrainActionClassification
{
    public TerrainActionClassification(
        TerrainActionCategory category,
        RoadKind? roadKind,
        bool erasesRoads,
        bool selectionMismatch,
        string description)
    {
        Category = category;
        RoadKind = roadKind;
        ErasesRoads = erasesRoads;
        SelectionMismatch = selectionMismatch;
        Description = description;
    }

    public TerrainActionCategory Category { get; }

    /// <summary>Non-null ONLY for an authorized road construction action:
    /// Pathen ⇒ Dirt, Paved road ⇒ Paved, each corroborated by its own
    /// paint. Every other action — Level, Raise, Cultivate, Reset paint,
    /// digging, unknown ops, selection mismatches — is null.</summary>
    public RoadKind? RoadKind { get; }

    /// <summary>True for paint-clearing operations that are NOT road
    /// construction: they repainted the ground, so recorded road ink of
    /// both kinds under the brush is stale and must be erased.</summary>
    public bool ErasesRoads { get; }

    /// <summary>True when the local player's selected build piece was
    /// available and did not match the operation. Road authority is refused
    /// in that case (fail closed).</summary>
    public bool SelectionMismatch { get; }

    /// <summary>Diagnostic identity line, e.g.
    /// "pathen (path_v2) paint=Dirt => Dirt road".</summary>
    public string Description { get; }
}
