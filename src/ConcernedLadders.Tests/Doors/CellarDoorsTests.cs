using System;
using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.ConcernedLadders.Domain.Doors;

namespace TheConcernedCat.ConcernedLadders.Tests.Doors;

/// <summary>CL-005 #486 acceptance 2 (donor coverage) and 5 (paired operation), as a contract: one cellar door per
/// CL-004 family with a stable ID, name and recorded cost, and one progress for both leaves.</summary>
public sealed class CellarDoorsTests
{
    [Fact]
    public void EveryDoorFamilyHasExactlyOneCellarDoorInTheSameOrder()
    {
        Assert.Equal(9, CellarDoors.All.Count);
        Assert.Equal(DoorFamilies.All.Count, CellarDoors.All.Count);
        for (int index = 0; index < CellarDoors.All.Count; ++index)
        {
            Assert.Same(DoorFamilies.All[index], CellarDoors.All[index].Door);
        }
    }

    [Fact]
    public void OnlyCensusFamiliesAreDonorsAndTheExclusionsCarryOver()
    {
        var donors = new HashSet<string>(CellarDoors.All.Select(c => c.Door.Donor), StringComparer.Ordinal);
        foreach (DoorDonorDecision decision in DoorFamilies.Census)
        {
            Assert.Equal(decision.Kind == DoorDonorClass.Family, donors.Contains(decision.Donor));
        }

        foreach (string special in new[] { "piece_drawbridge", "piece_drawbridge_log", "wood_window", "piece_dvergr_wood_door" })
        {
            Assert.DoesNotContain(special, donors);
        }
    }

    [Fact]
    public void PrefabIdsAndTokensAreStableAndDistinctFromTheTrapdoors()
    {
        string[] expected =
        {
            "tcc_cellardoor_wood", "tcc_cellardoor_wood_gate", "tcc_cellardoor_roundpole", "tcc_cellardoor_iron_gate",
            "tcc_cellardoor_darkwood", "tcc_cellardoor_timberwood", "tcc_cellardoor_hexagonal", "tcc_cellardoor_ashwood",
            "tcc_cellardoor_flametal",
        };
        Assert.Equal(expected, CellarDoors.All.Select(c => c.Prefab).ToArray());
        var trapdoors = new HashSet<string>(DoorFamilies.All.Select(f => f.TrapdoorPrefab), StringComparer.Ordinal);
        foreach (CellarDoorFamily cellar in CellarDoors.All)
        {
            Assert.DoesNotContain(cellar.Prefab, trapdoors);
            Assert.Equal("cl_cellardoor_" + cellar.Door.Family, cellar.Token);
            Assert.Same(cellar, CellarDoors.ByPrefab(cellar.Prefab));
        }

        Assert.Null(CellarDoors.ByPrefab("tcc_trapdoor_wood"));
        Assert.Null(CellarDoors.ByPrefab("wood_door"));
        Assert.Equal(CellarDoors.All.Count, CellarDoors.All.Select(c => c.Token).Distinct().Count());
    }

    [Fact]
    public void NamesAreTitleCaseAndMatchTheTrapdoorNames()
    {
        foreach (CellarDoorFamily cellar in CellarDoors.All)
        {
            Assert.EndsWith(" Cellar Door", cellar.English, StringComparison.Ordinal);
            Assert.Equal(cellar.Door.EnglishTrapdoor.Replace(" Trapdoor", string.Empty) + " Cellar Door", cellar.English);
            foreach (string word in cellar.English.Split(' '))
            {
                Assert.True(char.IsUpper(word[0]), cellar.English + " is not Title Case");
            }
        }

        Assert.Equal(CellarDoors.All.Count, CellarDoors.All.Select(c => c.English).Distinct().Count());
    }

    [Fact]
    public void EachCostIsTheDonorsDoubledWithTheSameItemsAndOrder()
    {
        Assert.Equal(2, CellarDoors.CostMultiplier);
        foreach (CellarDoorFamily cellar in CellarDoors.All)
        {
            Assert.Equal(cellar.Door.Costs.Count, cellar.Costs.Count);
            for (int index = 0; index < cellar.Costs.Count; ++index)
            {
                Assert.Equal(cellar.Door.Costs[index].Item, cellar.Costs[index].Item);
                Assert.Equal(cellar.Door.Costs[index].Amount * 2, cellar.Costs[index].Amount);
            }
        }

        // The recorded table (CELLAR_DOORS.md section 5), a few rows spelled out.
        Assert.Equal(new[] { ("Wood", 8) }, Costs("tcc_cellardoor_wood"));
        Assert.Equal(new[] { ("Wood", 24) }, Costs("tcc_cellardoor_wood_gate"));
        Assert.Equal(new[] { ("Iron", 8) }, Costs("tcc_cellardoor_iron_gate"));
        Assert.Equal(new[] { ("Wood", 32), ("Iron", 8), ("Tar", 4) }, Costs("tcc_cellardoor_darkwood"));
        Assert.Equal(new[] { ("FlametalNew", 2), ("Blackwood", 10) }, Costs("tcc_cellardoor_ashwood"));
        Assert.Equal(new[] { ("FlametalNew", 32) }, Costs("tcc_cellardoor_flametal"));
    }

    [Fact]
    public void TheCloneReadBackRefusesTheDonorsOwnCostAndAnEmptyList()
    {
        CellarDoorFamily wood = CellarDoors.ByPrefab("tcc_cellardoor_wood")!;
        Assert.Empty(TrapdoorBlueprint.RequirementMismatches(wood.Costs, new[] { new ObservedRequirement("JVLmock_Wood", 8, true) }));
        // Jotunn keeping the donor's requirements would read back as the donor's 4 Wood: refused.
        Assert.NotEmpty(TrapdoorBlueprint.RequirementMismatches(wood.Costs, new[] { new ObservedRequirement("Wood", 4, true) }));
        Assert.NotEmpty(TrapdoorBlueprint.RequirementMismatches(wood.Costs, Array.Empty<ObservedRequirement>()));
        Assert.NotEmpty(TrapdoorBlueprint.RequirementMismatches(wood.Costs, null));
        Assert.NotEmpty(TrapdoorBlueprint.RequirementMismatches(wood.Costs, new[] { new ObservedRequirement("Wood", 8, false) }));
        // The family overload is unchanged: it still checks the donor's own cost.
        Assert.Empty(TrapdoorBlueprint.RequirementMismatches(wood.Door, new[] { new ObservedRequirement("Wood", 4, true) }));
    }

    [Fact]
    public void TheSettingIsItsOwnAndOffByDefault()
    {
        Assert.False(CellarDoorSettings.EnabledByDefault);
        Assert.Equal("CellarDoors", CellarDoorSettings.Section);
        Assert.Equal("Enabled", CellarDoorSettings.Key);
        Assert.NotEqual(TrapdoorSettings.Section, CellarDoorSettings.Section);
        Assert.Contains("UNFINISHED", CellarDoorSettings.Description, StringComparison.Ordinal);
        Assert.Contains("delete every placed cellar door", CellarDoorSettings.Description, StringComparison.Ordinal);
        Assert.Contains("same value", CellarDoorSettings.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLocalizationNamesEveryCellarDoorAndCollidesWithNoTrapdoorToken()
    {
        Dictionary<string, string> english = CellarDoors.English();
        Assert.Equal(CellarDoors.All.Count + 1, english.Count);
        Assert.Contains(CellarDoors.DescriptionToken, english.Keys);
        foreach (CellarDoorFamily cellar in CellarDoors.All) { Assert.Equal(cellar.English, english[cellar.Token]); }
        Assert.Empty(english.Keys.Intersect(TrapdoorBlueprint.English().Keys));
        Assert.All(english.Keys, key => Assert.False(key.StartsWith("$", StringComparison.Ordinal)));
    }

    private static (string, int)[] Costs(string prefab) =>
        CellarDoors.ByPrefab(prefab)!.Costs.Select(c => (c.Item, c.Amount)).ToArray();
}

/// <summary>CL-005 #486 acceptance 5 and 6: both leaves follow one progress through the trapdoor's own motion, so a
/// leaf that meets something holds both, and the pair is a passage or a landing only when both are.</summary>
public sealed class CellarPairTests
{
    private sealed class Script : ICellarLeafOccupancy
    {
        internal Func<CellarLeaf, float, bool> Rule { get; set; } = (_, _) => false;

        internal List<(CellarLeaf Leaf, float Angle)> Asked { get; } = new List<(CellarLeaf, float)>();

        public bool Occupied(CellarLeaf leaf, float angleDegrees)
        {
            Asked.Add((leaf, angleDegrees));
            return Rule(leaf, angleDegrees);
        }
    }

    private sealed class Clear : IHatchOccupancy
    {
        public bool Occupied(float angleDegrees) => false;
    }

    [Fact]
    public void AClearPairMovesExactlyAsAClearTrapdoorLid()
    {
        var script = new Script();
        var pair = new CellarPairOccupancy<Script>(script);
        float cellar = 0f, hatch = 0f;
        for (int frame = 0; frame < 120; ++frame)
        {
            cellar = HatchMotion.Step(cellar, true, 1f / 60f, 1f, pair);
            hatch = HatchMotion.Step(hatch, true, 1f / 60f, 1f, new Clear());
            Assert.Equal(hatch, cellar);
        }

        Assert.Equal(1f, cellar);
        // Both leaves were asked about every checked pose.
        Assert.NotEmpty(script.Asked);
        Assert.Equal(script.Asked.Count(a => a.Leaf == CellarLeaf.Left), script.Asked.Count(a => a.Leaf == CellarLeaf.Right));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void OneBlockedLeafHoldsBoth(int which)
    {
        CellarLeaf blocked = which == 0 ? CellarLeaf.Left : CellarLeaf.Right;
        var script = new Script { Rule = (leaf, angle) => leaf == blocked && angle > 30f };
        var pair = new CellarPairOccupancy<Script>(script);
        float progress = 0f;
        for (int frame = 0; frame < 300; ++frame) { progress = HatchMotion.Step(progress, true, 1f / 60f, 1f, pair); }

        // One progress for the pair: held at the last pose clear for both, never fully open.
        Assert.True(HatchMotion.Angle(progress) <= 30f);
        Assert.True(HatchMotion.Angle(progress) > 25f);
        Assert.False(HatchMotion.IsPassage(progress, true));
        Assert.False(HatchMotion.IsLanding(progress, true));

        // Cleared, the pair resumes to fully open.
        script.Rule = (_, _) => false;
        for (int frame = 0; frame < 300; ++frame) { progress = HatchMotion.Step(progress, true, 1f / 60f, 1f, pair); }
        Assert.True(HatchMotion.IsPassage(progress, true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ClosingOverSomeoneUnderOneLeafHoldsBothOpenUntilClear(int which)
    {
        CellarLeaf blocked = which == 0 ? CellarLeaf.Left : CellarLeaf.Right;
        var script = new Script { Rule = (leaf, angle) => leaf == blocked && angle < 60f };
        var pair = new CellarPairOccupancy<Script>(script);
        float progress = 1f;
        for (int frame = 0; frame < 300; ++frame) { progress = HatchMotion.Step(progress, false, 1f / 60f, 1f, pair); }
        Assert.True(HatchMotion.Angle(progress) >= 60f);
        Assert.False(HatchMotion.IsLanding(progress, false));
        Assert.False(HatchMotion.IsPassage(progress, false));

        // Cleared, the pair resumes to fully closed.
        script.Rule = (_, _) => false;
        for (int frame = 0; frame < 300; ++frame) { progress = HatchMotion.Step(progress, false, 1f / 60f, 1f, pair); }
        Assert.True(HatchMotion.IsLanding(progress, false));
    }

    [Fact]
    public void ThePairSettlesWhereBothLeavesAreClear()
    {
        // Stored open, but something stands where the open right leaf would be: the pair starts closed, not split.
        var script = new Script { Rule = (leaf, angle) => leaf == CellarLeaf.Right && angle > 80f };
        float progress = HatchMotion.Settle(true, new CellarPairOccupancy<Script>(script));
        Assert.Equal(0f, progress);
        script.Rule = (_, _) => false;
        Assert.Equal(1f, HatchMotion.Settle(true, new CellarPairOccupancy<Script>(script)));
    }
}
