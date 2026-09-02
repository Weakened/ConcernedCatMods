namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>Game-independent mirror of the terrain paint a captured
/// operation lays down (<c>TerrainModifier.PaintType</c> on the Unity
/// side). Paint alone is NEVER road authority (RC10, DEF-v1.0-007); it is
/// only the corroborating half of the identity-plus-paint agreement rule in
/// <see cref="TerrainActionClassifier"/>.</summary>
internal enum TerrainPaintKind
{
    None = 0,
    Dirt = 1,
    Cultivate = 2,
    Paved = 3,
    Reset = 4,

    /// <summary>A paint value this version does not know (future game
    /// updates). Never road authority.</summary>
    Other = 5,
}
