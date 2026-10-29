using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TheConcernedCat.Interop.WorkArea;

/// <summary>Why a drawn boundary is not a work area. A rejected polygon is
/// never treated as a region of any size: the player is asked to redraw it.
/// </summary>
internal enum WorkAreaShapeProblem
{
    None = 0,
    TooFewPoints,
    TooManyPoints,
    NotFinite,
    TooLarge,
    TooSmall,
    SelfIntersecting,
}

/// <summary>One vertex on the ground plane, in world metres. A work area is
/// a footprint, so height is not part of it.</summary>
internal readonly struct WorkAreaPoint : IEquatable<WorkAreaPoint>
{
    public WorkAreaPoint(float x, float z)
    {
        X = x;
        Z = z;
    }

    public float X { get; }

    public float Z { get; }

    public bool Equals(WorkAreaPoint other) => X.Equals(other.X) && Z.Equals(other.Z);

    public override bool Equals(object? obj) => obj is WorkAreaPoint other && Equals(other);

    public override int GetHashCode() => unchecked((X.GetHashCode() * 397) ^ Z.GetHashCode());
}

/// <summary>#296: the shape of a work area, as both the drawing product and
/// any consumer understand it. Game-free and compiled into each product, so
/// the provider's validation and the consumer's are the same code.
///
/// A polygon here is closed (the last point joins the first; the first point
/// is not repeated), finite, bounded and simple. Every rule the issue names
/// is a refusal, never a repair: a self-intersecting, oversized, degenerate or
/// non-finite boundary is not quietly turned into "the whole region" or a
/// smaller one. Containment is deterministic (even-odd, a fixed half-open edge
/// rule), and the revision is derived from the stored vertices alone, so two
/// sessions and two products on the same runtime agree on it. It is computed
/// from round-trip float text, which differs between .NET runtimes, so a
/// consumer compares revisions it received, never one it computed elsewhere.
/// </summary>
internal sealed class WorkAreaPolygon
{
    /// <summary>Fewest vertices a closed region can have.</summary>
    public const int MinPoints = 3;

    /// <summary>Most vertices a work area may carry. A freehand stroke is
    /// simplified to this before it is offered; the cap keeps containment and
    /// the wire format bounded.</summary>
    public const int MaxPoints = 64;

    /// <summary>Largest width or depth of a work area, in metres. A work area
    /// is a camp-sized patch for one worker in loaded ground, not a biome.
    /// </summary>
    public const float MaxExtentMetres = 256f;

    /// <summary>Smallest enclosed area, in square metres. Below this the
    /// boundary is a scribble, not a place.</summary>
    public const float MinAreaSquareMetres = 16f;

    private readonly WorkAreaPoint[] _points;

    private WorkAreaPolygon(WorkAreaPoint[] points, float area)
    {
        _points = points;
        AreaSquareMetres = area;
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        foreach (WorkAreaPoint point in points)
        {
            minX = Math.Min(minX, point.X);
            minZ = Math.Min(minZ, point.Z);
            maxX = Math.Max(maxX, point.X);
            maxZ = Math.Max(maxZ, point.Z);
        }

        MinX = minX;
        MinZ = minZ;
        MaxX = maxX;
        MaxZ = maxZ;
        Revision = ComputeRevision(points);
    }

    public IReadOnlyList<WorkAreaPoint> Points => _points;

    public float AreaSquareMetres { get; }

    public float MinX { get; }

    public float MinZ { get; }

    public float MaxX { get; }

    public float MaxZ { get; }

    public float CenterX => (MinX + MaxX) * 0.5f;

    public float CenterZ => (MinZ + MaxZ) * 0.5f;

    /// <summary>A stable 16-hex-digit fingerprint of the stored vertices.
    /// The same vertices always give the same revision on the same runtime;
    /// an edited boundary gives a new one, which is
    /// how a consumer knows the area behind an accepted job changed.</summary>
    public string Revision { get; }

    /// <summary>Validates and builds. Never throws for bad input.</summary>
    public static WorkAreaShapeProblem TryCreate(
        IReadOnlyList<WorkAreaPoint>? points, out WorkAreaPolygon? polygon)
    {
        polygon = null;
        if (points == null)
        {
            return WorkAreaShapeProblem.TooFewPoints;
        }

        var cleaned = new List<WorkAreaPoint>(points.Count);
        foreach (WorkAreaPoint point in points)
        {
            if (!IsFinite(point.X) || !IsFinite(point.Z))
            {
                return WorkAreaShapeProblem.NotFinite;
            }

            // A repeated vertex adds no edge; dropping it is not a repair of
            // the shape, it is the same shape written once.
            if (cleaned.Count == 0 || !cleaned[cleaned.Count - 1].Equals(point))
            {
                cleaned.Add(point);
            }
        }

        if (cleaned.Count > 1 && cleaned[0].Equals(cleaned[cleaned.Count - 1]))
        {
            cleaned.RemoveAt(cleaned.Count - 1);
        }

        if (cleaned.Count < MinPoints)
        {
            return WorkAreaShapeProblem.TooFewPoints;
        }

        if (cleaned.Count > MaxPoints)
        {
            return WorkAreaShapeProblem.TooManyPoints;
        }

        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        foreach (WorkAreaPoint point in cleaned)
        {
            minX = Math.Min(minX, point.X);
            minZ = Math.Min(minZ, point.Z);
            maxX = Math.Max(maxX, point.X);
            maxZ = Math.Max(maxZ, point.Z);
        }

        if (maxX - minX > MaxExtentMetres || maxZ - minZ > MaxExtentMetres)
        {
            return WorkAreaShapeProblem.TooLarge;
        }

        if (HasSelfIntersection(cleaned))
        {
            return WorkAreaShapeProblem.SelfIntersecting;
        }

        double signed = SignedArea(cleaned);
        double area = Math.Abs(signed);
        if (area < MinAreaSquareMetres)
        {
            return WorkAreaShapeProblem.TooSmall;
        }

        // One winding (counter-clockwise) for every stored polygon, so a
        // consumer can rely on it.
        if (signed < 0)
        {
            cleaned.Reverse();
        }

        polygon = new WorkAreaPolygon(cleaned.ToArray(), (float)area);
        return WorkAreaShapeProblem.None;
    }

    /// <summary>Even-odd containment with a half-open edge rule: a point
    /// exactly on a left or bottom edge is inside, on a right or top edge is
    /// outside. Deterministic for every input; a non-finite point is outside.
    /// </summary>
    public bool Contains(float x, float z)
    {
        if (!IsFinite(x) || !IsFinite(z) || x < MinX || x > MaxX || z < MinZ || z > MaxZ)
        {
            return false;
        }

        bool inside = false;
        int count = _points.Length;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            WorkAreaPoint a = _points[i];
            WorkAreaPoint b = _points[j];
            if ((a.Z > z) != (b.Z > z))
            {
                double crossX = a.X + ((double)(z - a.Z) * (b.X - a.X) / (b.Z - a.Z));
                if (x < crossX)
                {
                    inside = !inside;
                }
            }
        }

        return inside;
    }

    /// <summary><c>x;z|x;z|...</c> with round-trip floats, invariant culture.
    /// </summary>
    public string ToWire()
    {
        var builder = new StringBuilder(_points.Length * 16);
        for (int index = 0; index < _points.Length; index++)
        {
            if (index > 0)
            {
                builder.Append('|');
            }

            builder.Append(_points[index].X.ToString("R", CultureInfo.InvariantCulture));
            builder.Append(';');
            builder.Append(_points[index].Z.ToString("R", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>Parses <see cref="ToWire"/> output and validates it. A string
    /// that does not parse completely is not a polygon.</summary>
    public static WorkAreaShapeProblem TryParseWire(string? wire, out WorkAreaPolygon? polygon)
    {
        polygon = null;
        if (string.IsNullOrEmpty(wire))
        {
            return WorkAreaShapeProblem.TooFewPoints;
        }

        string[] pairs = wire!.Split('|');
        if (pairs.Length > MaxPoints + 1)
        {
            return WorkAreaShapeProblem.TooManyPoints;
        }

        var points = new List<WorkAreaPoint>(pairs.Length);
        foreach (string pair in pairs)
        {
            string[] parts = pair.Split(';');
            if (parts.Length != 2 ||
                !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                return WorkAreaShapeProblem.NotFinite;
            }

            points.Add(new WorkAreaPoint(x, z));
        }

        return TryCreate(points, out polygon);
    }

    /// <summary>A sentence for the player, never a code.</summary>
    public static string Describe(WorkAreaShapeProblem problem)
    {
        return problem switch
        {
            WorkAreaShapeProblem.None => "a valid work area",
            WorkAreaShapeProblem.TooFewPoints => "the boundary needs at least three distinct points",
            WorkAreaShapeProblem.TooManyPoints => "the boundary has more than " + MaxPoints + " points; draw it more simply",
            WorkAreaShapeProblem.NotFinite => "the boundary has a point that is not a real position",
            WorkAreaShapeProblem.TooLarge => "the area is wider than " + MaxExtentMetres.ToString("0", CultureInfo.InvariantCulture) + " m",
            WorkAreaShapeProblem.TooSmall => "the area encloses less than " + MinAreaSquareMetres.ToString("0", CultureInfo.InvariantCulture) + " square metres",
            WorkAreaShapeProblem.SelfIntersecting => "the boundary crosses itself; draw it without crossing",
            _ => "the boundary is not a work area",
        };
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static double SignedArea(IReadOnlyList<WorkAreaPoint> points)
    {
        double sum = 0;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            sum += ((double)points[j].X * points[i].Z) - ((double)points[i].X * points[j].Z);
        }

        return sum * 0.5;
    }

    /// <summary>O(n²) over at most <see cref="MaxPoints"/> edges. Adjacent
    /// edges share a vertex and are not a crossing; any other touch is.</summary>
    private static bool HasSelfIntersection(IReadOnlyList<WorkAreaPoint> points)
    {
        int count = points.Count;
        for (int i = 0; i < count; i++)
        {
            WorkAreaPoint a1 = points[i];
            WorkAreaPoint a2 = points[(i + 1) % count];
            for (int j = i + 1; j < count; j++)
            {
                bool adjacent = j == i + 1 || (i == 0 && j == count - 1);
                WorkAreaPoint b1 = points[j];
                WorkAreaPoint b2 = points[(j + 1) % count];
                if (adjacent)
                {
                    // Adjacent edges may only meet at their shared vertex: a
                    // fold-back along the same line is a degenerate crossing.
                    if (Collinear(a1, a2, b1, b2) && Overlaps(a1, a2, b1, b2))
                    {
                        return true;
                    }

                    continue;
                }

                if (SegmentsTouch(a1, a2, b1, b2))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static double Cross(WorkAreaPoint o, WorkAreaPoint a, WorkAreaPoint b)
    {
        return (((double)a.X - o.X) * ((double)b.Z - o.Z)) - (((double)a.Z - o.Z) * ((double)b.X - o.X));
    }

    private static bool OnSegment(WorkAreaPoint p, WorkAreaPoint q, WorkAreaPoint r)
    {
        return Math.Min(p.X, r.X) <= q.X && q.X <= Math.Max(p.X, r.X)
            && Math.Min(p.Z, r.Z) <= q.Z && q.Z <= Math.Max(p.Z, r.Z);
    }

    private static bool SegmentsTouch(WorkAreaPoint p1, WorkAreaPoint p2, WorkAreaPoint q1, WorkAreaPoint q2)
    {
        double d1 = Cross(q1, q2, p1);
        double d2 = Cross(q1, q2, p2);
        double d3 = Cross(p1, p2, q1);
        double d4 = Cross(p1, p2, q2);
        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
        {
            return true;
        }

        return (d1 == 0 && OnSegment(q1, p1, q2))
            || (d2 == 0 && OnSegment(q1, p2, q2))
            || (d3 == 0 && OnSegment(p1, q1, p2))
            || (d4 == 0 && OnSegment(p1, q2, p2));
    }

    private static bool Collinear(WorkAreaPoint a1, WorkAreaPoint a2, WorkAreaPoint b1, WorkAreaPoint b2)
    {
        return Cross(a1, a2, b1) == 0 && Cross(a1, a2, b2) == 0;
    }

    /// <summary>Whether two collinear segments share more than one point.
    /// </summary>
    private static bool Overlaps(WorkAreaPoint a1, WorkAreaPoint a2, WorkAreaPoint b1, WorkAreaPoint b2)
    {
        bool horizontalish = Math.Abs(a2.X - a1.X) >= Math.Abs(a2.Z - a1.Z);
        double aMin = horizontalish ? Math.Min(a1.X, a2.X) : Math.Min(a1.Z, a2.Z);
        double aMax = horizontalish ? Math.Max(a1.X, a2.X) : Math.Max(a1.Z, a2.Z);
        double bMin = horizontalish ? Math.Min(b1.X, b2.X) : Math.Min(b1.Z, b2.Z);
        double bMax = horizontalish ? Math.Max(b1.X, b2.X) : Math.Max(b1.Z, b2.Z);
        return Math.Min(aMax, bMax) - Math.Max(aMin, bMin) > 0;
    }

    /// <summary>FNV-1a 64 over the invariant wire text of the normalised
    /// (counter-clockwise) vertices. Deliberately not
    /// <see cref="string.GetHashCode()"/>, which differs between runtimes.
    /// </summary>
    private static string ComputeRevision(IReadOnlyList<WorkAreaPoint> points)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        foreach (WorkAreaPoint point in points)
        {
            string text = point.X.ToString("R", CultureInfo.InvariantCulture) + ";" +
                point.Z.ToString("R", CultureInfo.InvariantCulture) + "|";
            foreach (char character in text)
            {
                hash ^= character;
                hash *= prime;
            }
        }

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }
}
