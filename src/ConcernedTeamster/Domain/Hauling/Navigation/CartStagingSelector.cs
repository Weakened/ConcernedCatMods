using System;
using System.Collections.Generic;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Navigation;

/// <summary>Where a cart should be brought when the point asked for is not a
/// place a loaded cart can reach or be left.</summary>
internal readonly struct CartStagingRequest
{
    public CartStagingRequest(
        WorkPoint desired,
        WorkPoint cartPosition,
        WorkPoint? pullerPosition,
        CartFootprint footprint,
        float loadedMassKg,
        float searchRadiusMetres,
        int revision)
    {
        Desired = desired;
        CartPosition = cartPosition;
        PullerPosition = pullerPosition;
        Footprint = footprint;
        LoadedMassKg = loadedMassKg;
        SearchRadiusMetres = searchRadiusMetres;
        Revision = revision;
    }

    /// <summary>Where the cart is wanted: a work patch, a delivery container.
    /// </summary>
    public WorkPoint Desired { get; }

    /// <summary>Where the cart stands; routes start here.</summary>
    public WorkPoint CartPosition { get; }

    /// <summary>Where Gunnar is, when he holds the handle.</summary>
    public WorkPoint? PullerPosition { get; }

    public CartFootprint Footprint { get; }

    public float LoadedMassKg { get; }

    /// <summary>How far from <see cref="Desired"/> a spot may be; at most
    /// <see cref="HaulLimits.MaxLegMetres"/>.</summary>
    public float SearchRadiusMetres { get; }

    public int Revision { get; }
}

internal enum CartStagingOutcome
{
    Unspecified = 0,

    /// <summary>A spot and a suitable plan that stops there.</summary>
    Chosen = 1,

    /// <summary>No candidate within the radius is safe and reachable.</summary>
    NoSafeSpot = 2,

    /// <summary>A budget ran out first; ask again later.</summary>
    BudgetExhausted = 3,

    /// <summary>The request had no usable points or cart size.</summary>
    Invalid = 4,
}

internal sealed class CartStagingChoice
{
    public CartStagingChoice(
        CartStagingOutcome outcome, WorkPoint? spot, CartRoutePlan? plan, int candidatesChecked, int routesPlanned,
        CartRouteVerdict lastRouteVerdict)
    {
        Outcome = outcome;
        Spot = spot;
        Plan = plan;
        CandidatesChecked = candidatesChecked;
        RoutesPlanned = routesPlanned;
        LastRouteVerdict = lastRouteVerdict;
    }

    public CartStagingOutcome Outcome { get; }

    /// <summary>Where Gunnar stops. The cart stands one hitch length behind him,
    /// on the ground the plan checked for parking.</summary>
    public WorkPoint? Spot { get; }

    /// <summary>A plan from Gunnar to <see cref="Spot"/> whose stop is the spot.
    /// </summary>
    public CartRoutePlan? Plan { get; }

    public int CandidatesChecked { get; }

    public int RoutesPlanned { get; }

    /// <summary>Why the last planned candidate was refused, for the log.
    /// </summary>
    public CartRouteVerdict LastRouteVerdict { get; }
}

/// <summary>Chooses a staging or rendezvous spot (CART-05): the nearest place
/// to the desired point where the loaded cart can be pulled and left level -
/// for when Gunnar cannot bring the cart into a narrow patch, but Thorstein can
/// carry to it.
///
/// Candidates are the desired point itself, then rings around it one cart length
/// apart, each ring starting on the side Gunnar approaches from and alternating
/// outwards; the order depends only on the request, so the same world gives the
/// same choice. Each candidate is first checked cheaply - loaded, not a recent
/// failure, dry and supported, level under where the cart would stand, one box
/// check for room - and only then asked for a full route, which must be suitable
/// and stop at the candidate itself. At most
/// <see cref="HaulLimits.ClearanceProbesPerPlan"/> candidates are looked at, and
/// every route spends the planner's shared query budget, so a selection is
/// bounded.</summary>
internal sealed class CartStagingSelector
{
    private readonly CartRoutePlanner _planner;
    private readonly ICartTerrainProbe _probe;
    private readonly HaulLimits _limits;
    private readonly List<WorkPoint> _candidates = new List<WorkPoint>();

    public CartStagingSelector(CartRoutePlanner planner, ICartTerrainProbe probe)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _limits = planner.Limits;
    }

    /// <summary>Never throws.</summary>
    public CartStagingChoice Select(CartStagingRequest request, float now)
    {
        try
        {
            return SelectCore(request, now);
        }
        catch (Exception exception) when (!(exception is OutOfMemoryException))
        {
            return new CartStagingChoice(CartStagingOutcome.NoSafeSpot, null, null, 0, 0, CartRouteVerdict.NoPath);
        }
    }

    /// <summary>The candidate spots, nearest first: <paramref name="desired"/>,
    /// then rings <paramref name="spacing"/> apart out to
    /// <paramref name="radius"/>, each starting towards
    /// <paramref name="towards"/> and alternating either side. At most
    /// <paramref name="maxCount"/>.</summary>
    public static void BuildCandidates(
        WorkPoint desired, WorkPoint towards, float spacing, float radius, int maxCount, List<WorkPoint> into)
    {
        into.Clear();
        if (maxCount < 1 || !(spacing > 0f))
        {
            return;
        }

        into.Add(desired);
        double baseAngle = CartRouteGeometry.TryFlatDirection(desired, towards, out float towardsX, out float towardsZ)
            ? Math.Atan2(towardsZ, towardsX)
            : 0d;
        int rings = (int)Math.Floor(Math.Max(0f, radius) / spacing);
        for (int ring = 1; ring <= rings && into.Count < maxCount; ring++)
        {
            double ringRadius = ring * spacing;
            int count = Math.Max(6, (int)Math.Floor(2d * Math.PI * ringRadius / spacing));
            double step = 2d * Math.PI / count;
            for (int slot = 0; slot < count && into.Count < maxCount; slot++)
            {
                int offset = (slot + 1) / 2;
                if (slot % 2 == 0)
                {
                    offset = -offset;
                }

                double angle = baseAngle + (offset * step);
                into.Add(new WorkPoint(
                    desired.X + (float)(Math.Cos(angle) * ringRadius),
                    desired.Y,
                    desired.Z + (float)(Math.Sin(angle) * ringRadius)));
            }
        }
    }

    private CartStagingChoice SelectCore(CartStagingRequest request, float now)
    {
        CartFootprint footprint = request.Footprint;
        if (!request.Desired.IsFinite || !request.CartPosition.IsFinite || !(footprint.WidthMetres > 0f) ||
            !(footprint.LengthMetres > 0f) || !CartRouteGeometry.IsFiniteValue(now) ||
            !CartRouteGeometry.IsFiniteValue(request.SearchRadiusMetres) ||
            (request.PullerPosition.HasValue && !request.PullerPosition.Value.IsFinite))
        {
            return new CartStagingChoice(CartStagingOutcome.Invalid, null, null, 0, 0, CartRouteVerdict.Unspecified);
        }

        float radius = Math.Max(0f, Math.Min(request.SearchRadiusMetres, _limits.MaxLegMetres));
        float spacing = Math.Max(footprint.LengthMetres, _limits.SampleSpacingMetres);
        BuildCandidates(request.Desired, request.CartPosition, spacing, radius, _limits.ClearanceProbesPerPlan, _candidates);

        var allowance = new CartProbeAllowance(_limits.ClearanceProbesPerPlan);
        float halfCorridor = (footprint.WidthMetres * 0.5f) + _limits.SideClearanceMetres;
        float level = _limits.MaxParkingGradeRatio;
        int checkedCount = 0;
        int routes = 0;
        CartRouteVerdict lastVerdict = CartRouteVerdict.Unspecified;

        foreach (WorkPoint candidate in _candidates)
        {
            checkedCount++;
            if (_planner.Failures != null && _planner.Failures.RefusesSpot(candidate, now))
            {
                continue;
            }

            if (!_probe.IsLoaded(candidate))
            {
                continue;
            }

            if (!CartRouteGeometry.TryFlatDirection(request.CartPosition, candidate, out float headingX, out float headingZ))
            {
                // The candidate is where the cart already stands: not a leg.
                continue;
            }

            if (!IsLevelParking(candidate, headingX, headingZ, footprint, level, out float axleHeight))
            {
                continue;
            }

            if (!allowance.TryTake())
            {
                return new CartStagingChoice(CartStagingOutcome.BudgetExhausted, null, null, checkedCount, routes, lastVerdict);
            }

            var pose = new CartPose(
                candidate,
                CartRouteGeometry.Offset(candidate, headingX, headingZ, -footprint.HitchLengthMetres),
                headingX, headingZ, 0f);
            CartClearanceSample room = _probe.CheckBox(
                pose.BoxCentre(footprint.LengthMetres, axleHeight), headingX, headingZ, halfCorridor,
                footprint.LengthMetres * 0.5f);
            if (room.Status != CartClearanceStatus.Clear)
            {
                continue;
            }

            CartRoutePlan plan = _planner.Plan(
                new CartRouteRequest(request.CartPosition, candidate, footprint, request.LoadedMassKg, request.Revision),
                request.PullerPosition,
                now);
            routes++;
            if (plan.Verdict == CartRouteVerdict.BudgetExhausted)
            {
                return new CartStagingChoice(CartStagingOutcome.BudgetExhausted, null, null, checkedCount, routes, plan.Verdict);
            }

            lastVerdict = plan.Verdict;
            if (plan.IsSuitable && plan.StopPoint.HasValue &&
                CartRouteGeometry.FlatDistance(plan.StopPoint.Value, plan.Waypoints[plan.Waypoints.Count - 1]) <=
                CartRouteGeometry.SamePointMetres)
            {
                return new CartStagingChoice(CartStagingOutcome.Chosen, candidate, plan, checkedCount, routes, lastVerdict);
            }
        }

        return new CartStagingChoice(CartStagingOutcome.NoSafeSpot, null, null, checkedCount, routes, lastVerdict);
    }

    /// <summary>Whether a cart arriving at <paramref name="spot"/> along the
    /// heading would stand on dry, supported, level ground: under Gunnar, the
    /// axle and both wheels.</summary>
    private bool IsLevelParking(
        WorkPoint spot, float headingX, float headingZ, CartFootprint footprint, float level, out float axleHeight)
    {
        axleHeight = spot.Y;
        CartGroundSample atSpot = _probe.SampleGround(spot.X, spot.Z, spot.Y);
        if (!IsDrySurface(atSpot))
        {
            return false;
        }

        WorkPoint axle = CartRouteGeometry.Offset(spot, headingX, headingZ, -footprint.HitchLengthMetres);
        CartGroundSample atAxle = _probe.SampleGround(axle.X, axle.Z, atSpot.Height);
        if (!IsDrySurface(atAxle))
        {
            return false;
        }

        float halfWidth = footprint.WidthMetres * 0.5f;
        float rightX = headingZ;
        float rightZ = -headingX;
        CartGroundSample atLeft = _probe.SampleGround(axle.X - (rightX * halfWidth), axle.Z - (rightZ * halfWidth), atAxle.Height);
        CartGroundSample atRight = _probe.SampleGround(axle.X + (rightX * halfWidth), axle.Z + (rightZ * halfWidth), atAxle.Height);
        if (!IsDrySurface(atLeft) || !IsDrySurface(atRight))
        {
            return false;
        }

        axleHeight = atAxle.Height;
        float cross = Math.Abs(atLeft.Height - atRight.Height) / footprint.WidthMetres;
        bool alongLevel = footprint.HitchLengthMetres < 0.5f * _limits.SampleSpacingMetres ||
            Math.Abs(atSpot.Height - atAxle.Height) / footprint.HitchLengthMetres <= level;
        return cross <= level && alongLevel;
    }

    private static bool IsDrySurface(CartGroundSample sample)
    {
        return sample.Status == CartGroundStatus.Surface && CartRouteGeometry.IsFiniteValue(sample.Height) &&
            !sample.Lava && !(sample.LiquidDepthMetres > 0f);
    }
}
