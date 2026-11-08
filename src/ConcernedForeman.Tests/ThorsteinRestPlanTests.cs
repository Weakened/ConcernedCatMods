using TheConcernedCat.Companions.Placement;
using TheConcernedCat.ConcernedForeman.Domain.Npc;

namespace ConcernedForeman.Tests;

/// <summary>#295 S5: where Thorstein sits at home, in what pose, and when he
/// moves. The planner is the shared companion one; the probe here is invented
/// ground, so every rule is proved with no game running.</summary>
public sealed class ThorsteinRestPlanTests
{
    private static readonly WorldPoint Home = new WorldPoint(100f, 30f, 200f);

    private sealed class Ground : IPlacementProbe
    {
        internal WorldPoint? Fire { get; set; }

        internal float WarmWithin { get; set; } = 3f;

        internal PlacementRejection Everywhere { get; set; }

        internal bool Unloaded { get; set; }

        public PlacementProbeSample Probe(WorldPoint position)
        {
            if (Unloaded)
            {
                return PlacementProbeSample.NotLoaded(position);
            }

            float warmth = Fire.HasValue && position.HorizontalDistanceTo(Fire.Value) <= WarmWithin ? 0f : -1f;
            return new PlacementProbeSample(position, Everywhere, warmth, SeatOffer.None);
        }
    }

    private static ThorsteinRestChoice Plan(Ground ground) =>
        ThorsteinRestPlan.Choose(
            new PlacementPlanner(ThorsteinRestPlan.Rules).Plan(
                new CompanionAnchor(AnchorKind.ClaimedBed, Home), ground));

    [Fact]
    public void He_sits_on_the_ground_near_home_and_on_the_same_floor()
    {
        ThorsteinRestChoice choice = Plan(new Ground());

        Assert.Equal(ThorsteinRestPose.SitOnGround, choice.Pose);
        float away = choice.Spot.HorizontalDistanceTo(Home);
        Assert.InRange(away, 1.5f - 0.01f, 6f + 0.01f);
        Assert.True(choice.Spot.VerticalDistanceTo(Home) <= 1.5f);
    }

    [Fact]
    public void A_fire_near_home_draws_him_to_its_warmth()
    {
        var fire = new WorldPoint(104f, 30f, 200f);
        ThorsteinRestChoice choice = Plan(new Ground { Fire = fire });

        Assert.Equal(ThorsteinRestPose.SitByFire, choice.Pose);
        Assert.True(choice.Spot.HorizontalDistanceTo(fire) <= 3f);
        Assert.Equal(2, choice.Value);
    }

    [Fact]
    public void The_same_camp_puts_him_in_the_same_place()
    {
        var ground = new Ground { Fire = new WorldPoint(97f, 30f, 203f) };

        Assert.Equal(Plan(ground).Spot, Plan(ground).Spot);
    }

    [Fact]
    public void Ground_still_loading_leaves_him_standing_and_says_so()
    {
        ThorsteinRestChoice choice = Plan(new Ground { Unloaded = true });

        Assert.Equal(ThorsteinRestPose.Stand, choice.Pose);
        Assert.Contains("loading", choice.Reason);
    }

    [Fact]
    public void Nowhere_to_sit_leaves_him_standing_and_names_what_blocked_it()
    {
        ThorsteinRestChoice choice = Plan(new Ground { Everywhere = PlacementRejection.Bed });

        Assert.False(choice.IsSitting);
        Assert.Contains("Bed", choice.Reason);
    }

    [Theory]
    [InlineData((int)CompanionPose.SitOnSeat)]
    [InlineData((int)CompanionPose.SleepInBed)]
    public void A_pose_this_build_does_not_show_is_standing_not_a_floating_sit(int pose)
    {
        PlacementResult seat = PlacementResult.Placed(new WorldPoint(1f, 2f, 3f), (CompanionPose)pose, 10, value: 1);

        Assert.Equal(ThorsteinRestPose.Stand, ThorsteinRestPlan.Choose(seat).Pose);
    }

    [Fact]
    public void His_band_is_closer_in_than_hulgis_and_on_one_floor()
    {
        Assert.Equal(1.5f, ThorsteinRestPlan.Rules.MinimumRadius);
        Assert.Equal(6f, ThorsteinRestPlan.Rules.MaximumRadius);
        Assert.Equal(1.5f, ThorsteinRestPlan.Rules.MaximumHeightDelta);
    }

    private static ThorsteinRestChoice Sitting(int value) =>
        new ThorsteinRestChoice(
            value >= 2 ? ThorsteinRestPose.SitByFire : ThorsteinRestPose.SitOnGround,
            new WorldPoint(1f, 0f, 1f), value, "test");

    [Fact]
    public void A_standing_figure_sits_as_soon_as_a_spot_is_found()
    {
        Assert.True(ThorsteinRestPlan.ShouldMove(ThorsteinRestChoice.Standing("x"), false, 0, Sitting(0)));
    }

    [Fact]
    public void Nothing_found_is_no_reason_to_move_anyone()
    {
        Assert.False(ThorsteinRestPlan.ShouldMove(Sitting(0), true, 0, ThorsteinRestChoice.Standing("x")));
        Assert.False(ThorsteinRestPlan.ShouldMove(Sitting(0), false, 0, ThorsteinRestChoice.Standing("x")));
        Assert.False(ThorsteinRestPlan.ShouldMove(
            ThorsteinRestChoice.Standing("x"), false, 0, ThorsteinRestChoice.Standing("y")));
    }

    [Fact]
    public void Something_built_on_his_spot_moves_him()
    {
        Assert.True(ThorsteinRestPlan.ShouldMove(Sitting(0), false, 0, Sitting(0)));
    }

    [Fact]
    public void A_fire_lit_later_moves_him_to_it()
    {
        Assert.True(ThorsteinRestPlan.ShouldMove(Sitting(0), true, 0, Sitting(2)));
    }

    [Fact]
    public void An_equal_spot_elsewhere_does_not_move_him()
    {
        Assert.False(ThorsteinRestPlan.ShouldMove(Sitting(2), true, 2, Sitting(2)));
        Assert.False(ThorsteinRestPlan.ShouldMove(Sitting(0), true, 0, Sitting(0)));
    }

    [Fact]
    public void A_fire_that_went_out_alone_does_not_get_him_up()
    {
        // Seated warm (value 2); the fire is out, so his spot is worth 0 now,
        // and so is the best on offer.
        Assert.False(ThorsteinRestPlan.ShouldMove(Sitting(2), true, 0, Sitting(0)));
    }

    [Fact]
    public void Seated_he_looks_again_rarely_and_while_ground_loads_often()
    {
        Assert.Equal(ThorsteinRestPlan.ReplanSeconds, ThorsteinRestPlan.NextLookIn(Sitting(0)));
        Assert.Equal(ThorsteinRestPlan.RetrySeconds, ThorsteinRestPlan.NextLookIn(Plan(new Ground { Unloaded = true })));
        Assert.True(ThorsteinRestPlan.RetrySeconds < ThorsteinRestPlan.ReplanSeconds);
    }

    [Fact]
    public void A_camp_seen_whole_with_nowhere_to_sit_is_not_swept_every_few_seconds()
    {
        ThorsteinRestChoice refused = Plan(new Ground { Everywhere = PlacementRejection.Shrine });

        Assert.False(refused.StillLoading);
        Assert.Equal(ThorsteinRestPlan.ReplanSeconds, ThorsteinRestPlan.NextLookIn(refused));
    }

    [Fact]
    public void Built_over_with_nowhere_else_he_gets_up_rather_than_sitting_in_a_wall()
    {
        ThorsteinRestChoice nowhere = ThorsteinRestChoice.Standing("no spot");

        Assert.True(ThorsteinRestPlan.ShouldStandUp(Sitting(0), false, nowhere));
        Assert.False(ThorsteinRestPlan.ShouldStandUp(Sitting(0), true, nowhere));
        Assert.False(ThorsteinRestPlan.ShouldStandUp(Sitting(0), false, Sitting(0)));
        Assert.False(ThorsteinRestPlan.ShouldStandUp(ThorsteinRestChoice.Standing("x"), false, nowhere));
    }
}
