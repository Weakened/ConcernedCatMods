using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Camp;

/// <summary>Where camp is, as of one survey. Immutable: a role may hold one for
/// as long as it likes, and it will never change under it.
///
/// <b>What a caller may rely on.</b> Camp centre, extent, perimeter points and
/// the nearest safe interior point - and nothing else. There is no list of
/// pieces here on purpose: a role that walked the members would be re-deciding
/// membership, and this library would have two answers to the same question.
/// </summary>
internal sealed class CampSnapshot
{
    private readonly IReadOnlyList<NpcPoint> _perimeter;

    internal CampSnapshot(
        CampAnchor anchor,
        NpcPoint centre,
        float extentMetres,
        IReadOnlyList<NpcPoint>? perimeter,
        int members,
        int revision,
        NpcWorldEpoch epoch,
        CampSurveyCost cost)
    {
        Anchor = anchor;
        Centre = centre;
        ExtentMetres = extentMetres < 0f || float.IsNaN(extentMetres) ? 0f : extentMetres;
        _perimeter = perimeter ?? Array.Empty<NpcPoint>();
        Members = members < 0 ? 0 : members;
        Revision = revision;
        Epoch = epoch;
        Cost = cost;
    }

    /// <summary>The empty camp: anchored nowhere, containing nothing. What a
    /// survey answers before an anchor can be had, and never a reason to walk
    /// anywhere.</summary>
    internal static CampSnapshot Nowhere(NpcWorldEpoch epoch, int revision) =>
        new CampSnapshot(CampAnchor.None, default, 0f, null, 0, revision, epoch, CampSurveyCost.Cached);

    internal CampAnchor Anchor { get; }

    /// <summary>The mean of the member positions, or the anchor when camp has
    /// no members yet.</summary>
    internal NpcPoint Centre { get; }

    /// <summary>The furthest member from <see cref="Centre"/>, plus the margin.
    /// </summary>
    internal float ExtentMetres { get; }

    /// <summary>The approximate perimeter, in order around camp. Empty only
    /// when camp has no members at all.</summary>
    internal IReadOnlyList<NpcPoint> Perimeter => _perimeter;

    /// <summary>How many player-built pieces this camp is made of.</summary>
    internal int Members { get; }

    /// <summary>Increases whenever camp changed. A plan made against an older
    /// revision is out of date, and every seam in this library that carries a
    /// revision means the same thing by it.</summary>
    internal int Revision { get; }

    /// <summary>The world load this camp was surveyed in. A snapshot from
    /// another load describes places whose names no longer mean anything.
    /// </summary>
    internal NpcWorldEpoch Epoch { get; }

    /// <summary>What the survey that produced this cost.</summary>
    internal CampSurveyCost Cost { get; }

    /// <summary>True when there is an anchor and at least one piece: camp is a
    /// place, not just a point somebody is standing on.</summary>
    internal bool IsEstablished => Anchor.HasAnchor && Members > 0;

    /// <summary>Whether a point is inside camp.
    ///
    /// Falls back to the extent when there are too few members for a polygon,
    /// so the answer is meaningful on day one with a single campfire.</summary>
    internal bool Contains(NpcPoint point)
    {
        if (!point.IsFinite || !Anchor.HasAnchor)
        {
            return false;
        }

        if (_perimeter.Count >= 3)
        {
            return CampBoundary.Contains(_perimeter, point);
        }

        return point.HorizontalDistanceTo(Centre) <= ExtentMetres;
    }

    /// <summary>The nearest point inside camp to <paramref name="from"/>.
    ///
    /// <b>"Safe" means away from the edge, not away from danger.</b> This
    /// library cannot see danger. What it can promise is that the point it
    /// returns is inside the perimeter rather than balanced on it, which is
    /// what a caller walking to "just inside camp" actually needs: a point on
    /// the boundary is inside or outside depending on float noise, and an NPC
    /// sent to one oscillates.
    ///
    /// Returns <see cref="Centre"/> when camp is too small to have an interior,
    /// and refuses - answering false - when there is no camp at all, rather
    /// than inventing the origin.</summary>
    internal bool TryNearestInteriorPoint(NpcPoint from, out NpcPoint interior)
    {
        interior = default;
        if (!Anchor.HasAnchor)
        {
            return false;
        }

        if (!from.IsFinite)
        {
            interior = Centre;
            return true;
        }

        if (Contains(from))
        {
            interior = from;
            return true;
        }

        if (_perimeter.Count < 3)
        {
            interior = Centre;
            return true;
        }

        NpcPoint nearest = NearestOnPerimeter(from);

        // Step in from the edge towards the centre by a tenth of the extent,
        // bounded to a metre at least: far enough that the containment test
        // agrees, near enough that it is still "just inside".
        float step = ExtentMetres * 0.1f;
        NpcPoint candidate = Towards(nearest, Centre, step < 1f ? 1f : step);
        interior = Contains(candidate) ? candidate : Centre;
        return true;
    }

    private NpcPoint NearestOnPerimeter(NpcPoint from)
    {
        NpcPoint best = _perimeter[0];
        float bestDistance = float.MaxValue;
        for (int index = 0; index < _perimeter.Count; index++)
        {
            NpcPoint a = _perimeter[index];
            NpcPoint b = _perimeter[(index + 1) % _perimeter.Count];
            NpcPoint onEdge = ClosestOnSegment(a, b, from);
            float distance = onEdge.HorizontalDistanceTo(from);

            // Strictly better only, and ties settled by the earlier edge, so the
            // same camp and the same standpoint always give the same answer.
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = onEdge;
            }
        }

        return best;
    }

    private static NpcPoint ClosestOnSegment(NpcPoint a, NpcPoint b, NpcPoint point)
    {
        float dx = b.X - a.X;
        float dz = b.Z - a.Z;
        double lengthSquared = ((double)dx * dx) + ((double)dz * dz);
        if (lengthSquared < 0.000001)
        {
            return a;
        }

        double t = ((((double)point.X - a.X) * dx) + (((double)point.Z - a.Z) * dz)) / lengthSquared;
        t = t < 0 ? 0 : t > 1 ? 1 : t;
        return new NpcPoint(a.X + (float)(dx * t), a.Y, a.Z + (float)(dz * t));
    }

    private static NpcPoint Towards(NpcPoint from, NpcPoint to, float distance)
    {
        float dx = to.X - from.X;
        float dz = to.Z - from.Z;
        double length = Math.Sqrt(((double)dx * dx) + ((double)dz * dz));
        if (length < 0.0001)
        {
            return to;
        }

        if (distance >= length)
        {
            return to;
        }

        return new NpcPoint(
            from.X + (float)(dx / length * distance),
            to.Y,
            from.Z + (float)(dz / length * distance));
    }

    public override string ToString() =>
        Anchor + ", " + Members + " pieces, extent " +
        Math.Round(ExtentMetres, 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " m";
}
