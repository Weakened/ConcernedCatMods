namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;

/// <summary>The precise protocol-level answer behind a
/// <see cref="HaulCommandResult"/>. C1's result carries a
/// <see cref="HaulAttentionReason"/>, which has no value for protocol answers
/// such as "busy" or "unknown haul"; this detail lets the
/// <c>concernedcat.haul/1</c> provider map every answer to its exact wire reason
/// instead of guessing. Zero is unspecified.</summary>
internal enum HaulCommandDetail
{
    Unspecified = 0,

    Accepted = 1,

    /// <summary>The expected revision is not the current one.</summary>
    StaleRevision = 2,

    /// <summary>Work authority is not granted.</summary>
    NoAuthority = 3,

    /// <summary>The seam is missing or Gunnar's body cannot work.</summary>
    WorkerUnavailable = 4,

    /// <summary>No lease is active.</summary>
    NoLease = 5,

    /// <summary>The request names a lease other than the active one.</summary>
    LeaseMismatch = 6,

    /// <summary>Another haul holds Gunnar, or this haul is not in Ready or
    /// Waiting.</summary>
    HaulBusy = 7,

    /// <summary>A transfer is in progress (Unloading); nothing may move.
    /// </summary>
    MotionForbidden = 8,

    /// <summary>The haul id is not the active haul.</summary>
    UnknownHaul = 9,

    /// <summary>The route planner refused the leg; the result's reason names the
    /// route problem. Nothing moved.</summary>
    RouteRefused = 10,

    /// <summary>The route planner's query budget is spent; try again later.
    /// Nothing moved.</summary>
    PlannerBudgetExhausted = 11,

    /// <summary><c>Transferring</c> is legal only while Waiting with the cart
    /// still.</summary>
    NotWaitingStill = 12,

    /// <summary><c>Done</c> is legal only while Unloading.</summary>
    NotUnloading = 13,

    /// <summary>The leased cart no longer resolves; the lease has ended.
    /// </summary>
    CartUnavailable = 14,

    /// <summary>A hitched haul that needs attention cannot simply wait: a
    /// person resolves it by detaching.</summary>
    CannotWaitWhileNeedingAttention = 15,

    /// <summary>Accepted; completes after the consumer's <c>Done</c>.</summary>
    Pending = 16,

    /// <summary>The cart's footprint could not be measured, so no route can be
    /// vouched for.</summary>
    FootprintUnknown = 17,

    /// <summary>The hitch that should carry the leg is not healthy.</summary>
    HitchUnhealthy = 18,
}
