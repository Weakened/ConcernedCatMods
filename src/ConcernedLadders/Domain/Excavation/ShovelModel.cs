using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Domain.Excavation;

/// <summary>What a part of the shovel is made of: which region of the donor's texture sheet it samples.</summary>
internal enum ShovelPartMaterial
{
    Wood,
    Iron,
}

/// <summary>One convex part of the shovel: a polygon extruded along one axis, in the held model's (the donor's
/// <c>attach</c> transform's) space.</summary>
internal sealed class ShovelPart
{
    internal ShovelPart(string name, ShovelPartMaterial material, int axis, (float A, float B)[] polygon, float from, float to)
    {
        Name = name;
        Material = material;
        Axis = axis;
        Polygon = polygon;
        From = from;
        To = to;
    }

    internal string Name { get; }

    internal ShovelPartMaterial Material { get; }

    /// <summary>The extrusion axis: 0 x, 1 y, 2 z. The polygon lies in the other two, in (y, z), (x, z) or (x, y)
    /// order.</summary>
    internal int Axis { get; }

    /// <summary>Convex, counter-clockwise.</summary>
    internal IReadOnlyList<(float A, float B)> Polygon { get; }

    internal float From { get; }

    internal float To { get; }

    /// <summary>A polygon point at an axial position, in model space.</summary>
    internal ClimbPoint Point((float A, float B) p, float axial) => Axis switch
    {
        0 => new ClimbPoint(axial, p.A, p.B),
        1 => new ClimbPoint(p.A, axial, p.B),
        _ => new ClimbPoint(p.A, p.B, axial),
    };
}

/// <summary>The Iron Shovel's own model (#550, owner playtest 2026-11-10: "create a shovel ... and not use pickaxe").
/// Until #550 the item showed the Iron Pickaxe it is cloned from, tinted. It now shows a shovel: a wooden shaft with a
/// T-grip, an iron socket and a pointed iron blade, built in code and textured from the donor's own <c>weapons1</c>
/// sheet (borrowed, never edited): its handle wood and its head iron, the regions the pickaxe's own mesh uses there
/// (read from the installed game by <c>cl-ladders-devtools/shovel/pickaxe.py</c>; the iron one trimmed clear of the
/// sheet's white background). No art is imported.
///
/// <para>Space: the donor's <c>attach</c> transform, which the game shows both in the hand and on the ground. The
/// pickaxe's handle runs along its +z from about -0.13 to 0.70 m with its head at about 0.6 m (the pickaxe mesh's +y,
/// turned onto +z by its child transform); the shovel keeps that axis and the grip near the origin, so the hand holds
/// it where it held the pickaxe. Game-free so the shape is tested; in game, OWNER GO-AROUND PENDING.</para></summary>
internal static class ShovelModel
{
    /// <summary>The donor's renderer child under <c>attach</c>, replaced by the shovel.</summary>
    internal const string DonorAttach = "attach";

    internal const string DonorModel = "pickaxe";

    /// <summary>The shovel's own renderer child under <c>attach</c>.</summary>
    internal const string Child = "shovel";

    /// <summary>The donor's texture sheet, by material name.</summary>
    internal const string DonorMaterial = "weapons1";

    /// <summary>The pickaxe handle's wood island in <c>weapons</c> (256 px).</summary>
    internal static UvRegion Wood => UvRegion.Rect(0.5455f, 0.6726f, 0.5686f, 0.9092f);

    /// <summary>The pickaxe head's iron island in <c>weapons</c>, trimmed from u 0.9299 and v 0.6083 to 0.9914 clear of
    /// the white background at its edges.</summary>
    internal static UvRegion Iron => UvRegion.Rect(0.935f, 0.65f, 0.975f, 0.95f);

    internal const float ShaftRadius = 0.022f;

    internal const float ShaftStart = -0.16f;

    internal const float ShaftEnd = 0.62f;

    internal const float GripHalfWidth = 0.075f;

    internal const float GripHalfThickness = 0.02f;

    internal const float SocketRadius = 0.03f;

    internal const float SocketStart = 0.58f;

    internal const float BladeStart = 0.68f;

    /// <summary>Where the blade's straight sides end and its point begins.</summary>
    internal const float BladeShoulder = 0.9f;

    internal const float BladeTip = 1.0f;

    internal const float BladeHalfWidth = 0.12f;

    internal const float BladeHalfThickness = 0.006f;

    /// <summary>The sides of the round parts.</summary>
    internal const int RoundSides = 8;

    internal static readonly IReadOnlyList<ShovelPart> Parts = new[]
    {
        new ShovelPart("shaft", ShovelPartMaterial.Wood, 2, Round(ShaftRadius), ShaftStart, ShaftEnd),
        // The T-grip: a bar across x at the shaft's end.
        new ShovelPart("grip", ShovelPartMaterial.Wood, 0,
            Rectangle(-GripHalfThickness, ShaftStart - 2f * GripHalfThickness, GripHalfThickness, ShaftStart),
            -GripHalfWidth, GripHalfWidth),
        new ShovelPart("socket", ShovelPartMaterial.Iron, 2, Round(SocketRadius), SocketStart, BladeStart),
        // The blade lies in the shaft's plane (x, z), thin in y, straight-sided to its shoulder and then pointed.
        new ShovelPart("blade", ShovelPartMaterial.Iron, 1, new[]
        {
            (-BladeHalfWidth, BladeStart),
            (BladeHalfWidth, BladeStart),
            (BladeHalfWidth, BladeShoulder),
            (0f, BladeTip),
            (-BladeHalfWidth, BladeShoulder),
        }, -BladeHalfThickness, BladeHalfThickness),
    };

    /// <summary>The whole shovel as one flat-shaded mesh: every part's caps and sides, each face wound outward from its
    /// part's centre, wood parts in <see cref="Wood"/> and iron parts in <see cref="Iron"/>.</summary>
    internal static CellarMesh Mesh()
    {
        var vertices = new List<ClimbPoint>();
        var normals = new List<ClimbPoint>();
        var uvs = new List<CellarUv>();
        var triangles = new List<int>();
        foreach (ShovelPart part in Parts) Add(part, part.Material == ShovelPartMaterial.Wood ? Wood : Iron, vertices, normals, uvs, triangles);
        return new CellarMesh(Child, vertices.ToArray(), normals.ToArray(), uvs.ToArray(), triangles.ToArray());
    }

    /// <summary>A part's centre: the polygon's mean at the middle of its extrusion. Inside it, because it is convex.</summary>
    internal static ClimbPoint Centre(ShovelPart part)
    {
        float a = 0f, b = 0f;
        foreach ((float pa, float pb) in part.Polygon) { a += pa; b += pb; }
        return part.Point((a / part.Polygon.Count, b / part.Polygon.Count), (part.From + part.To) / 2f);
    }

    private static void Add(ShovelPart part, UvRegion region, List<ClimbPoint> vertices, List<ClimbPoint> normals, List<CellarUv> uvs, List<int> triangles)
    {
        ClimbPoint centre = Centre(part);
        IReadOnlyList<(float A, float B)> polygon = part.Polygon;
        float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
        foreach ((float a, float b) in polygon)
        {
            minA = Math.Min(minA, a); maxA = Math.Max(maxA, a); minB = Math.Min(minB, b); maxB = Math.Max(maxB, b);
        }

        // Caps: the polygon at each end, mapped by its own extents.
        foreach (float axial in new[] { part.From, part.To })
        {
            var cap = new ClimbPoint[polygon.Count];
            var capUv = new CellarUv[polygon.Count];
            for (int index = 0; index < polygon.Count; ++index)
            {
                cap[index] = part.Point(polygon[index], axial);
                capUv[index] = region.Map(polygon[index].A, polygon[index].B, minA, maxA, minB, maxB);
            }

            Face(cap, capUv, centre, vertices, normals, uvs, triangles);
        }

        // Sides: one quad per edge, mapped around the whole perimeter and along the extrusion, so the region wraps the
        // part once (a shaft's grain runs along it).
        float perimeter = 0f;
        for (int index = 0; index < polygon.Count; ++index) perimeter += Length(polygon[index], polygon[(index + 1) % polygon.Count]);
        float at = 0f;
        for (int index = 0; index < polygon.Count; ++index)
        {
            (float A, float B) p = polygon[index], q = polygon[(index + 1) % polygon.Count];
            float next = at + Length(p, q);
            var quad = new[] { part.Point(p, part.From), part.Point(q, part.From), part.Point(q, part.To), part.Point(p, part.To) };
            var quadUv = new[]
            {
                region.Map(part.From, at, part.From, part.To, 0f, perimeter),
                region.Map(part.From, next, part.From, part.To, 0f, perimeter),
                region.Map(part.To, next, part.From, part.To, 0f, perimeter),
                region.Map(part.To, at, part.From, part.To, 0f, perimeter),
            };
            Face(quad, quadUv, centre, vertices, normals, uvs, triangles);
            at = next;
        }
    }

    /// <summary>One planar convex face as a triangle fan, wound so Unity's front face (<c>Cross(b - a, c - a)</c>) points
    /// away from <paramref name="centre"/>, with its unit normal on every vertex.</summary>
    private static void Face(ClimbPoint[] face, CellarUv[] faceUv, ClimbPoint centre, List<ClimbPoint> vertices, List<ClimbPoint> normals,
        List<CellarUv> uvs, List<int> triangles)
    {
        ClimbPoint normal = CellarBlueprint.TriangleNormal(face[0], face[1], face[2]);
        float cx = 0f, cy = 0f, cz = 0f;
        foreach (ClimbPoint p in face) { cx += p.X; cy += p.Y; cz += p.Z; }
        cx = cx / face.Length - centre.X; cy = cy / face.Length - centre.Y; cz = cz / face.Length - centre.Z;
        bool outward = normal.X * cx + normal.Y * cy + normal.Z * cz > 0f;
        float length = (float)Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z);
        if (!(length > 0f)) throw new InvalidOperationException("A degenerate shovel face.");
        float sign = outward ? 1f : -1f;
        var unit = new ClimbPoint(sign * normal.X / length, sign * normal.Y / length, sign * normal.Z / length);
        int first = vertices.Count;
        for (int index = 0; index < face.Length; ++index)
        {
            vertices.Add(face[index]);
            normals.Add(unit);
            uvs.Add(faceUv[index]);
        }

        for (int index = 1; index + 1 < face.Length; ++index)
        {
            if (outward) triangles.AddRange(new[] { first, first + index, first + index + 1 });
            else triangles.AddRange(new[] { first, first + index + 1, first + index });
        }
    }

    private static (float A, float B)[] Round(float radius)
    {
        var points = new (float A, float B)[RoundSides];
        for (int index = 0; index < RoundSides; ++index)
        {
            double angle = 2.0 * Math.PI * (index + 0.5) / RoundSides;
            points[index] = ((float)(radius * Math.Cos(angle)), (float)(radius * Math.Sin(angle)));
        }

        return points;
    }

    private static (float A, float B)[] Rectangle(float minA, float minB, float maxA, float maxB) =>
        new[] { (minA, minB), (maxA, minB), (maxA, maxB), (minA, maxB) };

    private static float Length((float A, float B) p, (float A, float B) q) =>
        (float)Math.Sqrt((q.A - p.A) * (q.A - p.A) + (q.B - p.B) * (q.B - p.B));
}
