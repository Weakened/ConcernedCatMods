using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Camp;

/// <summary>Turns a set of member positions into an approximate perimeter.
///
/// <b>It is an operational boundary, not geometry.</b> The question it answers
/// is "is this inside camp, roughly, and where inside camp is near here" - for
/// deciding whether to walk somewhere, not for drawing anything. So it is a
/// convex hull pushed out by a margin, which is deliberately generous around a
/// horseshoe-shaped base: an NPC that thinks the courtyard is inside is right,
/// and an NPC that thinks the gap between two wings is outside would refuse to
/// cross its own yard.
///
/// <b>Degenerate camps are ordinary.</b> One piece, two pieces, or several
/// pieces in a line all have no hull, and those are the first days of every
/// save. They produce a ring, so every camp has a perimeter and no caller has a
/// special case.
///
/// <b>Determinism.</b> Ties are broken by X and then Z, the same way the
/// shipped companion sensing orders equally good fires, so the same camp always
/// produces the same perimeter and an NPC never paces between two answers.
/// </summary>
internal static class CampBoundary
{
    /// <summary>How many points a ring gets. Eight is enough for a
    /// point-in-polygon test to behave and few enough to hand to a role.
    /// </summary>
    private const int RingPoints = 8;

    /// <summary>The hull of these positions, pushed out by
    /// <paramref name="marginMetres"/>, on the horizontal plane. Height is
    /// carried from the centre, because camp is a footprint.</summary>
    internal static IReadOnlyList<NpcPoint> Perimeter(
        IReadOnlyList<NpcPoint> members, NpcPoint centre, float marginMetres)
    {
        if (members == null || members.Count == 0)
        {
            return Array.Empty<NpcPoint>();
        }

        float margin = marginMetres < 0f || float.IsNaN(marginMetres) ? 0f : marginMetres;
        IReadOnlyList<NpcPoint> hull = Hull(members);

        if (hull.Count < 3)
        {
            float reach = 0f;
            foreach (NpcPoint member in members)
            {
                float distance = member.HorizontalDistanceTo(centre);
                if (distance > reach)
                {
                    reach = distance;
                }
            }

            return Ring(centre, reach + margin);
        }

        var expanded = new List<NpcPoint>(hull.Count);
        foreach (NpcPoint vertex in hull)
        {
            expanded.Add(PushOut(vertex, centre, margin));
        }

        return expanded;
    }

    /// <summary>The mean of the member positions. Used as camp's centre because
    /// it is the one summary that does not move when a far corner is added and
    /// removed repeatedly, which a bounding-box centre does.</summary>
    internal static NpcPoint Centre(IReadOnlyList<NpcPoint> members, NpcPoint fallback)
    {
        if (members == null || members.Count == 0)
        {
            return fallback;
        }

        double x = 0;
        double y = 0;
        double z = 0;
        foreach (NpcPoint member in members)
        {
            x += member.X;
            y += member.Y;
            z += member.Z;
        }

        return new NpcPoint((float)(x / members.Count), (float)(y / members.Count), (float)(z / members.Count));
    }

    /// <summary>The furthest a member sits from <paramref name="centre"/>, plus
    /// the margin: camp's extent as one number, for the many callers that only
    /// need "roughly how big".</summary>
    internal static float Extent(IReadOnlyList<NpcPoint> members, NpcPoint centre, float marginMetres)
    {
        float reach = 0f;
        if (members != null)
        {
            foreach (NpcPoint member in members)
            {
                float distance = member.HorizontalDistanceTo(centre);
                if (distance > reach)
                {
                    reach = distance;
                }
            }
        }

        return reach + (marginMetres < 0f || float.IsNaN(marginMetres) ? 0f : marginMetres);
    }

    /// <summary>Whether a point is inside a perimeter, by ray casting on the
    /// horizontal plane. A point exactly on an edge may answer either way, and
    /// no caller should care: the margin exists so the edge is never the
    /// interesting place.</summary>
    internal static bool Contains(IReadOnlyList<NpcPoint> perimeter, NpcPoint point)
    {
        if (perimeter == null || perimeter.Count < 3 || !point.IsFinite)
        {
            return false;
        }

        bool inside = false;
        for (int index = 0, previous = perimeter.Count - 1; index < perimeter.Count; previous = index++)
        {
            NpcPoint a = perimeter[index];
            NpcPoint b = perimeter[previous];
            if (a.Z > point.Z != b.Z > point.Z
                && point.X < ((b.X - a.X) * (point.Z - a.Z) / (b.Z - a.Z)) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static IReadOnlyList<NpcPoint> Hull(IReadOnlyList<NpcPoint> members)
    {
        var points = new List<NpcPoint>(members.Count);
        foreach (NpcPoint member in members)
        {
            if (member.IsFinite)
            {
                points.Add(member);
            }
        }

        points.Sort(CompareByXThenZ);

        // Andrew's monotone chain. Collinear points are dropped (the cross
        // product test is strict), so a row of fence posts yields two points
        // and falls through to the ring above, which is the correct answer for
        // a camp that is a line.
        var hull = new List<NpcPoint>(points.Count + 1);
        for (int pass = 0; pass < 2; pass++)
        {
            int start = hull.Count;
            for (int index = 0; index < points.Count; index++)
            {
                NpcPoint candidate = points[pass == 0 ? index : points.Count - 1 - index];
                while (hull.Count >= start + 2
                    && Cross(hull[hull.Count - 2], hull[hull.Count - 1], candidate) <= 0)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(candidate);
            }

            if (hull.Count > start)
            {
                // The last point of each pass is the first of the next.
                hull.RemoveAt(hull.Count - 1);
            }
        }

        return hull;
    }

    private static int CompareByXThenZ(NpcPoint left, NpcPoint right)
    {
        int byX = left.X.CompareTo(right.X);
        return byX != 0 ? byX : left.Z.CompareTo(right.Z);
    }

    private static double Cross(NpcPoint origin, NpcPoint a, NpcPoint b) =>
        ((double)(a.X - origin.X) * (b.Z - origin.Z)) - ((double)(a.Z - origin.Z) * (b.X - origin.X));

    private static NpcPoint PushOut(NpcPoint vertex, NpcPoint centre, float margin)
    {
        if (margin <= 0f)
        {
            return vertex;
        }

        float dx = vertex.X - centre.X;
        float dz = vertex.Z - centre.Z;
        double length = Math.Sqrt((dx * dx) + (dz * dz));
        if (length < 0.0001)
        {
            return new NpcPoint(vertex.X + margin, vertex.Y, vertex.Z);
        }

        return new NpcPoint(
            vertex.X + (float)(dx / length * margin),
            vertex.Y,
            vertex.Z + (float)(dz / length * margin));
    }

    private static IReadOnlyList<NpcPoint> Ring(NpcPoint centre, float radius)
    {
        float reach = radius < 0.5f ? 0.5f : radius;
        var ring = new List<NpcPoint>(RingPoints);
        for (int index = 0; index < RingPoints; index++)
        {
            double angle = 2 * Math.PI * index / RingPoints;
            ring.Add(new NpcPoint(
                centre.X + (float)(Math.Cos(angle) * reach),
                centre.Y,
                centre.Z + (float)(Math.Sin(angle) * reach)));
        }

        return ring;
    }
}
