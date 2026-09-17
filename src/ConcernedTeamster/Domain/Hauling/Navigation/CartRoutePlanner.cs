using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TheConcernedCat.ConcernedTeamster.Domain.Load;
using TheConcernedCat.ConcernedTeamster.Domain.Risk;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Navigation;

internal enum CartSteeringStatus
{
    Unspecified = 0,

    /// <summary>A goal to steer towards is given.</summary>
    Following = 1,

    /// <summary>Gunnar has reached the plan's stop.</summary>
    Finished = 2,

    /// <summary>Gunnar or the cart is outside the corridor the plan verified:
    /// plan again from here.</summary>
    LeftCorridor = 3,

    /// <summary>The plan is refused or empty; there is nothing to follow.
    /// </summary>
    NotSuitable = 4,
}

/// <summary>One local steering answer: why, and the goal when there is one.
/// </summary>
internal readonly struct CartSteering
{
    public CartSteering(CartSteeringStatus status, SteeringGoal? goal)
    {
        Status = status;
        Goal = goal;
    }

    public CartSteeringStatus Status { get; }

    public SteeringGoal? Goal { get; }
}

/// <summary>Gunnar's cart navigation (CART-05): agent B's implementation of the
/// <see cref="ICartRoutePlanner"/> seam. Main thread only; never moves anything.
///
/// Global planning and local steering are kept apart. <see cref="Plan(CartRouteRequest, float)"/>
/// is the expensive, budgeted step: it refuses cheaply what it can before asking
/// the navmesh (a leg longer than <see cref="HaulLimits.MaxLegMetres"/>, ends on
/// unloaded ground, a target that failed lately, a spent query budget), then asks
/// for a walker's path, joins the exact start and end to it when the navmesh put
/// them elsewhere, and hands the line to <see cref="CartRouteEvaluator"/> with a
/// fresh allowance of <see cref="HaulLimits.ClearanceProbesPerPlan"/> probes.
/// <see cref="NextGoal"/> is cheap and local: one waypoint at a time, in order,
/// never backwards, and nothing at all once Gunnar or the cart leaves the
/// verified corridor. A running plan is checked against the world again every
/// <see cref="HaulLimits.PlanRefreshSeconds"/> through <see cref="Refresh"/>,
/// which re-verifies what is left of the route without another navmesh query.
/// </summary>
internal sealed class CartRoutePlanner : ICartRoutePlanner
{
    private readonly HaulLimits _limits;
    private readonly ICartPathSource _paths;
    private readonly ICartTerrainProbe _probe;
    private readonly CartRouteEvaluator _evaluator;
    private readonly CartQueryBudget _budget;
    private readonly ConditionalWeakTable<CartRoutePlan, PlanContext> _contexts =
        new ConditionalWeakTable<CartRoutePlan, PlanContext>();
    private readonly List<WorkPoint> _corners = new List<WorkPoint>(64);
    private readonly List<CartDoorway> _doorways = new List<CartDoorway>();

    public CartRoutePlanner(
        HaulLimits limits,
        ICartPathSource paths,
        ICartTerrainProbe probe,
        CartRouteFailureCache? failures = null,
        LoadModel? climb = null,
        RiskModel? descent = null)
    {
        _limits = (limits ?? throw new ArgumentNullException(nameof(limits))).Validate();
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        Failures = failures;
        _evaluator = new CartRouteEvaluator(_limits, probe, climb, descent);
        _budget = new CartQueryBudget(_limits.PathQueriesPerMinute);
    }

    public HaulLimits Limits => _limits;

    /// <summary>The navmesh query budget shared by every plan this planner
    /// makes.</summary>
    public CartQueryBudget Budget => _budget;

    /// <summary>Ways that recently failed; consulted before every plan. May be
    /// replaced when a new cart is leased.</summary>
    public CartRouteFailureCache? Failures { get; set; }

    /// <summary>What the last plan, refresh or back-off check found, for the
    /// log.</summary>
    public CartRouteAssessment? LastAssessment { get; private set; }

    public CartRoutePlan Plan(CartRouteRequest request, float now) => Plan(request, null, now);

    /// <summary>Plans a leg for a cart that stands at
    /// <paramref name="cartPosition"/>, so the first turn is predicted from the
    /// cart's real side. Never throws.</summary>
    public CartRoutePlan Plan(CartRouteRequest request, WorkPoint? cartPosition, float now)
    {
        try
        {
            return PlanCore(request, cartPosition, now);
        }
        catch (Exception exception) when (!(exception is OutOfMemoryException))
        {
            return Refuse(request.Revision, CartRouteFinding.ProbeFaulted, null);
        }
    }

    public SteeringGoal? NextGoal(CartRoutePlan plan, WorkPoint pullerPosition, WorkPoint cartPosition)
    {
        return Follow(plan, pullerPosition, cartPosition).Goal;
    }

    /// <summary><see cref="NextGoal"/> with the reason when there is no goal.
    /// </summary>
    public CartSteering Follow(CartRoutePlan plan, WorkPoint pullerPosition, WorkPoint cartPosition)
    {
        if (plan == null || !plan.IsSuitable || !plan.StopPoint.HasValue)
        {
            return new CartSteering(CartSteeringStatus.NotSuitable, null);
        }

        if (!pullerPosition.IsFinite)
        {
            return new CartSteering(CartSteeringStatus.LeftCorridor, null);
        }

        IReadOnlyList<WorkPoint> waypoints = plan.Waypoints;
        _contexts.TryGetValue(plan, out PlanContext? context);
        int stop = context != null ? context.Assessment.StopWaypointIndex : IndexOfStop(plan);
        float half = plan.NarrowestClearanceMetres * 0.5f;
        if (stop < 1 || !(half > 0f))
        {
            return new CartSteering(CartSteeringStatus.NotSuitable, null);
        }

        int next = context != null ? context.NextIndex : 1;
        if (next > stop)
        {
            return new CartSteering(CartSteeringStatus.Finished, null);
        }

        // Only the stretch being walked and the one after it: steering is local,
        // and a route that bends back near itself must not skip ahead.
        int bestSegment = -1;
        float bestDistance = float.MaxValue;
        int lastSegment = Math.Min(next, stop - 1);
        for (int segment = Math.Max(0, next - 1); segment <= lastSegment; segment++)
        {
            float distance = CartRouteGeometry.FlatDistanceToSegment(
                pullerPosition, waypoints[segment], waypoints[segment + 1], out _);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestSegment = segment;
            }
        }

        if (bestSegment < 0 || bestDistance > half)
        {
            return new CartSteering(CartSteeringStatus.LeftCorridor, null);
        }

        if (bestSegment + 1 > next)
        {
            next = bestSegment + 1;
        }

        while (next <= stop &&
            CartRouteGeometry.FlatDistance(pullerPosition, waypoints[next]) <= CartRouteGeometry.WaypointArrivalRadiusMetres)
        {
            next++;
        }

        if (context != null)
        {
            context.NextIndex = next;
        }

        if (next > stop)
        {
            return new CartSteering(CartSteeringStatus.Finished, null);
        }

        if (context != null && cartPosition.IsFinite && !CartInsideCorridor(context, cartPosition, half))
        {
            return new CartSteering(CartSteeringStatus.LeftCorridor, null);
        }

        var goal = new SteeringGoal(
            waypoints[next],
            CartRouteGeometry.WaypointArrivalRadiusMetres,
            next == stop,
            next,
            plan.RequestRevision);
        return new CartSteering(CartSteeringStatus.Following, goal);
    }

    /// <summary>Whether <paramref name="plan"/> is due for its periodic check
    /// against the world.</summary>
    public bool NeedsRefresh(CartRoutePlan plan, float now)
    {
        if (plan == null || !plan.IsSuitable || !_contexts.TryGetValue(plan, out PlanContext? context))
        {
            return false;
        }

        return now < context!.CheckedAt || now - context.CheckedAt >= _limits.PlanRefreshSeconds;
    }

    /// <summary>Checks what is left of <paramref name="plan"/> against the world
    /// as it is now - new obstacles, doors, water, unloaded ground - from where
    /// Gunnar and the cart actually are, without a navmesh query. Returns a new
    /// plan to follow from here (its waypoints start at Gunnar), the same plan
    /// when it is already finished, or a refusal. Never throws.</summary>
    public CartRoutePlan Refresh(CartRoutePlan plan, WorkPoint pullerPosition, WorkPoint cartPosition, float now)
    {
        if (plan == null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        try
        {
            return RefreshCore(plan, pullerPosition, cartPosition, now);
        }
        catch (Exception exception) when (!(exception is OutOfMemoryException))
        {
            return Refuse(plan.RequestRevision, CartRouteFinding.ProbeFaulted, null);
        }
    }

    /// <summary>A short, verified move of the cart straight back the way it
    /// points, one sample spacing: the gentlest manoeuvre off a snag. True only
    /// when the ground behind is loaded, dry, supported, within the grade limit,
    /// through no doorway and clear for the cart's whole box. The goal is for
    /// Gunnar, who stays hitched; arriving means stop and plan again. Never
    /// throws.</summary>
    public bool TryPlanBackOff(
        WorkPoint pullerPosition, WorkPoint cartPosition, CartFootprint footprint, float now, out SteeringGoal goal)
    {
        goal = default;
        try
        {
            return TryPlanBackOffCore(pullerPosition, cartPosition, footprint, out goal);
        }
        catch (Exception exception) when (!(exception is OutOfMemoryException))
        {
            LastAssessment = CartRouteAssessment.Refused(
                CartRouteFinding.ProbeFaulted, cartPosition, 0f, string.Empty, default);
            return false;
        }
    }

    private CartRoutePlan PlanCore(CartRouteRequest request, WorkPoint? cartPosition, float now)
    {
        CartFootprint footprint = request.Footprint;
        if (!request.From.IsFinite || !request.To.IsFinite || !(footprint.WidthMetres > 0f) ||
            !(footprint.LengthMetres > 0f) || !CartRouteGeometry.IsFiniteValue(now))
        {
            return Refuse(request.Revision, CartRouteFinding.InvalidRequest, null);
        }

        WorkPoint from = request.From;
        WorkPoint to = request.To;
        if (CartRouteGeometry.FlatDistance(from, to) > _limits.MaxLegMetres)
        {
            return Refuse(request.Revision, CartRouteFinding.LegTooLong, to);
        }

        CartRouteFinding? unloadedStart = CheckLoaded(from);
        if (unloadedStart.HasValue)
        {
            return Refuse(request.Revision, unloadedStart.Value, from);
        }

        CartRouteFinding? unloadedEnd = CheckLoaded(to);
        if (unloadedEnd.HasValue)
        {
            return Refuse(request.Revision, unloadedEnd.Value, to);
        }

        if (Failures != null && Failures.RefusesSpot(to, now))
        {
            return Refuse(request.Revision, CartRouteFinding.RecentFailure, to);
        }

        if (!_budget.TryTake(now))
        {
            return Refuse(request.Revision, CartRouteFinding.QueryBudget, from);
        }

        _corners.Clear();
        CartPathStatus status;
        try
        {
            status = _paths.FindPath(from, to, _corners);
        }
        catch (Exception)
        {
            status = CartPathStatus.Unavailable;
        }

        if (status == CartPathStatus.NotFound || (status == CartPathStatus.Found && !CornersUsable()))
        {
            return Refuse(request.Revision, CartRouteFinding.PathNotFound, to);
        }

        if (status != CartPathStatus.Found)
        {
            return Refuse(request.Revision, CartRouteFinding.PathSourceUnavailable, to);
        }

        var route = new List<WorkPoint>(_corners.Count + 2);
        if (CartRouteGeometry.FlatDistance(from, _corners[0]) > CartRouteGeometry.WaypointArrivalRadiusMetres)
        {
            route.Add(from);
        }

        route.AddRange(_corners);
        if (CartRouteGeometry.FlatDistance(to, _corners[_corners.Count - 1]) > CartRouteGeometry.WaypointArrivalRadiusMetres)
        {
            route.Add(to);
        }

        if (Failures != null && Failures.RefusesRoute(route, now))
        {
            return Refuse(request.Revision, CartRouteFinding.RecentFailure, to);
        }

        var allowance = new CartProbeAllowance(_limits.ClearanceProbesPerPlan);
        CartRouteAssessment assessment = _evaluator.Evaluate(
            route, footprint, request.LoadedMassKg, cartPosition, true, allowance);
        return Accept(assessment, request.Revision, footprint, request.LoadedMassKg, now);
    }

    private CartRoutePlan RefreshCore(CartRoutePlan plan, WorkPoint pullerPosition, WorkPoint cartPosition, float now)
    {
        if (!plan.IsSuitable)
        {
            return plan;
        }

        if (!_contexts.TryGetValue(plan, out PlanContext? context) || context == null ||
            !CartRouteGeometry.IsFiniteValue(now))
        {
            return Refuse(plan.RequestRevision, CartRouteFinding.InvalidRequest, pullerPosition);
        }

        CartSteering steering = Follow(plan, pullerPosition, cartPosition);
        if (steering.Status == CartSteeringStatus.Finished)
        {
            return plan;
        }

        if (steering.Status != CartSteeringStatus.Following)
        {
            return Refuse(plan.RequestRevision, CartRouteFinding.LeftCorridor, pullerPosition);
        }

        var route = new List<WorkPoint>(plan.Waypoints.Count + 1) { pullerPosition };
        for (int index = context.NextIndex; index < plan.Waypoints.Count; index++)
        {
            route.Add(plan.Waypoints[index]);
        }

        if (Failures != null && Failures.RefusesRoute(route, now))
        {
            return Refuse(plan.RequestRevision, CartRouteFinding.RecentFailure, pullerPosition);
        }

        var allowance = new CartProbeAllowance(_limits.ClearanceProbesPerPlan);
        CartRouteAssessment assessment = _evaluator.Evaluate(
            route, context.Footprint, context.LoadedMassKg,
            cartPosition.IsFinite ? cartPosition : (WorkPoint?)null, true, allowance);
        return Accept(assessment, plan.RequestRevision, context.Footprint, context.LoadedMassKg, now);
    }

    private CartRoutePlan Accept(
        CartRouteAssessment assessment, int revision, CartFootprint footprint, float loadedMassKg, float now)
    {
        if (assessment.IsSuitable && Failures != null && Failures.RefusesRoute(assessment.Waypoints, now))
        {
            return Refuse(revision, CartRouteFinding.RecentFailure, assessment.StopPoint);
        }

        LastAssessment = assessment;
        CartRoutePlan plan = assessment.ToPlan(revision);
        if (plan.IsSuitable)
        {
            _contexts.Add(plan, new PlanContext(assessment, footprint, loadedMassKg, now));
        }

        return plan;
    }

    private bool TryPlanBackOffCore(
        WorkPoint pullerPosition, WorkPoint cartPosition, CartFootprint footprint, out SteeringGoal goal)
    {
        goal = default;
        if (!pullerPosition.IsFinite || !cartPosition.IsFinite || !(footprint.WidthMetres > 0f) ||
            !(footprint.LengthMetres > 0f) ||
            !CartRouteGeometry.TryFlatDirection(cartPosition, pullerPosition, out float headingX, out float headingZ))
        {
            LastAssessment = CartRouteAssessment.Refused(
                CartRouteFinding.InvalidRequest, cartPosition, 0f, string.Empty, default);
            return false;
        }

        float distance = _limits.SampleSpacingMetres;
        float halfCorridor = (footprint.WidthMetres * 0.5f) + _limits.SideClearanceMetres;
        WorkPoint pullerTarget = CartRouteGeometry.Offset(pullerPosition, headingX, headingZ, -distance);
        WorkPoint cartTarget = CartRouteGeometry.Offset(cartPosition, headingX, headingZ, -distance);
        int samples = 0;

        CartRouteFinding? problem = null;
        WorkPoint problemAt = cartTarget;

        CartGroundSample now = _probe.SampleGround(cartPosition.X, cartPosition.Z, cartPosition.Y);
        CartGroundSample puller = _probe.SampleGround(pullerTarget.X, pullerTarget.Z, pullerPosition.Y);
        CartGroundSample axle = _probe.SampleGround(cartTarget.X, cartTarget.Z, cartPosition.Y);
        samples += 3;
        float rightX = headingZ;
        float rightZ = -headingX;
        float halfWidth = footprint.WidthMetres * 0.5f;
        float nearWheels = axle.Status == CartGroundStatus.Surface ? axle.Height : cartPosition.Y;
        CartGroundSample left = _probe.SampleGround(
            cartTarget.X - (rightX * halfWidth), cartTarget.Z - (rightZ * halfWidth), nearWheels);
        CartGroundSample right = _probe.SampleGround(
            cartTarget.X + (rightX * halfWidth), cartTarget.Z + (rightZ * halfWidth), nearWheels);
        samples += 2;

        CartGroundSample[] checks = { now, puller, axle, left, right };
        foreach (CartGroundSample sample in checks)
        {
            problem ??= sample.Status switch
            {
                CartGroundStatus.Unloaded => CartRouteFinding.Unloaded,
                CartGroundStatus.Surface when sample.Lava => CartRouteFinding.Lava,
                CartGroundStatus.Surface when sample.LiquidDepthMetres > 0f => CartRouteFinding.Water,
                CartGroundStatus.Surface when CartRouteGeometry.IsFiniteValue(sample.Height) => (CartRouteFinding?)null,
                CartGroundStatus.NoSurface => CartRouteFinding.NoSurface,
                _ => CartRouteFinding.GroundUnreadable,
            };
        }

        if (problem == null)
        {
            float grade = Math.Abs(axle.Height - now.Height) / distance;
            float cross = Math.Abs(left.Height - right.Height) / footprint.WidthMetres;
            if (grade > _limits.MaxGradeRatio || cross > _limits.MaxGradeRatio)
            {
                problem = CartRouteFinding.RunningGradeTooSteep;
            }
        }

        var allowance = new CartProbeAllowance(_limits.ClearanceProbesPerPlan);
        int doorScans = 0;
        if (problem == null)
        {
            WorkPoint boxNow = new CartPose(pullerPosition, cartPosition, headingX, headingZ, 0f)
                .BoxCentre(footprint.LengthMetres, now.Height);
            WorkPoint boxThen = new CartPose(pullerTarget, cartTarget, headingX, headingZ, 0f)
                .BoxCentre(footprint.LengthMetres, axle.Height);

            doorScans++;
            if (!_probe.TryFindDoorways(boxNow, distance + footprint.LengthMetres + (2f * halfCorridor) + 2f, _doorways))
            {
                problem = CartRouteFinding.DoorScanUnreadable;
            }
            else
            {
                foreach (CartDoorway doorway in _doorways)
                {
                    if (doorway.IsCrossedBy(boxNow, boxThen, halfCorridor) ||
                        doorway.IsCrossedBy(pullerPosition, pullerTarget, halfCorridor))
                    {
                        problem = CartRouteFinding.Doorway;
                        problemAt = doorway.FloorCentre;
                        break;
                    }
                }
            }

            if (problem == null)
            {
                allowance.TryTake();
                CartClearanceSample sweep = _probe.SweepBox(
                    boxNow, boxThen, headingX, headingZ, halfCorridor, footprint.LengthMetres * 0.5f, true);
                if (sweep.Status == CartClearanceStatus.Blocked)
                {
                    problem = CartRouteFinding.Obstructed;
                }
                else if (sweep.Status != CartClearanceStatus.Clear)
                {
                    problem = CartRouteFinding.ClearanceUnreadable;
                }
            }
        }

        var costs = new CartRouteCosts(samples, allowance.Used, doorScans, 0);
        if (problem != null)
        {
            LastAssessment = CartRouteAssessment.Refused(problem.Value, problemAt, 0f, string.Empty, costs);
            return false;
        }

        LastAssessment = CartRouteAssessment.Suitable(
            new[] { pullerPosition, pullerTarget }, 1,
            new[] { pullerPosition, pullerTarget }, new[] { cartPosition, cartTarget }, 1,
            distance, Math.Abs(axle.Height - now.Height) / distance, 2f * halfCorridor, 0f, 180f, costs);
        goal = new SteeringGoal(pullerTarget, CartRouteGeometry.WaypointArrivalRadiusMetres, true, 1, -1);
        return true;
    }

    private CartRouteFinding? CheckLoaded(WorkPoint point)
    {
        try
        {
            return _probe.IsLoaded(point) ? (CartRouteFinding?)null : CartRouteFinding.Unloaded;
        }
        catch (Exception)
        {
            return CartRouteFinding.ProbeFaulted;
        }
    }

    private bool CornersUsable()
    {
        if (_corners.Count < 2)
        {
            return false;
        }

        foreach (WorkPoint corner in _corners)
        {
            if (!corner.IsFinite)
            {
                return false;
            }
        }

        return true;
    }

    private static bool CartInsideCorridor(PlanContext context, WorkPoint cart, float half)
    {
        IReadOnlyList<WorkPoint> track = context.Assessment.CartTrack;
        int last = Math.Min(track.Count - 1, context.Assessment.StopTrackIndex);
        if (last < 1)
        {
            return track.Count > 0 && CartRouteGeometry.FlatDistance(cart, track[0]) <= half;
        }

        for (int segment = 0; segment < last; segment++)
        {
            if (CartRouteGeometry.FlatDistanceToSegment(cart, track[segment], track[segment + 1], out _) <= half)
            {
                return true;
            }
        }

        return false;
    }

    private static int IndexOfStop(CartRoutePlan plan)
    {
        if (!plan.StopPoint.HasValue)
        {
            return -1;
        }

        WorkPoint stop = plan.StopPoint.Value;
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int index = 1; index < plan.Waypoints.Count; index++)
        {
            float distance = CartRouteGeometry.FlatDistance(stop, plan.Waypoints[index]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = index;
            }
        }

        return best;
    }

    private CartRoutePlan Refuse(int revision, CartRouteFinding finding, WorkPoint? at)
    {
        LastAssessment = CartRouteAssessment.Refused(finding, at, 0f, string.Empty, default);
        return CartRoutePlan.Refused(CartRouteFindings.VerdictOf(finding), revision);
    }

    private sealed class PlanContext
    {
        public PlanContext(CartRouteAssessment assessment, CartFootprint footprint, float loadedMassKg, float checkedAt)
        {
            Assessment = assessment;
            Footprint = footprint;
            LoadedMassKg = loadedMassKg;
            CheckedAt = checkedAt;
        }

        public CartRouteAssessment Assessment { get; }

        public CartFootprint Footprint { get; }

        public float LoadedMassKg { get; }

        public float CheckedAt { get; }

        /// <summary>The first waypoint not yet reached; only ever grows.</summary>
        public int NextIndex { get; set; } = 1;
    }
}
