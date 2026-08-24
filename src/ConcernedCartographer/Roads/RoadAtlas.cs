using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Roads;

internal sealed class RoadAtlas
{
    private RoadStroke? _activeStroke;

    public RoadAtlas()
    {
    }

    public RoadAtlas(IEnumerable<RoadStroke> strokes)
    {
        Strokes.AddRange(strokes);
        IsDirty = false;
    }

    public List<RoadStroke> Strokes { get; } = new();
    public bool IsDirty { get; private set; }
    public int PointCount => Strokes.Sum(stroke => stroke.Points.Count);

    public bool RecordSample(
        RoadKind kind,
        Vector3 position,
        float minimumSpacingMeters,
        float maximumGapMeters,
        out RoadSegment segment)
    {
        segment = default;

        if (_activeStroke is null || _activeStroke.Kind != kind)
        {
            StartStroke(kind, position);
            return false;
        }

        Vector3 previous = _activeStroke.Points[_activeStroke.Points.Count - 1];
        float distance = HorizontalDistance(previous, position);

        if (distance < minimumSpacingMeters)
        {
            return false;
        }

        if (distance > maximumGapMeters)
        {
            StartStroke(kind, position);
            return false;
        }

        _activeStroke.Points.Add(position);
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

    private void StartStroke(RoadKind kind, Vector3 position)
    {
        _activeStroke = new RoadStroke(Guid.NewGuid(), kind);
        _activeStroke.Points.Add(position);
        Strokes.Add(_activeStroke);
        IsDirty = true;
    }

    private static float HorizontalDistance(Vector3 left, Vector3 right)
    {
        float dx = left.x - right.x;
        float dz = left.z - right.z;
        return Mathf.Sqrt((dx * dx) + (dz * dz));
    }
}
