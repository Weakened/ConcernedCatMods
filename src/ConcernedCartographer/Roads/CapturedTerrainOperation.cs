using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>A successful paint-clearing terrain operation captured on the
/// placing client, already classified by actual action identity
/// (DEF-v1.0-007): the road kind it is authorized to create (null for
/// Level/Raise/Cultivate/Reset/unknown, which only erase road-ness), where,
/// the brush radius used for reconciliation, and the classified identity
/// for the diagnostic log.</summary>
internal readonly struct CapturedTerrainOperation
{
    public CapturedTerrainOperation(
        RoadKind? roadKind,
        Vector3 position,
        float radiusMeters,
        TerrainActionCategory category,
        string actionDescription)
    {
        RoadKind = roadKind;
        Position = position;
        RadiusMeters = radiusMeters;
        Category = category;
        ActionDescription = actionDescription;
    }

    /// <summary>Non-null ONLY for explicit local-player Pathen (Dirt) or
    /// Paved road (Paved) construction; every other action is null and
    /// acts as a road-erasure signal.</summary>
    public RoadKind? RoadKind { get; }

    public Vector3 Position { get; }
    public float RadiusMeters { get; }

    /// <summary>The classified player action (Level, Raise, Pathen…), for
    /// branching diagnostics.</summary>
    public TerrainActionCategory Category { get; }

    /// <summary>The classifier's identity line for the always-on
    /// rate-limited diagnostic log.</summary>
    public string ActionDescription { get; }
}
