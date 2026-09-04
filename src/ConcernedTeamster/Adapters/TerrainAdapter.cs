using System.Runtime.CompilerServices;
using TheConcernedCat.ConcernedTeamster.Domain.Terrain;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters;

/// <summary>Read-only terrain sampling around a cart (CT-004): ground height
/// ahead of and behind the cart along its heading (for the domain grade
/// math) and the terrain paint under it (surface kind). **No terrain write
/// path exists here** — the only game members touched are the height/paint
/// getters verified in CART_INTERNALS.md, all probed at startup as part of
/// the cart telemetry capability. Heading comes from the pull handle
/// (m_attachPoint) direction, the semantics-anchored "front", with the
/// transform's forward axis as fallback; a cart lying on its side yields
/// grade-unavailable instead of a garbage number.</summary>
public static class TerrainAdapter
{
    /// <summary>Ground heights are sampled this far ahead of and behind the
    /// cart center (3 m total run — about a cart length), far enough to span
    /// terrain vertices, close enough to read the local slope.</summary>
    public const float HalfRunMeters = 1.5f;

    /// <summary>Immutable result of one ground read; grade and surface fail
    /// independently.</summary>
    public readonly struct GroundReading
    {
        public GroundReading(bool gradeAvailable, float instantGradePercent, TerrainSurfaceKind surface)
        {
            GradeAvailable = gradeAvailable;
            InstantGradePercent = instantGradePercent;
            Surface = surface;
        }

        public bool GradeAvailable { get; }

        public float InstantGradePercent { get; }

        public TerrainSurfaceKind Surface { get; }

        public static GroundReading Unavailable => new(false, float.NaN, TerrainSurfaceKind.Unavailable);
    }

    /// <summary>Reads grade and surface under one cart, or the unavailable
    /// reading on any failure. Never throws.</summary>
    public static GroundReading TryReadGround(object? cartComponent)
    {
        if (cartComponent is null || !CartAdapter.CapabilityEnabled)
        {
            return GroundReading.Unavailable;
        }

        try
        {
            return ReadGroundCore(cartComponent);
        }
        catch
        {
            // Fail closed: terrain features degrade to "unavailable", never
            // to an exception in the sample path.
            return GroundReading.Unavailable;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static GroundReading ReadGroundCore(object cartComponent)
    {
        Vagon? vagon = cartComponent as Vagon;
        if (vagon == null)
        {
            return GroundReading.Unavailable;
        }

        Vector3 center = vagon.transform.position;

        bool gradeAvailable = false;
        float instantGradePercent = float.NaN;
        Vector3 heading = ResolveHeading(vagon);
        if (heading.sqrMagnitude > 0f)
        {
            Vector3 ahead = center + heading * HalfRunMeters;
            Vector3 behind = center - heading * HalfRunMeters;
            if (Heightmap.GetHeight(ahead, out float heightAhead) &&
                Heightmap.GetHeight(behind, out float heightBehind))
            {
                instantGradePercent = GradeMath.ComputeInstantGradePercent(
                    heightAhead, heightBehind, HalfRunMeters * 2f);
                gradeAvailable = !float.IsNaN(instantGradePercent);
            }
        }

        TerrainSurfaceKind surface = TerrainSurfaceKind.Unavailable;
        Heightmap heightmap = Heightmap.FindHeightmap(center);
        if (heightmap != null)
        {
            heightmap.WorldToVertex(center, out int vertexX, out int vertexY);
            // An out-of-range vertex returns black — the game's own "nothing
            // painted" encoding — so the tile-edge case degrades to
            // Untouched, exactly what the game would render there.
            Color paint = heightmap.GetPaintMask(vertexX, vertexY);
            surface = TerrainPaint.Classify(paint.r, paint.g, paint.b, TerrainPaint.DefaultThreshold);
        }

        return new GroundReading(gradeAvailable, instantGradePercent, surface);
    }

    /// <summary>Unit XZ heading from cart center toward the pull handle;
    /// falls back to the transform's forward axis, and reports zero (grade
    /// unavailable) only when both are vertical — a flipped cart.</summary>
    private static Vector3 ResolveHeading(Vagon vagon)
    {
        Transform attachPoint = vagon.m_attachPoint;
        Vector3 direction = attachPoint != null
            ? attachPoint.position - vagon.transform.position
            : vagon.transform.forward;
        direction.y = 0f;
        float sqrMagnitude = direction.sqrMagnitude;
        if (sqrMagnitude < 1e-6f)
        {
            direction = vagon.transform.forward;
            direction.y = 0f;
            sqrMagnitude = direction.sqrMagnitude;
            if (sqrMagnitude < 1e-6f)
            {
                return Vector3.zero;
            }
        }

        return direction / Mathf.Sqrt(sqrMagnitude);
    }
}
