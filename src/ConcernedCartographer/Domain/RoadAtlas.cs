using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedCartographer.Roads;

internal sealed class RoadAtlas
{
    // Grid cell for the segment index. Neighbor scan widens with the query
    // radius, so the cell size only affects bucket granularity.
    private const float GridCellMeters = 4f;

    // Segments touching the newest points of a source's active stroke never
    // suppress that source's next sample; otherwise ordinary forward walking
    // would suppress itself whenever the suppression radius exceeds the
    // minimum point spacing.
    private const int ActiveTailExemptPoints = 3;

    /// <summary>Maintenance simplification tolerance: the polyline may move
    /// at most this far from the original geometry. Well under both the map
    /// texel (~11.6 m) and the default suppression radius (2 m).</summary>
    public const float SimplifyToleranceMeters = 1.0f;

    /// <summary>Maintenance merge tolerance: same-kind, same-source strokes
    /// whose endpoints sit this close are joined into one polyline. Below
    /// every stroke-breaking distance (the 8 m default gap, teleports), so
    /// merging can only heal fragmentation, never bridge real gaps.</summary>
    public const float JoinToleranceMeters = 2.5f;

    private readonly Dictionary<long, List<GridEntry>> _grid = new();

    // Each observation source builds its own stroke, so interleaved
    // observations (walking while paving elsewhere) stay coherent polylines
    // instead of breaking each other on every sample.
    private readonly Dictionary<RoadObservationSource, RoadStroke> _activeStrokes = new();

    public RoadAtlas()
    {
    }

    public RoadAtlas(IEnumerable<RoadStroke> strokes)
    {
        foreach (RoadStroke stroke in strokes)
        {
            if (stroke.Points.Count == 0)
            {
                continue;
            }

            Strokes.Add(stroke);
            IndexStroke(stroke);
        }

        IsDirty = false;
    }

    public List<RoadStroke> Strokes { get; } = new();
    public bool IsDirty { get; private set; }

    public int PointCount
    {
        get
        {
            int total = 0;
            foreach (RoadStroke stroke in Strokes)
            {
                total += stroke.Points.Count;
            }

            return total;
        }
    }

    public bool RecordSample(
        RoadObservationSource source,
        RoadKind kind,
        RoadPoint position,
        RoadSamplingRules rules,
        out RoadSegment segment)
    {
        segment = default;
        _activeStrokes.TryGetValue(source, out RoadStroke? activeStroke);

        if (IsSuppressedDuplicate(activeStroke, kind, position, rules.DuplicateSuppressionMeters))
        {
            // The observation is on already-recorded ground. Never re-ink it,
            // and end this source's active stroke so a later uncovered stretch
            // starts fresh with no connector across the covered section.
            _activeStrokes.Remove(source);
            return false;
        }

        if (activeStroke is null || activeStroke.Kind != kind)
        {
            StartStroke(source, kind, position);
            return false;
        }

        RoadPoint previous = activeStroke.Points[activeStroke.Points.Count - 1];
        float distance = previous.HorizontalDistanceTo(position);

        if (distance < rules.MinimumSpacingMeters)
        {
            return false;
        }

        if (distance > rules.MaximumGapMeters)
        {
            StartStroke(source, kind, position);
            return false;
        }

        activeStroke.Points.Add(position);
        IndexEntry(activeStroke, activeStroke.Points.Count - 2);
        IsDirty = true;
        segment = new RoadSegment(kind, previous, position);
        return true;
    }

    /// <summary>True when recorded geometry of this kind passes within
    /// <paramref name="radiusMeters"/> of the position, with no active-tail
    /// exemption. Used for exact-replay idempotency, which must hold even
    /// when configurable duplicate suppression is disabled.</summary>
    public bool ContainsPointNear(RoadKind kind, RoadPoint position, float radiusMeters)
    {
        return QueryGeometry(kind, position, radiusMeters, activeStroke: null, exemptFromSegment: int.MaxValue);
    }

    /// <summary>Removes every point of the given kind within
    /// <paramref name="radiusMeters"/> of <paramref name="center"/>, for
    /// reconciliation when terrain is repainted, cultivated, or reset. A
    /// stroke whose interior is removed splits into separate strokes (the
    /// first surviving run keeps the original identity); points outside the
    /// radius — including other-kind ink and unrelated nearby roads — are
    /// never touched. Returns the number of removed points.</summary>
    public int RemoveCoverage(RoadKind kind, RoadPoint center, float radiusMeters)
    {
        if (radiusMeters <= 0f)
        {
            return 0;
        }

        int removedPoints = 0;
        bool structureChanged = false;
        var rebuilt = new List<RoadStroke>(Strokes.Count);
        var replacedOriginals = new List<RoadStroke>();

        foreach (RoadStroke stroke in Strokes)
        {
            if (stroke.Kind != kind)
            {
                rebuilt.Add(stroke);
                continue;
            }

            var survivingRuns = new List<List<RoadPoint>>();
            List<RoadPoint>? currentRun = null;
            int removedFromStroke = 0;

            foreach (RoadPoint point in stroke.Points)
            {
                if (point.HorizontalDistanceTo(center) <= radiusMeters)
                {
                    removedFromStroke++;
                    currentRun = null;
                    continue;
                }

                if (currentRun is null)
                {
                    currentRun = new List<RoadPoint>();
                    survivingRuns.Add(currentRun);
                }

                currentRun.Add(point);
            }

            if (removedFromStroke == 0)
            {
                rebuilt.Add(stroke);
                continue;
            }

            removedPoints += removedFromStroke;
            structureChanged = true;
            replacedOriginals.Add(stroke);

            bool first = true;
            foreach (List<RoadPoint> run in survivingRuns)
            {
                var replacement = new RoadStroke(first ? stroke.Id : Guid.NewGuid(), stroke.Kind, stroke.Source);
                replacement.Points.AddRange(run);
                rebuilt.Add(replacement);
                first = false;
            }
        }

        if (!structureChanged)
        {
            return 0;
        }

        // Any source actively extending a replaced stroke must start fresh;
        // its stroke object no longer belongs to the atlas.
        var staleSources = new List<RoadObservationSource>();
        foreach (KeyValuePair<RoadObservationSource, RoadStroke> active in _activeStrokes)
        {
            if (replacedOriginals.Contains(active.Value))
            {
                staleSources.Add(active.Key);
            }
        }

        foreach (RoadObservationSource source in staleSources)
        {
            _activeStrokes.Remove(source);
        }

        Strokes.Clear();
        Strokes.AddRange(rebuilt);
        RebuildIndex();
        IsDirty = true;
        return removedPoints;
    }

    /// <summary>Compacts the atlas: joins same-kind, same-source strokes
    /// whose endpoints sit within <see cref="JoinToleranceMeters"/> (healing
    /// fragmentation from suppression breaks and scan order, never bridging
    /// real gaps), then Douglas-Peucker-simplifies every polyline within
    /// <see cref="SimplifyToleranceMeters"/>. Ends all active strokes first;
    /// intended for load time, before new observations arrive. Suppression
    /// is segment-based, so thinned straight stretches keep suppressing
    /// re-walks of their whole length.</summary>
    public MaintenanceResult PerformMaintenance()
    {
        EndAllStrokes();

        int mergedStrokes = MergeAdjacentStrokes();

        int removedPoints = 0;
        foreach (RoadStroke stroke in Strokes)
        {
            if (stroke.Points.Count < 3)
            {
                continue;
            }

            List<RoadPoint> simplified = RoadGeometry.Simplify(stroke.Points, SimplifyToleranceMeters);
            if (simplified.Count < stroke.Points.Count)
            {
                removedPoints += stroke.Points.Count - simplified.Count;
                stroke.Points.Clear();
                stroke.Points.AddRange(simplified);
            }
        }

        if (mergedStrokes > 0 || removedPoints > 0)
        {
            RebuildIndex();
            IsDirty = true;
        }

        return new MaintenanceResult(mergedStrokes, removedPoints);
    }

    /// <summary>Structural edit hatch for the repair tools: ends all active
    /// strokes, hands the stroke list to the edit, then restores the atlas
    /// invariants (no empty strokes, fresh segment index, dirty flag). All
    /// tool operations funnel through here so they cannot desynchronize the
    /// suppression index.</summary>
    public void EditStrokes(Action<List<RoadStroke>> edit)
    {
        EndAllStrokes();
        edit(Strokes);
        Strokes.RemoveAll(stroke => stroke.Points.Count == 0);
        RebuildIndex();
        IsDirty = true;
    }

    /// <summary>Finds the stroke whose polyline passes nearest to the
    /// position within <paramref name="maxRadiusMeters"/>. Linear over the
    /// atlas; tool invocations are rare and post-maintenance atlases are
    /// small.</summary>
    public bool TryFindNearestStroke(
        RoadPoint position,
        float maxRadiusMeters,
        bool includeHidden,
        out RoadStroke? stroke,
        out float distanceMeters)
    {
        stroke = null;
        distanceMeters = float.MaxValue;

        foreach (RoadStroke candidate in Strokes)
        {
            if (candidate.Hidden && !includeHidden)
            {
                continue;
            }

            List<RoadPoint> points = candidate.Points;
            for (int index = 0; index < Math.Max(1, points.Count - 1); index++)
            {
                RoadPoint start = points[index];
                RoadPoint end = index + 1 < points.Count ? points[index + 1] : start;
                float distance = RoadGeometry.HorizontalDistanceToSegment(position, start, end);
                if (distance < distanceMeters)
                {
                    distanceMeters = distance;
                    stroke = candidate;
                }
            }
        }

        return stroke is not null && distanceMeters <= maxRadiusMeters;
    }

    public void EndStroke(RoadObservationSource source)
    {
        _activeStrokes.Remove(source);
    }

    public void EndAllStrokes()
    {
        _activeStrokes.Clear();
    }

    public void MarkClean()
    {
        IsDirty = false;
    }

    public readonly struct MaintenanceResult
    {
        public MaintenanceResult(int mergedStrokes, int removedPoints)
        {
            MergedStrokes = mergedStrokes;
            RemovedPoints = removedPoints;
        }

        public int MergedStrokes { get; }
        public int RemovedPoints { get; }
    }

    private int MergeAdjacentStrokes()
    {
        // O(strokes²) endpoint comparison; runs only at load time, where a
        // few hundred strokes make this trivial. The per-sample hot path
        // stays on the grid.
        int merged = 0;
        bool joinedSomething = true;
        while (joinedSomething)
        {
            joinedSomething = false;
            for (int i = 0; i < Strokes.Count && !joinedSomething; i++)
            {
                RoadStroke into = Strokes[i];
                for (int j = 0; j < Strokes.Count; j++)
                {
                    if (j == i)
                    {
                        continue;
                    }

                    RoadStroke other = Strokes[j];
                    if (other.Kind != into.Kind || other.Source != into.Source)
                    {
                        continue;
                    }

                    if (TryJoin(into, other))
                    {
                        Strokes.RemoveAt(j);
                        merged++;
                        joinedSomething = true;
                        break;
                    }
                }
            }
        }

        return merged;
    }

    private static bool TryJoin(RoadStroke into, RoadStroke other)
    {
        List<RoadPoint> a = into.Points;
        List<RoadPoint> b = other.Points;
        RoadPoint aStart = a[0];
        RoadPoint aEnd = a[a.Count - 1];
        RoadPoint bStart = b[0];
        RoadPoint bEnd = b[b.Count - 1];

        if (aEnd.HorizontalDistanceTo(bStart) <= JoinToleranceMeters)
        {
            AppendRun(a, b, reverse: false);
            return true;
        }

        if (aEnd.HorizontalDistanceTo(bEnd) <= JoinToleranceMeters)
        {
            AppendRun(a, b, reverse: true);
            return true;
        }

        if (aStart.HorizontalDistanceTo(bEnd) <= JoinToleranceMeters)
        {
            PrependRun(a, b, reverse: false);
            return true;
        }

        if (aStart.HorizontalDistanceTo(bStart) <= JoinToleranceMeters)
        {
            PrependRun(a, b, reverse: true);
            return true;
        }

        return false;
    }

    private static void AppendRun(List<RoadPoint> into, List<RoadPoint> run, bool reverse)
    {
        RoadPoint junction = into[into.Count - 1];
        int count = run.Count;
        for (int step = 0; step < count; step++)
        {
            RoadPoint point = run[reverse ? count - 1 - step : step];
            if (step == 0 && point.HorizontalDistanceTo(junction) <= float.Epsilon)
            {
                continue;
            }

            into.Add(point);
        }
    }

    private static void PrependRun(List<RoadPoint> into, List<RoadPoint> run, bool reverse)
    {
        RoadPoint junction = into[0];
        var prefix = new List<RoadPoint>(run.Count);
        int count = run.Count;
        for (int step = 0; step < count; step++)
        {
            RoadPoint point = run[reverse ? count - 1 - step : step];
            prefix.Add(point);
        }

        if (prefix.Count > 0 && prefix[prefix.Count - 1].HorizontalDistanceTo(junction) <= float.Epsilon)
        {
            prefix.RemoveAt(prefix.Count - 1);
        }

        into.InsertRange(0, prefix);
    }

    private void StartStroke(RoadObservationSource source, RoadKind kind, RoadPoint position)
    {
        var stroke = new RoadStroke(Guid.NewGuid(), kind, source);
        stroke.Points.Add(position);
        _activeStrokes[source] = stroke;
        Strokes.Add(stroke);
        IndexEntry(stroke, 0);
        IsDirty = true;
    }

    private bool IsSuppressedDuplicate(
        RoadStroke? activeStroke,
        RoadKind kind,
        RoadPoint position,
        float radiusMeters)
    {
        if (radiusMeters <= 0f)
        {
            return false;
        }

        int exemptFromSegment = activeStroke is null
            ? int.MaxValue
            : activeStroke.Points.Count - 1 - ActiveTailExemptPoints;
        return QueryGeometry(kind, position, radiusMeters, activeStroke, exemptFromSegment);
    }

    private bool QueryGeometry(
        RoadKind kind,
        RoadPoint position,
        float radiusMeters,
        RoadStroke? activeStroke,
        int exemptFromSegment)
    {
        int cellRange = (int)Math.Ceiling(radiusMeters / GridCellMeters);
        int centerX = ToCell(position.X);
        int centerZ = ToCell(position.Z);

        for (int cellX = centerX - cellRange; cellX <= centerX + cellRange; cellX++)
        {
            for (int cellZ = centerZ - cellRange; cellZ <= centerZ + cellRange; cellZ++)
            {
                if (!_grid.TryGetValue(CellKey(kind, cellX, cellZ), out List<GridEntry>? entries))
                {
                    continue;
                }

                foreach (GridEntry entry in entries)
                {
                    if (ReferenceEquals(entry.Stroke, activeStroke) && entry.SegmentIndex >= exemptFromSegment)
                    {
                        continue;
                    }

                    if (EntryDistance(entry, position) <= radiusMeters)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static float EntryDistance(GridEntry entry, RoadPoint position)
    {
        List<RoadPoint> points = entry.Stroke.Points;
        RoadPoint start = points[entry.SegmentIndex];
        RoadPoint end = entry.SegmentIndex + 1 < points.Count ? points[entry.SegmentIndex + 1] : start;
        return RoadGeometry.HorizontalDistanceToSegment(position, start, end);
    }

    private void RebuildIndex()
    {
        // Grid entries carry (stroke, segmentIndex) pairs, so any structural
        // edit invalidates them wholesale. Structural edits are as rare as
        // hoe swings and world loads; a full rebuild is simpler than
        // incremental repair and costs well under a millisecond at 10k
        // points.
        _grid.Clear();
        foreach (RoadStroke stroke in Strokes)
        {
            IndexStroke(stroke);
        }
    }

    private void IndexStroke(RoadStroke stroke)
    {
        if (stroke.Points.Count == 1)
        {
            IndexEntry(stroke, 0);
            return;
        }

        for (int segmentIndex = 0; segmentIndex < stroke.Points.Count - 1; segmentIndex++)
        {
            IndexEntry(stroke, segmentIndex);
        }
    }

    /// <summary>Indexes one segment (or a lone point) into every grid cell
    /// its bounding box overlaps. Called with index 0 for a new stroke's
    /// first point; when the second point arrives the same entry index is
    /// re-indexed as a real segment, leaving a harmless duplicate in the
    /// first point's cell.</summary>
    private void IndexEntry(RoadStroke stroke, int segmentIndex)
    {
        List<RoadPoint> points = stroke.Points;
        RoadPoint start = points[segmentIndex];
        RoadPoint end = segmentIndex + 1 < points.Count ? points[segmentIndex + 1] : start;

        int minCellX = ToCell(Math.Min(start.X, end.X));
        int maxCellX = ToCell(Math.Max(start.X, end.X));
        int minCellZ = ToCell(Math.Min(start.Z, end.Z));
        int maxCellZ = ToCell(Math.Max(start.Z, end.Z));

        for (int cellX = minCellX; cellX <= maxCellX; cellX++)
        {
            for (int cellZ = minCellZ; cellZ <= maxCellZ; cellZ++)
            {
                long key = CellKey(stroke.Kind, cellX, cellZ);
                if (!_grid.TryGetValue(key, out List<GridEntry>? entries))
                {
                    entries = new List<GridEntry>();
                    _grid.Add(key, entries);
                }

                entries.Add(new GridEntry(stroke, segmentIndex));
            }
        }
    }

    private static int ToCell(float coordinate)
    {
        return (int)Math.Floor(coordinate / GridCellMeters);
    }

    private static long CellKey(RoadKind kind, int cellX, int cellZ)
    {
        // Cells span roughly ±3 million with 4 m cells across any sane world
        // size, so 21 bits per axis plus the kind stays collision-free.
        return ((long)kind << 62) ^ (((long)(uint)cellX) << 31) ^ (long)(uint)cellZ;
    }

    private readonly struct GridEntry
    {
        public GridEntry(RoadStroke stroke, int segmentIndex)
        {
            Stroke = stroke;
            SegmentIndex = segmentIndex;
        }

        public RoadStroke Stroke { get; }
        public int SegmentIndex { get; }
    }
}
