using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedCartographer.Roads;

internal sealed class RoadAtlas
{
    // Grid cell for the duplicate-suppression index. Neighbor scan widens with
    // the configured radius, so the cell size only affects bucket granularity.
    private const float GridCellMeters = 4f;

    // The newest points of the active stroke never suppress the next sample;
    // otherwise ordinary forward walking would suppress itself whenever the
    // suppression radius exceeds the minimum point spacing.
    private const int ActiveTailExemptPoints = 3;

    private readonly Dictionary<long, List<GridEntry>> _grid = new();
    private RoadStroke? _activeStroke;

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
        RoadKind kind,
        RoadPoint position,
        RoadSamplingRules rules,
        out RoadSegment segment)
    {
        segment = default;

        if (IsSuppressedDuplicate(kind, position, rules.DuplicateSuppressionMeters))
        {
            // The player is on already-recorded ground. Never re-ink it, and end
            // the active stroke so a later uncovered stretch starts fresh with
            // no connector across the covered section.
            _activeStroke = null;
            return false;
        }

        if (_activeStroke is null || _activeStroke.Kind != kind)
        {
            StartStroke(kind, position);
            return false;
        }

        RoadPoint previous = _activeStroke.Points[_activeStroke.Points.Count - 1];
        float distance = previous.HorizontalDistanceTo(position);

        if (distance < rules.MinimumSpacingMeters)
        {
            return false;
        }

        if (distance > rules.MaximumGapMeters)
        {
            StartStroke(kind, position);
            return false;
        }

        _activeStroke.Points.Add(position);
        IndexPoint(_activeStroke, _activeStroke.Points.Count - 1);
        IsDirty = true;
        segment = new RoadSegment(kind, previous, position);
        return true;
    }

    public void EndStroke()
    {
        _activeStroke = null;
    }

    public void MarkClean()
    {
        IsDirty = false;
    }

    private void StartStroke(RoadKind kind, RoadPoint position)
    {
        _activeStroke = new RoadStroke(Guid.NewGuid(), kind);
        _activeStroke.Points.Add(position);
        Strokes.Add(_activeStroke);
        IndexPoint(_activeStroke, 0);
        IsDirty = true;
    }

    private bool IsSuppressedDuplicate(RoadKind kind, RoadPoint position, float radiusMeters)
    {
        if (radiusMeters <= 0f)
        {
            return false;
        }

        int cellRange = (int)Math.Ceiling(radiusMeters / GridCellMeters);
        int centerX = ToCell(position.X);
        int centerZ = ToCell(position.Z);
        int exemptFromIndex = _activeStroke is null
            ? int.MaxValue
            : _activeStroke.Points.Count - ActiveTailExemptPoints;

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
                    if (ReferenceEquals(entry.Stroke, _activeStroke) && entry.PointIndex >= exemptFromIndex)
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
