using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Domain.Railings;

/// <summary>Builds a railing piece's one mesh from its parts (#556): every bar a square-section prism along its own axis
/// (twisted bars as rings turned a little further at each step, so the faces spiral), every knuckle a small octahedron and
/// every finial a pointed one. Flat-shaded, every face wound outward from its part's axis, with tiling UVs (one unit per
/// <see cref="UvRegion.UvMetres"/>) for the tier's borrowed seamless material, as the ladders use it. Game-free so the mesh
/// is tested: closed faces, outward winding, inside the railing's bounds.</summary>
internal static class RailingMesh
{
    /// <summary>A twisted bar gets one ring per this many degrees of twist (at least <see cref="MinimumTwistSegments"/>).</summary>
    internal const float DegreesPerSegment = 45f;

    internal const int MinimumTwistSegments = 6;

    internal static CellarMesh Build(string name, IReadOnlyList<RailingPart> parts)
    {
        var builder = new Builder();
        foreach (RailingPart part in parts)
        {
            switch (part.Kind)
            {
                case RailingPartKind.Knuckle: builder.Octahedron(part.From, part.Width * 0.5f, 1f); break;
                case RailingPartKind.Finial: builder.Octahedron(part.From, part.Width * 0.5f, 1.8f); break;
                default: builder.Bar(part.From, part.To, part.Width, part.TwistDegrees); break;
            }
        }

        return builder.ToMesh(name);
    }

    /// <summary>Rings a bar is built from: one for a plain bar, more for a twisted one.</summary>
    internal static int Segments(float twistDegrees) =>
        twistDegrees <= 0f ? 1 : Math.Max(MinimumTwistSegments, (int)Math.Ceiling(twistDegrees / DegreesPerSegment));

    private sealed class Builder
    {
        private readonly List<ClimbPoint> _vertices = new List<ClimbPoint>();
        private readonly List<ClimbPoint> _normals = new List<ClimbPoint>();
        private readonly List<CellarUv> _uvs = new List<CellarUv>();
        private readonly List<int> _triangles = new List<int>();

        internal void Bar(Point3 from, Point3 to, float width, float twistDegrees)
        {
            Point3 axis = to - from;
            float length = axis.Length;
            if (!(length > 0f) || !(width > 0f)) throw new ArgumentException("A railing bar has a length and a width.");
            Point3 d = axis.Normalized();
            Point3 reference = Math.Abs(d.Y) > 0.9f ? new Point3(0f, 0f, 1f) : new Point3(0f, 1f, 0f);
            Point3 side = Point3.Cross(reference, d).Normalized();
            Point3 up = Point3.Cross(d, side).Normalized();
            int segments = Segments(twistDegrees);
            float half = width * 0.5f;
            var rings = new Point3[segments + 1][];
            for (int ring = 0; ring <= segments; ring++)
            {
                float t = ring / (float)segments;
                double angle = twistDegrees * t * Math.PI / 180.0;
                float c = (float)Math.Cos(angle), s = (float)Math.Sin(angle);
                Point3 rotatedSide = side * c + up * s, rotatedUp = up * c - side * s;
                Point3 centre = from + axis * t;
                rings[ring] = new[]
                {
                    centre + rotatedSide * half + rotatedUp * half,
                    centre - rotatedSide * half + rotatedUp * half,
                    centre - rotatedSide * half - rotatedUp * half,
                    centre + rotatedSide * half - rotatedUp * half,
                };
            }

            for (int ring = 0; ring < segments; ring++)
            {
                Point3 middle = from + axis * ((ring + 0.5f) / segments);
                float along0 = length * ring / segments, along1 = length * (ring + 1) / segments;
                for (int edge = 0; edge < 4; edge++)
                {
                    int next = (edge + 1) % 4;
                    Quad(new[] { rings[ring][edge], rings[ring][next], rings[ring + 1][next], rings[ring + 1][edge] }, middle,
                        new[] { Uv(along0, edge * width), Uv(along0, (edge + 1) * width), Uv(along1, (edge + 1) * width), Uv(along1, edge * width) });
                }
            }

            Quad(rings[0], from + axis * 0.5f, new[] { Uv(0f, 0f), Uv(width, 0f), Uv(width, width), Uv(0f, width) });
            Quad(rings[segments], from + axis * 0.5f, new[] { Uv(0f, 0f), Uv(width, 0f), Uv(width, width), Uv(0f, width) });
        }

        /// <summary>An octahedron of radius <paramref name="radius"/> at <paramref name="centre"/>, its top point
        /// <paramref name="stretch"/> times as far up (a pointed finial when above one).</summary>
        internal void Octahedron(Point3 centre, float radius, float stretch)
        {
            if (!(radius > 0f)) throw new ArgumentException("A railing knuckle has a size.");
            Point3 top = centre + new Point3(0f, radius * stretch, 0f), bottom = centre - new Point3(0f, radius, 0f);
            Point3[] ring =
            {
                centre + new Point3(radius, 0f, 0f), centre + new Point3(0f, 0f, radius),
                centre - new Point3(radius, 0f, 0f), centre - new Point3(0f, 0f, radius),
            };
            Point3 inside = centre + new Point3(0f, radius * (stretch - 1f) * 0.25f, 0f);
            for (int i = 0; i < 4; i++)
            {
                Point3 a = ring[i], b = ring[(i + 1) % 4];
                Triangle(new[] { a, b, top }, inside);
                Triangle(new[] { b, a, bottom }, inside);
            }
        }

        private static CellarUv Uv(float u, float v) => new CellarUv(u / UvRegion.UvMetres, v / UvRegion.UvMetres);

        private void Quad(Point3[] quad, Point3 inside, CellarUv[] uv)
        {
            int first = Add(quad, inside, uv, out bool outward);
            if (outward) _triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            else _triangles.AddRange(new[] { first, first + 2, first + 1, first, first + 3, first + 2 });
        }

        private void Triangle(Point3[] triangle, Point3 inside)
        {
            var uv = new CellarUv[3];
            for (int i = 0; i < 3; i++) uv[i] = Uv(triangle[i].X + triangle[i].Z, triangle[i].Y);
            int first = Add(triangle, inside, uv, out bool outward);
            if (outward) _triangles.AddRange(new[] { first, first + 1, first + 2 });
            else _triangles.AddRange(new[] { first, first + 2, first + 1 });
        }

        /// <summary>Adds a planar face's vertices with its outward unit normal; reports whether its listed order already
        /// winds outward (Unity's front face, <c>Cross(b - a, c - a)</c>).</summary>
        private int Add(Point3[] face, Point3 inside, CellarUv[] uv, out bool outward)
        {
            Point3 normal = Point3.Cross(face[1] - face[0], face[2] - face[0]);
            Point3 centre = new Point3(0f, 0f, 0f);
            foreach (Point3 p in face) centre = centre + p;
            centre = centre * (1f / face.Length);
            outward = Point3.Dot(normal, centre - inside) > 0f;
            Point3 unit = (outward ? normal : normal * -1f).Normalized();
            if (!(unit.Length > 0.5f)) throw new InvalidOperationException("A degenerate railing face.");
            int first = _vertices.Count;
            for (int i = 0; i < face.Length; i++)
            {
                _vertices.Add(new ClimbPoint(face[i].X, face[i].Y, face[i].Z));
                _normals.Add(new ClimbPoint(unit.X, unit.Y, unit.Z));
                _uvs.Add(uv[i]);
            }

            return first;
        }

        internal CellarMesh ToMesh(string name) =>
            new CellarMesh(name, _vertices.ToArray(), _normals.ToArray(), _uvs.ToArray(), _triangles.ToArray());
    }
}
