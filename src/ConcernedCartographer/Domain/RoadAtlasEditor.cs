using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>Pure road-correction operations over an atlas, each with local
/// undo. Selection is proximity-based: every operation targets the stroke
/// nearest a given position. Operations only ever touch the atlas — never
/// terrain or world data — and funnel through
/// <see cref="RoadAtlas.EditStrokes"/> so the suppression index stays
/// consistent.</summary>
internal sealed class RoadAtlasEditor
{
    public const float DefaultSelectRadiusMeters = 10f;
    private const int UndoDepth = 20;

    private readonly RoadAtlas _atlas;
    private readonly List<UndoRecord> _undo = new();

    public RoadAtlasEditor(RoadAtlas atlas)
    {
        _atlas = atlas;
    }

    public int UndoCount => _undo.Count;

    public bool DeleteNearest(RoadPoint position, float maxRadiusMeters, out string summary)
    {
        if (!_atlas.TryFindNearestStroke(position, maxRadiusMeters, includeHidden: true, out RoadStroke? stroke, out float distance))
        {
            summary = NothingNearby(maxRadiusMeters);
            return false;
        }

        PushUndo("delete", new[] { stroke! }, Array.Empty<RoadStroke>());
        _atlas.EditStrokes(strokes => strokes.Remove(stroke!));
        summary = $"Deleted {Describe(stroke!)} ({distance:0.#} m away). 'cc_roads undo' restores it.";
        return true;
    }

    public bool ReclassifyNearest(RoadPoint position, float maxRadiusMeters, out string summary)
    {
        if (!_atlas.TryFindNearestStroke(position, maxRadiusMeters, includeHidden: true, out RoadStroke? stroke, out _))
        {
            summary = NothingNearby(maxRadiusMeters);
            return false;
        }

        RoadKind newKind = stroke!.Kind == RoadKind.Dirt ? RoadKind.Paved : RoadKind.Dirt;
        var replacement = new RoadStroke(stroke.Id, newKind, stroke.Source) { Hidden = stroke.Hidden };
        replacement.Points.AddRange(stroke.Points);

        PushUndo("reclassify", new[] { stroke }, new[] { replacement });
        _atlas.EditStrokes(strokes =>
        {
            strokes.Remove(stroke);
            strokes.Add(replacement);
        });
        summary = $"Reclassified {Describe(stroke)} to {newKind}.";
        return true;
    }

    public bool SetHiddenNearest(RoadPoint position, float maxRadiusMeters, bool hidden, out string summary)
    {
        // Hiding targets the nearest visible stroke; unhiding the nearest
        // hidden one.
        RoadStroke? stroke = FindNearestWithHidden(position, maxRadiusMeters, hidden: !hidden);
        if (stroke is null)
        {
            summary = hidden
                ? NothingNearby(maxRadiusMeters)
                : $"No hidden road within {maxRadiusMeters:0.#} m.";
            return false;
        }

        var replacement = new RoadStroke(stroke.Id, stroke.Kind, stroke.Source) { Hidden = hidden };
        replacement.Points.AddRange(stroke.Points);
        PushUndo(hidden ? "hide" : "unhide", new[] { stroke }, new[] { replacement });
        _atlas.EditStrokes(strokes =>
        {
            strokes.Remove(stroke);
            strokes.Add(replacement);
        });
        summary = $"{(hidden ? "Hid" : "Unhid")} {Describe(replacement)}.";
        return true;
    }

    public bool SplitNearest(RoadPoint position, float maxRadiusMeters, out string summary)
    {
        if (!_atlas.TryFindNearestStroke(position, maxRadiusMeters, includeHidden: true, out RoadStroke? stroke, out _))
        {
            summary = NothingNearby(maxRadiusMeters);
            return false;
        }

        if (stroke!.Points.Count < 3)
        {
            summary = "That road is too short to split.";
            return false;
        }

        int nearestIndex = -1;
        float nearestDistance = float.MaxValue;
        for (int index = 1; index < stroke.Points.Count - 1; index++)
        {
            float distance = stroke.Points[index].HorizontalDistanceTo(position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = index;
            }
        }

        var head = new RoadStroke(stroke.Id, stroke.Kind, stroke.Source) { Hidden = stroke.Hidden };
        head.Points.AddRange(stroke.Points.GetRange(0, nearestIndex + 1));
        var tail = new RoadStroke(Guid.NewGuid(), stroke.Kind, stroke.Source) { Hidden = stroke.Hidden };
        tail.Points.AddRange(stroke.Points.GetRange(nearestIndex, stroke.Points.Count - nearestIndex));

        PushUndo("split", new[] { stroke }, new[] { head, tail });
        _atlas.EditStrokes(strokes =>
        {
            strokes.Remove(stroke);
            strokes.Add(head);
            strokes.Add(tail);
        });
        summary = $"Split {Describe(stroke)} into {head.Points.Count} + {tail.Points.Count} points at the shared point.";
        return true;
    }

    public bool JoinNearest(RoadPoint position, float maxRadiusMeters, out string summary)
    {
        RoadStroke? first = null;
        RoadStroke? second = null;
        float bestPairDistance = float.MaxValue;

        List<RoadStroke> strokes = _atlas.Strokes;
        for (int i = 0; i < strokes.Count; i++)
        {
            foreach (RoadPoint endpointA in Endpoints(strokes[i]))
            {
                if (endpointA.HorizontalDistanceTo(position) > maxRadiusMeters)
                {
                    continue;
                }

                for (int j = i + 1; j < strokes.Count; j++)
                {
                    if (strokes[j].Kind != strokes[i].Kind || strokes[j].Source != strokes[i].Source)
                    {
                        continue;
                    }

                    foreach (RoadPoint endpointB in Endpoints(strokes[j]))
                    {
                        float pairDistance = endpointA.HorizontalDistanceTo(endpointB);
                        if (endpointB.HorizontalDistanceTo(position) <= maxRadiusMeters && pairDistance < bestPairDistance)
                        {
                            bestPairDistance = pairDistance;
                            first = strokes[i];
                            second = strokes[j];
                        }
                    }
                }
            }
        }

        if (first is null || second is null)
        {
            summary = $"No two same-kind road ends within {maxRadiusMeters:0.#} m of you.";
            return false;
        }

        var joined = new RoadStroke(first.Id, first.Kind, first.Source) { Hidden = first.Hidden };
        joined.Points.AddRange(first.Points);
        AppendNearestEndFirst(joined.Points, second.Points);

        PushUndo("join", new[] { first, second }, new[] { joined });
        RoadStroke firstCaptured = first;
        RoadStroke secondCaptured = second;
        _atlas.EditStrokes(list =>
        {
            list.Remove(firstCaptured);
            list.Remove(secondCaptured);
            list.Add(joined);
        });
        summary = $"Joined two {joined.Kind} roads into one ({joined.Points.Count} points, ends were {bestPairDistance:0.#} m apart).";
        return true;
    }

    public bool Undo(out string summary)
    {
        if (_undo.Count == 0)
        {
            summary = "Nothing to undo.";
            return false;
        }

        UndoRecord record = _undo[_undo.Count - 1];
        _undo.RemoveAt(_undo.Count - 1);

        _atlas.EditStrokes(strokes =>
        {
            foreach (RoadStroke created in record.After)
            {
                strokes.Remove(created);
            }

            foreach (RoadStroke restored in record.Before)
            {
                strokes.Add(restored);
            }
        });

        summary = $"Undid '{record.Operation}'; {record.Before.Count} road(s) restored.";
        return true;
    }

    public string DescribeNearest(RoadPoint position, float maxRadiusMeters)
    {
        if (!_atlas.TryFindNearestStroke(position, maxRadiusMeters, includeHidden: true, out RoadStroke? stroke, out float distance))
        {
            return NothingNearby(maxRadiusMeters);
        }

        return $"Nearest: {Describe(stroke!)}, {distance.ToString("0.#", CultureInfo.InvariantCulture)} m away.";
    }

    private RoadStroke? FindNearestWithHidden(RoadPoint position, float maxRadiusMeters, bool hidden)
    {
        RoadStroke? best = null;
        float bestDistance = maxRadiusMeters;
        foreach (RoadStroke stroke in _atlas.Strokes)
        {
            if (stroke.Hidden != hidden)
            {
                continue;
            }

            foreach (RoadPoint point in stroke.Points)
            {
                float distance = point.HorizontalDistanceTo(position);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = stroke;
                }
            }
        }

        return best;
    }

    private static IEnumerable<RoadPoint> Endpoints(RoadStroke stroke)
    {
        yield return stroke.Points[0];
        if (stroke.Points.Count > 1)
        {
            yield return stroke.Points[stroke.Points.Count - 1];
        }
    }

    private static void AppendNearestEndFirst(List<RoadPoint> into, List<RoadPoint> run)
    {
        RoadPoint junction = into[into.Count - 1];
        bool reverse = run[run.Count - 1].HorizontalDistanceTo(junction) < run[0].HorizontalDistanceTo(junction);
        int count = run.Count;
        for (int step = 0; step < count; step++)
        {
            into.Add(run[reverse ? count - 1 - step : step]);
        }
    }

    private void PushUndo(string operation, IReadOnlyList<RoadStroke> before, IReadOnlyList<RoadStroke> after)
    {
        var snapshots = new List<RoadStroke>(before.Count);
        foreach (RoadStroke stroke in before)
        {
            var clone = new RoadStroke(stroke.Id, stroke.Kind, stroke.Source) { Hidden = stroke.Hidden };
            clone.Points.AddRange(stroke.Points);
            snapshots.Add(clone);
        }

        _undo.Add(new UndoRecord(operation, snapshots, new List<RoadStroke>(after)));
        if (_undo.Count > UndoDepth)
        {
            _undo.RemoveAt(0);
        }
    }

    private static string Describe(RoadStroke stroke)
    {
        string hidden = stroke.Hidden ? ", hidden" : "";
        return $"a {stroke.Kind} road ({stroke.Points.Count} points, recorded by {stroke.Source}{hidden})";
    }

    private static string NothingNearby(float maxRadiusMeters)
    {
        return $"No recorded road within {maxRadiusMeters.ToString("0.#", CultureInfo.InvariantCulture)} m of you.";
    }

    private sealed class UndoRecord
    {
        public UndoRecord(string operation, List<RoadStroke> before, List<RoadStroke> after)
        {
            Operation = operation;
            Before = before;
            After = after;
        }

        public string Operation { get; }
        public List<RoadStroke> Before { get; }
        public List<RoadStroke> After { get; }
    }
}
