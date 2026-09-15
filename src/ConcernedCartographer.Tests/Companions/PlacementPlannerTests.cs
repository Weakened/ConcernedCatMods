using TheConcernedCat.Companions.Placement;

namespace ConcernedCartographer.Tests.Companions;

public class PlacementPlannerTests
{
    private static readonly CompanionAnchor Bed =
        new(AnchorKind.ClaimedBed, new WorldPoint(0f, 30f, 0f));

    [Fact]
    public void PlacesTheCompanionOnClearGroundWithinTheRadiusBand()
    {
        PlacementResult result = new PlacementPlanner().Plan(Bed, StubProbe.AllClear());

        Assert.True(result.Found);
        Assert.Equal(CompanionPose.SitOnGround, result.Pose);

        float distance = result.Position.HorizontalDistanceTo(Bed.Position);
        Assert.InRange(distance, PlacementRules.DefaultMinimumRadius, PlacementRules.DefaultMaximumRadius);
    }

    [Fact]
    public void NeverProbesMoreThanItsBound()
    {
        var probe = StubProbe.AlwaysRejected(PlacementRejection.Occupied);
        PlacementResult result = new PlacementPlanner().Plan(Bed, probe);

        Assert.False(result.Found);
        Assert.Equal(result.CandidatesProbed, probe.Calls);
        Assert.True(probe.Calls <= PlacementPlanner.MaximumCandidates);
    }

    [Fact]
    public void NeverLooksOutsideTheConfiguredRadius()
    {
        // The bound on where a companion may sit is also the bound on how much
        // world gets inspected.
        var rules = new PlacementRules(minimumRadius: 3f, maximumRadius: 10f);
        var probe = StubProbe.AllClear();
        new PlacementPlanner(rules).Plan(Bed, probe);

        foreach (WorldPoint probed in probe.Probed)
        {
            Assert.True(probed.HorizontalDistanceTo(Bed.Position) <= 10.001f);
        }
    }

    [Fact]
    public void DefersWhenNothingIsAvailableRatherThanPlacingAnyway()
    {
        PlacementResult result = new PlacementPlanner()
            .Plan(Bed, StubProbe.AlwaysRejected(PlacementRejection.Water));

        Assert.False(result.Found);
        Assert.True(result.BlockedBy.HasFlag(PlacementRejection.Water));
    }

    [Fact]
    public void DefersWithoutAnAnchor()
    {
        PlacementResult result = new PlacementPlanner().Plan(CompanionAnchor.None, StubProbe.AllClear());

        Assert.False(result.Found);
        Assert.Equal(0, result.CandidatesProbed);
    }

    [Fact]
    public void DefersWhileTheAreaIsStillLoading()
    {
        PlacementResult result = new PlacementPlanner()
            .Plan(Bed, StubProbe.AlwaysRejected(PlacementRejection.NotLoaded));

        Assert.False(result.Found);
        Assert.True(result.BlockedBy.HasFlag(PlacementRejection.NotLoaded));
    }

    // The parameter is an int rather than the flags enum itself: the enum is
    // internal, and a public xUnit theory cannot take an internal parameter.
    [Theory]
    [InlineData(1 << 1)]  // Water
    [InlineData(1 << 2)]  // Unsupported
    [InlineData(1 << 3)]  // TooSteep
    [InlineData(1 << 4)]  // Occupied
    [InlineData(1 << 5)]  // Fire
    [InlineData(1 << 6)]  // Doorway
    [InlineData(1 << 7)]  // Bed
    [InlineData(1 << 8)]  // Shrine
    public void EveryHazardRulesACandidateOut(int hazardFlag)
    {
        var hazard = (PlacementRejection)hazardFlag;
        Assert.True(Enum.IsDefined(typeof(PlacementRejection), hazard));

        // Exactly one spot is clear; everything else carries the hazard.
        var probe = new StubProbe(position =>
            position.X > 4f
                ? Clear(position)
                : new PlacementProbeSample(position, hazard, -1f, SeatAvailability.None));

        PlacementResult result = new PlacementPlanner().Plan(Bed, probe);

        Assert.True(result.Found);
        Assert.True(result.Position.X > 4f);
    }

    [Fact]
    public void PrefersAFreeSeatOverBareGround()
    {
        var probe = new StubProbe(position =>
            position.Z > 3f
                ? new PlacementProbeSample(position, PlacementRejection.None, -1f, SeatAvailability.Free)
                : Clear(position));

        PlacementResult result = new PlacementPlanner().Plan(Bed, probe);

        Assert.Equal(CompanionPose.SitOnSeat, result.Pose);
        Assert.True(result.Position.Z > 3f);
    }

    [Fact]
    public void PrefersWarmthOverBareGround()
    {
        var probe = new StubProbe(position =>
            position.Z > 3f
                ? new PlacementProbeSample(position, PlacementRejection.None, 2f, SeatAvailability.None)
                : Clear(position));

        PlacementResult result = new PlacementPlanner().Plan(Bed, probe);

        Assert.Equal(CompanionPose.SitByFire, result.Pose);
    }

    [Fact]
    public void YieldsAnOccupiedSeatAndSitsOnTheGroundInstead()
    {
        var probe = new StubProbe(position =>
            new PlacementProbeSample(position, PlacementRejection.None, -1f, SeatAvailability.Occupied));

        PlacementResult result = new PlacementPlanner().Plan(Bed, probe);

        Assert.True(result.Found);
        Assert.Equal(CompanionPose.SitOnGround, result.Pose);
    }

    [Fact]
    public void UnverifiedSeatingIsTreatedAsNoSeating()
    {
        // Until seating is confirmed in game, a documented gap beats a figure
        // floating over a bench.
        var probe = new StubProbe(position =>
            new PlacementProbeSample(position, PlacementRejection.None, -1f, SeatAvailability.Unverified));

        PlacementResult result = new PlacementPlanner().Plan(Bed, probe);

        Assert.True(result.Found);
        Assert.Equal(CompanionPose.SitOnGround, result.Pose);
    }

    [Fact]
    public void AFireTooFarAwayDoesNotCountAsWarm()
    {
        var probe = new StubProbe(position => new PlacementProbeSample(
            position,
            PlacementRejection.None,
            PlacementRules.DefaultFireComfortRadius + 1f,
            SeatAvailability.None));

        Assert.Equal(CompanionPose.SitOnGround, new PlacementPlanner().Plan(Bed, probe).Pose);
    }

    [Fact]
    public void RejectsSpotsTooFarAboveOrBelowTheAnchor()
    {
        // A probe that reports ground on a roof or in a cellar is honest; the
        // planner is what refuses to put the companion there.
        var probe = new StubProbe(position => Clear(position.WithHeight(
            Bed.Position.Y + PlacementRules.DefaultMaximumHeightDelta + 5f)));

        PlacementResult result = new PlacementPlanner().Plan(Bed, probe);

        Assert.False(result.Found);
        Assert.True(result.BlockedBy.HasFlag(PlacementRejection.OutOfRange));
    }

    [Fact]
    public void RejectsSpotsTheProbeMovedOutsideTheBand()
    {
        var probe = new StubProbe(position => Clear(new WorldPoint(500f, 30f, 500f)));

        PlacementResult result = new PlacementPlanner().Plan(Bed, probe);

        Assert.False(result.Found);
        Assert.True(result.BlockedBy.HasFlag(PlacementRejection.OutOfRange));
    }

    [Fact]
    public void TheSameWorldAlwaysProducesTheSameSpot()
    {
        // Otherwise the companion appears to wander between logins for no
        // reason the player can see.
        PlacementResult first = new PlacementPlanner().Plan(Bed, StubProbe.AllClear());
        PlacementResult second = new PlacementPlanner().Plan(Bed, StubProbe.AllClear());

        Assert.Equal(first.Position, second.Position);
        Assert.Equal(first.Pose, second.Pose);
    }

    [Fact]
    public void RepositionsOnlyWhenTheAnchorMovesMeaningfully()
    {
        var planner = new PlacementPlanner();
        var moved = new CompanionAnchor(AnchorKind.ClaimedBed, new WorldPoint(40f, 30f, 40f));
        var jittered = new CompanionAnchor(AnchorKind.ClaimedBed, new WorldPoint(0.2f, 30f, 0.2f));

        Assert.True(planner.ShouldReposition(Bed, moved));
        Assert.False(planner.ShouldReposition(Bed, jittered));
        Assert.False(planner.ShouldReposition(Bed, Bed));
    }

    [Fact]
    public void LosingABedRepositionsToTheDefaultSpawn()
    {
        var planner = new PlacementPlanner();
        var spawn = new CompanionAnchor(AnchorKind.DefaultSpawn, Bed.Position);

        // Same position, different kind: the anchor genuinely changed meaning.
        Assert.True(planner.ShouldReposition(Bed, spawn));
        Assert.True(planner.ShouldReposition(Bed, CompanionAnchor.None));
    }

    [Fact]
    public void ARadiusBandCollapsedToOneRingStillPlaces()
    {
        var rules = new PlacementRules(minimumRadius: 5f, maximumRadius: 5f);
        PlacementResult result = new PlacementPlanner(rules).Plan(Bed, StubProbe.AllClear());

        Assert.True(result.Found);
        Assert.Equal(5f, result.Position.HorizontalDistanceTo(Bed.Position), 2);
    }

    [Fact]
    public void RulesRejectAnInvertedRadiusBand()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlacementRules(minimumRadius: 10f, maximumRadius: 3f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlacementRules(minimumRadius: -1f));
    }

    private static PlacementProbeSample Clear(WorldPoint position)
    {
        return new PlacementProbeSample(position, PlacementRejection.None, -1f, SeatAvailability.None);
    }

    private sealed class StubProbe : IPlacementProbe
    {
        private readonly Func<WorldPoint, PlacementProbeSample> _sample;

        public StubProbe(Func<WorldPoint, PlacementProbeSample> sample)
        {
            _sample = sample;
        }

        public int Calls { get; private set; }

        public List<WorldPoint> Probed { get; } = new();

        public PlacementProbeSample Probe(WorldPoint position)
        {
            Calls++;
            Probed.Add(position);
            return _sample(position);
        }

        public static StubProbe AllClear() => new(Clear);

        public static StubProbe AlwaysRejected(PlacementRejection rejection) =>
            new(position => new PlacementProbeSample(
                position, rejection, -1f, SeatAvailability.None));
    }
}
