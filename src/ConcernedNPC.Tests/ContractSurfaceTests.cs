using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedNPC.Planning;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Routing;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>The value types the later leaves build on. No implementation exists
/// yet, so what is provable now is exactly the properties the leaves are
/// entitled to rely on - and every one of them is a property where the
/// convenient default would be wrong.</summary>
public class ValueTypeDefaultTests
{
    [Fact]
    public void Every_zero_value_refuses()
    {
        // The house rule, stated once over the whole new surface: a value nobody
        // computed is never a grant, a body, an area, a route or a plan.
        Assert.Equal(WorkAreaState.Unspecified, default(WorkAreaState));
        Assert.Equal(AreaScanOutcome.Unspecified, default(AreaScanOutcome));
        Assert.Equal(AreaSampleVerdict.Unreadable, default(AreaSampleVerdict));
        Assert.Equal(NpcContainerUse.Off, default(NpcContainerUse));
        Assert.Equal(JobPlanVerdict.Unspecified, default(JobPlanVerdict));
        Assert.Equal(ReservationOutcome.Unspecified, default(ReservationOutcome));
        Assert.Equal(RouteVerdict.Unspecified, default(RouteVerdict));
        Assert.Equal(InterruptionCause.Unspecified, default(InterruptionCause));
        Assert.Equal(InterruptionResponse.Unspecified, default(InterruptionResponse));
    }

    [Fact]
    public void A_defaulted_route_plan_cannot_be_walked()
    {
        Assert.False(default(RoutePlan).IsSuitable);
        Assert.Empty(default(RoutePlan).Waypoints);
    }

    [Fact]
    public void A_defaulted_job_plan_cannot_be_acted_on()
    {
        Assert.False(default(JobPlan).IsActionable);
        Assert.Empty(default(JobPlan).Steps);
        Assert.True(default(JobPlan).Manifest.IsEmpty);
    }

    [Fact]
    public void A_defaulted_scan_proves_nothing()
    {
        Assert.False(default(AreaScanReport).IsConclusive);
    }

    [Fact]
    public void A_defaulted_container_access_permits_nothing()
    {
        NpcContainerAccess access = default;

        Assert.False(access.CanTake);
        Assert.False(access.CanDeposit);
        Assert.False(access.Permits(NpcContainerUse.Take));
        Assert.False(access.Permits(NpcContainerUse.Both));
    }
}

public class ContainerAccessTests
{
    [Fact]
    public void Off_is_the_default_and_permits_nothing_even_with_no_refusal()
    {
        var access = new NpcContainerAccess(NpcContainerUse.Off, NpcContainerRefusal.None);

        Assert.False(access.CanTake);
        Assert.False(access.CanDeposit);
        Assert.False(access.Permits(NpcContainerUse.Off));
    }

    [Fact]
    public void Take_and_deposit_are_separate_trusts()
    {
        var takeOnly = new NpcContainerAccess(NpcContainerUse.Take, NpcContainerRefusal.None);
        var depositOnly = new NpcContainerAccess(NpcContainerUse.Deposit, NpcContainerRefusal.None);

        Assert.True(takeOnly.CanTake);
        Assert.False(takeOnly.CanDeposit);
        Assert.False(depositOnly.CanTake);
        Assert.True(depositOnly.CanDeposit);
        Assert.False(takeOnly.Permits(NpcContainerUse.Both));
    }

    [Fact]
    public void Both_permits_each_half_and_the_whole()
    {
        var both = new NpcContainerAccess(NpcContainerUse.Both, NpcContainerRefusal.None);

        Assert.True(both.CanTake);
        Assert.True(both.CanDeposit);
        Assert.True(both.Permits(NpcContainerUse.Both));
    }

    [Fact]
    public void The_players_allowance_is_not_permission_on_its_own()
    {
        // Enabling a chest says what the player allows. The world still has to
        // agree, every time, at the moment of use. Every refusal there is, so a
        // value added later without being considered here fails this test.
        foreach (NpcContainerRefusal refusal in Enum.GetValues<NpcContainerRefusal>())
        {
            if (refusal == NpcContainerRefusal.None)
            {
                continue;
            }

            var access = new NpcContainerAccess(NpcContainerUse.Both, refusal);

            Assert.False(access.CanTake, refusal.ToString());
            Assert.False(access.CanDeposit, refusal.ToString());
            Assert.False(access.Permits(NpcContainerUse.Take), refusal.ToString());
        }
    }
}

public class ReservationIdTests
{
    [Fact]
    public void The_same_step_of_the_same_job_is_the_same_id_every_time()
    {
        // The whole design. An id minted at the moment of reserving is a
        // different id on the second run, so a job resumed after a reload
        // cannot tell whether it already holds what it is about to reserve.
        Assert.Equal(ReservationId.For("build-shelter", 3), ReservationId.For("build-shelter", 3));
        Assert.NotEqual(ReservationId.For("build-shelter", 3), ReservationId.For("build-shelter", 4));
        Assert.NotEqual(ReservationId.For("build-shelter", 3), ReservationId.For("haul-stone", 3));
    }

    [Fact]
    public void An_id_round_trips_through_its_text()
    {
        ReservationId id = ReservationId.For("build-shelter", 12);

        Assert.True(ReservationId.TryParse(id.Value, out ReservationId back));
        Assert.Equal(id, back);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("has#separator", 0)]
    [InlineData("job", -1)]
    public void An_id_that_could_not_be_read_back_is_never_minted(string? jobId, int step)
    {
        Assert.True(ReservationId.For(jobId, step).IsEmpty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nohash")]
    [InlineData("#0")]
    [InlineData("job#")]
    [InlineData("job#notanumber")]
    [InlineData("job#-1")]
    [InlineData("job#1#2")]
    public void Text_that_is_not_an_id_is_refused(string? text)
    {
        Assert.False(ReservationId.TryParse(text, out _));
    }

    [Fact]
    public void A_defaulted_id_is_empty_and_matches_nothing()
    {
        Assert.True(default(ReservationId).IsEmpty);
        Assert.NotEqual(ReservationId.For("job", 0), default(ReservationId));
    }
}

public class WorldEpochTests
{
    [Fact]
    public void An_epoch_nobody_set_resolves_nothing()
    {
        // Fail closed. An unknown epoch that matched everything would make every
        // stale id from a previous world load usable.
        var known = new NpcWorldEpoch(Guid.NewGuid());

        Assert.True(NpcWorldEpoch.Unknown.IsUnknown);
        Assert.False(NpcWorldEpoch.Unknown.Matches(NpcWorldEpoch.Unknown));
        Assert.False(NpcWorldEpoch.Unknown.Matches(known));
        Assert.False(known.Matches(NpcWorldEpoch.Unknown));
        Assert.True(known.Matches(known));
    }

    [Fact]
    public void Two_world_loads_are_two_epochs()
    {
        Assert.False(new NpcWorldEpoch(Guid.NewGuid()).Matches(new NpcWorldEpoch(Guid.NewGuid())));
    }
}

public class JobPlanTests
{
    private static JobManifest TwoLines() => new JobManifest(new[]
    {
        new JobManifestLine("a-material", 40),
        new JobManifestLine("another-material", 2),
    });

    [Fact]
    public void A_manifest_totals_the_whole_job()
    {
        JobManifest manifest = TwoLines();

        Assert.False(manifest.IsEmpty);
        Assert.Equal(2, manifest.Lines.Count);
        Assert.Equal(42, manifest.TotalUnits);
        Assert.Equal(40, manifest.RequiredOf("a-material"));
        Assert.Equal(0, manifest.RequiredOf("never-asked-for"));
        Assert.Equal(0, manifest.RequiredOf(null));
    }

    [Fact]
    public void A_manifest_drops_lines_that_ask_for_nothing()
    {
        var manifest = new JobManifest(new[]
        {
            new JobManifestLine("a-material", 0),
            new JobManifestLine(string.Empty, 5),
            new JobManifestLine("a-material", -3),
        });

        Assert.True(manifest.IsEmpty);
        Assert.Equal(0, manifest.TotalUnits);
    }

    [Fact]
    public void Two_lines_for_one_item_mean_both()
    {
        var manifest = new JobManifest(new[]
        {
            new JobManifestLine("a-material", 10),
            new JobManifestLine("a-material", 5),
        });

        Assert.Equal(15, manifest.RequiredOf("a-material"));
    }

    [Fact]
    public void A_manifest_cannot_be_edited_behind_the_plan_that_used_it()
    {
        var lines = new List<JobManifestLine> { new JobManifestLine("a-material", 40) };
        var manifest = new JobManifest(lines);

        lines.Add(new JobManifestLine("another-material", 99));

        Assert.Single(manifest.Lines);
        Assert.Equal(40, manifest.TotalUnits);
    }

    [Fact]
    public void A_plan_knows_what_it_was_computed_against()
    {
        var epoch = new NpcWorldEpoch(Guid.NewGuid());
        var plan = new JobPlan(
            JobPlanVerdict.Planned,
            TwoLines(),
            new[] { new JobStep(0, "an-action", "a-subject", default, false, 1) },
            areaRevision: 7,
            epoch,
            string.Empty);

        Assert.True(plan.IsActionable);
        Assert.False(plan.IsStale(7, epoch));
        Assert.True(plan.IsStale(8, epoch));
        Assert.True(plan.IsStale(7, new NpcWorldEpoch(Guid.NewGuid())));
        Assert.True(plan.IsStale(7, NpcWorldEpoch.Unknown));
    }

    [Fact]
    public void A_plan_with_no_steps_is_not_actionable_however_it_is_labelled()
    {
        var plan = new JobPlan(
            JobPlanVerdict.Planned, JobManifest.Empty, null, 1, new NpcWorldEpoch(Guid.NewGuid()), string.Empty);

        Assert.False(plan.IsActionable);
    }

    [Fact]
    public void Nothing_to_do_is_not_something_to_walk_through()
    {
        var plan = new JobPlan(
            JobPlanVerdict.NothingToDo, JobManifest.Empty, null, 1, new NpcWorldEpoch(Guid.NewGuid()), string.Empty);

        Assert.False(plan.IsActionable);
    }

    [Fact]
    public void A_refusal_needs_a_verdict_that_explains_it()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => JobPlan.Refused(JobPlanVerdict.Planned, "no", NpcWorldEpoch.Unknown));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => JobPlan.Refused(JobPlanVerdict.Unspecified, "no", NpcWorldEpoch.Unknown));

        JobPlan refused = JobPlan.Refused(JobPlanVerdict.ShortOfMaterial, "not enough", NpcWorldEpoch.Unknown);
        Assert.False(refused.IsActionable);
        Assert.Equal("not enough", refused.Reason);
    }

    [Fact]
    public void A_plan_cannot_be_edited_behind_its_holder()
    {
        var steps = new List<JobStep> { new JobStep(0, "an-action", "a-subject", default, false, 1) };
        var plan = new JobPlan(
            JobPlanVerdict.Planned, JobManifest.Empty, steps, 1, new NpcWorldEpoch(Guid.NewGuid()), string.Empty);

        steps.Add(new JobStep(1, "another", "thing", default, false, 1));

        Assert.Single(plan.Steps);
    }
}

public class RoutePlanTests
{
    private static readonly NpcPoint Origin = default;

    [Fact]
    public void A_suitable_route_needs_somewhere_to_walk()
    {
        // The shape a partially-initialised result takes: a suitable verdict
        // with nothing in it. It must not be followable.
        Assert.False(new RoutePlan(RouteVerdict.Suitable, null, 0f, 1).IsSuitable);
        Assert.False(new RoutePlan(RouteVerdict.Suitable, new[] { Origin }, 0f, 1).IsSuitable);
        Assert.True(new RoutePlan(RouteVerdict.Suitable, new[] { Origin, Origin }, 1f, 1).IsSuitable);
    }

    [Fact]
    public void A_refusal_needs_a_verdict_that_explains_it()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RoutePlan.Refused(RouteVerdict.Suitable, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => RoutePlan.Refused(RouteVerdict.Unspecified, 1));

        RoutePlan refused = RoutePlan.Refused(RouteVerdict.NoPath, 4);
        Assert.False(refused.IsSuitable);
        Assert.Equal(4, refused.RequestRevision);
    }

    [Fact]
    public void A_route_cannot_be_edited_behind_its_follower()
    {
        var waypoints = new List<NpcPoint> { Origin, new NpcPoint(1f, 0f, 0f) };
        var plan = new RoutePlan(RouteVerdict.Suitable, waypoints, 1f, 1);

        waypoints.Add(new NpcPoint(99f, 0f, 0f));

        Assert.Equal(2, plan.Waypoints.Count);
    }
}

public class AreaScanTests
{
    [Fact]
    public void Only_a_complete_look_at_loaded_ground_proves_a_job_is_done()
    {
        Assert.True(new AreaScanReport(AreaScanOutcome.Empty, 20, 0, 20, 0, false).IsConclusive);
        Assert.True(new AreaScanReport(AreaScanOutcome.Exhausted, 20, 0, 0, 20, false).IsConclusive);

        // Belt and braces: the counts overrule the outcome, because this is the
        // property a finished job is claimed on.
        Assert.False(new AreaScanReport(AreaScanOutcome.Empty, 20, 1, 19, 0, false).IsConclusive);
        Assert.False(new AreaScanReport(AreaScanOutcome.Empty, 20, 0, 20, 0, true).IsConclusive);
        Assert.False(new AreaScanReport(AreaScanOutcome.NotLoaded, 20, 20, 0, 0, false).IsConclusive);
        Assert.False(new AreaScanReport(AreaScanOutcome.Incomplete, 20, 0, 20, 0, true).IsConclusive);
        Assert.False(new AreaScanReport(AreaScanOutcome.Found, 20, 0, 0, 0, false).IsConclusive);
    }

    [Fact]
    public void A_sample_is_only_standable_when_it_says_so()
    {
        Assert.True(new AreaSample(AreaSampleVerdict.Standable, default, AreaRejection.None).IsStandable);
        Assert.False(new AreaSample(AreaSampleVerdict.NotLoaded, default, AreaRejection.None).IsStandable);
        Assert.False(default(AreaSample).IsStandable);
    }

    [Fact]
    public void A_point_may_be_several_kinds_of_wrong_at_once()
    {
        AreaRejection rejection = AreaRejection.Water | AreaRejection.TooSteep;
        var sample = new AreaSample(AreaSampleVerdict.Rejected, default, rejection);

        Assert.True((sample.Rejection & AreaRejection.Water) == AreaRejection.Water);
        Assert.True((sample.Rejection & AreaRejection.TooSteep) == AreaRejection.TooSteep);
        Assert.False((sample.Rejection & AreaRejection.Occupied) == AreaRejection.Occupied);
    }
}

public class PointTests
{
    [Fact]
    public void Distance_ignores_height_and_height_is_its_own_question()
    {
        var ground = new NpcPoint(0f, 0f, 0f);
        var above = new NpcPoint(3f, 100f, 4f);

        Assert.Equal(5f, ground.HorizontalDistanceTo(above), 3);
        Assert.Equal(100f, ground.VerticalDistanceTo(above), 3);
    }

    [Fact]
    public void A_position_nobody_could_compute_is_not_the_origin()
    {
        Assert.True(default(NpcPoint).IsFinite);
        Assert.False(new NpcPoint(float.NaN, 0f, 0f).IsFinite);
        Assert.False(new NpcPoint(0f, float.PositiveInfinity, 0f).IsFinite);
    }
}

public class InterruptionTests
{
    [Fact]
    public void Only_continuing_or_replanning_lets_a_job_go_on()
    {
        Assert.True(Outcome(InterruptionResponse.Continue).MayResume);
        Assert.True(Outcome(InterruptionResponse.Replan).MayResume);
        Assert.False(Outcome(InterruptionResponse.Refund).MayResume);
        Assert.False(Outcome(InterruptionResponse.NeedsAttention).MayResume);
        Assert.False(default(InterruptionOutcome).MayResume);
    }

    private static InterruptionOutcome Outcome(InterruptionResponse response) =>
        new InterruptionOutcome(response, InterruptionCause.WorldReloaded, 0f, string.Empty);
}
