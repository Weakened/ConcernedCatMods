using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedCartographer.Roads;

internal sealed class RoadAtlas
{
    // Grid cell for the duplicate-suppression index. Neighbor scan widens with
    // the configured radius, so the cell size only affects bucket granularity.
    private const float GridCellMeters = 4f;

    // The newest points of a source's active stroke never suppress that
    // source's next sample; otherwise ordinary forward walking would suppress
    // itself whenever the suppression radius exceeds the minimum point spacing.
    private const int ActiveTailExemptPoints = 3;

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
            Strokes.Add(stroke);
            for (int index = 0; index < stroke.Points.Count; index++)
            {
                IndexPoint(stroke, index);
            }
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
        IndexPoint(activeStroke, activeStroke.Points.Count - 1);
        IsDirty = true;
        segment = new RoadSegment(kind, previous, position);
        return true;
    }

    /// <summary>True when a point of this kind already exists within
    /// <paramref name="radiusMeters"/>, with no active-tail exemption. Used
    /// for exact-replay idempotency, which must hold even when configurable
    /// duplicate suppression is disabled.</summary>
    public bool ContainsPointNear(RoadKind kind, RoadPoint position, float radiusMeters)
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
                    if (entry.Stroke.Points[entry.PointIndex].HorizontalDistanceTo(position) <= radiusMeters)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
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

    private void StartStroke(RoadObservationSource source, RoadKind kind, RoadPoint position)
    {
        var stroke = new RoadStroke(Guid.NewGuid(), kind, source);
        stroke.Points.Add(position);
        _activeStrokes[source] = stroke;
        Strokes.Add(stroke);
        IndexPoint(stroke, 0);
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

        int cellRange = (int)Math.Ceiling(radiusMeters / GridCellMeters);
        int centerX = ToCell(position.X);
        int centerZ = ToCell(position.Z);
        int exemptFromIndex = activeStroke is null
            ? int.MaxValue
            : activeStroke.Points.Count - ActiveTailExemptPoints;

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
                    if (ReferenceEquals(entry.Stroke, activeStroke) && entry.PointIndex >= exemptFromIndex)
                    {
                        continue;
                    }

                    if (entry.Stroke.Points[entry.PointIndex].HorizontalDistanceTo(position) <= radiusMeters)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private void RebuildIndex()
    {
        // Grid entries carry (stroke, pointIndex) pairs, so any structural
        // edit invalidates them wholesale. Reconciliation ops are as rare as
        // hoe swings; a full rebuild is simpler than incremental repair and
        // costs well under a millisecond at 10k points.
        _grid.Clear();
        foreach (RoadStroke stroke in Strokes)
        {
            for (int index = 0; index < stroke.Points.Count; index++)
            {
                IndexPoint(stroke, index);
            }
        }
    }

    private void IndexPoint(RoadStroke stroke, int pointIndex)
    {
        RoadPoint point = stroke.Points[pointIndex];
        long key = CellKey(stroke.Kind, ToCell(point.X), ToCell(point.Z));
        if (!_grid.TryGetValue(key, out List<GridEntry>? entries))
        {
            entries = new List<GridEntry>();
            _grid.Add(key, entries);
        }

        entries.Add(new GridEntry(stroke, pointIndex));
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
        public GridEntry(RoadStroke stroke, int pointIndex)
        {
            Stroke = stroke;
            PointIndex = pointIndex;
        }

        public RoadStroke Stroke { get; }
        public int PointIndex { get; }
    }
}
