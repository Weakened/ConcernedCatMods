namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling;

/// <summary>Why the player's explicit "assign this cart to Gunnar" was refused
/// (CART-01). Every value has a player sentence; zero is a bug.</summary>
internal enum CartAssignmentRefusal
{
    Unspecified = 0,
    NoAuthority = 1,

    /// <summary>Hauling is not enabled, or Gunnar is not available.</summary>
    WorkerUnavailable = 2,

    /// <summary>Not a hand cart (for example a siege engine).</summary>
    NotACart = 3,

    /// <summary>Nothing identifiable is selected, or more than one candidate
    /// could be meant. Never resolved by picking the nearest.</summary>
    AmbiguousSelection = 4,

    Destroyed = 5,
    NotLoaded = 6,

    /// <summary>This process does not own the cart's network object.</summary>
    NotOwnedHere = 7,

    /// <summary>Its container is open, or someone is pulling it.</summary>
    InUse = 8,

    /// <summary>The parking brake is engaged on it.</summary>
    Braked = 9,

    NotUpright = 10,

    /// <summary>Another lease already holds it.</summary>
    AlreadyLeased = 11,

    /// <summary>Gunnar already holds another cart.</summary>
    WorkerBusy = 12,

    /// <summary>The selection refers to a previous world load.</summary>
    StaleIdentity = 13,

    /// <summary>The player is too far from the cart to be selecting it.</summary>
    TooFarToSelect = 14,
}

/// <summary>Why a hitch attempt did not attach (DECISIONS.md D4 preconditions).
/// </summary>
internal enum HitchRefusal
{
    Unspecified = 0,
    NoAuthority = 1,
    LeaseNotActive = 2,
    CartGone = 3,
    NotOwnedHere = 4,

    /// <summary>The cart's mass has not caught up with its load yet.</summary>
    MassNotCurrent = 5,

    InUse = 6,
    Braked = 7,

    /// <summary>Some cart on this client already has a joint; attaching would
    /// detach it.</summary>
    OtherJointOnClient = 8,

    NotUpright = 9,

    /// <summary>Gunnar is not close enough to the handle.</summary>
    OutOfReach = 10,

    /// <summary>Gunnar's body lacks what the joint needs.</summary>
    PullerBodyInvalid = 11,

    /// <summary>Attached, but the joint could not be verified; detached again.
    /// </summary>
    VerifyFailed = 12,

    /// <summary>The attach seam is missing or changed on this game build.
    /// </summary>
    SeamUnavailable = 13,
}

/// <summary>The single actionable reason a haul stopped (CART-05, CART-06).
/// </summary>
internal enum HaulAttentionReason
{
    Unspecified = 0,

    NoRoute = 1,
    TooSteep = 2,
    TooNarrow = 3,
    ForbiddenDoor = 4,
    Water = 5,
    UnsupportedGap = 6,
    OutsideLoadedArea = 7,

    /// <summary>Gunnar is pushing but the cart is not moving.</summary>
    Wedged = 8,

    JointBroke = 9,
    CartTipped = 10,
    PlayerTookOver = 11,
    BrakeEngaged = 12,
    OwnershipLost = 13,
    CartDestroyed = 14,
    CartUnloaded = 15,
    AuthorityLost = 16,
    OtherPeersConnected = 17,
    HitchFailed = 18,
    ApproachTimedOut = 19,
    RendezvousTimedOut = 20,

    /// <summary>No suitable ground to leave the cart on.</summary>
    UnsafeParking = 21,

    WorkerBodyLost = 22,
    LeaseInvalidated = 23,
}
