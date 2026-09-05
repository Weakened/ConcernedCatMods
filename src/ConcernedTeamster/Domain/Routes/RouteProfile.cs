using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Terrain;

namespace TheConcernedCat.ConcernedTeamster.Domain.Routes;

/// <summary>One worst-grade candidate section of a profiled route.</summary>
public readonly struct RouteProfileSegment
{
    public RouteProfileSegment(float startMeters, float lengthMeters, float gradePercent)
    {
        StartMeters = startMeters;
        LengthMeters = lengthMeters;
        GradePercent = gradePercent;
    }

    /// <summary>Distance from the route start (first point) to the segment.</summary>
    public float StartMeters { get; }

    public float LengthMeters { get; }

    /// <summary>Signed grade in traversal direction (+ climbs, − descends).</summary>
    public float GradePercent { get; }
}

/// <summary>Immutable cart-safety profile of one route (CT-023). The honesty
/// invariant is structural: SampledMeters + UnsampledMeters equals
/// TotalDistanceMeters exactly, grades and surfaces come only from segments
/// whose ends BOTH sampled, and nothing is interpolated across a gap —
/// unloaded terrain shows up as unsampled meters, never as guessed data.</summary>
public sealed class RouteProfile
{
    /// <summary>|grade| band upper bounds in percent; the last band is
    /// unbounded (25%+). Bands partition GradeBandMeters.</summary>
    public static readonly float[] GradeBandUpperBoundsPercent = { 3f, 8f, 15f, 25f };

    public const int GradeBandCount = 5;

    public RouteProfile(
        float totalDistanceMeters,
        float sampledMeters,
        float unsampledMeters,
        IReadOnlyDictionary<TerrainSurfaceKind, float> surfaceMeters,
        float surfaceUnknownMeters,
        IReadOnlyList<float> gradeBandMeters,
        float worstUphillGradePercent,
        float worstDownhillGradePercent,
        float maxAbsGradePercent,
        IReadOnlyList<RouteProfileSegment> worstSegments,
        IReadOnlyList<RouteProfileSegment> unsampledSpans,
        float sampleSpacingMeters,
        int positionCount,
        int sampledPositionCount)
    {
        TotalDistanceMeters = totalDistanceMeters;
        SampledMeters = sampledMeters;
        UnsampledMeters = unsampledMeters;
        SurfaceMeters = surfaceMeters;
        SurfaceUnknownMeters = surfaceUnknownMeters;
        GradeBandMeters = gradeBandMeters;
        WorstUphillGradePercent = worstUphillGradePercent;
        WorstDownhillGradePercent = worstDownhillGradePercent;
        MaxAbsGradePercent = maxAbsGradePercent;
        WorstSegments = worstSegments;
        UnsampledSpans = unsampledSpans;
        SampleSpacingMeters = sampleSpacingMeters;
        PositionCount = positionCount;
        SampledPositionCount = sampledPositionCount;
    }

    /// <summary>Horizontal (XZ) polyline length — same convention the route
    /// picker displays.</summary>
    public float TotalDistanceMeters { get; }

    /// <summary>Meters covered by segments whose both ends sampled.</summary>
    public float SampledMeters { get; }

    /// <summary>Meters touching at least one failed sample (unloaded
    /// terrain, capability off). Explicitly reported, never guessed.</summary>
    public float UnsampledMeters { get; }

    /// <summary>Sampled meters by known surface kind (a segment counts under
    /// its start sample's surface). Kinds with zero meters are omitted.</summary>
    public IReadOnlyDictionary<TerrainSurfaceKind, float> SurfaceMeters { get; }

    /// <summary>Sampled meters whose surface could not be classified even
    /// though height (and so grade) was readable.</summary>
    public float SurfaceUnknownMeters { get; }

    /// <summary>Sampled meters per |grade| band (see
    /// <see cref="GradeBandUpperBoundsPercent"/>); always
    /// <see cref="GradeBandCount"/> entries.</summary>
    public IReadOnlyList<float> GradeBandMeters { get; }

    /// <summary>Steepest climb in traversal direction; NaN when no sampled
    /// segment climbs.</summary>
    public float WorstUphillGradePercent { get; }

    /// <summary>Steepest descent (most negative grade); NaN when no sampled
    /// segment descends.</summary>
    public float WorstDownhillGradePercent { get; }

    /// <summary>Steepest sampled section regardless of direction; NaN when
    /// no segment produced a grade. This is the load bottleneck grade —
    /// routes are hauled both ways, so the steepest section is a climb in
    /// one of them.</summary>
    public float MaxAbsGradePercent { get; }

    /// <summary>Up to three steepest sampled sections, steepest first.</summary>
    public IReadOnlyList<RouteProfileSegment> WorstSegments { get; }

    /// <summary>Up to three longest contiguous unsampled stretches, longest
    /// first (CT-024: gap disclosure with locations). GradePercent is NaN —
    /// nothing was measured there. Their lengths sum to at most
    /// <see cref="UnsampledMeters"/>; the total is always exact even when
    /// more than three spans exist.</summary>
    public IReadOnlyList<RouteProfileSegment> UnsampledSpans { get; }

    public float SampleSpacingMeters { get; }

    public int PositionCount { get; }

    public int SampledPositionCount { get; }
}
