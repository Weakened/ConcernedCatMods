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
        BedClaim claim = BedClaim.Unclaimed) =>
        new HousingFacts(key, measured, inside, roof, cover, warm, claim);

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

    // ---- ownership is three-way, because vanilla's is ----------------------

    [Fact]
    public void ABedSomebodyElseSleepsInIsNeverTaken()
    {
        Assert.Equal(
            HousingRefusal.AlreadyClaimed,
            HousingRules.Judge(Bed(claim: BedClaim.SomebodyElses)));
    }

    [Fact]
    public void YourOwnBedIsItsOwnAnswerAndNotAStrangersClaim()
    {
        // "Somebody already sleeps in it" about the player's own bed reads as a
        // refusal of the building the feature exists to validate.
        Assert.Equal(
            HousingRefusal.YourOwnBed,
            HousingRules.Judge(Bed(claim: BedClaim.YoursAndCurrent)));
    }

    [Fact]
    public void ABedYouOwnButNoLongerSleepInIsStillFree()
    {
        // The regression this exists to stop: nothing in vanilla ever clears
        // s_owner, so reading ownership as a plain flag retires every bed a
        // player has ever slept in and pins the settlement's capacity at zero
        // forever. Vanilla itself hands such a bed straight back.
        Assert.Equal(
            HousingRefusal.None,
            HousingRules.Judge(Bed(claim: BedClaim.YoursButNotCurrent)));
    }

    [Fact]
    public void AStaleClaimOfYourOwnIsStillJudgedOnTheBuilding()
    {
        // Falling through the ownership check must not skip the roof.
        Assert.Equal(
            HousingRefusal.NoRoof,
            HousingRules.Judge(Bed(claim: BedClaim.YoursButNotCurrent, roof: false)));
    }

    [Fact]
    public void APlaythroughOfStaleClaimsDoesNotEmptyTheSettlement()
    {
        // Four beds slept in over a playthrough, one of them current.
        HousingCapacity capacity = HousingCapacity.Measure(new[]
        {
            Bed("a", claim: BedClaim.YoursButNotCurrent),
            Bed("b", claim: BedClaim.YoursButNotCurrent),
            Bed("c", claim: BedClaim.YoursButNotCurrent),
            Bed("d", claim: BedClaim.YoursAndCurrent),
        });

        Assert.Equal(3, capacity.Habitable);
        Assert.Equal(1, capacity.Occupied);
    }

    [Fact]
    public void ABedOutsideTheSettlementIsNotTheSettlementsToOffer()
    {
        Assert.Equal(HousingRefusal.OutsideSettlement, HousingRules.Judge(Bed(inside: false)));
    }

    [Fact]
    public void OursAreAskedFirstBecauseTheyAreNotAboutTheBuilding()
    {
        // A stranger's bed in a shed with no roof is refused as theirs: telling
        // the player to roof somebody else's bed would be advice they must not
        // act on.
        Assert.Equal(
            HousingRefusal.AlreadyClaimed,
            HousingRules.Judge(Bed(claim: BedClaim.SomebodyElses, roof: false, warm: false)));
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
        Assert.True(capacity.Surveyed);
        Assert.Contains("houses nobody", capacity.Describe());
    }

    [Fact]
    public void NoBedsAtAllIsNotTheSameAsNotHavingLooked()
    {
        // The name of this test used to be a promise the type did not keep:
        // both answers were zero and nothing could tell them apart, so a failed
        // survey was read out as an affirmative "it houses nobody" and a player
        // would go and rebuild a cottage that was fine.
        HousingCapacity looked = HousingCapacity.Measure(Array.Empty<HousingFacts>());
        HousingCapacity didNot = HousingCapacity.NotSurveyed;

        Assert.True(looked.Surveyed);
        Assert.False(didNot.Surveyed);
        Assert.NotEqual(looked.Describe(), didNot.Describe());
        Assert.Contains("houses nobody", looked.Describe());
        Assert.DoesNotContain("houses nobody", didNot.Describe());
        Assert.Contains("could not be checked", didNot.Describe());
    }

    [Fact]
    public void ANullSurveyIsNotASurvey()
    {
        Assert.False(HousingCapacity.Measure(null).Surveyed);
        Assert.Equal(0, HousingCapacity.NotSurveyed.Habitable);
        Assert.Empty(HousingCapacity.NotSurveyed.Beds);
    }

    [Fact]
    public void CapacityIsTheCountOfBedsSomebodyCouldActuallySleepIn()
    {
        HousingCapacity capacity = HousingCapacity.Measure(new[]
        {
            Bed("a"),
            Bed("b"),
            Bed("c", claim: BedClaim.SomebodyElses),
            Bed("d", roof: false),
            Bed("e", cover: 0.5f),
            Bed("f", warm: false),
            Bed("g", inside: false),
        });

        Assert.Equal(2, capacity.Habitable);
        Assert.Equal(1, capacity.Occupied);
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

    // ---- a partial count says it is partial ---------------------------------

    [Fact]
    public void ATruncatedSurveySaysTheCountIsAFloor()
    {
        string text = HousingCapacity.Measure(new[] { Bed("hut") }, truncated: true).Describe();

        Assert.Contains("at least", text);
        Assert.Contains("not exactly", text);
    }

    [Fact]
    public void ACompleteSurveyClaimsNothingAboutBedsItDidNotSkip()
    {
        Assert.DoesNotContain("at least", HousingCapacity.Measure(new[] { Bed("hut") }).Describe());
    }

    [Fact]
    public void GroundThatIsNotLoadedIsSaidOutLoudRatherThanCountedAsEmpty()
    {
        // A bed in an unloaded zone is not in the world's piece list at all, so
        // it can never come back as "could not be checked". The only place that
        // fact can reach a player is the survey itself.
        string text = HousingCapacity
            .Measure(Array.Empty<HousingFacts>(), groundIncomplete: true)
            .Describe();

        Assert.Contains("not loaded", text);
        Assert.DoesNotContain("houses nobody", text);
    }

    [Fact]
    public void GroundThatIsNotLoadedQualifiesACountThatFoundBeds()
    {
        string text = HousingCapacity
            .Measure(new[] { Bed("hut") }, groundIncomplete: true)
            .Describe();

        Assert.Contains("1 free place", text);
        Assert.Contains("not loaded", text);
    }

    // ---- what a player reads -----------------------------------------------

    [Fact]
    public void EveryRefusalHasASentenceAndNoneOfThemSaysBug()
    {
        foreach (HousingRefusal refusal in Enum.GetValues(typeof(HousingRefusal)))
        {
            string sentence = HousingSentences.For(refusal);
            Assert.False(string.IsNullOrWhiteSpace(sentence));
            Assert.DoesNotContain("bug", sentence, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void EveryRefusalIsReachableFromSomeSetOfFacts()
    {
        // The enum-iterating test above proves every value has a string, not
        // that any of them can happen. A dead value survived exactly that gap
        // once already: it had a sentence, a doc comment describing a state the
        // code could not produce, and a passing test.
        var produced = new HashSet<HousingRefusal>();
        foreach (HousingFacts facts in new[]
        {
            Bed(),
            Bed(roof: false),
            Bed(cover: 0f),
            Bed(warm: false),
            Bed(claim: BedClaim.SomebodyElses),
            Bed(claim: BedClaim.YoursAndCurrent),
            Bed(inside: false),
            HousingFacts.Unmeasured("x"),
        })
        {
            produced.Add(HousingRules.Judge(facts));
        }

        foreach (HousingRefusal refusal in Enum.GetValues(typeof(HousingRefusal)))
        {
            Assert.Contains(refusal, produced);
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

        Assert.Contains("1 free place", text);
        Assert.Contains("hut", text);
        Assert.Contains("shed", text);
        Assert.Contains("needs a roof", text);
    }

    [Fact]
    public void FreeAndLivedInAreReportedApartSoNoRoomIsNotReadAsNoHousing()
    {
        string text = HousingCapacity.Measure(new[]
        {
            Bed("mine", claim: BedClaim.YoursAndCurrent),
            Bed("theirs", claim: BedClaim.SomebodyElses),
        }).Describe();

        Assert.Contains("0 free places", text);
        Assert.Contains("2 lived in", text);
        Assert.Contains("you sleep here", text);
        Assert.Contains("somebody else already sleeps in it", text);
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
        Assert.Equal(BedClaim.Unclaimed, facts.Claim);
        Assert.Equal("bed", facts.BedKey);
    }
}
