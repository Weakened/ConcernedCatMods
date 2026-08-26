using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>A successful terrain-paint operation captured on the placing
/// client: what road paint it lays down (null for Cultivate/Reset, which
/// erase road-ness), where, and the brush radius used for reconciliation.</summary>
internal readonly struct CapturedTerrainOperation
{
    public CapturedTerrainOperation(RoadKind? roadKind, Vector3 position, float radiusMeters)
    {
        RoadKind = roadKind;
        Position = position;
        RadiusMeters = radiusMeters;
    }

    public RoadKind? RoadKind { get; }
    public Vector3 Position { get; }
    public float RadiusMeters { get; }
}
