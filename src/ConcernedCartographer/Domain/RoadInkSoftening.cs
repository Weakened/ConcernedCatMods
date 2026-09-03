using System;

namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>RC13 polish 1: the softened large-map road ink profile. The
/// owner prefers the minimap's slightly soft presentation over the
/// razor-sharp vector edge, so the vector layer feathers the Dirt/Paved
/// quads across their width through an alpha gradient texture instead of
/// stamping extra geometry — same quad count, same centerline, same
/// budget, no under-stroke double-render. The profile is pure and pinned
/// here so its invariants are testable: a fully opaque core (colors
/// unchanged), a symmetric monotonic falloff to transparent edges, and a
/// widen factor chosen so the 50%-alpha extent of the feathered quad
/// equals the original crisp width exactly — the road *reads* as wide as
/// RC12 at every zoom, because widths stay screen-pixel-derived at every
/// rebake. Routes intentionally keep the crisp edge (they are plans, not
/// terrain ink) and route quads never use this profile.</summary>
internal static class RoadInkSoftening
{
    /// <summary>How much wider the feathered quad is than the crisp RC12
    /// quad. 4/3 pairs with <see cref="OpaqueCoreFraction"/> so the alpha
    /// profile crosses 0.5 exactly at the original half width
    /// (0.75 · 4/3 = 1): perceived width is preserved.</summary>
    public const float WidenFactor = 4f / 3f;

    /// <summary>Fraction of the feathered half-width that stays fully
    /// opaque (the crisp color core).</summary>
    public const float OpaqueCoreFraction = 0.5f;

    /// <summary>Alpha at a normalized offset from the quad centerline
    /// (0 = center, 1 = feathered edge). Symmetric (the absolute offset is
    /// used), 1 inside the opaque core, linear falloff to 0 at the
    /// edge.</summary>
    public static float Alpha(float normalizedOffsetFromCenter)
    {
        float offset = Math.Abs(normalizedOffsetFromCenter);
        if (offset >= 1f)
        {
            return 0f;
        }

        if (offset <= OpaqueCoreFraction)
        {
            return 1f;
        }

        return (1f - offset) / (1f - OpaqueCoreFraction);
    }
}
