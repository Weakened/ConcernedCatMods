using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Tests.Doors;

/// <summary>#548: the Roundpole doors' leaves showed the whole fence-gate texture sheet (bark, ring ends and the pale cut
/// face) because every door surface was a built-in cube, whose faces each map the full 0 to 1 square. Seven of the nine
/// leaf textures are such sheets. Every door surface is now a generated mesh whose UVs stay inside its family's pinned
/// region, and the two seamless sheets keep tiling as the cellar frame always did.</summary>
public sealed class DoorSurfacesTests
{
    private const float Epsilon = 1e-5f;

    [Fact]
    public void EveryFamilyHasPinnedRegionsInsideTheTexture()
    {
        foreach (DoorFamily family in DoorFamilies.All)
        {
            DoorSurface surface = DoorSurfaces.For(family);
            foreach (UvRegion region in new[] { surface.Leaf, surface.Frame })
            {
                if (region.Tiles) continue;
                Assert.True(region.U0 >= 0f && region.V0 >= 0f && region.U1 <= 1f && region.V1 <= 1f, family.Donor);
                Assert.True(region.U1 > region.U0 && region.V1 > region.V0, family.Donor);
            }
        }
    }

    [Fact]
    public void OnlyTheSeamlessSheetsTile()
    {
        foreach (DoorFamily family in DoorFamilies.All)
        {
            DoorSurface surface = DoorSurfaces.For(family);
            TrapdoorMaterials names = TrapdoorBlueprint.MaterialsFor(family);
            // door_wood and woodwall are Planks5c_low, metalwall a 32 px tile: seamless. Every other is an atlas.
            Assert.Equal(names.Lid == "door_wood", surface.Leaf.Tiles);
            Assert.Equal(names.Frame == "woodwall" || names.Frame == "metalwall", surface.Frame.Tiles);
        }

        Assert.False(DoorSurfaces.For(DoorFamilies.All.Single(family => family.Donor == "wood_fence_gate")).Leaf.Tiles);
    }

    [Fact]
    public void ARegionIsRefusedOutsideTheUnitSquareOrWithoutArea()
    {
        Assert.Throws<ArgumentException>(() => UvRegion.Rect(-0.1f, 0f, 0.5f, 0.5f));
        Assert.Throws<ArgumentException>(() => UvRegion.Rect(0f, 0f, 1.1f, 0.5f));
        Assert.Throws<ArgumentException>(() => UvRegion.Rect(0.5f, 0f, 0.5f, 0.5f));
        Assert.Throws<ArgumentException>(() => UvRegion.Rect(0f, 0.6f, 0.5f, 0.5f));
    }

    [Fact]
    public void ATilingMeshKeepsTheMetricMappingTheFrameAlwaysHad()
    {
        foreach (CellarPrism prism in CellarGeometry.Frame)
        {
            CellarMesh mesh = CellarBlueprint.Mesh(prism);
            // End caps: (z, y) over the module's width.
            for (int index = 0; index < 8; ++index)
            {
                ClimbPoint p = mesh.Vertices[index];
                Assert.Equal(p.Z / CellarBlueprint.UvMetres, mesh.Uvs[index].U, 5);
                Assert.Equal(p.Y / CellarBlueprint.UvMetres, mesh.Uvs[index].V, 5);
            }

            // Sides: x first.
            for (int index = 8; index < 24; ++index) Assert.Equal(mesh.Vertices[index].X / CellarBlueprint.UvMetres, mesh.Uvs[index].U, 5);
        }
    }

    public static IEnumerable<object[]> AtlasSurfaces()
    {
        foreach (DoorFamily family in DoorFamilies.All)
        {
            DoorSurface surface = DoorSurfaces.For(family);
            if (!surface.Leaf.Tiles) yield return new object[] { family.Donor, "leaf" };
            if (!surface.Frame.Tiles) yield return new object[] { family.Donor, "frame" };
        }
    }

    [Theory]
    [MemberData(nameof(AtlasSurfaces))]
    public void EveryDoorSurfaceStaysInsideItsRegionAndCoversIt(string donor, string role)
    {
        UvRegion region = Region(donor, role);
        foreach (CellarMesh mesh in Meshes(region))
        {
            Assert.All(mesh.Uvs, uv =>
            {
                Assert.InRange(uv.U, region.U0 - Epsilon, region.U1 + Epsilon);
                Assert.InRange(uv.V, region.V0 - Epsilon, region.V1 + Epsilon);
            });
            // Each rectangular face spans its region's corners, so no face shows only a sliver of it.
            for (int face = 0; face < 6; ++face)
            {
                IEnumerable<CellarUv> uvs = mesh.Uvs.Skip(face * 4).Take(4);
                if (!IsRectangle(mesh, face)) continue;
                Assert.Equal(region.U0, uvs.Min(uv => uv.U), 4);
                Assert.Equal(region.U1, uvs.Max(uv => uv.U), 4);
                Assert.Equal(region.V0, uvs.Min(uv => uv.V), 4);
                Assert.Equal(region.V1, uvs.Max(uv => uv.V), 4);
            }
        }
    }

    [Fact]
    public void AFacesLongSideRunsAlongTheRegionsLongSide()
    {
        // The Roundpole bark island is taller (v) than wide (u): a board's 1.79 m length must run along v, as the
        // native gate's poles do.
        UvRegion bark = DoorSurfaces.For(DoorFamilies.All.Single(family => family.Donor == "wood_fence_gate")).Leaf;
        Assert.True(bark.V1 - bark.V0 > bark.U1 - bark.U0);
        CellarMesh board = CellarBlueprint.Mesh(CellarBlueprint.BoxPrism("board", new ClimbPoint(0f, 0f, 0f), new ClimbPoint(0.15f, 0.03f, 1.79f)), bark);
        int top = TopFace(board);
        List<int> corners = Enumerable.Range(top * 4, 4).ToList();
        int far = corners.OrderByDescending(index => board.Vertices[index].Z).First();
        int near = corners.OrderBy(index => board.Vertices[index].Z).First();
        Assert.Equal(bark.V1 - bark.V0, Math.Abs(board.Uvs[far].V - board.Uvs[near].V), 4);

        // And a wide, short region turns it the other way.
        UvRegion wide = UvRegion.Rect(0.1f, 0.4f, 0.9f, 0.5f);
        CellarMesh across = CellarBlueprint.Mesh(CellarBlueprint.BoxPrism("board", new ClimbPoint(0f, 0f, 0f), new ClimbPoint(0.15f, 0.03f, 1.79f)), wide);
        Assert.Equal(wide.U1 - wide.U0, Math.Abs(across.Uvs[far].U - across.Uvs[near].U), 4);
    }

    [Fact]
    public void ABoxPrismIsTheBoxAndItsMeshIsClosedAndOutward()
    {
        var centre = new ClimbPoint(0.2f, -0.03f, 0.4f);
        var size = new ClimbPoint(0.835f, 0.06f, 1.7924f);
        CellarPrism prism = CellarBlueprint.BoxPrism("leaf", centre, size);
        Assert.Equal(centre.X - size.X / 2f, prism.MinX, 5);
        Assert.Equal(centre.X + size.X / 2f, prism.MaxX, 5);
        Assert.Equal(centre.Z - size.Z / 2f, prism.Profile.Min(point => point.Z), 5);
        Assert.Equal(centre.Y + size.Y / 2f, prism.Profile.Max(point => point.Y), 5);
        CellarMesh mesh = CellarBlueprint.Mesh(prism, DoorSurfaces.For(DoorFamilies.All[2]).Leaf);
        Assert.Equal(24, mesh.Vertices.Count);
        Assert.Equal(36, mesh.Triangles.Count);
        ClimbPoint middle = CellarBlueprint.Centroid(prism);
        for (int triangle = 0; triangle < 12; ++triangle)
        {
            ClimbPoint a = mesh.Vertices[mesh.Triangles[triangle * 3]], b = mesh.Vertices[mesh.Triangles[triangle * 3 + 1]], c = mesh.Vertices[mesh.Triangles[triangle * 3 + 2]];
            ClimbPoint n = CellarBlueprint.TriangleNormal(a, b, c);
            float outward = n.X * (a.X - middle.X) + n.Y * (a.Y - middle.Y) + n.Z * (a.Z - middle.Z);
            Assert.True(outward > 0f, "triangle " + triangle + " faces in");
        }
    }

    [Fact]
    public void NoDoorPieceDrawsTheBuiltInCube()
    {
        foreach (string file in new[] { "Pieces/DoorPieceKit.cs", "Pieces/TrapdoorPieceRegistration.cs", "Pieces/CellarDoorPieceRegistration.cs" })
        {
            string code = File.ReadAllText(Path.Combine(RepoRoot, "src", "ConcernedLadders", file));
            Assert.DoesNotContain("CreatePrimitive", code, StringComparison.Ordinal);
            Assert.DoesNotContain("localScale", code, StringComparison.Ordinal);
        }

        // Every visual box and frame visual is given its family's region; the surface is chosen once per family.
        foreach (string file in new[] { "Pieces/TrapdoorPieceRegistration.cs", "Pieces/CellarDoorPieceRegistration.cs" })
        {
            string code = File.ReadAllText(Path.Combine(RepoRoot, "src", "ConcernedLadders", file));
            Assert.Single(Regex.Matches(code, @"DoorSurface\s+surface\s*=\s*DoorSurfaces\s*\.\s*For\s*\(\s*family\s*\)\s*;"));
            Assert.Equal(2, Regex.Matches(code, @"FrameRoot\s*\([^;]*surface\s*\.\s*Frame\s*,\s*layer\s*\)").Count);
        }
    }

    [Fact]
    public void TheAssetRecordListsEveryPinnedRegion()
    {
        string doc = Regex.Replace(File.ReadAllText(Path.Combine(RepoRoot, "docs", "mods", "concerned-ladders", "DOOR_ASSET_SOURCES.md")), @"[ \t]+", " ");
        foreach (DoorFamily family in DoorFamilies.All)
        {
            DoorSurface surface = DoorSurfaces.For(family);
            string row = doc.Split('\n').SingleOrDefault(line => line.StartsWith("| `" + family.Donor + "` | ", StringComparison.Ordinal) && line.Contains("region", StringComparison.Ordinal))
                         ?? throw new Xunit.Sdk.XunitException("DOOR_ASSET_SOURCES.md has no region row for " + family.Donor);
            Assert.Contains("| " + Text(surface.Leaf) + " |", row, StringComparison.Ordinal);
            Assert.Contains("| " + Text(surface.Frame) + " |", row, StringComparison.Ordinal);
        }
    }

    private static string Text(UvRegion region) =>
        region.Tiles
            ? "tiles"
            : string.Format(CultureInfo.InvariantCulture, "u {0:0.0###}-{1:0.0###}, v {2:0.0###}-{3:0.0###}", region.U0, region.U1, region.V0, region.V1);

    private static UvRegion Region(string donor, string role)
    {
        DoorSurface surface = DoorSurfaces.For(DoorFamilies.All.Single(family => family.Donor == donor));
        return role == "leaf" ? surface.Leaf : surface.Frame;
    }

    /// <summary>The meshes a surface is drawn with: every trapdoor and leaf box, the gate's boards and rails, the
    /// marks and straps, and the four cellar frame prisms.</summary>
    private static IEnumerable<CellarMesh> Meshes(UvRegion region)
    {
        var boxes = new List<(ClimbPoint Centre, ClimbPoint Size)>();
        boxes.AddRange(TrapdoorBlueprint.FrameVisuals.Select(part => (part.Centre, part.Size)));
        boxes.AddRange(TrapdoorBlueprint.HingeStraps.Select(part => (part.Centre, part.Size)));
        foreach (LeafLook look in new[] { LeafLook.Solid, LeafLook.Gate })
        {
            boxes.AddRange(LeafPattern.Parts("lid", TrapdoorBlueprint.LidVisual.Centre, TrapdoorBlueprint.LidVisual.Size, look).Select(part => (part.Centre, part.Size)));
            CellarPart leaf = CellarBlueprint.LeafVisual(CellarLeaf.Left);
            boxes.AddRange(LeafPattern.Parts("leaf", leaf.Centre, leaf.Size, look).Select(part => (part.Centre, part.Size)));
        }

        boxes.AddRange(CellarBlueprint.HingeMarks(CellarLeaf.Left).Select(part => (part.Centre, part.Size)));
        foreach ((ClimbPoint centre, ClimbPoint size) in boxes) yield return CellarBlueprint.Mesh(CellarBlueprint.BoxPrism("box", centre, size), region);
        foreach (CellarPrism prism in CellarGeometry.Frame) yield return CellarBlueprint.Mesh(prism, region);
    }

    private static bool IsRectangle(CellarMesh mesh, int face)
    {
        ClimbPoint a = mesh.Vertices[face * 4], b = mesh.Vertices[face * 4 + 1], c = mesh.Vertices[face * 4 + 2];
        float dot = (b.X - a.X) * (c.X - b.X) + (b.Y - a.Y) * (c.Y - b.Y) + (b.Z - a.Z) * (c.Z - b.Z);
        return Math.Abs(dot) < 1e-5f;
    }

    private static int TopFace(CellarMesh mesh)
    {
        for (int face = 0; face < 6; ++face)
        {
            if (mesh.Normals[face * 4].Y > 0.99f) return face;
        }

        throw new InvalidOperationException("No top face.");
    }

    private static string RepoRoot
    {
        get
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ConcernedCatMods.sln"))) { directory = directory.Parent; }
            return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }
}
