using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Trips;

namespace TheConcernedCat.ConcernedTeamster.Domain.RoadQuality;

/// <summary>The per-world segment score table (CT-017). AddTrip updates
/// only the segments the trip touches (cost is O(trip samples) dictionary
/// work — no full recomputation ever), and because every stat is additive
/// the fold order cannot change the result. Segments aggregate ALL
/// recorded history: pruning old raw trips from the sidecar deliberately
/// does not subtract their contribution (documented in the format notes).</summary>
public sealed class RoadQualityIndex
{
    private readonly Dictionary<RoadSegmentKey, RoadSegmentStats> _segments = new();

    public IReadOnlyDictionary<RoadSegmentKey, RoadSegmentStats> Segments => _segments;

    /// <summary>Segments touched by the most recent AddTrip — the measured
    /// cost bound (always ≤ that trip's sample count).</summary>
    public int LastAddTouchedSegments { get; private set; }

    public void AddTrip(Trip trip)
    {
        var touched = new HashSet<RoadSegmentKey>();
        float? previousGrade = null;
        RoadSegmentKey previousKey = default;
        bool hasPrevious = false;

        foreach (TripSample sample in trip.Samples)
        {
            RoadSegmentKey key = RoadSegmentKey.FromPosition(sample.PositionX, sample.PositionZ);
            if (!_segments.TryGetValue(key, out RoadSegmentStats? stats))
            {
                stats = new RoadSegmentStats();
                _segments[key] = stats;
            }

            // The grade-delta pair only counts when the previous sample sat
            // in the SAME segment — deltas across cell borders belong to
            // neither cell.
            float? pairedPrevious = hasPrevious && previousKey.Equals(key) ? previousGrade : null;
            stats.AddSample(sample.GradePercent, sample.SpeedMetersPerSecond, pairedPrevious);
            touched.Add(key);

            previousGrade = sample.GradePercent;
            previousKey = key;
            hasPrevious = true;
        }

        LastAddTouchedSegments = touched.Count;
    }

    public void RestoreSegment(RoadSegmentKey key, RoadSegmentStats stats)
    {
        _segments[key] = stats;
    }

    public void Clear()
    {
        _segments.Clear();
        LastAddTouchedSegments = 0;
    }

    /// <summary>Batch construction — used for v1→v2 migration and by the
    /// incremental-equals-batch proof.</summary>
    public static RoadQualityIndex ComputeFromTrips(IReadOnlyList<Trip> trips)
    {
        var index = new RoadQualityIndex();
        foreach (Trip trip in trips)
        {
            index.AddTrip(trip);
        }

        return index;
    }
}
