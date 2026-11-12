using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.ConcernedLadders.Domain.Excavation;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Tests.Excavation;

/// <summary>#550: the Iron Shovel showed the Iron Pickaxe it is cloned from. It now shows its own shovel, built in code
/// from the donor's own texture sheet: a wooden shaft and T-grip, an iron socket and a pointed iron blade, along the
/// axis the pickaxe's handle used, so the hand holds it where it held the pickaxe.</summary>
public sealed class ShovelModelTests
{
    private const float Epsilon = 1e-5f;

    [Fact]
    public void TheMeshIsClosedFlatShadedAndFacesOutOfEveryPart()
    {
        CellarMesh mesh = ShovelModel.Mesh();
        Assert.Equal(mesh.Vertices.Count, mesh.Normals.Count);
        Assert.Equal(mesh.Vertices.Count, mesh.Uvs.Count);
        Assert.Equal(0, mesh.Triangles.Count % 3);
        Assert.All(mesh.Triangles, index => Assert.InRange(index, 0, mesh.Vertices.Count - 1));
        Assert.All(mesh.Normals, n => Assert.Equal(1f, (float)Math.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z), 4));

        // Each triangle faces away from the centre of the part it belongs to, and agrees with its vertices' normal.
        for (int triangle = 0; triangle < mesh.Triangles.Count / 3; ++triangle)
        {
            ClimbPoint a = mesh.Vertices[mesh.Triangles[triangle * 3]], b = mesh.Vertices[mesh.Triangles[triangle * 3 + 1]], c = mesh.Vertices[mesh.Triangles[triangle * 3 + 2]];
            ClimbPoint n = CellarBlueprint.TriangleNormal(a, b, c);
            ClimbPoint normal = mesh.Normals[mesh.Triangles[triangle * 3]];
            Assert.True(n.X * normal.X + n.Y * normal.Y + n.Z * normal.Z > 0f, "triangle " + triangle + " is wound against its normal");
            ShovelPart part = PartOf(mesh.Triangles[triangle * 3]);
            Assert.All(new[] { mesh.Triangles[triangle * 3 + 1], mesh.Triangles[triangle * 3 + 2] }, index => Assert.Same(part, PartOf(index)));
            ClimbPoint centre = ShovelModel.Centre(part);
            float mx = (a.X + b.X + c.X) / 3f - centre.X, my = (a.Y + b.Y + c.Y) / 3f - centre.Y, mz = (a.Z + b.Z + c.Z) / 3f - centre.Z;
            Assert.True(n.X * mx + n.Y * my + n.Z * mz > 0f, part.Name + " triangle " + triangle + " faces in");
        }
    }

    [Fact]
    public void EveryPartSamplesItsOwnRegionOfTheDonorSheet()
    {
        CellarMesh mesh = ShovelModel.Mesh();
        int vertex = 0;
        foreach (ShovelPart part in ShovelModel.Parts)
        {
            UvRegion region = part.Material == ShovelPartMaterial.Wood ? ShovelModel.Wood : ShovelModel.Iron;
            int count = 2 * part.Polygon.Count + 4 * part.Polygon.Count;
            for (int index = vertex; index < vertex + count; ++index)
            {
                Assert.True(Contains(part, mesh.Vertices[index]), part.Name + " vertex " + index + " outside its part");
                Assert.InRange(mesh.Uvs[index].U, region.U0 - Epsilon, region.U1 + Epsilon);
                Assert.InRange(mesh.Uvs[index].V, region.V0 - Epsilon, region.V1 + Epsilon);
            }

            vertex += count;
        }

        Assert.Equal(mesh.Vertices.Count, vertex);
        // Neither region reaches the sheet's white background, and they do not overlap.
        Assert.False(ShovelModel.Wood.Tiles || ShovelModel.Iron.Tiles);
        Assert.True(ShovelModel.Wood.U1 < ShovelModel.Iron.U0);
    }

    [Fact]
    public void ItIsAShovelAlongThePickaxesHandleAxis()
    {
        CellarMesh mesh = ShovelModel.Mesh();
        // Along +z from the grip behind the hand to the blade's point beyond where the pickaxe's head was (about 0.6 m).
        Assert.Equal(ShovelModel.ShaftStart - 2f * ShovelModel.GripHalfThickness, mesh.Vertices.Min(p => p.Z), 4);
        Assert.Equal(ShovelModel.BladeTip, mesh.Vertices.Max(p => p.Z), 4);
        Assert.InRange(ShovelModel.BladeTip - (ShovelModel.ShaftStart - 2f * ShovelModel.GripHalfThickness), 1.1f, 1.3f);
        // The blade is wider than everything else and flat: it lies in the shaft's plane, thin across it.
        ShovelPart blade = ShovelModel.Parts.Single(part => part.Name == "blade");
        Assert.Equal(ShovelPartMaterial.Iron, blade.Material);
        Assert.Equal(1, blade.Axis);
        Assert.True(2f * ShovelModel.BladeHalfWidth > 4f * ShovelModel.ShaftRadius);
        Assert.True(2f * ShovelModel.BladeHalfThickness < 0.02f);
        // The shaft and grip are wood, the socket and blade iron, and the socket overlaps both shaft and blade.
        Assert.Equal(new[] { "shaft", "grip", "socket", "blade" }, ShovelModel.Parts.Select(part => part.Name));
        Assert.Equal(new[] { ShovelPartMaterial.Wood, ShovelPartMaterial.Wood, ShovelPartMaterial.Iron, ShovelPartMaterial.Iron },
            ShovelModel.Parts.Select(part => part.Material));
        Assert.True(ShovelModel.SocketStart < ShovelModel.ShaftEnd && ShovelModel.BladeStart > ShovelModel.SocketStart);
        Assert.True(ShovelModel.BladeShoulder > ShovelModel.BladeStart && ShovelModel.BladeTip > ShovelModel.BladeShoulder);
    }

    [Fact]
    public void TheItemSwapsThePickaxeModelAndKeepsTheTintOnlyAsAFallback()
    {
        string code = Regex.Replace(File.ReadAllText(Path.Combine(RepoRoot, "src", "ConcernedLadders", "Items", "IronShovelItem.cs")), @"//[^\n]*", " ");
        Assert.Single(Regex.Matches(code, @"if\s*\(\s*!\s*ReplaceModel\s*\(\s*prefab\s*\)\s*\)\s*Tint\s*\(\s*prefab\s*\)\s*;"));
        Assert.Matches(new Regex(@"filter\s*\.\s*sharedMesh\s*=\s*mesh\s*;"), code);
        Assert.Matches(new Regex(@"ShovelModel\s*\.\s*Mesh\s*\(\s*\)"), code);
        // The pickaxe's own child carries the shovel in the attach's space, with the donor's material shared, never
        // copied or edited; nothing is created or destroyed (ExcavationSourceAuditTests refuses Destroy in Items).
        Assert.Matches(new Regex(@"model\s*\.\s*localRotation\s*=\s*Quaternion\s*\.\s*identity\s*;\s*model\s*\.\s*localScale\s*=\s*Vector3\s*\.\s*one\s*;"), code);
        Assert.Matches(new Regex(@"sharedMaterials\s*=\s*new\s*\[\s*\]\s*\{\s*material\s*\}\s*;"), code);
        Assert.DoesNotMatch(new Regex(@"new\s+GameObject\s*\("), code);
        Assert.DoesNotContain("CreatePrimitive", code, StringComparison.Ordinal);
    }

    /// <summary>The part a vertex belongs to: the mesh lists each part's caps and sides in order.</summary>
    private static ShovelPart PartOf(int vertex)
    {
        int first = 0;
        foreach (ShovelPart part in ShovelModel.Parts)
        {
            int count = 6 * part.Polygon.Count;
            if (vertex < first + count) return part;
            first += count;
        }

        throw new InvalidOperationException("Vertex " + vertex + " belongs to no part.");
    }

    private static bool Contains(ShovelPart part, ClimbPoint p)
    {
        float axial = part.Axis == 0 ? p.X : part.Axis == 1 ? p.Y : p.Z;
        float a = part.Axis == 0 ? p.Y : p.X;
        float b = part.Axis == 2 ? p.Y : p.Z;
        if (axial < part.From - Epsilon || axial > part.To + Epsilon) return false;
        return a >= part.Polygon.Min(q => q.A) - Epsilon && a <= part.Polygon.Max(q => q.A) + Epsilon &&
               b >= part.Polygon.Min(q => q.B) - Epsilon && b <= part.Polygon.Max(q => q.B) + Epsilon;
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
