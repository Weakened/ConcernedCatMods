using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Routing;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>Walking one body from here to there, on ground a probe confirms.
/// </summary>
public sealed class LocalRoutePlannerTests
{
    /// <summary>The same request gives the same route, waypoint for
    /// waypoint.</summary>
    [Fact]
    public void Identical_inputs_give_an_identical_route()
    {
        var planner = new LocalRoutePlanner(new FakeProbe());
        var request = new RouteRequest(At(0f, 0f), At(40f, 0f), 1f, 3);

        RoutePlan first = planner.Plan(request, 0f);
        RoutePlan second = planner.Plan(request, 0f);

        Assert.True(first.IsSuitable);
        Assert.Equal(Corners(first), Corners(second));
        Assert.Equal(first.LengthMetres, second.LengthMetres, 4);
    }

    /// <summary>Two routes to the same destination share the request's revision
    /// and differ in the plan's.
    ///
    /// <b>This is the whole reason the two numbers exist.</b> The ordinary case
    /// for re-planning is that the destination has not changed at all - the NPC
    /// learned that a gap it believed in does not fit, and the route now goes
    /// the other way round. If a goal carried only the request's revision, a
    /// goal from the abandoned route would be bit-identical in the one field
    /// meant to tell them apart, and the follower would walk it.</summary>
    [Fact]
    public void Two_answers_to_one_request_are_told_apart_by_the_plan_revision()
    {
        var planner = new LocalRoutePlanner(new FakeProbe());
        var request = new RouteRequest(At(0f, 0f), At(40f, 0f), 1f, 11);

        RoutePlan first = planner.Plan(request, 0f);
        RoutePlan second = planner.Plan(request, 0f);

        Assert.Equal(first.RequestRevision, second.RequestRevision);
        Assert.NotEqual(first.PlanRevision, second.PlanRevision);
        Assert.True(second.PlanRevision > first.PlanRevision);

        RouteGoal? goal = planner.NextGoal(first, At(0f, 0f), -1);
        Assert.NotNull(goal);
        Assert.Equal(first.PlanRevision, goal!.Value.PlanRevision);
        Assert.NotEqual(second.PlanRevision, goal.Value.PlanRevision);
    }

    /// <summary>Every plan the planner produces carries a different revision,
    /// refusals included - so a follower holding a goal across any re-plan can
    /// tell.</summary>
    [Fact]
    public void Every_plan_including_a_refusal_gets_its_own_revision()
    {
        var planner = new LocalRoutePlanner(new FakeProbe());

        RoutePlan refused = planner.Plan(new RouteRequest(At(0f, 0f), At(900f, 0f), 1f, 1), 0f);
        RoutePlan good = planner.Plan(new RouteRequest(At(0f, 0f), At(40f, 0f), 1f, 1), 0f);

        Assert.Equal(RouteVerdict.TooFar, refused.Verdict);
        Assert.True(good.PlanRevision > refused.PlanRevision);
        Assert.Equal(2, planner.PlansProduced);
    }

    /// <summary>Too far is answered on distance alone, without asking the ground
    /// anything. Measuring is free and probing is not.</summary>
    [Fact]
    public void Too_far_is_answered_without_spending_a_probe()
    {
        var probe = new FakeProbe();
        var planner = new LocalRoutePlanner(probe);

        RoutePlan plan = planner.Plan(new RouteRequest(At(0f, 0f), At(900f, 0f), 1f, 1), 0f);

        Assert.Equal(RouteVerdict.TooFar, plan.Verdict);
        Assert.Equal(0, probe.Probes);
        Assert.False(plan.IsSuitable);
    }

    /// <summary>The follower never gets a goal it has already passed, even
    /// standing on the first waypoint.
    ///
    /// <b>The failure this is written against</b> is a planner that answered
    /// with the nearest waypoint: a route that bends back near itself passes
    /// close to its own later waypoints, and a follower picking the nearest
    /// would cut the corner and walk through whatever the bend was going
    /// round.</summary>
    [Fact]
    public void The_goal_index_only_ever_moves_forward()
    {
        var planner = new LocalRoutePlanner(new FakeProbe());
        RoutePlan plan = planner.Plan(new RouteRequest(At(0f, 0f), At(40f, 0f), 1f, 1), 0f);
        Assert.True(plan.Waypoints.Count > 3);

        int last = -1;
        var seen = new List<int>();
        while (true)
        {
            // Ask from the start every time, which is the position that would
            // make a nearest-waypoint planner answer zero forever.
            RouteGoal? goal = planner.NextGoal(plan, At(0f, 0f), last);
            if (goal == null)
            {
                break;
            }

            Assert.True(goal.Value.WaypointIndex > last);
            last = goal.Value.WaypointIndex;
            seen.Add(last);
        }

        Assert.Equal(plan.Waypoints.Count - 1, seen.Count);
        Assert.Equal(plan.Waypoints.Count - 1, last);
    }

    /// <summary>Standing on a waypoint steps over it, forwards, and the last one
    /// is arrived at rather than stepped over.</summary>
    [Fact]
    public void Standing_on_a_waypoint_steps_over_it_forwards()
    {
        var planner = new LocalRoutePlanner(new FakeProbe());
        RoutePlan plan = planner.Plan(new RouteRequest(At(0f, 0f), At(40f, 0f), 2f, 1), 0f);

        NpcPoint second = plan.Waypoints[1];
        RouteGoal? goal = planner.NextGoal(plan, second, -1);

        Assert.NotNull(goal);
        Assert.True(goal!.Value.WaypointIndex >= 2);

        int last = plan.Waypoints.Count - 1;
        RouteGoal? final = planner.NextGoal(plan, plan.Waypoints[last], last - 1);
        Assert.NotNull(final);
        Assert.True(final!.Value.IsFinal);
        Assert.Equal(last, final.Value.WaypointIndex);

        // And the final goal's radius is the tolerance the request asked for,
        // not a number the planner invented.
        Assert.Equal(2f, final.Value.ArrivalRadiusMetres);

        Assert.Null(planner.NextGoal(plan, plan.Waypoints[last], last));
    }

    /// <summary>A refusal is never followable, whatever index is handed
    /// back.</summary>
    [Fact]
    public void A_refusal_has_no_goals()
    {
        var planner = new LocalRoutePlanner(new FakeProbe());
        RoutePlan refused = planner.Plan(new RouteRequest(At(0f, 0f), At(900f, 0f), 1f, 1), 0f);

        Assert.Null(planner.NextGoal(refused, At(0f, 0f), -1));
        Assert.Null(planner.NextGoal(refused, At(0f, 0f), 0));
        Assert.Null(planner.NextGoal(default, At(0f, 0f), -1));
    }

    /// <summary>A walk that failed is refused again for a while - for that
    /// destination - and allowed again once the pause is over. Not the same as
    /// no path.</summary>
    [Fact]
    public void A_walk_that_failed_is_refused_again_for_a_while()
    {
        var planner = new LocalRoutePlanner(new FakeProbe());
        var request = new RouteRequest(At(0f, 0f), At(40f, 0f), 1f, 1);
        Assert.True(planner.Plan(request, 0f).IsSuitable);

        planner.RememberSetback(At(40f, 0f), At(20f, 0f), At(21f, 0f), 0f);

        Assert.Equal(RouteVerdict.RefusedRecently, planner.Plan(request, 1f).Verdict);
        Assert.True(planner.Plan(request, NpcWalkSetbacks.FirstPauseSeconds + 1f).IsSuitable);
    }

    /// <summary>Ground that is not loaded is "ask again", never "no way
    /// through".</summary>
    [Fact]
    public void Unloaded_ground_is_ask_again_rather_than_no_path()
    {
        var probe = new FakeProbe { Otherwise = AreaSampleVerdict.NotLoaded };
        var planner = new LocalRoutePlanner(probe);

        Assert.Equal(
            RouteVerdict.OutsideLoadedArea,
            planner.Plan(new RouteRequest(At(0f, 0f), At(40f, 0f), 1f, 1), 0f).Verdict);

        var unreadable = new FakeProbe { Otherwise = AreaSampleVerdict.Unreadable };
        Assert.Equal(
            RouteVerdict.BudgetExhausted,
            new LocalRoutePlanner(unreadable).Plan(new RouteRequest(At(0f, 0f), At(40f, 0f), 1f, 1), 0f).Verdict);
    }

    /// <summary>Something in the way with an edge to it is stepped around, and
    /// the route goes past rather than being given up on.</summary>
    [Fact]
    public void A_refused_sample_is_stepped_around()
    {
        var probe = new FakeProbe { Obstacle = At(19.5f, 0f), ObstacleRadius = 1.5f };
        var planner = new LocalRoutePlanner(probe);

        RoutePlan plan = planner.Plan(new RouteRequest(At(0f, 0f), At(40f, 0f), 1f, 1), 0f);

        Assert.True(plan.IsSuitable);

        bool wentAround = false;
        foreach (NpcPoint corner in plan.Waypoints)
        {
            if (System.Math.Abs(corner.Z) > 0.5f)
            {
                wentAround = true;
            }
        }

        Assert.True(wentAround);

        // And the detour is the same detour every time: a route that went round
        // one way and then the other would be an NPC that could not make its
        // mind up in front of the same rock.
        RoutePlan again = new LocalRoutePlanner(
            new FakeProbe { Obstacle = At(19.5f, 0f), ObstacleRadius = 1.5f })
            .Plan(new RouteRequest(At(0f, 0f), At(40f, 0f), 1f, 1), 0f);
        Assert.Equal(Corners(plan), Corners(again));
    }

    /// <summary>What the planner will <b>not</b> do, said as a test so nobody
    /// mistakes it for a bug later: a wall long enough that no sideways step
    /// clears it is answered as no path, not searched round.
    ///
    /// Going round a building is what the setback memory and a re-plan are for.
    /// A local planner that started searching would be the unbounded navigation
    /// search this leaf exists to refuse.</summary>
    [Fact]
    public void A_wall_no_sideways_step_clears_is_no_path_rather_than_a_search()
    {
        var probe = new FakeProbe { Obstacle = At(20f, 0f), ObstacleRadius = 40f };
        var planner = new LocalRoutePlanner(probe);

        RoutePlan plan = planner.Plan(new RouteRequest(At(0f, 0f), At(40f, 0f), 1f, 1), 0f);

        Assert.Equal(RouteVerdict.NoPath, plan.Verdict);
        Assert.True(probe.Probes <= LocalRoutePlanner.MostSamplesPerPlan * 5);
    }

    /// <summary>Ground that refuses everywhere is no path, and it is reported
    /// rather than searched for forever.</summary>
    [Fact]
    public void Ground_that_refuses_everywhere_is_no_path()
    {
        var probe = new FakeProbe { Otherwise = AreaSampleVerdict.Rejected };
        var planner = new LocalRoutePlanner(probe);

        RoutePlan plan = planner.Plan(new RouteRequest(At(0f, 0f), At(40f, 0f), 1f, 1), 0f);

        Assert.Equal(RouteVerdict.NoPath, plan.Verdict);
    }

    /// <summary>The cost of one plan has a ceiling, and it does not grow with
    /// the distance: a long walk is sampled more coarsely, not more often.
    ///
    /// The stated bound is samples times one probe each, plus the four detour
    /// probes a refused sample may spend.</summary>
    [Fact]
    public void One_plan_costs_no_more_than_its_stated_ceiling()
    {
        const int ceiling = LocalRoutePlanner.MostSamplesPerPlan * 5;

        var open = new FakeProbe();
        new LocalRoutePlanner(open).Plan(
            new RouteRequest(At(0f, 0f), At(LocalRoutePlanner.LongestRouteMetres - 1f, 0f), 1f, 1), 0f);
        Assert.True(open.Probes <= LocalRoutePlanner.MostSamplesPerPlan);

        // A short walk in open ground is a handful of probes, not the ceiling.
        var near = new FakeProbe();
        new LocalRoutePlanner(near).Plan(new RouteRequest(At(0f, 0f), At(8f, 0f), 1f, 1), 0f);
        Assert.True(near.Probes <= 3);

        // And the pathological case - every sample refused, every detour tried -
        // still stops at the ceiling.
        var hostile = new FakeProbe { Otherwise = AreaSampleVerdict.Rejected };
        new LocalRoutePlanner(hostile).Plan(
            new RouteRequest(At(0f, 0f), At(LocalRoutePlanner.LongestRouteMetres - 1f, 0f), 1f, 1), 0f);
        Assert.True(hostile.Probes <= ceiling);
    }

    /// <summary>A request that is not one is refused, and so is a walk to where
    /// the NPC already stands - a route to where you are is not a route, and
    /// treating one as followable is how an NPC reports arriving somewhere it
    /// never went.</summary>
    [Fact]
    public void A_request_that_is_not_one_is_refused()
    {
        var planner = new LocalRoutePlanner(new FakeProbe());

        Assert.Equal(
            RouteVerdict.InvalidRequest,
            planner.Plan(new RouteRequest(new NpcPoint(float.NaN, 0f, 0f), At(10f, 0f), 1f, 1), 0f).Verdict);
        Assert.Equal(
            RouteVerdict.InvalidRequest,
            planner.Plan(new RouteRequest(At(0f, 0f), At(10f, 0f), -1f, 1), 0f).Verdict);
        Assert.Equal(
            RouteVerdict.InvalidRequest,
            planner.Plan(new RouteRequest(At(0f, 0f), At(0f, 0f), 0f, 1), 0f).Verdict);

        // Already within the arrival tolerance is the same thing.
        Assert.Equal(
            RouteVerdict.InvalidRequest,
            planner.Plan(new RouteRequest(At(0f, 0f), At(3f, 0f), 5f, 1), 0f).Verdict);
    }

    /// <summary>A tolerance of nothing is refused rather than planned.
    ///
    /// <b>The failure this is written against</b> is quiet: the plan comes back
    /// suitable, the final goal's arrival radius is zero, and the NPC walks to
    /// the chest and then stands in front of it forever, because arriving would
    /// take exact float equality. It also makes the zero that a refusal carries
    /// mean two things at once.</summary>
    [Fact]
    public void A_tolerance_of_nothing_is_refused_rather_than_planned()
    {
        var planner = new LocalRoutePlanner(new FakeProbe());

        Assert.Equal(
            RouteVerdict.InvalidRequest,
            planner.Plan(new RouteRequest(At(0f, 0f), At(40f, 0f), 0f, 1), 0f).Verdict);
        Assert.Equal(
            RouteVerdict.InvalidRequest,
            planner.Plan(new RouteRequest(At(0f, 0f), At(40f, 0f), float.NaN, 1), 0f).Verdict);

        // And the smallest real tolerance is the radius the final goal gets.
        RoutePlan plan = planner.Plan(new RouteRequest(At(0f, 0f), At(40f, 0f), 0.5f, 1), 0f);
        Assert.True(plan.IsSuitable);
        RouteGoal? last = planner.NextGoal(plan, At(0f, 0f), plan.Waypoints.Count - 2);
        Assert.NotNull(last);
        Assert.True(last!.Value.IsFinal);
        Assert.Equal(0.5f, last.Value.ArrivalRadiusMetres);
    }

    /// <summary>A route always starts where the NPC is and ends where it is
    /// going, and never reports the two as the same place.</summary>
    [Fact]
    public void A_route_runs_from_where_he_is_to_where_he_is_going()
    {
        var planner = new LocalRoutePlanner(new FakeProbe());
        RoutePlan plan = planner.Plan(new RouteRequest(At(0f, 0f), At(40f, 0f), 1f, 1), 0f);

        Assert.True(plan.IsSuitable);
        Assert.Equal(At(0f, 0f), plan.Waypoints[0]);
        Assert.True(plan.LengthMetres > 0f);
        Assert.NotEqual(plan.Waypoints[0], plan.Waypoints[plan.Waypoints.Count - 1]);
    }

    private static string Corners(RoutePlan plan)
    {
        var text = new System.Text.StringBuilder();
        foreach (NpcPoint corner in plan.Waypoints)
        {
            text.Append(corner).Append(';');
        }

        return text.ToString();
    }

    private static NpcPoint At(float x, float z) => new NpcPoint(x, 0f, z);
}
