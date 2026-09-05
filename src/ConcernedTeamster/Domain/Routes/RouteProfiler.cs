using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Cartographer;
using TheConcernedCat.ConcernedTeamster.Domain.Terrain;

namespace TheConcernedCat.ConcernedTeamster.Domain.Routes;

/// <summary>One terrain read along a route: false means the ground there
/// could not be sampled (unloaded terrain, capability off) — the profiler
/// records the stretch as unsampled and never guesses. Surface may be
/// <see cref="TerrainSurfaceKind.Unavailable"/> even when height succeeds.</summary>
public delegate bool RouteSampleProbe(float x, float z, out float height, out TerrainSurfaceKind surface);

/// <summary>Incremental, budgeted route profiler (CT-023). Construction is
/// pure geometry (positions along the polyline at fixed spacing, capped at
/// <see cref="MaxSamplePositions"/> by coarsening the spacing); each
/// <see cref="Advance"/> probes at most the caller's budget of positions —
/// the bookkeeping (<see cref="LastAdvanceSampleCount"/>,
/// <see cref="TotalSamplesConsumed"/>) is public so tests assert the budget
/// is respected. <see cref="Cancel"/> stops all further work permanently.
/// The finished profile partitions every meter into sampled or unsampled;
/// grades and surfaces come only from fully sampled segments.</summary>
public sealed class RouteProfiler
{
    public const float DefaultSampleSpacingMeters = 4f;
    public const int MaxSamplePositions = 4096;
    private const int WorstSegmentCount = 3;

    private readonly RouteSampleProbe _probe;
    private readonly float[] _vertexX;
    private readonly float[] _vertexZ;
    private readonly float[] _vertexT;
    private readonly float[] _positionT;
    private readonly bool[] _sampled;
    private readonly float[] _heights;
    private readonly TerrainSurfaceKind[] _surfaces;

    private int _nextIndex;
    private int _segmentCursor;
    private bool _cancelled;

    public RouteProfiler(
        IReadOnlyList<CartographerRoutePoint> points,
        RouteSampleProbe probe,
        float sampleSpacingMeters = DefaultSampleSpacingMeters)
    {
        _probe = probe;
        if (sampleSpacingMeters <= 0f || float.IsNaN(sampleSpacingMeters))
        {
            sampleSpacingMeters = DefaultSampleSpacingMeters;
        }

        int vertexCount = points?.Count ?? 0;
        _vertexX = new float[vertexCount];
        _vertexZ = new float[vertexCount];
        _vertexT = new float[vertexCount];
        float total = 0f;
        for (int index = 0; index < vertexCount; index++)
        {
            CartographerRoutePoint point = points![index];
            _vertexX[index] = point.X;
            _vertexZ[index] = point.Z;
            if (index > 0)
            {
                float dx = point.X - _vertexX[index - 1];
                float dz = point.Z - _vertexZ[index - 1];
                total += (float)Math.Sqrt((dx * dx) + (dz * dz));
            }

            _vertexT[index] = total;
        }

        TotalDistanceMeters = total;
        // !(total > 0f) also catches NaN from poisoned coordinates; either
        // way the route degrades to an empty profile instead of throwing.
        if (vertexCount < 2 || !(total > 0f) || float.IsInfinity(total))
        {
            _positionT = Array.Empty<float>();
            _sampled = Array.Empty<bool>();
            _heights = Array.Empty<float>();
            _surfaces = Array.Empty<TerrainSurfaceKind>();
            SampleSpacingMeters = sampleSpacingMeters;
            return;
        }

        // Bounded by construction: absurdly long routes coarsen the spacing
        // instead of growing the work.
        int stepCount = (int)Math.Floor(total / sampleSpacingMeters);
        if (stepCount + 2 > MaxSamplePositions)
        {
            sampleSpacingMeters = total / (MaxSamplePositions - 1);
            stepCount = MaxSamplePositions - 2;
        }

        SampleSpacingMeters = sampleSpacingMeters;
        var positions = new List<float>(stepCount + 2);
        for (int step = 0; step <= stepCount; step++)
        {
            positions.Add(step * sampleSpacingMeters);
        }

        // Close the route with its true endpoint unless the last step
        // already effectively reached it (tiny tail segments would produce
        // noise grades over centimeters). A single-position list must
        // append, never snap — snapping [0] to [total] would leave zero
        // segments and break the sampled+unsampled == total partition.
        if (positions.Count == 1 || total - positions[positions.Count - 1] > 0.5f)
        {
            positions.Add(total);
        }
        else
        {
            positions[positions.Count - 1] = total;
        }

        _positionT = positions.ToArray();
        _sampled = new bool[_positionT.Length];
        _heights = new float[_positionT.Length];
        _surfaces = new TerrainSurfaceKind[_positionT.Length];
    }

    public float TotalDistanceMeters { get; }

    public float SampleSpacingMeters { get; }

    public int PositionCount => _positionT.Length;

    public int PositionsProbed => _nextIndex;

    public bool IsCancelled => _cancelled;

    /// <summary>True when every position has been probed (a cancelled
    /// profiler is not complete — it is abandoned).</summary>
    public bool IsComplete => !_cancelled && _nextIndex >= _positionT.Length;

    public int TotalSamplesConsumed { get; private set; }

    public int LastAdvanceSampleCount { get; private set; }

    /// <summary>Probes up to <paramref name="maxSamples"/> positions and
    /// returns how many were actually consumed — never more than the
    /// budget, zero once cancelled or complete. The probe is invoked
    /// synchronously; callers own chunking this across frames.</summary>
    public int Advance(int maxSamples)
    {
        LastAdvanceSampleCount = 0;
        if (_cancelled || maxSamples <= 0)
        {
            return 0;
        }

        int consumed = 0;
        while (consumed < maxSamples && _nextIndex < _positionT.Length)
        {
            int index = _nextIndex;
            PositionAt(_positionT[index], out float x, out float z);

            bool ok;
            float height;
            TerrainSurfaceKind surface;
            try
            {
                ok = _probe(x, z, out height, out surface);
            }
            catch
            {
                // A throwing probe is an unsampled point, not a crashed
                // profiler — the gap shows up honestly in the profile.
                ok = false;
                height = float.NaN;
                surface = TerrainSurfaceKind.Unavailable;
            }

            _sampled[index] = ok;
            _heights[index] = ok ? height : float.NaN;
            _surfaces[index] = ok ? surface : TerrainSurfaceKind.Unavailable;
            _nextIndex++;
            consumed++;
        }

        LastAdvanceSampleCount = consumed;
        TotalSamplesConsumed += consumed;
        return consumed;
    }

    /// <summary>Stops all further probing permanently; a cancelled profiler
    /// never yields a profile (partial data is discarded, not published).</summary>
    public void Cancel()
    {
        _cancelled = true;
    }

    /// <summary>The finished profile, or null while incomplete or after
    /// cancellation.</summary>
    public RouteProfile? TryBuildProfile()
    {
        if (!IsComplete)
        {
            return null;
        }

        float sampledMeters = 0f;
        float unsampledMeters = 0f;
        float surfaceUnknownMeters = 0f;
        var surfaceMeters = new Dictionary<TerrainSurfaceKind, float>();
        var bandMeters = new float[RouteProfile.GradeBandCount];
        float worstUp = float.NaN;
        float worstDown = float.NaN;
        float maxAbs = float.NaN;
        var worst = new List<RouteProfileSegment>(WorstSegmentCount + 1);
        var gaps = new List<RouteProfileSegment>(WorstSegmentCount + 1);
        float gapStart = float.NaN;
        float gapLength = 0f;
        int sampledPositions = 0;

        for (int index = 0; index < _positionT.Length; index++)
        {
            if (_sampled[index])
            {
                sampledPositions++;
            }
        }

        for (int index = 1; index < _positionT.Length; index++)
        {
            float length = _positionT[index] - _positionT[index - 1];
            if (length <= 0f)
            {
                continue;
            }

            if (!_sampled[index - 1] || !_sampled[index])
            {
                unsampledMeters += length;
                // Consecutive unsampled segments are contiguous (ascending
                // walk), so extending the open span merges them into one.
                if (float.IsNaN(gapStart))
                {
                    gapStart = _positionT[index - 1];
                }

                gapLength += length;
                continue;
            }

            CloseGap(gaps, ref gapStart, ref gapLength);
            sampledMeters += length;
            float grade = (_heights[index] - _heights[index - 1]) / length * 100f;

            TerrainSurfaceKind kind = _surfaces[index - 1];
            if (kind == TerrainSurfaceKind.Unavailable)
            {
                surfaceUnknownMeters += length;
            }
            else
            {
                surfaceMeters.TryGetValue(kind, out float meters);
                surfaceMeters[kind] = meters + length;
            }

            float magnitude = Math.Abs(grade);
            bandMeters[BandIndex(magnitude)] += length;
            if (grade > 0f && (float.IsNaN(worstUp) || grade > worstUp))
            {
                worstUp = grade;
            }

            if (grade < 0f && (float.IsNaN(worstDown) || grade < worstDown))
            {
                worstDown = grade;
            }

            if (float.IsNaN(maxAbs) || magnitude > maxAbs)
            {
                maxAbs = magnitude;
            }

            InsertWorst(worst, new RouteProfileSegment(_positionT[index - 1], length, grade));
        }

        CloseGap(gaps, ref gapStart, ref gapLength);

        return new RouteProfile(
            TotalDistanceMeters,
            sampledMeters,
            unsampledMeters,
            surfaceMeters,
            surfaceUnknownMeters,
            bandMeters,
            worstUp,
            worstDown,
            maxAbs,
            worst,
            gaps,
            SampleSpacingMeters,
            _positionT.Length,
            sampledPositions);
    }

    private static int BandIndex(float absGradePercent)
    {
        float[] bounds = RouteProfile.GradeBandUpperBoundsPercent;
        for (int index = 0; index < bounds.Length; index++)
        {
            if (absGradePercent < bounds[index])
            {
                return index;
            }
        }

        return bounds.Length;
    }

    /// <summary>Closes the open unsampled span, keeping only the longest
    /// <see cref="WorstSegmentCount"/> spans (longest first). The span's
    /// grade is NaN — nothing was measured there.</summary>
    private static void CloseGap(List<RouteProfileSegment> gaps, ref float gapStart, ref float gapLength)
    {
        if (float.IsNaN(gapStart) || gapLength <= 0f)
        {
            gapStart = float.NaN;
            gapLength = 0f;
            return;
        }

        var span = new RouteProfileSegment(gapStart, gapLength, float.NaN);
        gapStart = float.NaN;
        gapLength = 0f;

        int insertAt = gaps.Count;
        for (int index = 0; index < gaps.Count; index++)
        {
            if (span.LengthMeters > gaps[index].LengthMeters)
            {
                insertAt = index;
                break;
            }
        }

        if (insertAt >= WorstSegmentCount)
        {
            return;
        }

        gaps.Insert(insertAt, span);
        if (gaps.Count > WorstSegmentCount)
        {
            gaps.RemoveAt(gaps.Count - 1);
        }
    }

    private static void InsertWorst(List<RouteProfileSegment> worst, RouteProfileSegment candidate)
    {
        float magnitude = Math.Abs(candidate.GradePercent);
        int insertAt = worst.Count;
        for (int index = 0; index < worst.Count; index++)
        {
            if (magnitude > Math.Abs(worst[index].GradePercent))
            {
                insertAt = index;
                break;
            }
        }

        if (insertAt >= WorstSegmentCount)
        {
            return;
        }

        worst.Insert(insertAt, candidate);
        if (worst.Count > WorstSegmentCount)
        {
            worst.RemoveAt(worst.Count - 1);
        }
    }

    /// <summary>XZ position at arc length t. Positions are visited in
    /// ascending t, so the vertex cursor makes the walk O(1) amortized.</summary>
    private void PositionAt(float t, out float x, out float z)
    {
        while (_segmentCursor < _vertexT.Length - 2 && _vertexT[_segmentCursor + 1] < t)
        {
            _segmentCursor++;
        }

        float segmentStart = _vertexT[_segmentCursor];
        float segmentEnd = _vertexT[_segmentCursor + 1];
        float span = segmentEnd - segmentStart;
        float fraction = span > 0f ? Math.Min(1f, Math.Max(0f, (t - segmentStart) / span)) : 0f;
        x = _vertexX[_segmentCursor] + ((_vertexX[_segmentCursor + 1] - _vertexX[_segmentCursor]) * fraction);
        z = _vertexZ[_segmentCursor] + ((_vertexZ[_segmentCursor + 1] - _vertexZ[_segmentCursor]) * fraction);
    }
}
