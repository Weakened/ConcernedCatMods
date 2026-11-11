using System.Text.RegularExpressions;
using TheConcernedCat.ConcernedLadders.Domain.Excavation;

namespace TheConcernedCat.ConcernedLadders.Tests.Excavation;

/// <summary>#577, earth fill: which cells around a dug hole get an earth block, how tall, which existing blocks a new hole
/// takes out, and that the setting and the prefab are wired as documented. In game: OWNER GO-AROUND PENDING.</summary>
public sealed class EarthFillTests
{
    private const float Ground = 50f;

    [Fact]
    public void TheRingIsEveryCellTouchingTheFloorButNotUnderIt()
    {
        DigFootprint hole = DigFootprint.FromCorners(0, 0, 2, 2, 2);
        var ring = EarthFill.Ring(hole).ToList();
        // A 2x2 floor is 4 cells; the 4x4 cells around and including it leave 12 in the ring.
        Assert.Equal(12, ring.Count);
        Assert.Equal(ring.Count, ring.Distinct().Count());
        Assert.All(ring, cell => Assert.False(EarthFill.Inside(hole, cell)));
        Assert.Contains(new FillCell(-1, -1), ring); // a corner cell
        Assert.Contains(new FillCell(2, 0), ring); // a side cell, east
        Assert.DoesNotContain(new FillCell(1, 1), ring); // under the floor
        Assert.True(EarthFill.Inside(hole, new FillCell(1, 1)));
        Assert.False(EarthFill.Inside(hole, new FillCell(2, 1)));
        // A 4x4 hole: 6x6 less 4x4.
        Assert.Equal(20, EarthFill.Ring(DigFootprint.FromCorners(0, 0, 4, 4, 4)).Count());
    }

    [Fact]
    public void OnFlatGroundEveryRingCellIsFilledFromTheFloorToTheGround()
    {
        DigFootprint hole = DigFootprint.FromCorners(10, 20, 12, 22, 2);
        var blocks = new List<FillBlock>();
        EarthFill.Plan(hole, Ground - 2f, (x, z) => Ground, new HashSet<FillCell>(), blocks);
        Assert.Equal(12, blocks.Count);
        Assert.All(blocks, block =>
        {
            Assert.Equal(Ground - 2f, block.Bottom);
            Assert.Equal(Ground, block.Top);
            Assert.Equal(2f, block.Height);
        });
    }

    [Fact]
    public void ABlockReachesTheHighestOuterCornerSoNoGroundShowsThroughItsTop()
    {
        DigFootprint hole = DigFootprint.FromCorners(0, 0, 2, 2, 2);
        // The ground rises 0.1 m per metre east; floor vertices would read lower, but are never asked.
        float Height(int x, int z) => Ground + 0.1f * x;
        var blocks = new List<FillBlock>();
        EarthFill.Plan(hole, Ground - 2f, (x, z) => EarthFillTestsFloor(hole, x, z) ? Ground - 2f : Height(x, z), new HashSet<FillCell>(), blocks);
        // The west side cell (-1, 0): its outer corners are x -1, so its top is the ground there.
        FillBlock west = blocks.Single(b => b.Cell.Equals(new FillCell(-1, 0)));
        Assert.Equal(Height(-1, 0), west.Top, 4);
        // The east side cell (2, 0): outer corners at x 3.
        FillBlock east = blocks.Single(b => b.Cell.Equals(new FillCell(2, 0)));
        Assert.Equal(Height(3, 0), east.Top, 4);
        // The north side cell (0, 2): outer corners at x 0 and x 1 - the higher one.
        FillBlock north = blocks.Single(b => b.Cell.Equals(new FillCell(0, 2)));
        Assert.Equal(Height(1, 3), north.Top, 4);
        // A corner cell (2, 2): three outer corners, the highest at x 3.
        FillBlock corner = blocks.Single(b => b.Cell.Equals(new FillCell(2, 2)));
        Assert.Equal(Height(3, 3), corner.Top, 4);
    }

    [Fact]
    public void ACellBesideAnotherHoleOfTheSameDepthGetsNoBlockAndAShallowerOneGetsAStep()
    {
        DigFootprint hole = DigFootprint.FromCorners(4, 0, 6, 2, 2);
        // An earlier hole west of it, x 0..4: its floor at the same level as the new one's.
        float? SameDepth(int x, int z) => x <= 4 ? Ground - 2f : Ground;
        var blocks = new List<FillBlock>();
        EarthFill.Plan(hole, Ground - 2f, SameDepth, new HashSet<FillCell>(), blocks);
        Assert.DoesNotContain(blocks, b => b.Cell.X == 3 && b.Cell.Z >= 0 && b.Cell.Z < 2); // the shared side stays open
        Assert.Contains(blocks, b => b.Cell.Equals(new FillCell(6, 0))); // the far side is filled
        // The earlier hole only 1 m deep: the shared side becomes a 1 m step up to its floor.
        float? Shallower(int x, int z) => x <= 4 ? Ground - 1f : Ground;
        EarthFill.Plan(hole, Ground - 2f, Shallower, new HashSet<FillCell>(), blocks);
        FillBlock step = blocks.Single(b => b.Cell.Equals(new FillCell(3, 0)));
        Assert.Equal(1f, step.Height, 4);
        // A rise under the minimum is not filled.
        EarthFill.Plan(hole, Ground - 2f, (x, z) => Ground - 2f + EarthFill.MinimumHeight * 0.5f, new HashSet<FillCell>(), blocks);
        Assert.Empty(blocks);
    }

    [Fact]
    public void ACellAlreadyFilledOrUnreadableGetsNoBlock()
    {
        DigFootprint hole = DigFootprint.FromCorners(0, 0, 2, 2, 2);
        var occupied = new HashSet<FillCell> { new FillCell(-1, 0), new FillCell(2, 2) };
        var blocks = new List<FillBlock>();
        EarthFill.Plan(hole, Ground - 2f, (x, z) => Ground, occupied, blocks);
        Assert.Equal(10, blocks.Count);
        Assert.DoesNotContain(blocks, b => occupied.Contains(b.Cell));
        // A corner nobody can read (an unloaded tile, say): the cells that need it get no block, the rest still do.
        EarthFill.Plan(hole, Ground - 2f, (x, z) => x == 3 && z == 0 ? null : Ground, new HashSet<FillCell>(), blocks);
        Assert.DoesNotContain(blocks, b => b.Cell.Equals(new FillCell(2, 0)) || b.Cell.Equals(new FillCell(2, -1)));
        Assert.Equal(10, blocks.Count);
        // A floor that is not a number fills nothing.
        EarthFill.Plan(hole, float.NaN, (x, z) => Ground, new HashSet<FillCell>(), blocks);
        Assert.Empty(blocks);
    }

    [Fact]
    public void ANewHoleTakesOutTheBlocksOnItsFloorAndThoseADeeperDigWouldLeaveHanging()
    {
        DigFootprint hole = DigFootprint.FromCorners(0, 0, 2, 2, 2);
        float floor = Ground - 2f;
        // Inside the new floor: always taken out, whatever its depth.
        Assert.True(EarthFill.TakesOut(hole, floor, new FillBlock(new FillCell(1, 1), Ground - 4f, Ground)));
        Assert.True(EarthFill.TakesOut(hole, floor, new FillBlock(new FillCell(0, 0), floor, Ground)));
        // In the ring on the same floor (a neighbour's block in a shared cell): kept, and its cell counts as filled.
        Assert.False(EarthFill.TakesOut(hole, floor, new FillBlock(new FillCell(-1, 0), floor, Ground)));
        // In the ring on a deeper floor (an older, deeper hole next door): kept.
        Assert.False(EarthFill.TakesOut(hole, floor, new FillBlock(new FillCell(-1, 0), Ground - 4f, Ground)));
        // In the ring on a higher floor (this spot was 1 m deep and is now dug to 2 m): taken out, so it is filled again
        // from the new floor and no slope shows under it.
        Assert.True(EarthFill.TakesOut(hole, floor, new FillBlock(new FillCell(-1, 0), Ground - 1f, Ground)));
        Assert.False(EarthFill.TakesOut(hole, floor, new FillBlock(new FillCell(-1, 0), floor + EarthFill.MatchTolerance * 0.5f, Ground)));
    }

    [Theory]
    [InlineData(-1, 0, 2, -2, 0, 0, 2)] // the west side block: a 2x2 flush west
    [InlineData(2, 1, 2, 2, 0, 4, 2)] // the east side block: a 2x2 flush east, in the same row
    [InlineData(0, 2, 2, 0, 2, 2, 4)] // the north side block: flush north
    [InlineData(0, -1, 2, 0, -2, 2, 0)] // the south side block: flush south
    [InlineData(2, 2, 2, 2, 2, 4, 4)] // a corner block: diagonal
    [InlineData(-1, 1, 4, -4, 0, 0, 4)] // a 4x4 aimed at the west side: flush west, lined up with the hole's south edge
    [InlineData(-1, 1, 1, -1, 1, 0, 2)] // a 1x1 takes exactly the block's own cell
    public void AimingAtABlockDigsSidewaysFlushAgainstItsHoleAndTakesTheBlockOut(int cellX, int cellZ, int side, int minX, int minZ, int maxX, int maxZ)
    {
        // #579, owner: "we should dig sideways as well, imagine minecraft". The hole the block belongs to: x 0..2, z 0..2.
        DigFootprint hole = DigFootprint.FromCorners(0, 0, 2, 2, 2);
        var block = new FillBlock(new FillCell(cellX, cellZ), Ground - 2f, Ground);
        (int x, int z) = EarthFill.AimVertex(block);
        DigFootprint next = DigSnap.Place(x, z, new DigSize(side, 2), new List<DigFootprint> { hole });
        Assert.Equal((minX, minZ, maxX, maxZ), (next.MinX, next.MinZ, next.MaxX, next.MaxZ));
        // The new hole never overlaps the old floor, and covers the block's cell, so the dig takes the block out.
        Assert.False(next.MinX < hole.MaxX && next.MaxX > hole.MinX && next.MinZ < hole.MaxZ && next.MaxZ > hole.MinZ);
        Assert.True(EarthFill.Inside(next, block.Cell));
        Assert.True(EarthFill.TakesOut(next, Ground - 2f, block));
    }

    [Fact]
    public void TheBlockWearsTheTerrainsCliffRockTopIncludedOnAUvMappedTop()
    {
        // From the decoded contact sheet of terrain_d_array (cl-ladders-devtools/out/terrain/terrain_d_array_sheet_579.png).
        Assert.Equal(4, EarthFill.TerrainCliffSlice);
        Assert.InRange(EarthFill.TopShare, 0.001f, 0.05f);
        string registration = Regex.Replace(File.ReadAllText(Path.Combine(RepoRoot, "src", "ConcernedLadders", "Pieces", "EarthFillPieceRegistration.cs")),
            @"/\*.*?\*/|//[^\n]*", " ", RegexOptions.Singleline);
        // The look is copied on the GPU from the live terrain's own material, never written back to it.
        Assert.Matches(new Regex(@"Graphics\.CopyTexture\s*\(\s*array\s*,\s*slice\s*,\s*mip\s*,\s*texture\s*,\s*0\s*,\s*mip\s*\)"), registration);
        Assert.DoesNotMatch(new Regex(@"terrain\.Set\w*\s*\(|terrain\.mainTexture\s*=|m_material\s*="), registration);
        // #583: the top, black under the world-mapped rock shader, is drawn in the wood wall's UV-mapped piece material
        // and gets the same cliff texture and normal map as the sides.
        Assert.Matches(new Regex(@"_top\s*=\s*new\s+Material\s*\(\s*FindMaterial\s*\(\s*TopMaterialPrefab\s*,\s*TopMaterialName\s*\)\s*\)"), registration);
        Assert.Matches(new Regex(@"private\s+const\s+string\s+TopMaterialPrefab\s*=\s*""woodwall""\s*;"), registration);
        Assert.Matches(new Regex(@"Dress\s*\(\s*_side\s*,\s*cliff\s*,\s*normal\s*,\s*scale\s*\)\s*;\s*DressTop\s*\(\s*_top\s*,\s*cliff\s*,\s*normal\s*\)\s*;"), registration);
        Assert.Matches(new Regex(@"_earthFill\?\.ApplyTerrainLook\s*\(\s*\)\s*;"), File.ReadAllText(Path.Combine(RepoRoot, "src", "ConcernedLadders", "Plugin.cs")));
    }

    [Fact]
    public void ABlockIsFoundAgainByItsCellFloorAndTop()
    {
        var block = new FillBlock(new FillCell(-3, 7), Ground - 2f, Ground);
        (float x, float y, float z) = EarthFill.Origin(block);
        Assert.Equal((-2.5f, Ground - 2f, 7.5f), (x, y, z));
        Assert.Equal(block.Cell, EarthFill.CellAt(x, z));
        Assert.True(block.Same(new FillBlock(block.Cell, Ground - 2f + 0.01f, Ground - 0.01f))); // read back through a float scale
        Assert.False(block.Same(new FillBlock(block.Cell, Ground - 1f, Ground))); // a neighbour's block of another depth
        Assert.False(block.Same(new FillBlock(new FillCell(-2, 7), Ground - 2f, Ground)));
    }

    [Fact]
    public void TheSettingIsOnInsideTheShovelsOwnOffByDefaultSwitchAndThePrefabIsWiredAsDocumented()
    {
        Assert.True(EarthFillSettings.EnabledByDefault);
        Assert.Equal("Excavation", EarthFillSettings.Section);
        string plugin = File.ReadAllText(Path.Combine(RepoRoot, "src", "ConcernedLadders", "Plugin.cs"));
        Assert.Matches(new Regex(@"Config\.Bind\s*\(\s*EarthFillSettings\.Section\s*,\s*EarthFillSettings\.Key\s*,\s*EarthFillSettings\.EnabledByDefault\s*,"), plugin);
        Assert.Matches(new Regex(@"Config\.Bind\s*\(\s*""Excavation""\s*,\s*""Enabled""\s*,\s*false\s*,"), plugin);
        // The block prefab is registered only inside the excavation switch, and handed to the runtime.
        Assert.Single(Regex.Matches(plugin, @"new\s+EarthFillPieceRegistration\s*\("));
        Assert.Matches(new Regex(@"if\s*\(\s*enabled\.Value\s*\)\s*\{[^}]*_earthFill\s*=\s*new\s+EarthFillPieceRegistration\s*\("), plugin);
        Assert.Matches(new Regex(@"_excavation\.FillEnabled\s*=\s*earthFill\.Value\s*;"), plugin);
        // The prefab: persistent, its scale saved and sent, removable, no cost, never a target. Code only.
        string registration = Regex.Replace(File.ReadAllText(Path.Combine(RepoRoot, "src", "ConcernedLadders", "Pieces", "EarthFillPieceRegistration.cs")),
            @"/\*.*?\*/|//[^\n]*", " ", RegexOptions.Singleline);
        foreach (string pinned in new[]
                 {
                     @"view\.m_persistent\s*=\s*true\s*;", @"view\.m_syncInitialScale\s*=\s*true\s*;", @"piece\.m_canBeRemoved\s*=\s*true\s*;",
                     @"piece\.m_resources\s*=\s*Array\.Empty<Piece\.Requirement>\s*\(\s*\)\s*;", @"piece\.m_primaryTarget\s*=\s*false\s*;",
                     @"piece\.m_randomTarget\s*=\s*false\s*;", @"piece\.m_targetNonPlayerBuilt\s*=\s*false\s*;",
                 })
        {
            Assert.Matches(new Regex(pinned), registration);
        }

        Assert.DoesNotMatch(new Regex(@"\bWearNTear\b|\bZDO\b(?!\.ObjectType)|GetZDO|ZRoutedRpc|InvokeRPC|ClaimOwnership|SetOwner|\bInstantiate\s*\(|PieceTable|AddPiece"), registration);
    }

    /// <summary>Whether a vertex is one of <paramref name="hole"/>'s floor vertices.</summary>
    private static bool EarthFillTestsFloor(DigFootprint hole, int x, int z) =>
        x >= hole.MinX && x <= hole.MaxX && z >= hole.MinZ && z <= hole.MaxZ;

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
