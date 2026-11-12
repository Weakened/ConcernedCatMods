using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Tests.Doors;

/// <summary>#547: the Wood Gate's trapdoor and cellar door were the Wood Door's in all but name, because both families
/// borrow the same donor materials. The Wood Gate's leaves are now a gate (boards with gaps over rails), every other
/// family keeps its one slab exactly as before, and no look changes the collider any geometry is measured with.</summary>
public sealed class LeafPatternTests
{
    private const float Epsilon = 1e-5f;

    public static IEnumerable<object[]> Leaves() => new[] { new object[] { "lid" }, new object[] { "left" }, new object[] { "right" } };

    /// <summary>The visual box of the trapdoor lid or a cellar leaf (the solid look, the box every look stays in).</summary>
    private static (ClimbPoint Centre, ClimbPoint Size) Box(string label)
    {
        if (label == "lid") return (TrapdoorBlueprint.LidVisual.Centre, TrapdoorBlueprint.LidVisual.Size);
        CellarPart visual = CellarBlueprint.LeafVisual(label == "left" ? CellarLeaf.Left : CellarLeaf.Right);
        return (visual.Centre, visual.Size);
    }

    [Fact]
    public void EveryFamilyReadsAsItsOwnDoor()
    {
        // #555 (owner 2026-11-10): every door type has its bulkhead, and each reads as its door.
        var expected = new Dictionary<string, LeafLook>
        {
            ["wood_door"] = LeafLook.Planked,
            ["wood_gate"] = LeafLook.Gate,
            ["wood_fence_gate"] = LeafLook.Poles,
            ["iron_grate"] = LeafLook.Bars,
            ["darkwood_gate"] = LeafLook.Strapped,
            ["stave_gate"] = LeafLook.Panelled,
            ["piece_hexagonal_door"] = LeafLook.Panelled,
            ["ashwood_door"] = LeafLook.Panelled,
            ["flametal_gate"] = LeafLook.Plated,
        };
        foreach (DoorFamily family in DoorFamilies.All) Assert.Equal(expected[family.Donor], LeafPattern.For(family));
        // The two families that share every donor material still differ in look.
        DoorFamily wood = DoorFamilies.All.Single(family => family.Donor == "wood_door");
        DoorFamily gate = DoorFamilies.All.Single(family => family.Donor == "wood_gate");
        Assert.Equal(TrapdoorBlueprint.MaterialsFor(wood).Lid, TrapdoorBlueprint.MaterialsFor(gate).Lid);
        Assert.NotEqual(LeafPattern.For(wood), LeafPattern.For(gate));
    }

    public static IEnumerable<object[]> LooksAndLeaves()
    {
        foreach (LeafLook look in Enum.GetValues(typeof(LeafLook)))
            foreach (string leaf in new[] { "lid", "left", "right" }) yield return new object[] { (int)look, leaf };
    }

    [Theory]
    [MemberData(nameof(LooksAndLeaves))]
    public void EveryLookStaysInsideTheLeafsColliderBoxWithUniqueParts(int lookValue, string label)
    {
        (ClimbPoint centre, ClimbPoint size) = Box(label);
        IReadOnlyList<LeafPiece> parts = LeafPattern.Parts("leaf", centre, size, (LeafLook)lookValue);
        Assert.NotEmpty(parts);
        Assert.Equal(parts.Count, parts.Select(part => part.Name).Distinct().Count());
        foreach (LeafPiece part in parts)
        {
            Assert.True(part.Size.X > 0f && part.Size.Y > 0f && part.Size.Z > 0f, part.Name);
            Inside(label + " " + part.Name, part.Centre.X - part.Size.X / 2f, part.Centre.X + part.Size.X / 2f, centre.X, size.X);
            Inside(label + " " + part.Name, part.Centre.Y - part.Size.Y / 2f, part.Centre.Y + part.Size.Y / 2f, centre.Y, size.Y);
            Inside(label + " " + part.Name, part.Centre.Z - part.Size.Z / 2f, part.Centre.Z + part.Size.Z / 2f, centre.Z, size.Z);
        }
    }

    [Theory]
    [InlineData("lid")]
    [InlineData("left")]
    public void EachLookHasItsDefiningFeature(string label)
    {
        (ClimbPoint centre, ClimbPoint size) = Box(label);
        IReadOnlyList<LeafPiece> Of(LeafLook look) => LeafPattern.Parts("leaf", centre, size, look);
        float top = centre.Y + size.Y / 2f;

        // Planks: tight seams, more and wider than the gate's gaps would allow; poles: narrower still.
        int planks = Of(LeafLook.Planked).Count(part => part.Role == LeafPieceRole.Board);
        int poles = Of(LeafLook.Poles).Count(part => part.Role == LeafPieceRole.Board);
        Assert.True(poles > planks, "poles are narrower than planks");
        Assert.Equal(3, Of(LeafLook.Poles).Count(part => part.Role == LeafPieceRole.Rail));

        // Bars: a frame of four, a cross bar, and bars that do not reach the top: the leaf is open between them.
        IReadOnlyList<LeafPiece> bars = Of(LeafLook.Bars);
        Assert.Equal(5, bars.Count(part => part.Role == LeafPieceRole.Rail));
        Assert.All(bars.Where(part => part.Role == LeafPieceRole.Board), bar =>
        {
            Assert.True(bar.Centre.Y + bar.Size.Y / 2f < top - 1e-4f);
            Assert.True(bar.Size.X < 0.05f);
        });

        // Straps and bands are iron hardware on top of the boards or plates.
        foreach (LeafLook look in new[] { LeafLook.Strapped, LeafLook.Plated })
        {
            List<LeafPiece> hardware = Of(look).Where(part => part.Role == LeafPieceRole.Strap).ToList();
            Assert.True(hardware.Count >= 2, look + " has its iron hardware");
            Assert.All(hardware, strap => Assert.Equal(top, strap.Centre.Y + strap.Size.Y / 2f, 4));
            Assert.All(Of(look).Where(part => part.Role == LeafPieceRole.Board), board => Assert.True(board.Centre.Y + board.Size.Y / 2f < top - 1e-4f));
        }

        Assert.Equal(2, Of(LeafLook.Plated).Count(part => part.Role == LeafPieceRole.Board));

        // Panelled: a full-thickness frame around a recessed panel that shows the door's own panel texture.
        IReadOnlyList<LeafPiece> panelled = Of(LeafLook.Panelled);
        LeafPiece panel = Assert.Single(panelled, part => part.Role == LeafPieceRole.Board);
        Assert.True(panel.Centre.Y + panel.Size.Y / 2f < top - 0.01f);
        Assert.Equal(4, panelled.Count(part => part.Role == LeafPieceRole.Rail && Math.Abs(part.Size.Y - size.Y) < 1e-4f));
    }

    [Theory]
    [MemberData(nameof(Leaves))]
    public void ASolidLeafIsExactlyTheOneBoxItWasBefore(string label)
    {
        (ClimbPoint centre, ClimbPoint size) = Box(label);
        IReadOnlyList<LeafPiece> parts = LeafPattern.Parts("leaf", centre, size, LeafLook.Solid);
        LeafPiece only = Assert.Single(parts);
        Assert.Equal("leaf", only.Name);
        Assert.Equal(LeafPieceRole.Board, only.Role);
        Assert.Equal(centre, only.Centre);
        Assert.Equal(size, only.Size);
    }

    [Theory]
    [MemberData(nameof(Leaves))]
    public void AGateStaysInsideTheLeafsColliderBox(string label)
    {
        (ClimbPoint centre, ClimbPoint size) = Box(label);
        foreach (LeafPiece part in LeafPattern.Parts("leaf", centre, size, LeafLook.Gate))
        {
            Inside(label + " " + part.Name, part.Centre.X - part.Size.X / 2f, part.Centre.X + part.Size.X / 2f, centre.X, size.X);
            Inside(label + " " + part.Name, part.Centre.Y - part.Size.Y / 2f, part.Centre.Y + part.Size.Y / 2f, centre.Y, size.Y);
            Inside(label + " " + part.Name, part.Centre.Z - part.Size.Z / 2f, part.Centre.Z + part.Size.Z / 2f, centre.Z, size.Z);
        }
    }

    [Theory]
    [MemberData(nameof(Leaves))]
    public void AGateHasGappedBoardsAcrossTheWholeWidthOverTwoFullWidthRails(string label)
    {
        (ClimbPoint centre, ClimbPoint size) = Box(label);
        IReadOnlyList<LeafPiece> parts = LeafPattern.Parts("leaf", centre, size, LeafLook.Gate);
        List<LeafPiece> boards = parts.Where(part => part.Role == LeafPieceRole.Board).OrderBy(part => part.Centre.X).ToList();
        List<LeafPiece> rails = parts.Where(part => part.Role == LeafPieceRole.Rail).ToList();
        Assert.True(boards.Count >= 2, label);
        Assert.Equal(2, rails.Count);
        Assert.Equal(parts.Count, parts.Select(part => part.Name).Distinct().Count());

        // The silhouette is the leaf's: the outer boards are flush with its sides and run its full length and top.
        Assert.Equal(centre.X - size.X / 2f, boards[0].Centre.X - boards[0].Size.X / 2f, 4);
        Assert.Equal(centre.X + size.X / 2f, boards[boards.Count - 1].Centre.X + boards[boards.Count - 1].Size.X / 2f, 4);
        foreach (LeafPiece board in boards)
        {
            Assert.Equal(size.Z, board.Size.Z, 5);
            Assert.Equal(centre.Y + size.Y / 2f, board.Centre.Y + board.Size.Y / 2f, 5);
            Assert.True(board.Size.X <= LeafPattern.BoardWidthMax + Epsilon, label + " board too wide");
        }

        // A real gap between neighbours, the same everywhere.
        for (int index = 1; index < boards.Count; ++index)
        {
            float gap = boards[index].Centre.X - boards[index].Size.X / 2f - (boards[index - 1].Centre.X + boards[index - 1].Size.X / 2f);
            Assert.Equal(LeafPattern.BoardGap, gap, 4);
        }

        // Rails span the width beneath the boards, one toward each end, and never overlap the boards.
        float boardBottom = boards[0].Centre.Y - boards[0].Size.Y / 2f;
        foreach (LeafPiece rail in rails)
        {
            Assert.Equal(size.X, rail.Size.X, 5);
            Assert.Equal(boardBottom, rail.Centre.Y + rail.Size.Y / 2f, 5);
            Assert.Equal(centre.Y - size.Y / 2f, rail.Centre.Y - rail.Size.Y / 2f, 5);
        }

        Assert.True(rails.Min(rail => rail.Centre.Z) < centre.Z && rails.Max(rail => rail.Centre.Z) > centre.Z, label);
    }

    [Theory]
    [InlineData(0.3f, 2)]
    [InlineData(0.39f, 2)]
    [InlineData(0.4f, 3)]
    [InlineData(0.835f, 5)]
    [InlineData(1.8f, 9)]
    public void TheBoardCountIsTheFewestThatKeepEachBoardNarrow(float width, int expected)
    {
        int count = LeafPattern.BoardCount(width);
        Assert.Equal(expected, count);
        Assert.True((width - (count - 1) * LeafPattern.BoardGap) / count <= LeafPattern.BoardWidthMax + Epsilon);
        if (count > 2) Assert.True((width - (count - 2) * LeafPattern.BoardGap) / (count - 1) > LeafPattern.BoardWidthMax);
    }

    [Fact]
    public void BothAdaptersBuildTheLeafThroughTheLookAndNeverTheBareBox()
    {
        foreach (string file in new[] { "Pieces/TrapdoorPieceRegistration.cs", "Pieces/CellarDoorPieceRegistration.cs" })
        {
            string code = File.ReadAllText(Path.Combine(RepoRoot, "src", "ConcernedLadders", file));
            Assert.Single(Regex.Matches(code, @"LeafVisuals\s*\([^;]*LeafPattern\s*\.\s*For\s*\(\s*family\s*\)\s*,[^;]*,\s*(strapMaterial|markMaterial)\s*,\s*surface\s*,\s*layer\s*\)"));
            Assert.DoesNotMatch(new Regex(@"VisualBox\s*\([^;]*visual\s*\.\s*Name"), code);
            Assert.DoesNotMatch(new Regex(@"Visual\s*\(\s*hinge\s*\.\s*transform\s*,\s*TrapdoorBlueprint\s*\.\s*LidVisual"), code);
        }
    }

    [Fact]
    public void TheAssetRecordStatesTheGateAsBuilt()
    {
        // Wrapped prose: compared with every run of whitespace as one space.
        string doc = Regex.Replace(File.ReadAllText(Path.Combine(RepoRoot, "docs", "mods", "concerned-ladders", "DOOR_ASSET_SOURCES.md")), @"\s+", " ");
        int lid = LeafPattern.BoardCount(TrapdoorBlueprint.LidVisual.Size.X);
        int leaf = LeafPattern.BoardCount(CellarBlueprint.LeafVisual(CellarLeaf.Left).Size.X);
        Assert.Contains(lid + " on the trapdoor lid", doc, StringComparison.Ordinal);
        Assert.Contains(leaf + " on each cellar leaf", doc, StringComparison.Ordinal);
        Assert.Contains("no wider than " + Number(LeafPattern.BoardWidthMax) + " m", doc, StringComparison.Ordinal);
        Assert.Contains(Number(LeafPattern.BoardGap) + " m gaps", doc, StringComparison.Ordinal);
        Assert.Contains("top " + Number(LeafPattern.BoardShare * 100f) + "% of the thickness", doc, StringComparison.Ordinal);
        Assert.Contains("bottom " + Number(100f - LeafPattern.BoardShare * 100f) + "% of the thickness", doc, StringComparison.Ordinal);
        Assert.Contains(Number(LeafPattern.RailWidth) + " m wide", doc, StringComparison.Ordinal);
        Assert.Contains(Number(LeafPattern.RailInset * 100f) + "% of the leaf's length in from each end", doc, StringComparison.Ordinal);
    }

    private static string Number(float value) =>
        Math.Round(value, 3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static void Inside(string what, float min, float max, float centre, float size)
    {
        Assert.True(min >= centre - size / 2f - Epsilon, what + " reaches below the leaf box");
        Assert.True(max <= centre + size / 2f + Epsilon, what + " reaches past the leaf box");
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
