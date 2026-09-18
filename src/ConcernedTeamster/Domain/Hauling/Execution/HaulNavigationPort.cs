using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;

/// <summary>Why steering has or has no goal. Zero is unspecified.</summary>
internal enum HaulSteeringStatus
{
    Unspecified = 0,

    /// <summary>A goal to steer toward is given.</summary>
    Following = 1,

    /// <summary>Gunnar reached the plan's stop.</summary>
    Finished = 2,

    /// <summary>Gunnar or the cart left the corridor the plan verified: plan
    /// again from here.</summary>
    LeftCorridor = 3,

    /// <summary>The plan is refused or empty.</summary>
    NotSuitable = 4,
}

/// <summary>One local steering answer.</summary>
internal readonly struct HaulSteering
{
    public HaulSteering(HaulSteeringStatus status, SteeringGoal? goal)
    {
        Status = status;
        Goal = goal;
    }

    public HaulSteeringStatus Status { get; }

    public SteeringGoal? Goal { get; }
}

/// <summary>The few navigation calls the executor needs beyond the C1
/// <see cref="ICartRoutePlanner"/> and <see cref="IHaulMotionMonitor"/> seams,
/// kept narrow so the executor never depends on agent B's types (#313/#314
/// integration). The adapter implements it over B's navigation kit; without it
/// the executor works on the contract interfaces alone.
///
/// Why each call exists:
/// - the planner measures the leased cart once and keeps it and Gunnar out of its
///   own clearance probes, so a plan with an unmeasured cart is refused;
/// - a plan made with Gunnar's real position predicts the first turn from the
///   cart's real heading;
/// - steering says when a plan is finished rather than merely goal-less, so
///   arriving is never mistaken for a lost route;
/// - a running plan is re-checked against the world, and a refusal ends it;
/// - every stall or wedge is remembered, so no re-plan drives back into the same
///   snag the same way.</summary>
internal interface IHaulNavigation
{
    /// <summary>A cart became Gunnar's lease: measure it. False when it cannot
    /// be measured, and nothing is planned for it until it can.</summary>
    bool UseCart(CartKey cart);

    /// <summary>The lease ended: nothing learned about that cart is kept.
    /// </summary>
    void ReleaseCart();

    /// <summary>The footprint of the cart in use, when measured.</summary>
    CartFootprint? Footprint { get; }

    /// <summary>Plans a leg; <paramref name="pullerPosition"/> is where Gunnar
    /// holds (or will hold) the handle, when known.</summary>
    CartRoutePlan Plan(CartRouteRequest request, WorkPoint? pullerPosition, float now);

    HaulSteering Steer(CartRoutePlan plan, WorkPoint pullerPosition, WorkPoint cartPosition);

    bool NeedsRefresh(CartRoutePlan plan, float now);

    /// <summary>What is left of the plan, checked against the world from where
    /// both bodies are; a refusal when it no longer holds.</summary>
    CartRoutePlan Refresh(CartRoutePlan plan, WorkPoint pullerPosition, WorkPoint cartPosition, float now);

    /// <summary>The cart was held at <paramref name="cartPosition"/> while
    /// going toward <paramref name="pullerPosition"/> on a leg to
    /// <paramref name="legTarget"/>.</summary>
    void RememberStall(WorkPoint legTarget, WorkPoint cartPosition, WorkPoint pullerPosition, float now);
}
