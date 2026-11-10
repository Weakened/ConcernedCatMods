using System;
using System.Collections.Generic;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Domain.Doors;

/// <summary>One box of a cellar door, in its parent's local metres (a leaf's hinge pivot).</summary>
internal readonly struct CellarPart
{
    internal CellarPart(string name, ClimbPoint centre, ClimbPoint size)
    {
        Name = name;
        Centre = centre;
        Size = size;
    }

    internal string Name { get; }

    internal ClimbPoint Centre { get; }

    internal ClimbPoint Size { get; }
}

/// <summary>A texture coordinate of a frame mesh vertex.</summary>
internal readonly struct CellarUv
{
    internal CellarUv(float u, float v)
    {
        U = u;
        V = v;
    }

    internal float U { get; }

    internal float V { get; }
}

/// <summary>One frame prism as a flat-shaded mesh in piece space: four vertices per face (so each face has its own
/// normal), two triangles per face wound so that Unity's front face (<c>Cross(b - a, c - a)</c>) points out of the
/// prism. The piece adapter builds a <c>Mesh</c> from exactly these arrays and uses it for both the renderer and the
/// convex mesh collider, so what is seen is what collides.</summary>
internal sealed class CellarMesh
{
    internal CellarMesh(string name, ClimbPoint[] vertices, ClimbPoint[] normals, CellarUv[] uvs, int[] triangles)
    {
        Name = name;
        Vertices = vertices;
        Normals = normals;
        Uvs = uvs;
        Triangles = triangles;
    }

    internal string Name { get; }

    internal IReadOnlyList<ClimbPoint> Vertices { get; }

    internal IReadOnlyList<ClimbPoint> Normals { get; }

    internal IReadOnlyList<CellarUv> Uvs { get; }

    internal IReadOnlyList<int> Triangles { get; }
}

/// <summary>The sloped cellar door's build plan (CL-005 #486, slice 2): the names of the parts the piece adapter
/// (<c>Pieces/CellarDoorPieceRegistration</c>) builds, the frame prisms as meshes, the slope transform, the two hinge
/// pivots and their leaf boxes, and the hinge marks, all from <see cref="CellarGeometry"/>. The adapter only walks this
/// plan, so the shape is tested here. Design defaults, not in-game observations: every placed-piece row is OWNER
/// GO-AROUND PENDING.
///
/// <para>Hierarchy under the piece root: the bare donor leaf (<see cref="TrapdoorBlueprint.LeafChild"/>) for the
/// hidden native animator; one frame collider per prism (<see cref="CellarPrism.Name"/>); the presentation roots
/// <see cref="TrapdoorBlueprint.NewRoot"/> and, where the donor has a worn material, <see cref="TrapdoorBlueprint.WornRoot"/>,
/// each holding one visual per prism; and <see cref="SlopeRoot"/>, the slope frame, holding the two hinge pivots, each
/// on a kinematic body with its leaf's collider, visual and hinge marks.</para>
///
/// <para>The leaf runtime (slice 3, <c>Runtime/CellarLeaves</c>) turns the two pivots from the native door state; its
/// queries ignore exactly <see cref="QueryIgnores"/>, the piece's own colliders.</para></summary>
internal static class CellarBlueprint
{
    /// <summary>The slope frame: a child of the root at <see cref="CellarGeometry.SlopeOrigin"/>, turned by
    /// <c>Quaternion.Euler(<see cref="SlopeEulerX"/>, 0, 0)</c>, so its local (x, y, z) is the slope frame's (u, w, v).</summary>
    internal const string SlopeRoot = "slope";

    /// <summary>The slope transform's local rotation about x, degrees (<see cref="CellarGeometry.ToPiece"/>'s).</summary>
    internal static float SlopeEulerX => -CellarGeometry.SlopeDegrees;

    internal static ClimbPoint SlopePosition => CellarGeometry.SlopeOrigin;

    /// <summary>A texture repeats once per this many metres of a frame face (the module's width), so the frame's
    /// grain is the same scale on every face. Cosmetic; not compared with the donor's own mapping.</summary>
    internal const float UvMetres = CellarGeometry.Module;

    /// <summary>The hinge marks: two thin plates on each leaf's top at its outer (hinge) edge, so the ghost shows
    /// which sides the leaves swing from. Visual only, no collider. Thinner than <see cref="CellarGeometry.LeafGap"/>,
    /// so an open leaf's marks stay clear of its cheek: a mark point stands at most its thickness outside the hinge.</summary>
    internal const float MarkThickness = 0.008f;

    internal const float MarkWidth = 0.08f;

    /// <summary>How far a mark reaches inboard from the hinge edge.</summary>
    internal const float MarkLength = 0.3f;

    /// <summary>The marks' centres up and down the slope from the pivot, which is half way along the leaf.</summary>
    internal const float MarkAlong = 0.6f;

    /// <summary>The usage tags every cellar door gets, replacing the donor's: the trapdoors' (lead default).</summary>
    internal static IReadOnlyList<string> Usage => TrapdoorBlueprint.Usage;

    internal static readonly IReadOnlyList<CellarLeaf> Leaves = new[] { CellarLeaf.Left, CellarLeaf.Right };

    /// <summary>The frame prisms as meshes, in <see cref="CellarGeometry.Frame"/>'s order and with its names.</summary>
    internal static readonly IReadOnlyList<CellarMesh> FrameMeshes = BuildMeshes();

    /// <summary>A leaf's hinge pivot: a child of <see cref="SlopeRoot"/> at <see cref="HingePosition"/>, closed at
    /// identity rotation, carrying the kinematic body.</summary>
    internal static string Hinge(CellarLeaf leaf) => leaf == CellarLeaf.Left ? "leaf_left_hinge" : "leaf_right_hinge";

    /// <summary>The pivot's position in the slope transform: <see cref="CellarGeometry.HingeInSlope"/>, (u, w, v) as
    /// (x, y, z).</summary>
    internal static ClimbPoint HingePosition(CellarLeaf leaf) => CellarGeometry.HingeInSlope(leaf);

    internal static CellarPart LeafCollider(CellarLeaf leaf) => Leaf(leaf, "_collider");

    /// <summary>Every collider the adapter builds, as a path from the piece root that <c>Transform.Find</c> resolves:
    /// the four frame prisms and the two leaf boxes. The leaf runtime resolves these once to the colliders themselves
    /// and each leaf's occupancy query ignores exactly those six by identity: its own box, the other leaf and the frame,
    /// which the model keeps 1 cm or more (the frame gaps) and 5.7 mm or more (the other leaf, a few degrees open) away, margins
    /// as thin as the query's skin. Nothing else is ignored, so anything another mod or the game parents under the piece
    /// is checked.</summary>
    internal static readonly IReadOnlyList<string> QueryIgnores = BuildQueryIgnores();

    internal static CellarPart LeafVisual(CellarLeaf leaf) => Leaf(leaf, string.Empty);

    /// <summary>The leaf's two hinge marks, in its pivot's space: on the leaf's top, from the hinge edge inboard.</summary>
    internal static IReadOnlyList<CellarPart> HingeMarks(CellarLeaf leaf)
    {
        float inboard = leaf == CellarLeaf.Right ? -1f : 1f;
        string side = leaf == CellarLeaf.Left ? "left" : "right";
        var size = new ClimbPoint(MarkLength, MarkThickness, MarkWidth);
        return new[]
        {
            new CellarPart("hinge_mark_" + side + "_front", new ClimbPoint(inboard * MarkLength / 2f, MarkThickness / 2f, -MarkAlong), size),
            new CellarPart("hinge_mark_" + side + "_rear", new ClimbPoint(inboard * MarkLength / 2f, MarkThickness / 2f, MarkAlong), size),
        };
    }

    /// <summary>A prism's eight corners in piece space: profile point <c>i</c> at <see cref="CellarPrism.MinX"/> is
    /// index <c>i</c>, at <see cref="CellarPrism.MaxX"/> index <c>i + 4</c>.</summary>
    internal static ClimbPoint Corner(CellarPrism prism, int index)
    {
        ProfilePoint point = prism.Profile[index % 4];
        return new ClimbPoint(index < 4 ? prism.MinX : prism.MaxX, point.Y, point.Z);
    }

    /// <summary>The flat-shaded mesh of a prism: the two end caps and the four extruded profile edges.</summary>
    internal static CellarMesh Mesh(CellarPrism prism)
    {
        if (prism.Profile.Count != 4) { throw new ArgumentException("A cellar prism has four profile points.", nameof(prism)); }
        ClimbPoint centroid = Centroid(prism);
        var vertices = new List<ClimbPoint>(24);
        var normals = new List<ClimbPoint>(24);
        var uvs = new List<CellarUv>(24);
        var triangles = new List<int>(36);

        // End caps: the profile at each end, mapped by (z, y).
        Face(new[] { Corner(prism, 0), Corner(prism, 1), Corner(prism, 2), Corner(prism, 3) }, centroid, vertices, normals, uvs, triangles, UvEnd);
        Face(new[] { Corner(prism, 4), Corner(prism, 5), Corner(prism, 6), Corner(prism, 7) }, centroid, vertices, normals, uvs, triangles, UvEnd);
        // Sides: each profile edge extruded along x, mapped by (x, distance along the edge).
        for (int edge = 0; edge < 4; ++edge)
        {
            int next = (edge + 1) % 4;
            ClimbPoint a = Corner(prism, edge), b = Corner(prism, next);
            Func<ClimbPoint, CellarUv> uv = p => new CellarUv(p.X / UvMetres, Distance2(p.Z - a.Z, p.Y - a.Y) / UvMetres);
            Face(new[] { a, b, Corner(prism, next + 4), Corner(prism, edge + 4) }, centroid, vertices, normals, uvs, triangles, uv);
        }

        return new CellarMesh(prism.Name, vertices.ToArray(), normals.ToArray(), uvs.ToArray(), triangles.ToArray());
    }

    /// <summary>The mean of a prism's eight corners: inside it, because it is convex.</summary>
    internal static ClimbPoint Centroid(CellarPrism prism)
    {
        float x = 0f, y = 0f, z = 0f;
        for (int index = 0; index < 8; ++index)
        {
            ClimbPoint corner = Corner(prism, index);
            x += corner.X; y += corner.Y; z += corner.Z;
        }

        return new ClimbPoint(x / 8f, y / 8f, z / 8f);
    }

    /// <summary>Unity's triangle normal direction, <c>Cross(b - a, c - a)</c>, not normalized.</summary>
    internal static ClimbPoint TriangleNormal(ClimbPoint a, ClimbPoint b, ClimbPoint c)
    {
        float ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
        float vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
        return new ClimbPoint(uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx);
    }

    private static CellarPart Leaf(CellarLeaf leaf, string suffix) =>
        new CellarPart((leaf == CellarLeaf.Left ? "leaf_left" : "leaf_right") + suffix, CellarGeometry.LeafCentreInHinge(leaf), CellarGeometry.LeafSize);

    private static CellarUv UvEnd(ClimbPoint p) => new CellarUv(p.Z / UvMetres, p.Y / UvMetres);

    /// <summary>Adds one planar convex quad (corners in order around it) as two triangles facing away from the prism's
    /// centroid, with its unit normal on all four vertices.</summary>
    private static void Face(ClimbPoint[] quad, ClimbPoint centroid, List<ClimbPoint> vertices, List<ClimbPoint> normals,
        List<CellarUv> uvs, List<int> triangles, Func<ClimbPoint, CellarUv> uv)
    {
        ClimbPoint normal = TriangleNormal(quad[0], quad[1], quad[2]);
        float cx = (quad[0].X + quad[1].X + quad[2].X + quad[3].X) / 4f - centroid.X;
        float cy = (quad[0].Y + quad[1].Y + quad[2].Y + quad[3].Y) / 4f - centroid.Y;
        float cz = (quad[0].Z + quad[1].Z + quad[2].Z + quad[3].Z) / 4f - centroid.Z;
        bool outward = normal.X * cx + normal.Y * cy + normal.Z * cz > 0f;
        float length = (float)Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z);
        if (!(length > 0f)) { throw new InvalidOperationException("A degenerate cellar frame face."); }
        float sign = outward ? 1f : -1f;
        var unit = new ClimbPoint(sign * normal.X / length, sign * normal.Y / length, sign * normal.Z / length);
        int first = vertices.Count;
        foreach (ClimbPoint corner in quad)
        {
            vertices.Add(corner);
            normals.Add(unit);
            uvs.Add(uv(corner));
        }

        if (outward)
        {
            triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
        }
        else
        {
            triangles.AddRange(new[] { first, first + 2, first + 1, first, first + 3, first + 2 });
        }
    }

    private static float Distance2(float a, float b) => (float)Math.Sqrt(a * a + b * b);

    private static IReadOnlyList<string> BuildQueryIgnores()
    {
        var paths = new List<string>(CellarGeometry.Frame.Count + Leaves.Count);
        foreach (CellarPrism prism in CellarGeometry.Frame) { paths.Add(prism.Name); }
        foreach (CellarLeaf leaf in Leaves) { paths.Add(SlopeRoot + "/" + Hinge(leaf) + "/" + LeafCollider(leaf).Name); }
        return paths.ToArray();
    }

    private static IReadOnlyList<CellarMesh> BuildMeshes()
    {
        var meshes = new CellarMesh[CellarGeometry.Frame.Count];
        for (int index = 0; index < meshes.Length; ++index) { meshes[index] = Mesh(CellarGeometry.Frame[index]); }
        return meshes;
    }
}
