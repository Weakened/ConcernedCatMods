using System;
using System.Collections.Generic;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling;

/// <summary>The measured size of the loaded cart that must fit along a route
/// (CART-05). Read from the live cart by an adapter, never assumed.</summary>
internal readonly struct CartFootprint
{
    public CartFootprint(float widthMetres, float lengthMetres, float hitchLengthMetres)
    {
        if (!(widthMetres > 0f) || !(lengthMetres > 0f) || hitchLengthMetres < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(widthMetres), "A footprint needs a positive width and length.");
        }

        WidthMetres = widthMetres;
        LengthMetres = lengthMetres;
        HitchLengthMetres = hitchLengthMetres;
    }

    public float WidthMetres { get; }

    public float LengthMetres { get; }

    /// <summary>From Gunnar's pivot to the cart's axle line: how far the cart
    /// trails him, which is what makes it cut corners.</summary>
    public float HitchLengthMetres { get; }
}

/// <summary>Why a route is or is not one a loaded cart can take.</summary>
internal enum CartRouteVerdict
{
    Unspecified = 0,
    Suitable = 1,
    NoPath = 2,
    TooSteep = 3,
    TooNarrow = 4,
    ForbiddenDoor = 5,
    Water = 6,
    UnsupportedGap = 7,
    OutsideLoadedArea = 8,

    /// <summary>The query budget ran out before an answer; try later.</summary>
    BudgetExhausted = 9,

    /// <summary>The route exists but its end is not a safe place to stop a
    /// loaded cart (a slope it could roll down).</summary>
    UnsafeStop = 10,
}

internal readonly struct CartRouteRequest
{
    public CartRouteRequest(WorkPoint from, WorkPoint to, CartFootprint footprint, float loadedMassKg, int revision)
    {
        From = from;
        To = to;
        Footprint = footprint;
        LoadedMassKg = loadedMassKg;
        Revision = revision;
    }

    public WorkPoint From { get; }

    public WorkPoint To { get; }

    public CartFootprint Footprint { get; }

    /// <summary>The cart's current total mass, for grade limits that depend on
    /// load. Measured, not assumed.</summary>
    public float LoadedMassKg { get; }

    public int Revision { get; }
}

/// <summary>A route the navigation layer (agent B) vouches for: waypoints a
/// loaded cart fits along, and where it may stop. Global planning; local
/// steering happens one <see cref="SteeringGoal"/> at a time.</summary>
internal sealed class CartRoutePlan
{
    public CartRoutePlan(
        CartRouteVerdict verdict,
        IReadOnlyList<WorkPoint> waypoints,
        float lengthMetres,
        float steepestGradeRatio,
        float narrowestClearanceMetres,
        WorkPoint? stopPoint,
        int requestRevision)
    {
        Verdict = verdict;
        Waypoints = waypoints ?? throw new ArgumentNullException(nameof(waypoints));
        LengthMetres = lengthMetres;
        SteepestGradeRatio = steepestGradeRatio;
        NarrowestClearanceMetres = narrowestClearanceMetres;
        StopPoint = stopPoint;
        RequestRevision = requestRevision;
    }

    public CartRouteVerdict Verdict { get; }

    public bool IsSuitable => Verdict == CartRouteVerdict.Suitable && Waypoints.Count >= 2;

    /// <summary>From the start to the end, including both.</summary>
    public IReadOnlyList<WorkPoint> Waypoints { get; }

    public float LengthMetres { get; }

    /// <summary>Rise over run of the steepest measured stretch.</summary>
    public float SteepestGradeRatio { get; }

    public float NarrowestClearanceMetres { get; }

    /// <summary>Where the loaded cart may be stopped and left standing: the end
    /// of the route when that is safe, else the nearest safe point before it.
    /// </summary>
    public WorkPoint? StopPoint { get; }

    public int RequestRevision { get; }

    public static CartRoutePlan Refused(CartRouteVerdict verdict, int requestRevision)
    {
        if (verdict == CartRouteVerdict.Suitable || verdict == CartRouteVerdict.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(verdict), "A refusal needs a refusing verdict.");
        }

        return new CartRoutePlan(verdict, Array.Empty<WorkPoint>(), 0f, 0f, 0f, null, requestRevision);
    }
}

/// <summary>The next thing to walk toward. Agent B hands these to agent A's
/// executor; B never moves the body itself.</summary>
internal readonly struct SteeringGoal
{
    public SteeringGoal(WorkPoint target, float arrivalRadiusMetres, bool isFinalStop, int waypointIndex, int planRevision)
    {
        Target = target;
        ArrivalRadiusMetres = arrivalRadiusMetres;
        IsFinalStop = isFinalStop;
        WaypointIndex = waypointIndex;
        PlanRevision = planRevision;
    }

    public WorkPoint Target { get; }

    public float ArrivalRadiusMetres { get; }

    /// <summary>The last goal of a plan: arriving means Stopping.</summary>
    public bool IsFinalStop { get; }

    public int WaypointIndex { get; }

    public int PlanRevision { get; }
}

/// <summary>The seam between cart navigation (agent B) and cart mechanics
/// (agent A). B implements it over the game's navmesh and terrain; A only
/// calls it.</summary>
internal interface ICartRoutePlanner
{
    /// <summary>Plans or refuses. Must respect <see cref="HaulLimits"/> query
    /// budgets and never throw for game reasons (it answers a verdict).</summary>
    CartRoutePlan Plan(CartRouteRequest request, float now);

    /// <summary>The goal to steer toward from <paramref name="pullerPosition"/>
    /// along <paramref name="plan"/>, or null when the plan is finished or no
    /// longer valid from here.</summary>
    SteeringGoal? NextGoal(CartRoutePlan plan, WorkPoint pullerPosition, WorkPoint cartPosition);
}
