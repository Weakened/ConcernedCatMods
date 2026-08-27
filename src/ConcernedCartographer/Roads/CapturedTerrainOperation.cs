using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>A successful terrain-paint operation captured on the placing
/// client: what road paint it lays down (null for Cultivate/Reset, which
/// erase road-ness), where, and the brush radius used for reconciliation.</summary>
internal readonly struct CapturedTerrainOperation
{
    public CapturedTerrainOperation(RoadKind? roadKind, Vector3 position, float radiusMeters, bool isTerraforming)
    {
        RoadKind = roadKind;
        Position = position;
        RadiusMeters = radiusMeters;
        IsTerraforming = isTerraforming;
    }

    public RoadKind? RoadKind { get; }
    public Vector3 Position { get; }
    public float RadiusMeters { get; }

    /// <summary>True for level/raise ops. Their dirt paint is a side effect
    /// of terraforming, not road building, so they reconcile covered
    /// other-kind ink but never record road observations themselves.</summary>
    public bool IsTerraforming { get; }
}
