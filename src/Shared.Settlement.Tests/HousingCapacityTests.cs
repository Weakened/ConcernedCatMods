using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Housing;

namespace Shared.Settlement.Tests;

/// <summary>CF-SET-009 (#286): capacity is measured from the real building using
/// vanilla's own bed rules, and a blueprint alone houses nobody.</summary>
public sealed class HousingCapacityTests
{
    private static HousingFacts Bed(
        string key = "bed-1",
        bool measured = true,
        bool inside = true,
        bool roof = true,
        float cover = 1f,
        bool warm = true,
        bool claimed = false) =>
        new HousingFacts(key, measured, inside, roof, cover, warm, claimed);

    // ---- vanilla's own rules ----------------------------------------------

    [Fact]
    public void ABedUnderAGoodRoofByAFireHousesSomebody()
    {
        Assert.Equal(HousingRefusal.None, HousingRules.Judge(Bed()));
    }

    [Fact]
    public void WithoutARoofItIsRefusedTheWayVanillaRefusesIt()
    {
        Assert.Equal(HousingRefusal.NoRoof, HousingRules.Judge(Bed(roof: false)));
    }

    [Fact]
    public void UnderARoofButTooOpenIsItsOwnReason()
    {
        // Bed.CheckExposure refuses the roof first and the cover second, and a
        // player told "it needs a roof" when it has one would go and build a
        // second roof.
        Assert.Equal(HousingRefusal.TooExposed, HousingRules.Judge(Bed(cover: 0.79f)));
    }

    [Fact]
    public void TheCoverThresholdIsVanillasAndTheBoundaryCounts()
    {
        Assert.Equal(0.8f, HousingRules.RequiredCover);
        Assert.Equal(HousingRefusal.None, HousingRules.Judge(Bed(cover: HousingRules.RequiredCover)));
        Assert.Equal(
            HousingRefusal.TooExposed,
            HousingRules.Judge(Bed(cover: HousingRules.RequiredCover - 0.0001f)));
    }

    [Fact]
    public void WithoutAFireItIsRefused()
    {
        Assert.Equal(HousingRefusal.NoFire, HousingRules.Judge(Bed(warm: false)));
    }

    // ---- the rules that are ours -------------------------------------------

    [Fact]
    public void ABedSomebodyAlreadySleepsInIsNeverTaken()
    {
        Assert.Equal(HousingRefusal.AlreadyClaimed, HousingRules.Judge(Bed(claimed: true)));
    }

    [Fact]
    public void ABedOutsideTheSettlementIsNotTheSettlementsToOffer()
    {
        Assert.Equal(HousingRefusal.OutsideSettlement, HousingRules.Judge(Bed(inside: false)));
    }

    [Fact]
    public void OursAreAskedFirstBecauseTheyAreNotAboutTheBuilding()
    {
        // A claimed bed in a shed with no roof is refused as claimed: telling
        // the player to roof somebody else's bed would be advice they must not
        // act on.
        Assert.Equal(
            HousingRefusal.AlreadyClaimed,
            HousingRules.Judge(Bed(claimed: true, roof: false, warm: false)));
    }

    // ---- not measured is not uninhabitable ---------------------------------

    [Fact]
    public void ABedThatCouldNotBeCheckedIsNotABedThatFailed()
    {
        Assert.Equal(HousingRefusal.NotMeasured, HousingRules.Judge(HousingFacts.Unmeasured("bed-far")));
    }

    [Fact]
    public void AnUnmeasuredBedIsCountedApartFromTheRefusals()
    {
        HousingCapacity capacity = HousingCapacity.Measure(new[]
        {
            Bed("good"),
            Bed("cold", warm: false),
            HousingFacts.Unmeasured("unloaded"),
        });

        Assert.Equal(1, capacity.Habitable);
        Assert.Equal(1, capacity.Unmeasured);
        Assert.Equal(3, capacity.Beds.Count);
    }

    // ---- capacity comes from real beds -------------------------------------

    [Fact]
    public void ASettlementWithNoBedsHousesNobodyAndSaysSo()
    {
        HousingCapacity capacity = HousingCapacity.Measure(Array.Empty<HousingFacts>());

        Assert.Equal(0, capacity.Habitable);
        Assert.Contains("houses nobody", capacity.Describe());
    }

    [Fact]
    public void NoBedsAtAllIsNotTheSameAsNotHavingLooked()
    {
        // A blueprint houses nobody; so does a null survey. Both are zero, and
        // neither is allowed to be a positive number.
        Assert.Equal(0, HousingCapacity.Measure(null).Habitable);
        Assert.Equal(0, HousingCapacity.None.Habitable);
        Assert.Empty(HousingCapacity.None.Beds);
    }

    [Fact]
    public void CapacityIsTheCountOfBedsSomebodyCouldActuallySleepIn()
    {
        HousingCapacity capacity = HousingCapacity.Measure(new[]
        {
            Bed("a"),
            Bed("b"),
            Bed("c", claimed: true),
            Bed("d", roof: false),
            Bed("e", cover: 0.5f),
            Bed("f", warm: false),
            Bed("g", inside: false),
        });

        Assert.Equal(2, capacity.Habitable);
        Assert.Equal(0, capacity.Unmeasured);
    }

    [Fact]
    public void EveryBedKeepsItsOwnVerdictInTheOrderItWasGiven()
    {
        HousingCapacity capacity = HousingCapacity.Measure(new[]
        {
            Bed("first", warm: false),
            Bed("second"),
        });

        Assert.Equal("first", capacity.Beds[0].Key);
        Assert.Equal(HousingRefusal.NoFire, capacity.Beds[0].Value);
        Assert.Equal("second", capacity.Beds[1].Key);
        Assert.Equal(HousingRefusal.None, capacity.Beds[1].Value);
    }

    // ---- what a player reads -----------------------------------------------

    [Fact]
    public void EveryRefusalHasASentenceAndNoneOfThemSaysBug()
    {
        foreach (HousingRefusal refusal in Enum.GetValues(typeof(HousingRefusal)))
        {
            string sentence = HousingRules.Describe(refusal);
            Assert.False(string.IsNullOrWhiteSpace(sentence));
            Assert.DoesNotContain("bug", sentence, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheReadoutNamesEveryBedAndItsReason()
    {
        string text = HousingCapacity.Measure(new[]
        {
            Bed("hut"),
            Bed("shed", roof: false),
        }).Describe();

        Assert.Contains("1 place", text);
        Assert.Contains("hut", text);
        Assert.Contains("shed", text);
        Assert.Contains("needs a roof", text);
    }

    [Fact]
    public void BedsThatCouldNotBeCheckedAreSaidOutLoud()
    {
        string text = HousingCapacity.Measure(new[]
        {
            Bed("hut"),
            HousingFacts.Unmeasured("far"),
        }).Describe();

        Assert.Contains("could not be checked", text);
    }

    [Fact]
    public void AnUnmeasuredFactCarriesNothingElseWorthReading()
    {
        HousingFacts facts = HousingFacts.Unmeasured("bed");

        Assert.False(facts.Measured);
        Assert.False(facts.UnderRoof);
        Assert.False(facts.Warm);
        Assert.Equal(0f, facts.CoverPercentage);
        Assert.Equal("bed", facts.BedKey);
    }
}
