using System;
using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.ConcernedLadders.Domain.Doors;

namespace TheConcernedCat.ConcernedLadders.Tests.Doors;

/// <summary>CL-004 #485 acceptance 1 (family inventory): every native door in the installed-game census has exactly one
/// decision, every buildable hinged door is a family, and the special drawbridge and shutter classifications stay
/// visible rather than silently missing.</summary>
public sealed class DoorFamiliesTests
{
    [Fact]
    public void EveryJotunnCandidateIsAFamily()
    {
        foreach (string donor in DoorFamilies.JotunnCandidates)
        {
            Assert.NotNull(DoorFamilies.ByDonor(donor));
            Assert.Equal(DoorDonorClass.Family, DoorFamilies.Classify(donor)!.Kind);
        }

        Assert.Equal(9, DoorFamilies.All.Count);
    }

    [Fact]
    public void EveryCensusPrefabHasExactlyOneDecision()
    {
        var donors = DoorFamilies.Census.Select(d => d.Donor).ToList();
        Assert.Equal(donors.Count, donors.Distinct(StringComparer.Ordinal).Count());
        // 23 prefabs carry a Door component in the 1.0.21 main bundle; three hammer pieces in the Doors usage tag
        // carry none (two grausten windows, the crystal wall); three cloth curtains are named like doors and are in
        // neither (usage.json).
        Assert.Equal(29, donors.Count);
        Assert.Equal(6, DoorFamilies.Census.Count(d => d.Kind == DoorDonorClass.NoDoorState));
        Assert.Equal(11, DoorFamilies.Census.Count(d => d.Kind == DoorDonorClass.NotBuildable));
    }

    [Fact]
    public void EveryFamilyIsInTheCensusAndEveryCensusFamilyHasAPiece()
    {
        foreach (DoorFamily family in DoorFamilies.All)
        {
            Assert.Equal(DoorDonorClass.Family, DoorFamilies.Classify(family.Donor)!.Kind);
        }

        foreach (DoorDonorDecision decision in DoorFamilies.Census.Where(d => d.Kind == DoorDonorClass.Family))
        {
            Assert.NotNull(DoorFamilies.ByDonor(decision.Donor));
        }
    }

    [Theory]
    [InlineData("piece_drawbridge", "SpecialSemantics")]
    [InlineData("piece_drawbridge_log", "SpecialSemantics")]
    [InlineData("wood_window", "WindowCovering")]
    [InlineData("piece_cloth_hanging_door", "NoDoorState")]
    [InlineData("Piece_grausten_window_2x2", "NoDoorState")]
    [InlineData("Piece_grausten_window_4x2", "NoDoorState")]
    [InlineData("crystal_wall_1x1", "NoDoorState")]
    [InlineData("piece_dvergr_wood_door", "NotBuildable")]
    [InlineData("dungeon_queen_door", "NotBuildable")]
    public void SpecialPrefabsAreClassifiedExplicitly(string donor, string kind)
    {
        var expected = (DoorDonorClass)Enum.Parse(typeof(DoorDonorClass), kind);
        DoorDonorDecision? decision = DoorFamilies.Classify(donor);
        Assert.NotNull(decision);
        Assert.Equal(expected, decision!.Kind);
        Assert.Null(DoorFamilies.ByDonor(donor));
    }

    [Fact]
    public void EveryExclusionGivesAReasonAndNoFamilyNeedsOne()
    {
        foreach (DoorDonorDecision decision in DoorFamilies.Census)
        {
            if (decision.Kind == DoorDonorClass.Family) { Assert.Equal(string.Empty, decision.Reason); }
            else { Assert.False(string.IsNullOrWhiteSpace(decision.Reason), decision.Donor); }
        }
    }

    [Fact]
    public void TrapdoorIdsAndTokensAreStableUniqueAndOwnNamespace()
    {
        var prefabs = DoorFamilies.All.Select(f => f.TrapdoorPrefab).ToList();
        Assert.Equal(prefabs.Count, prefabs.Distinct(StringComparer.Ordinal).Count());
        Assert.All(prefabs, p => Assert.StartsWith("tcc_trapdoor_", p, StringComparison.Ordinal));
        // The published ladder prefab IDs are a save contract and must never be reused.
        Assert.DoesNotContain("tcc_ladder_wood", prefabs);
        Assert.DoesNotContain("tcc_ladder_copper", prefabs);
        Assert.DoesNotContain("tcc_ladder_iron", prefabs);
        var tokens = DoorFamilies.All.Select(f => f.TrapdoorToken).ToList();
        Assert.Equal(tokens.Count, tokens.Distinct(StringComparer.Ordinal).Count());
        Assert.All(tokens, t => Assert.StartsWith("cl_trapdoor_", t, StringComparison.Ordinal));
        Assert.Equal("tcc_trapdoor_wood", DoorFamilies.ByDonor("wood_door")!.TrapdoorPrefab);
        Assert.Same(DoorFamilies.ByDonor("flametal_gate"), DoorFamilies.ByTrapdoorPrefab("tcc_trapdoor_flametal"));
    }

    [Fact]
    public void EnglishNamesAreUniqueTitleCaseTrapdoors()
    {
        var names = DoorFamilies.All.Select(f => f.EnglishTrapdoor).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        foreach (string name in names)
        {
            Assert.EndsWith(" Trapdoor", name, StringComparison.Ordinal);
            Assert.All(name.Split(' '), word => Assert.True(char.IsUpper(word[0]), name));
        }
    }

    /// <summary>Pinned from the installed game: changing a cost here must be a deliberate, visible decision.</summary>
    [Theory]
    [InlineData("wood_door", "piece_workbench", "Wood:4")]
    [InlineData("wood_gate", "piece_workbench", "Wood:12")]
    [InlineData("wood_fence_gate", "piece_workbench", "Wood:4")]
    [InlineData("iron_grate", "forge", "Iron:4")]
    [InlineData("darkwood_gate", "forge", "Wood:16,Iron:4,Tar:2")]
    [InlineData("stave_gate", "forge", "Frostwood:24,Gold:4")]
    [InlineData("piece_hexagonal_door", "blackforge", "YggdrasilWood:8,Copper:8")]
    [InlineData("ashwood_door", "piece_workbench", "FlametalNew:1,Blackwood:5")]
    [InlineData("flametal_gate", "blackforge", "FlametalNew:16")]
    public void EachTrapdoorCostsWhatItsDonorCostsAtTheDonorsStation(string donor, string station, string costs)
    {
        DoorFamily family = DoorFamilies.ByDonor(donor)!;
        Assert.Equal(station, family.Station);
        Assert.Equal(costs, string.Join(",", family.Costs.Select(c => c.Item + ":" + c.Amount)));
        Assert.All(family.Costs, c => Assert.True(c.Amount > 0));
    }

    [Fact]
    public void TheSwingConstantsAreTheNativeClipLengths()
    {
        // door_animator, darkwood_gate_animator and Flametal_gate_animator opening clips in 1.0.21 (clips.txt).
        Assert.Equal(0.5f, DoorFamilies.LightSwing);
        Assert.Equal(1.0f, DoorFamilies.MediumSwing);
        Assert.Equal(2.0f, DoorFamilies.HeavySwing);
    }

    /// <summary>The native <c>WearNTear.MaterialType</c> ordinals the adapter will compare against.</summary>
    [Fact]
    public void DonorMaterialOrdinalsAreTheGames()
    {
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, new[]
        {
            (int)DonorMaterial.Wood, (int)DonorMaterial.Stone, (int)DonorMaterial.Iron, (int)DonorMaterial.HardWood,
            (int)DonorMaterial.Marble, (int)DonorMaterial.Ashstone, (int)DonorMaterial.Ancient, (int)DonorMaterial.Ice,
            (int)DonorMaterial.Timberwood,
        });
    }

    /// <summary>Pinned from the installed game (census.json): health, material ordinal, burnable, tokens, names.</summary>
    [Theory]
    [InlineData("wood_door", 400f, 0, true, "$piece_wooddoor", "Wood Door", "Wood Trapdoor")]
    [InlineData("wood_gate", 400f, 0, true, "$piece_woodgate", "Wood Gate", "Wood Gate Trapdoor")]
    [InlineData("wood_fence_gate", 400f, 0, true, "$piece_woodfencegate", "Roundpole Gate", "Roundpole Trapdoor")]
    [InlineData("iron_grate", 2000f, 2, false, "$piece_irongate", "Iron Gate", "Iron Gate Trapdoor")]
    [InlineData("darkwood_gate", 1000f, 3, true, "$piece_darkwoodgate", "Darkwood Gate", "Darkwood Trapdoor")]
    [InlineData("stave_gate", 1000f, 3, true, "$piece_stavegate", "Timberwood Gate", "Timberwood Trapdoor")]
    [InlineData("piece_hexagonal_door", 1500f, 3, true, "$piece_hexagonalgate", "Hexagonal Gate", "Hexagonal Trapdoor")]
    [InlineData("ashwood_door", 1000f, 0, true, "$piece_ashwood_door", "Ashwood Door", "Ashwood Trapdoor")]
    [InlineData("flametal_gate", 3000f, 2, false, "$piece_flametalgate", "Flametal Gate", "Flametal Trapdoor")]
    public void EachFamilyKeepsItsDonorsPinnedValues(string donor, float health, int material, bool burnable,
        string token, string english, string trapdoor)
    {
        DoorFamily family = DoorFamilies.ByDonor(donor)!;
        Assert.Equal(health, family.Health);
        Assert.Equal(material, (int)family.Material);
        Assert.Equal(burnable, family.Burnable);
        Assert.Equal(token, family.DonorToken);
        Assert.Equal(english, family.DonorEnglish);
        Assert.Equal(trapdoor, family.EnglishTrapdoor);
    }

    [Fact]
    public void SwingTimesAreTheThreeNativeAnimatorClasses()
    {
        var light = new HashSet<string> { "wood_door", "wood_gate", "wood_fence_gate", "iron_grate" };
        var heavy = new HashSet<string> { "flametal_gate" };
        foreach (DoorFamily family in DoorFamilies.All)
        {
            float expected = light.Contains(family.Donor) ? DoorFamilies.LightSwing
                : heavy.Contains(family.Donor) ? DoorFamilies.HeavySwing : DoorFamilies.MediumSwing;
            Assert.Equal(expected, family.SwingSeconds);
            Assert.InRange(family.SwingSeconds, HatchMotion.MinimumSwingSeconds, HatchMotion.MaximumSwingSeconds);
        }
    }

    [Fact]
    public void MetalDonorsDoNotBurnAndWoodDonorsDo()
    {
        foreach (DoorFamily family in DoorFamilies.All)
        {
            Assert.Equal(family.Material != DonorMaterial.Iron, family.Burnable);
            Assert.True(family.Health > 0f);
            Assert.StartsWith("$piece_", family.DonorToken, StringComparison.Ordinal);
        }
    }
}
