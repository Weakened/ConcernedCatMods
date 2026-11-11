using System.Text.RegularExpressions;
using TheConcernedCat.ConcernedLadders.Domain.Basement;
using TheConcernedCat.ConcernedLadders.Domain.Railings;

namespace TheConcernedCat.ConcernedLadders.Tests.Basement;

/// <summary>#568: the basement wall pieces line a dug hole. A dug side cannot be vertical (one sloping lattice cell, an engine
/// limit), so the wall stands on the floor's edge with its inner face on it and a lip at ground level covers the slope out to
/// the untouched ground. These pin that shape, the sizes matching the shovel's cubes, names, costs and the switch. In game:
/// OWNER GO-AROUND PENDING.</summary>
public sealed class BasementWallTests
{
    private const float Epsilon = 1e-4f;

    public static IEnumerable<object[]> Walls() =>
        BasementWalls.All.Where(piece => !piece.IsCorner).Select(piece => new object[] { piece.Prefab });

    [Fact]
    public void EveryCubeDepthAndBothMaterialsHaveWallsAndACornerCap()
    {
        Assert.Equal(14, BasementWalls.All.Count);
        Assert.Equal(new[] { 1, 2, 4 }, BasementWalls.Heights);
        foreach (BasementMaterial material in new[] { BasementMaterial.Wood, BasementMaterial.Stone })
        {
            var mine = BasementWalls.All.Where(piece => piece.Material == material).ToList();
            Assert.Single(mine, piece => piece.IsCorner);
            foreach (int height in BasementWalls.Heights)
                foreach (int width in BasementWalls.Widths)
                    Assert.Single(mine, piece => !piece.IsCorner && piece.Width == width && piece.Height == height);
        }

        Assert.Equal(BasementWalls.All.Count, BasementWalls.All.Select(piece => piece.Prefab).Distinct().Count());
        Assert.Equal(BasementWalls.All.Count, BasementWalls.All.Select(piece => piece.Token).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Walls))]
    public void TheWallStandsOnTheFloorEdgeAndItsLipCoversTheSlopingCell(string prefab)
    {
        BasementPiece piece = Find(prefab);
        BasementPart panel = Part(piece, "panel"), lip = Part(piece, "lip");
        // Panel: inner face on the floor's edge (z 0), from the floor (y 0) up to the lip, the wall's full width.
        Assert.InRange(Min(panel, p => p.Z, s => s.Z), -Epsilon, Epsilon);
        Assert.InRange(Min(panel, p => p.Y, s => s.Y), -Epsilon, Epsilon);
        Assert.Equal(piece.Width, panel.Size.X, 4);
        // Lip: its top at ground level (y = height), reaching from the floor's edge out over the whole sloping cell.
        Assert.Equal(piece.Height, Max(lip, p => p.Y, s => s.Y), 4);
        Assert.InRange(Min(lip, p => p.Z, s => s.Z), -Epsilon, Epsilon);
        Assert.Equal(BasementPiece.LipReach, Max(lip, p => p.Z, s => s.Z), 4);
        Assert.Equal(piece.Width, lip.Size.X, 4);
        // The two meet without a gap or an overlap.
        Assert.Equal(Max(panel, p => p.Y, s => s.Y), Min(lip, p => p.Y, s => s.Y), 4);
        Assert.Equal(BasementPiece.LipReach, 1f);
    }

    [Theory]
    [MemberData(nameof(Walls))]
    public void TheWallSnapsAtItsFloorCornersItsTopCornersAndItsLipsOuterEdge(string prefab)
    {
        BasementPiece piece = Find(prefab);
        IReadOnlyList<Point3> snaps = piece.Snaps();
        float half = piece.Width / 2f;
        Assert.Equal(6, snaps.Count);
        foreach (float x in new[] { -half, half })
        {
            Assert.Contains(snaps, s => Near(s, x, 0f, 0f));
            Assert.Contains(snaps, s => Near(s, x, piece.Height, 0f));
            Assert.Contains(snaps, s => Near(s, x, piece.Height, BasementPiece.LipReach));
        }
    }

    [Fact]
    public void TheCornerCapFillsTheDiagonalCellBetweenTwoLips()
    {
        foreach (BasementPiece cap in BasementWalls.All.Where(piece => piece.IsCorner))
        {
            BasementPart slab = Assert.Single(cap.Parts());
            Assert.InRange(Min(slab, p => p.X, s => s.X), -Epsilon, Epsilon);
            Assert.InRange(Min(slab, p => p.Z, s => s.Z), -Epsilon, Epsilon);
            Assert.Equal(BasementPiece.LipReach, Max(slab, p => p.X, s => s.X), 4);
            Assert.Equal(BasementPiece.LipReach, Max(slab, p => p.Z, s => s.Z), 4);
            // Its top at ground level, as thick as a lip.
            Assert.InRange(Max(slab, p => p.Y, s => s.Y), -Epsilon, Epsilon);
            Assert.Equal(BasementPiece.LipThickness, slab.Size.Y, 4);
            Assert.Equal(4, cap.Snaps().Count);
            Assert.All(cap.Snaps(), s => Assert.Equal(0f, s.Y));
        }
    }

    [Fact]
    public void NamesCostsAndStationsFollowTheMaterial()
    {
        BasementPiece wood = BasementWalls.All.Single(piece => piece.Material == BasementMaterial.Wood && piece.Width == 2 && piece.Height == 4);
        Assert.Equal("tcc_basement_wood_2x4", wood.Prefab);
        Assert.Equal("cl_basement_wood_2x4", wood.Token);
        Assert.Equal("Wooden Basement Wall 2x4", wood.English);
        Assert.Equal(("Wood", "piece_workbench", "woodwall", "woodwall"), (wood.Item, wood.Station, wood.Donor, wood.MaterialName));
        BasementPiece cap = BasementWalls.All.Single(piece => piece.Material == BasementMaterial.Stone && piece.IsCorner);
        Assert.Equal("tcc_basement_stone_corner", cap.Prefab);
        Assert.Equal("Stone Basement Corner Cap", cap.English);
        Assert.Equal(("Stone", "piece_stonecutter", "stone_wall_2x1", "stone_mat"), (cap.Item, cap.Station, cap.Donor, cap.MaterialName));
        Assert.Equal(1, cap.Amount);
        // One unit per two square metres of panel and lip: a 1x1 wall costs 1, a 2x4 wall 5.
        Assert.Equal(1, BasementWalls.All.Single(piece => piece.Material == BasementMaterial.Wood && piece.Width == 1 && piece.Height == 1).Amount);
        Assert.Equal(5, wood.Amount);
        Dictionary<string, string> english = BasementWalls.English();
        Assert.All(BasementWalls.All, piece => Assert.Equal(piece.English, english[piece.Token]));
        Assert.True(english.ContainsKey(BasementWalls.DescriptionToken));
    }

    [Fact]
    public void BasementWallsExistOnlyBehindTheirOwnSwitch()
    {
        Assert.False(BasementSettings.EnabledByDefault);
        string plugin = File.ReadAllText(Path.Combine(RepoRoot, "src", "ConcernedLadders", "Plugin.cs"));
        Assert.Matches(new Regex(@"Config\.Bind\s*\(\s*BasementSettings\.Section\s*,\s*BasementSettings\.Key\s*,\s*BasementSettings\.EnabledByDefault\s*,"), plugin);
        Assert.Single(Regex.Matches(plugin, @"new\s+BasementPieceRegistration\s*\("));
        Assert.Matches(new Regex(@"if\s*\(\s*_basementEnabled\.Value\s*\)\s*\{\s*_basement\s*=\s*new\s+BasementPieceRegistration\s*\("), plugin);
        // Code only: the class's own documentation names what it does not do.
        string registration = Regex.Replace(File.ReadAllText(Path.Combine(RepoRoot, "src", "ConcernedLadders", "Pieces", "BasementPieceRegistration.cs")),
            @"/\*.*?\*/|//[^\n]*", " ", RegexOptions.Singleline);
        Assert.DoesNotMatch(new Regex(@"\bZDO\b|GetZDO|ZRoutedRpc|InvokeRPC|ClaimOwnership|SetOwner|\bInstantiate\s*\(|ZNetScene"), registration);
    }

    private static BasementPiece Find(string prefab) => BasementWalls.All.Single(piece => piece.Prefab == prefab);

    private static BasementPart Part(BasementPiece piece, string name) => piece.Parts().Single(part => part.Name == name);

    private static float Min(BasementPart part, Func<Point3, float> axis, Func<Point3, float> size) => axis(part.Centre) - size(part.Size) / 2f;

    private static float Max(BasementPart part, Func<Point3, float> axis, Func<Point3, float> size) => axis(part.Centre) + size(part.Size) / 2f;

    private static bool Near(Point3 p, float x, float y, float z) =>
        Math.Abs(p.X - x) < Epsilon && Math.Abs(p.Y - y) < Epsilon && Math.Abs(p.Z - z) < Epsilon;

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
