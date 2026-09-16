using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.Companions.Surroundings;
using Xunit;

namespace TheConcernedCat.ConcernedCartographer.Tests.Companions;

/// <summary>The owner's rules, 2026-09-16, one test each: a fire and a drink,
/// the fire closest to the bed, outside by day and inside by night, and a
/// spare bed until morning.</summary>
public sealed class CommonSenseTests
{
    private static readonly WorldPoint Home = new WorldPoint(0f, 30f, 0f);

    private static CampFire Fire(float x, float z, bool sheltered = false, bool burning = true)
    {
        return new CampFire(new WorldPoint(x, 30f, z), burning, sheltered, hazardMetres: 1f);
    }

    private static CampBed Bed(float x, float z, bool claimed = false)
    {
        return new CampBed(new WorldPoint(x, 30.5f, z), 90f, claimed, sheltered: true);
    }

    private static IReadOnlyList<HangoutIntent> Wishes(
        bool night = false, bool wet = false, CampFire[]? fires = null, CampBed[]? beds = null)
    {
        return CommonSense.Preferences(
            new CampSnapshot(Home, night, wet, fires, beds), CompanionTemperament.Hulgi);
    }

    private static string Describe(IReadOnlyList<HangoutIntent> wishes)
    {
        return string.Join(", ", wishes.Select(w => w.Kind + (w.Target >= 0 ? "#" + w.Target : "")));
    }

    [Fact]
    public void ByDayHeGoesToTheFireClosestToTheBedInsideOrOut()
    {
        // The owner's camp: a fire moved outside the cottage. And a second one
        // further off, inside somewhere else.
        var wishes = Wishes(fires: new[] { Fire(12f, 0f, sheltered: true), Fire(6f, 2f) });
        Assert.Equal("Fire#1, Fire#0, Home", Describe(wishes));
    }

    [Fact]
    public void ByNightTheOnlyFireOutsideComesAfterARoof()
    {
        // "If no fire inside but there's outside, he sits outside during the
        // day and inside if available during night."
        CampFire[] fires = { Fire(6f, 2f) };
        Assert.Equal("Fire#0, Home", Describe(Wishes(fires: fires)));
        Assert.Equal("Shelter, Fire#0, Home", Describe(Wishes(night: true, fires: fires)));
    }

    [Fact]
    public void ByNightAFireUnderARoofIsBestOfBoth()
    {
        CampFire[] fires = { Fire(4f, 0f), Fire(9f, 0f, sheltered: true) };
        Assert.Equal("Fire#1, Shelter, Fire#0, Home", Describe(Wishes(night: true, fires: fires)));
    }

    [Fact]
    public void RainSendsHimUnderARoofButNotToBed()
    {
        CampFire[] fires = { Fire(4f, 0f) };
        CampBed[] beds = { Bed(2f, 2f) };
        Assert.Equal("Shelter, Fire#0, Home", Describe(Wishes(wet: true, fires: fires, beds: beds)));
    }

    [Fact]
    public void AtNightASpareBedComesFirstAndAClaimedOneNever()
    {
        CampBed[] beds = { Bed(8f, 0f), Bed(1f, 1f, claimed: true), Bed(3f, 0f) };
        var wishes = Wishes(night: true, fires: new[] { Fire(5f, 5f) }, beds: beds);
        Assert.Equal("Sleep#2, Sleep#0, Shelter, Fire#0, Home", Describe(wishes));

        // By day nobody sleeps.
        Assert.DoesNotContain(Wishes(fires: new[] { Fire(5f, 5f) }, beds: beds), w => w.Kind == HangoutKind.Sleep);
    }

    [Fact]
    public void AFireThatIsOutOrFarAwayIsNotHisFire()
    {
        var wishes = Wishes(fires: new[] { Fire(3f, 0f, burning: false), Fire(80f, 0f) });
        Assert.Equal("Home", Describe(wishes));

        // And a bed in the next village is not his bed.
        Assert.Equal("Shelter, Home", Describe(Wishes(night: true, beds: new[] { Bed(90f, 0f) })));
    }

    [Fact]
    public void HomeIsAlwaysOnTheListAndAlwaysLast()
    {
        foreach (bool night in new[] { false, true })
        {
            foreach (bool wet in new[] { false, true })
            {
                var wishes = Wishes(night, wet, new[] { Fire(2f, 2f) }, new[] { Bed(1f, 1f) });
                Assert.Equal(HangoutKind.Home, wishes[wishes.Count - 1].Kind);
                Assert.Single(wishes, w => w.Kind == HangoutKind.Home);
            }
        }
    }

    [Fact]
    public void TheSameCampAlwaysGivesTheSameAnswer()
    {
        // Two fires exactly as far from home.
        CampFire[] fires = { Fire(0f, 5f), Fire(5f, 0f), Fire(-5f, 0f) };
        string first = Describe(Wishes(fires: fires));
        Assert.Equal("Fire#2, Fire#0, Fire#1, Home", first);
        Assert.Equal(first, Describe(Wishes(fires: fires)));
    }

    [Fact]
    public void ASeatBeatsTheGroundForTheSameWishAndAnyBetterWishBeatsBoth()
    {
        Assert.True(CommonSense.Rank(0, onSeat: true) < CommonSense.Rank(0, onSeat: false));
        Assert.True(CommonSense.Rank(0, onSeat: false) < CommonSense.Rank(1, onSeat: true));
        Assert.True(CommonSense.Rank(5, onSeat: false) < CommonSense.Nowhere);
    }

    [Fact]
    public void ACompanionWhoDoesNotCareForFireOrBedsJustStaysAround()
    {
        var hermit = new CompanionTemperament(lovesFire: false, sleepsInSpareBeds: false, campRadiusMetres: 25f);
        var wishes = CommonSense.Preferences(
            new CampSnapshot(Home, night: true, wet: false, new[] { Fire(2f, 2f) }, new[] { Bed(1f, 1f) }),
            hermit);
        Assert.Equal("Shelter, Home", Describe(wishes));
    }
}
