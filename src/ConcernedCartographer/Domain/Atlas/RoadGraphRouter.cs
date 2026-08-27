using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Road-aware routing: builds a graph from the road atlas (stroke
/// points as nodes; consecutive-point edges plus junction links between
/// nearby points of different strokes) and runs bounded A* between the
/// nearest road entry/exit to the requested endpoints. Returns null when
/// either endpoint cannot snap or no path exists within the node budget —
/// callers fall back to a straight segment.</summary>
internal static class RoadGraphRouter
{
    private const float JunctionLinkMeters = 8f;

    public static List<RoadPoint>? FindPath(
        RoadAtlas roads,
        RoadPoint start,
        RoadPoint goal,
        float snapRadiusMeters,
        int maxExpandedNodes = 20000)
    {
        var nodes = new List<RoadPoint>();
        var edges = new List<List<int>>();
        foreach (RoadStroke stroke in roads.Strokes)
        {
            int first = nodes.Count;
            for (int index = 0; index < stroke.Points.Count; index++)
            {
                nodes.Add(stroke.Points[index]);
                edges.Add(new List<int>());
                if (index > 0)
                {
                    edges[first + index].Add(first + index - 1);
                    edges[first + index - 1].Add(first + index);
                }
            }
        }

        if (nodes.Count == 0)
        {
            return null;
        }

        // Junction links via a coarse grid.
        var cells = new Dictionary<long, List<int>>();
        for (int index = 0; index < nodes.Count; index++)
        {
            long key = CellKey(nodes[index]);
            if (!cells.TryGetValue(key, out List<int>? bucket))
            {
                bucket = new List<int>();
                cells.Add(key, bucket);
            }

            bucket.Add(index);
        }

        for (int index = 0; index < nodes.Count; index++)
        {
            int cellX = (int)Math.Floor(nodes[index].X / JunctionLinkMeters);
            int cellZ = (int)Math.Floor(nodes[index].Z / JunctionLinkMeters);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!cells.TryGetValue(Combine(cellX + dx, cellZ + dz), out List<int>? bucket))
                    {
                        continue;
                    }

                    foreach (int other in bucket)
                    {
                        if (other > index &&
                            nodes[index].HorizontalDistanceTo(nodes[other]) <= JunctionLinkMeters &&
                            !edges[index].Contains(other))
                        {
                            edges[index].Add(other);
                            edges[other].Add(index);
                        }
                    }
                }
            }
        }

        int entry = Nearest(nodes, start, snapRadiusMeters);
        int exit = Nearest(nodes, goal, snapRadiusMeters);
        if (entry < 0 || exit < 0)
        {
            return null;
        }

        // Bounded A*.
        var open = new SortedSet<(float F, int Node)>();
        var gScore = new Dictionary<int, float> { [entry] = 0f };
        var cameFrom = new Dictionary<int, int>();
        open.Add((nodes[entry].HorizontalDistanceTo(nodes[exit]), entry));
        int expanded = 0;

        while (open.Count > 0 && expanded < maxExpandedNodes)
        {
            (float _, int current) = open.Min;
            open.Remove(open.Min);
            expanded++;

            if (current == exit)
            {
                var path = new List<RoadPoint> { start };
                var reversed = new List<int> { current };
                while (cameFrom.TryGetValue(current, out int previous))
                {
                    reversed.Add(previous);
                    current = previous;
                }

                for (int index = reversed.Count - 1; index >= 0; index--)
                {
                    path.Add(nodes[reversed[index]]);
                }

                path.Add(goal);
                return path;
            }

            foreach (int neighbor in edges[current])
            {
                float tentative = gScore[current] + nodes[current].HorizontalDistanceTo(nodes[neighbor]);
                if (!gScore.TryGetValue(neighbor, out float known) || tentative < known)
                {
                    gScore[neighbor] = tentative;
                    cameFrom[neighbor] = current;
                    open.Add((tentative + nodes[neighbor].HorizontalDistanceTo(nodes[exit]), neighbor));
                }
            }
        }

        return null;
    }

    private static int Nearest(List<RoadPoint> nodes, RoadPoint position, float maxRadius)
    {
        int best = -1;
        float bestDistance = maxRadius;
        for (int index = 0; index < nodes.Count; index++)
        {
            float distance = nodes[index].HorizontalDistanceTo(position);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = index;
            }
        }

        return best;
    }

    private static long CellKey(RoadPoint point)
    {
        return Combine(
            (int)Math.Floor(point.X / JunctionLinkMeters),
            (int)Math.Floor(point.Z / JunctionLinkMeters));
    }

    private static long Combine(int cellX, int cellZ)
    {
        return ((long)(uint)cellX << 32) ^ (uint)cellZ;
    }
}
