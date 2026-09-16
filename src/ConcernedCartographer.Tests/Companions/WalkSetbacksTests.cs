using System.Linq;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.Companions.Surroundings;
using Xunit;

namespace TheConcernedCat.ConcernedCartographer.Tests.Companions;

/// <summary>What he learns from a walk that did not work (#310): the spot, and
/// the place and the way through it that stopped him - so one wall is one bump,
/// not one for every spot around the fire behind it.</summary>
public sealed class WalkSetbacksTests
{
    // Stopped at the origin, floor at y = 10, walking north (+Z) into something
    // just ahead.
    private static readonly WorldPoint Stopped = At(0f, 0f);
    private static readonly WorldPoint North = At(0f, 1.5f);

    private static WorldPoint At(float x, float z, float y = 10f)
    {
        return new WorldPoint(x, y, z);
    }

    private static WalkSetbacks StoppedGoingNorth(float now = 0f)
    {
        var setbacks = new WalkSetbacks();
        setbacks.Remember(At(0f, 8f), Stopped, North, now);
        return setbacks;
    }

    [Fact]
    public void TheWallFromTheSignOffSessionStopsTheSecondSpotBehindItToo()
    {
        // b35c2d8, 16:10: stopped at (-37.0, 81.3, 23.0) by a woodwall, going
        // for a spot by the fire under the roof. The next look chose another
        // spot by the same fire, routed through the same gap, and he walked into
        // the same wall - six times, alternating.
        var setbacks = new WalkSetbacks();
        WalkSetback first = setbacks.Remember(
            new WorldPoint(-34.5f, 81.9f, 29.8f),
            new WorldPoint(-37.0f, 81.3f, 23.0f),
            new WorldPoint(-36.9f, 81.4f, 24.5f),
            now: 100f);
        Assert.True(first.HasPlace);

        WorldPoint[] toTheOtherSpot =
        {
            new WorldPoint(-30.0f, 83.0f, 18.0f),
            new WorldPoint(-37.2f, 81.3f, 21.5f),
            new WorldPoint(-36.9f, 81.4f, 24.5f),
            new WorldPoint(-35.0f, 82.0f, 29.0f),
        };
        Assert.True(setbacks.RefusesRoute(toTheOtherSpot, now: 101f));

        // The seat he did reach went round the other side: away from the gap,
        // then north well clear of it.
        WorldPoint[] roundTheOtherSide =
        {
            new WorldPoint(-37.0f, 81.3f, 23.0f),
            new WorldPoint(-40.0f, 81.3f, 20.0f),
            new WorldPoint(-42.0f, 83.5f, 30.0f),
        };
        Assert.False(setbacks.RefusesRoute(roundTheOtherSide, now: 101f));
    }

    [Fact]
    public void SeveralSpotsAreLeftAloneAtOnce()
    {
        // The old memory held one spot, so failing the second spot freed the
        // first - A, B, A, B.
        var setbacks = new WalkSetbacks();
        setbacks.Remember(At(0f, 8f), now: 0f);
        setbacks.Remember(At(5f, 8f), now: 1f);

        Assert.True(setbacks.RefusesSpot(At(0f, 8f), now: 2f));
        Assert.True(setbacks.RefusesSpot(At(5.8f, 8.9f), now: 2f));
        Assert.False(setbacks.RefusesSpot(At(10f, 8f), now: 2f));

        // A spot on the floor above is not the same spot.
        Assert.False(setbacks.RefusesSpot(At(0f, 8f, y: 13f), now: 2f));
    }

    [Fact]
    public void TheSameWayThroughTheSamePlaceIsRefusedFromAnywhere()
    {
        WalkSetbacks setbacks = StoppedGoingNorth();

        // From well back, straight through.
        Assert.True(setbacks.RefusesRoute(new[] { At(0f, -6f), At(0f, 6f) }, now: 1f));

        // From right where he sat down, on the same way.
        Assert.True(setbacks.RefusesRoute(new[] { Stopped, At(0.4f, 5f) }, now: 1f));

        // Slightly aside and slightly askew is still the same gap.
        Assert.True(setbacks.RefusesRoute(new[] { At(-3f, -4f), At(0.5f, 0.2f), At(1.5f, 6f) }, now: 1f));
    }

    [Fact]
    public void AWayAwayFromItOrAcrossItIsStillOpen()
    {
        WalkSetbacks setbacks = StoppedGoingNorth();

        // Back the way he came.
        Assert.False(setbacks.RefusesRoute(new[] { Stopped, At(0f, -6f) }, now: 1f));

        // Along the wall, either way.
        Assert.False(setbacks.RefusesRoute(new[] { At(-5f, 0f), At(5f, 0f) }, now: 1f));
        Assert.False(setbacks.RefusesRoute(new[] { At(5f, 0.3f), At(-5f, 0.3f) }, now: 1f));

        // Through the same place, coming the other way.
        Assert.False(setbacks.RefusesRoute(new[] { At(0f, 6f), At(0f, -6f) }, now: 1f));
    }

    [Fact]
    public void ARouteWideOfThePlaceOrOnAnotherFloorDoesNotGoThroughIt()
    {
        WalkSetbacks setbacks = StoppedGoingNorth();

        Assert.False(setbacks.RefusesRoute(new[] { At(1.5f, -6f), At(1.5f, 6f) }, now: 1f));
        Assert.False(setbacks.RefusesRoute(new[] { At(0f, -6f, y: 13f), At(0f, 6f, y: 13f) }, now: 1f));
    }

    [Fact]
    public void ALegThatStopsShortOfThePlaceOrStartsBeyondItDoesNotGoThroughIt()
    {
        WalkSetbacks setbacks = StoppedGoingNorth();

        // Up to a metre short, then off along the wall.
        Assert.False(setbacks.RefusesRoute(new[] { At(0f, -6f), At(0f, -1f), At(4f, -1f) }, now: 1f));

        // Starting on the far side of what stopped him.
        Assert.False(setbacks.RefusesRoute(new[] { At(0f, 1f), At(0f, 6f) }, now: 1f));

        // But a few centimetres past where he stood is where he stood.
        Assert.True(setbacks.RefusesRoute(new[] { At(0f, 0.3f), At(0f, 6f) }, now: 1f));
    }

    [Fact]
    public void NothingIsLeftAloneForever()
    {
        WalkSetbacks setbacks = StoppedGoingNorth(now: 10f);
        WorldPoint[] through = { At(0f, -6f), At(0f, 6f) };

        Assert.True(setbacks.RefusesSpot(At(0f, 8f), now: 69.9f));
        Assert.True(setbacks.RefusesRoute(through, now: 69.9f));

        Assert.False(setbacks.RefusesSpot(At(0f, 8f), now: 70f));
        Assert.False(setbacks.RefusesRoute(through, now: 70f));
    }

    [Fact]
    public void TheSameTroubleAgainIsLeftAloneLongerUpToFiveMinutes()
    {
        var setbacks = new WalkSetbacks();

        // Each time the pause is over he tries again, and walks into the same
        // wall - on the way to a different spot each time, four metres apart,
        // so it is the place that makes it "again", not the spot.
        float now = 0f;
        float[] pauses = new float[6];
        for (int attempt = 0; attempt < pauses.Length; attempt++)
        {
            WalkSetback setback = setbacks.Remember(At(attempt * 4f, 8f), Stopped, North, now);
            pauses[attempt] = setback.PauseSeconds;
            Assert.Equal(attempt + 1, setback.Strikes);
            now = setback.Until + 1f;
        }

        Assert.Equal(new[] { 60f, 120f, 240f, 300f, 300f, 300f }, pauses);

        // The same trouble takes over from the time before: one memory, not six.
        Assert.Single(setbacks.Remembered);
    }

    [Fact]
    public void AnotherPlaceOrAnotherWayIsAFirstTime()
    {
        WalkSetbacks setbacks = StoppedGoingNorth();

        WalkSetback elsewhere = setbacks.Remember(At(20f, 8f), At(20f, 0f), At(20f, 1.5f), now: 1f);
        WalkSetback otherWay = setbacks.Remember(At(0f, -8f), Stopped, At(0f, -1.5f), now: 2f);

        Assert.Equal(1, elsewhere.Strikes);
        Assert.Equal(1, otherWay.Strikes);
        Assert.Equal(3, setbacks.Remembered.Count);
    }

    [Fact]
    public void TroubleLongAgoCountsAsAFirstTimeAgain()
    {
        WalkSetbacks setbacks = StoppedGoingNorth(now: 0f);

        WalkSetback muchLater = setbacks.Remember(
            At(0f, 8f), Stopped, North, now: 60f + WalkSetbacks.RecallSeconds + 1f);

        Assert.Equal(1, muchLater.Strikes);
        Assert.Equal(WalkSetbacks.FirstPauseSeconds, muchLater.PauseSeconds);
    }

    [Fact]
    public void TakingTooLongLeavesOnlyTheSpotAlone()
    {
        var setbacks = new WalkSetbacks();
        WalkSetback setback = setbacks.Remember(At(0f, 8f), now: 0f);

        Assert.False(setback.HasPlace);
        Assert.True(setbacks.RefusesSpot(At(0f, 8f), now: 1f));
        Assert.False(setbacks.RefusesRoute(new[] { At(0f, -6f), At(0f, 6f) }, now: 1f));
    }

    [Fact]
    public void WithNoWayToTellWhichWayHeWasGoingOnlyTheSpotIsLeftAlone()
    {
        var setbacks = new WalkSetbacks();

        // Stopped on the very point he was heading for, which is also the spot.
        WalkSetback setback = setbacks.Remember(Stopped, Stopped, Stopped, now: 0f);

        Assert.False(setback.HasPlace);
        Assert.True(setbacks.RefusesSpot(Stopped, now: 1f));
        Assert.False(setbacks.RefusesRoute(new[] { At(0f, -6f), At(0f, 6f) }, now: 1f));

        // Stopped on the point he was heading for, short of the spot: the way
        // to the spot is the way he was going.
        WalkSetback towardsTheSpot = setbacks.Remember(At(0f, 8f), Stopped, Stopped, now: 2f);
        Assert.True(towardsTheSpot.HasPlace);
        Assert.True(setbacks.RefusesRoute(new[] { At(0f, -6f), At(0f, 6f) }, now: 3f));
    }

    [Fact]
    public void HeRemembersAHandfulAndForgetsTheOneThatEndedFirst()
    {
        var setbacks = new WalkSetbacks();
        for (int index = 0; index < WalkSetbacks.Capacity + 3; index++)
        {
            setbacks.Remember(At(index * 10f, 8f), now: index);
        }

        Assert.Equal(WalkSetbacks.Capacity, setbacks.Remembered.Count);
        Assert.False(setbacks.RefusesSpot(At(0f, 8f), now: 20f));
        Assert.True(setbacks.RefusesSpot(At((WalkSetbacks.Capacity + 2) * 10f, 8f), now: 20f));
        Assert.Equal(
            Enumerable.Range(3, WalkSetbacks.Capacity).Select(index => index * 10f),
            setbacks.Remembered.Select(setback => setback.Spot.X));
    }

    [Fact]
    public void ARouteOfFewerThanTwoCornersGoesNowhere()
    {
        WalkSetbacks setbacks = StoppedGoingNorth();

        Assert.False(setbacks.RefusesRoute(new WorldPoint[0], now: 1f));
        Assert.False(setbacks.RefusesRoute(new[] { Stopped }, now: 1f));
        Assert.False(setbacks.RefusesRoute(new[] { Stopped, Stopped }, now: 1f));
    }

    [Fact]
    public void ClearForgetsEverything()
    {
        WalkSetbacks setbacks = StoppedGoingNorth();
        setbacks.Clear();

        Assert.Empty(setbacks.Remembered);
        Assert.False(setbacks.RefusesSpot(At(0f, 8f), now: 1f));
        Assert.False(setbacks.RefusesRoute(new[] { At(0f, -6f), At(0f, 6f) }, now: 1f));
    }

    [Theory]
    [InlineData(1, 60f)]
    [InlineData(2, 120f)]
    [InlineData(3, 240f)]
    [InlineData(4, 300f)]
    [InlineData(40, 300f)]
    public void APauseDoublesEachTimeUpToTheLongest(int strikes, float seconds)
    {
        Assert.Equal(seconds, WalkSetbacks.PauseFor(strikes));
    }
}
