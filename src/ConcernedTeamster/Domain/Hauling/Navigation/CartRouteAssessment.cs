using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Navigation;

/// <summary>Everything one route evaluation found: the finding that decided
/// it, where, what the checks measured and what they cost. A suitable
/// assessment also carries the verified route itself - Gunnar's line and the
/// cart's predicted track, sample for sample - which the planner keeps beside
/// the contract's <see cref="CartRoutePlan"/> for steering and rechecks.
/// </summary>
internal sealed class CartRouteAssessment
{
    private static readonly IReadOnlyList<WorkPoint> NoPoints = Array.Empty<WorkPoint>();

    private CartRouteAssessment(
        CartRouteFinding finding,
        WorkPoint? at,
        float atAlongMetres,
        string obstacle,
        IReadOnlyList<WorkPoint> waypoints,
        int stopWaypointIndex,
        IReadOnlyList<WorkPoint> pullerTrack,
        IReadOnlyList<WorkPoint> cartTrack,
        int stopTrackIndex,
        float lengthMetres,
        float steepestGradeRatio,
        float corridorWidthMetres,
        float stopShortfallMetres,
        float maxArticulationDegrees,
        CartRouteCosts costs)
    {
        Finding = finding;
        At = at;
        AtAlongMetres = atAlongMetres;
        Obstacle = obstacle ?? string.Empty;
        Waypoints = waypoints;
        StopWaypointIndex = stopWaypointIndex;
        PullerTrack = pullerTrack;
        CartTrack = cartTrack;
        StopTrackIndex = stopTrackIndex;
        LengthMetres = lengthMetres;
        SteepestGradeRatio = steepestGradeRatio;
        CorridorWidthMetres = corridorWidthMetres;
        StopShortfallMetres = stopShortfallMetres;
        MaxArticulationDegrees = maxArticulationDegrees;
        Costs = costs;
    }

    public CartRouteFinding Finding { get; }

    public CartRouteVerdict Verdict => CartRouteFindings.VerdictOf(Finding);

    public bool IsSuitable => Finding == CartRouteFinding.Suitable;

    /// <summary>Where the deciding problem is; for a suitable route, the stop.
    /// </summary>
    public WorkPoint? At { get; }

    /// <summary>How far along Gunnar's line <see cref="At"/> is.</summary>
    public float AtAlongMetres { get; }

    /// <summary>What blocked the cart, for the log; empty when nothing did.
    /// </summary>
    public string Obstacle { get; }

    /// <summary>Gunnar's verified line, start to end, simplified to the points
    /// that bend it and the stop. Empty when refused.</summary>
    public IReadOnlyList<WorkPoint> Waypoints { get; }

    /// <summary>The waypoint Gunnar stops at: the last one when the end is a
    /// safe place for the cart, else an earlier one. -1 when refused.</summary>
    public int StopWaypointIndex { get; }

    /// <summary>Every sample of Gunnar's verified line. Empty when refused.
    /// </summary>
    public IReadOnlyList<WorkPoint> PullerTrack { get; }

    /// <summary>The cart axle's predicted position at each puller sample.
    /// </summary>
    public IReadOnlyList<WorkPoint> CartTrack { get; }

    public int StopTrackIndex { get; }

    public float LengthMetres { get; }

    public float SteepestGradeRatio { get; }

    /// <summary>The free width every stretch was verified to have: the cart's
    /// width plus its side clearance on both sides.</summary>
    public float CorridorWidthMetres { get; }

    /// <summary>How far short of the route's end the stop is.</summary>
    public float StopShortfallMetres { get; }

    public float MaxArticulationDegrees { get; }

    public CartRouteCosts Costs { get; }

    public WorkPoint? StopPoint => IsSuitable && StopWaypointIndex >= 0 ? Waypoints[StopWaypointIndex] : (WorkPoint?)null;

    /// <summary>The contract plan for this assessment.</summary>
    public CartRoutePlan ToPlan(int requestRevision)
    {
        if (!IsSuitable)
        {
            return CartRoutePlan.Refused(Verdict, requestRevision);
        }

        return new CartRoutePlan(
            CartRouteVerdict.Suitable,
            Waypoints,
            LengthMetres,
            SteepestGradeRatio,
            CorridorWidthMetres,
            StopPoint,
            requestRevision);
    }

    /// <summary>One line for the log.</summary>
    public string Describe()
    {
        string where = At.HasValue
            ? string.Format(CultureInfo.InvariantCulture, " at {0} ({1:0.#} m along)", At.Value, AtAlongMetres)
            : string.Empty;
        string what = Obstacle.Length > 0 ? " [" + Obstacle + "]" : string.Empty;
        return Verdict + "/" + Finding + ": " + CartRouteFindings.Describe(Finding) + where + what +
            string.Format(
                CultureInfo.InvariantCulture,
                "; {0} ground samples, {1} clearance probes, {2} door scans, {3} repairs",
                Costs.GroundSamples, Costs.ClearanceProbes, Costs.DoorScans, Costs.Repairs);
    }

    public static CartRouteAssessment Refused(
        CartRouteFinding finding, WorkPoint? at, float atAlongMetres, string obstacle, CartRouteCosts costs)
    {
        if (finding == CartRouteFinding.Suitable || finding == CartRouteFinding.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(finding), "A refusal needs a refusing finding.");
        }

        return new CartRouteAssessment(
            finding, at, atAlongMetres, obstacle, NoPoints, -1, NoPoints, NoPoints, -1, 0f, 0f, 0f, 0f, 0f, costs);
    }

    public static CartRouteAssessment Suitable(
        IReadOnlyList<WorkPoint> waypoints,
        int stopWaypointIndex,
        IReadOnlyList<WorkPoint> pullerTrack,
        IReadOnlyList<WorkPoint> cartTrack,
        int stopTrackIndex,
        float lengthMetres,
        float steepestGradeRatio,
        float corridorWidthMetres,
        float stopShortfallMetres,
        float maxArticulationDegrees,
        CartRouteCosts costs)
    {
        if (waypoints == null || waypoints.Count < 2 || stopWaypointIndex < 1 || stopWaypointIndex >= waypoints.Count)
        {
            throw new ArgumentException("A suitable route needs two waypoints and a stop after the start.");
        }

        return new CartRouteAssessment(
            CartRouteFinding.Suitable,
            waypoints[stopWaypointIndex],
            lengthMetres - stopShortfallMetres,
            string.Empty,
            waypoints,
            stopWaypointIndex,
            pullerTrack,
            cartTrack,
            stopTrackIndex,
            lengthMetres,
            steepestGradeRatio,
            corridorWidthMetres,
            stopShortfallMetres,
            maxArticulationDegrees,
            costs);
    }
}

/// <summary>What one evaluation asked of the world.</summary>
internal readonly struct CartRouteCosts
{
    public CartRouteCosts(int groundSamples, int clearanceProbes, int doorScans, int repairs)
    {
        GroundSamples = groundSamples;
        ClearanceProbes = clearanceProbes;
        DoorScans = doorScans;
        Repairs = repairs;
    }

    public int GroundSamples { get; }

    public int ClearanceProbes { get; }

    public int DoorScans { get; }

    /// <summary>Course corrections made to squeeze the cart past obstacles.
    /// </summary>
    public int Repairs { get; }
}
