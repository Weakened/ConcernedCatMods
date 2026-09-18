using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Navigation;

/// <summary>Exactly what decided a route, finer than the contract's
/// <see cref="CartRouteVerdict"/>: which check, so the log and a reviewer can
/// tell a measured narrow passage from a turn the cart cannot follow. Each
/// finding belongs to exactly one verdict (<see cref="CartRouteFindings"/>);
/// players are told the verdict's sentence, never a finding's.</summary>
internal enum CartRouteFinding
{
    Unspecified = 0,

    /// <summary>Every check passed.</summary>
    Suitable = 1,

    // NoPath
    InvalidRequest = 2,
    LegTooLong = 3,
    PathNotFound = 4,
    PathSourceUnavailable = 5,

    /// <summary>The route goes where a haul recently stuck, the same way, or
    /// ends where a leg recently failed (<see cref="CartRouteFailureCache"/>).
    /// </summary>
    RecentFailure = 6,

    GroundUnreadable = 7,
    ClearanceUnreadable = 8,
    DoorScanUnreadable = 9,

    /// <summary>A probe threw instead of answering; caught here.</summary>
    ProbeFaulted = 10,

    // OutsideLoadedArea
    Unloaded = 11,

    // TooSteep
    RunningGradeTooSteep = 12,
    CrossSlopeTooSteep = 13,
    LedgeUp = 14,

    /// <summary>Teamster's calibrated climb data says this load stalls or
    /// breaks the hitch on this grade.</summary>
    CalibratedClimbRefused = 15,

    /// <summary>Teamster's calibrated descent data says this load was dragged,
    /// ran away or broke the hitch on this grade.</summary>
    CalibratedDescentRefused = 16,

    // TooNarrow
    Obstructed = 17,

    /// <summary>The cart, trailing behind Gunnar, would cut into something on
    /// the inside of a turn.</summary>
    InsideCornerClipped = 18,

    /// <summary>The turn is sharper than a cart on a hitch can follow.</summary>
    TurnTooSharp = 19,

    /// <summary>The free room measured across the route is less than the cart
    /// and its side clearance.</summary>
    MeasuredTooNarrow = 20,

    /// <summary>Squeezing past obstacles would need more course corrections
    /// than the route has room for.</summary>
    RepairsExhausted = 21,

    // ForbiddenDoor
    Doorway = 22,

    // Water
    Water = 23,
    Lava = 24,

    // UnsupportedGap
    NoSurface = 25,
    Drop = 26,
    WheelUnsupported = 27,

    // BudgetExhausted
    QueryBudget = 28,
    ProbeBudget = 29,

    // UnsafeStop
    NoLevelStop = 30,

    /// <summary>Gunnar or the cart is no longer inside the corridor the plan
    /// verified, so the plan says nothing about where they are (NoPath: plan
    /// again from here).</summary>
    LeftCorridor = 31,

    /// <summary>The target is within one hitch length of the cart: there is no
    /// leg to pull (NoPath).</summary>
    TargetWithinHitch = 32,
}

/// <summary>How findings, verdicts and attention reasons relate.</summary>
internal static class CartRouteFindings
{
    private static readonly Dictionary<CartRouteFinding, CartRouteVerdict> Verdicts =
        new Dictionary<CartRouteFinding, CartRouteVerdict>
        {
            [CartRouteFinding.Suitable] = CartRouteVerdict.Suitable,
            [CartRouteFinding.InvalidRequest] = CartRouteVerdict.NoPath,
            [CartRouteFinding.LegTooLong] = CartRouteVerdict.NoPath,
            [CartRouteFinding.PathNotFound] = CartRouteVerdict.NoPath,
            [CartRouteFinding.PathSourceUnavailable] = CartRouteVerdict.NoPath,
            [CartRouteFinding.RecentFailure] = CartRouteVerdict.NoPath,
            [CartRouteFinding.GroundUnreadable] = CartRouteVerdict.NoPath,
            [CartRouteFinding.ClearanceUnreadable] = CartRouteVerdict.NoPath,
            [CartRouteFinding.DoorScanUnreadable] = CartRouteVerdict.NoPath,
            [CartRouteFinding.ProbeFaulted] = CartRouteVerdict.NoPath,
            [CartRouteFinding.Unloaded] = CartRouteVerdict.OutsideLoadedArea,
            [CartRouteFinding.RunningGradeTooSteep] = CartRouteVerdict.TooSteep,
            [CartRouteFinding.CrossSlopeTooSteep] = CartRouteVerdict.TooSteep,
            [CartRouteFinding.LedgeUp] = CartRouteVerdict.TooSteep,
            [CartRouteFinding.CalibratedClimbRefused] = CartRouteVerdict.TooSteep,
            [CartRouteFinding.CalibratedDescentRefused] = CartRouteVerdict.TooSteep,
            [CartRouteFinding.Obstructed] = CartRouteVerdict.TooNarrow,
            [CartRouteFinding.InsideCornerClipped] = CartRouteVerdict.TooNarrow,
            [CartRouteFinding.TurnTooSharp] = CartRouteVerdict.TooNarrow,
            [CartRouteFinding.MeasuredTooNarrow] = CartRouteVerdict.TooNarrow,
            [CartRouteFinding.RepairsExhausted] = CartRouteVerdict.TooNarrow,
            [CartRouteFinding.Doorway] = CartRouteVerdict.ForbiddenDoor,
            [CartRouteFinding.Water] = CartRouteVerdict.Water,
            [CartRouteFinding.Lava] = CartRouteVerdict.Water,
            [CartRouteFinding.NoSurface] = CartRouteVerdict.UnsupportedGap,
            [CartRouteFinding.Drop] = CartRouteVerdict.UnsupportedGap,
            [CartRouteFinding.WheelUnsupported] = CartRouteVerdict.UnsupportedGap,
            [CartRouteFinding.QueryBudget] = CartRouteVerdict.BudgetExhausted,
            [CartRouteFinding.ProbeBudget] = CartRouteVerdict.BudgetExhausted,
            [CartRouteFinding.NoLevelStop] = CartRouteVerdict.UnsafeStop,
            [CartRouteFinding.LeftCorridor] = CartRouteVerdict.NoPath,
            [CartRouteFinding.TargetWithinHitch] = CartRouteVerdict.NoPath,
        };

    private static readonly Dictionary<CartRouteFinding, string> Descriptions =
        new Dictionary<CartRouteFinding, string>
        {
            [CartRouteFinding.Suitable] = "every check passed",
            [CartRouteFinding.InvalidRequest] = "the request had no usable start, end or cart size",
            [CartRouteFinding.LegTooLong] = "the leg is longer than one planned leg may be",
            [CartRouteFinding.PathNotFound] = "the navmesh has no full path (it may still be building)",
            [CartRouteFinding.PathSourceUnavailable] = "the navmesh could not be asked",
            [CartRouteFinding.RecentFailure] = "the route repeats a way that recently stuck or failed",
            [CartRouteFinding.GroundUnreadable] = "the ground could not be read",
            [CartRouteFinding.ClearanceUnreadable] = "clearance could not be probed",
            [CartRouteFinding.DoorScanUnreadable] = "doors near the route could not be read",
            [CartRouteFinding.ProbeFaulted] = "a probe failed with an exception",
            [CartRouteFinding.Unloaded] = "part of the route is not loaded",
            [CartRouteFinding.RunningGradeTooSteep] = "the route is steeper than the grade limit",
            [CartRouteFinding.CrossSlopeTooSteep] = "the ground tilts across the cart more than the grade limit",
            [CartRouteFinding.LedgeUp] = "a ledge rises too sharply for the wheels",
            [CartRouteFinding.CalibratedClimbRefused] = "calibration data says this load stalls on this climb",
            [CartRouteFinding.CalibratedDescentRefused] = "calibration data says this load is not controlled on this descent",
            [CartRouteFinding.Obstructed] = "something stands in the cart's way",
            [CartRouteFinding.InsideCornerClipped] = "the trailing cart would cut into something inside a turn",
            [CartRouteFinding.TurnTooSharp] = "a turn is sharper than the cart can follow",
            [CartRouteFinding.MeasuredTooNarrow] = "the passage is narrower than the cart and its clearance",
            [CartRouteFinding.RepairsExhausted] = "the route needs more squeezing past obstacles than it has room for",
            [CartRouteFinding.Doorway] = "the route passes through a doorway",
            [CartRouteFinding.Water] = "the route crosses water",
            [CartRouteFinding.Lava] = "the route crosses lava",
            [CartRouteFinding.NoSurface] = "there is nothing solid under part of the route",
            [CartRouteFinding.Drop] = "the ground drops away along the route",
            [CartRouteFinding.WheelUnsupported] = "a wheel would run off the edge",
            [CartRouteFinding.QueryBudget] = "the navmesh query budget for this minute is spent",
            [CartRouteFinding.ProbeBudget] = "the clearance probe budget for this plan is spent",
            [CartRouteFinding.NoLevelStop] = "there is no level place along the route to stop the cart",
            [CartRouteFinding.LeftCorridor] = "Gunnar or the cart is outside the corridor the plan verified",
            [CartRouteFinding.TargetWithinHitch] = "the target is within one hitch length of the cart",
        };

    public static CartRouteVerdict VerdictOf(CartRouteFinding finding)
    {
        return Verdicts.TryGetValue(finding, out CartRouteVerdict verdict) ? verdict : CartRouteVerdict.Unspecified;
    }

    /// <summary>English for the log only.</summary>
    public static string Describe(CartRouteFinding finding)
    {
        return Descriptions.TryGetValue(finding, out string? text) ? text : "unknown finding";
    }

    /// <summary>The attention reason a refused route stops a haul with. A spent
    /// budget is not a reason to stop: it means "ask again later", so it has
    /// none and this answers false.</summary>
    public static bool TryGetAttention(CartRouteVerdict verdict, out HaulAttentionReason reason)
    {
        switch (verdict)
        {
            case CartRouteVerdict.NoPath:
                reason = HaulAttentionReason.NoRoute;
                return true;
            case CartRouteVerdict.TooSteep:
                reason = HaulAttentionReason.TooSteep;
                return true;
            case CartRouteVerdict.TooNarrow:
                reason = HaulAttentionReason.TooNarrow;
                return true;
            case CartRouteVerdict.ForbiddenDoor:
                reason = HaulAttentionReason.ForbiddenDoor;
                return true;
            case CartRouteVerdict.Water:
                reason = HaulAttentionReason.Water;
                return true;
            case CartRouteVerdict.UnsupportedGap:
                reason = HaulAttentionReason.UnsupportedGap;
                return true;
            case CartRouteVerdict.OutsideLoadedArea:
                reason = HaulAttentionReason.OutsideLoadedArea;
                return true;
            case CartRouteVerdict.UnsafeStop:
                reason = HaulAttentionReason.UnsafeParking;
                return true;
            default:
                reason = HaulAttentionReason.Unspecified;
                return false;
        }
    }

    /// <summary>Every real finding, for exhaustive tests.</summary>
    public static IEnumerable<CartRouteFinding> All()
    {
        foreach (CartRouteFinding finding in Enum.GetValues(typeof(CartRouteFinding)))
        {
            if (finding != CartRouteFinding.Unspecified)
            {
                yield return finding;
            }
        }
    }
}
