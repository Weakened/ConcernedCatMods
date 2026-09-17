namespace TheConcernedCat.Interop.Haul;

/// <summary><c>concernedcat.haul</c> major 1: Concerned Teamster (Gunnar) is the
/// provider, Concerned Foreman (Thorstein's collection orders) the consumer.
/// Normative text: <c>docs/settlement/cart-and-collection/CONTRACTS.md</c> §3.
///
/// The provider owns the cart lease and Gunnar's body; it never moves material
/// and never writes custody. The consumer owns the order and all material
/// custody; it never moves Gunnar or the cart. Every mutating request carries a
/// request id, the provider epoch it was based on and the revision it expects,
/// and the consumer polls for state instead of receiving callbacks.</summary>
internal static class HaulContract
{
    public const string Id = "concernedcat.haul";
    public const int Major = 1;
    public const int Minor = 0;

    /// <summary>The provider plugin's BepInEx GUID.</summary>
    public const string ProviderGuid = "com.theconcernedcat.valheim.concernedteamster";

    /// <summary>The consumer plugin's BepInEx GUID.</summary>
    public const string ConsumerGuid = "com.theconcernedcat.valheim.concernedforeman";

    /// <summary>Ops, the value of <see cref="Keys.Op"/>.</summary>
    internal static class Ops
    {
        /// <summary>Handshake: versions, provider epoch, authority, Gunnar's
        /// availability and current lease.</summary>
        public const string Hello = "hello";

        /// <summary>The current lease: its cart's in-session key and position,
        /// whether the cart is still and upright, the haul phase.</summary>
        public const string DescribeLease = "describeLease";

        /// <summary>Start or replace one leg: haul the leased cart to a point.
        /// </summary>
        public const string RequestHaul = "requestHaul";

        /// <summary>Poll one haul: phase, reason, revision, positions.</summary>
        public const string GetHaul = "getHaul";

        /// <summary>The consumer is (or has stopped) transferring material at the
        /// cart: hold it still until told otherwise.</summary>
        public const string AcknowledgeWait = "acknowledgeWait";

        /// <summary>Stop a haul, and optionally detach and park.</summary>
        public const string CancelHaul = "cancelHaul";
    }

    /// <summary>Field keys. Values are invariant-culture strings; enums are
    /// names; points are <c>x;y;z</c>; booleans are <c>true</c>/<c>false</c>.
    /// </summary>
    internal static class Keys
    {
        public const string Op = "op";
        public const string ContractMajor = "contractMajor";
        public const string ContractMinor = "contractMinor";
        public const string ConsumerVersion = "consumerVersion";
        public const string ProviderVersion = "providerVersion";
        public const string ProviderEpoch = "providerEpoch";
        public const string RequestId = "requestId";
        public const string ExpectedRevision = "expectedRevision";
        public const string Revision = "revision";
        public const string Status = "status";
        public const string Reason = "reason";
        public const string Detail = "detail";
        public const string Authority = "authority";
        public const string WorkerAvailable = "workerAvailable";
        public const string OrderId = "orderId";
        public const string HaulId = "haulId";
        public const string LeaseId = "leaseId";
        public const string CartSessionKey = "cartSessionKey";
        public const string Purpose = "purpose";
        public const string Target = "target";
        public const string ArrivalRadius = "arrivalRadius";
        public const string Phase = "phase";
        public const string Arrived = "arrived";
        public const string Attached = "attached";
        public const string CartPosition = "cartPosition";
        public const string WorkerPosition = "workerPosition";
        public const string CartStill = "cartStill";
        public const string CartUpright = "cartUpright";
        public const string Activity = "activity";
        public const string Disposition = "disposition";
    }
}

/// <summary>Every reply carries one. Same request id and same payload answers
/// <see cref="AlreadySatisfied"/>; same id with a different payload answers
/// <see cref="Rejected"/> with reason <c>DuplicateRequestDifferentPayload</c>.
/// </summary>
internal enum HaulReplyStatus
{
    Unspecified = 0,
    Accepted = 1,
    AlreadySatisfied = 2,
    Rejected = 3,

    /// <summary>The request was based on an old provider epoch or revision:
    /// re-read state and decide again.</summary>
    Stale = 4,

    /// <summary>The provider cannot serve now (no authority, no worker, no
    /// world); nothing changed.</summary>
    Unavailable = 5,

    /// <summary>The provider caught an exception; nothing is assumed to have
    /// changed and the consumer must re-read state.</summary>
    ProviderError = 6,
}

/// <summary>Gunnar's haul phases as they travel (mirrors Teamster's
/// <c>HaulPhase</c>, name for name).</summary>
internal enum HaulWirePhase
{
    Unspecified = 0,
    Unassigned = 1,
    Ready = 2,
    Approaching = 3,
    Hitching = 4,
    Pulling = 5,
    Stopping = 6,
    Waiting = 7,
    Unloading = 8,
    Detaching = 9,
    Paused = 10,
    NeedsAttention = 11,
    Recovering = 12,
}

/// <summary>What a requested leg is for.</summary>
internal enum HaulLegPurpose
{
    Unspecified = 0,

    /// <summary>Bring the cart to a safe, reachable point where Thorstein can
    /// load it.</summary>
    ToRendezvous = 1,

    /// <summary>Bring the loaded cart to the delivery container.</summary>
    ToDestination = 2,
}

/// <summary>What the consumer is doing at a waiting cart.</summary>
internal enum HaulWaitActivity
{
    Unspecified = 0,

    /// <summary>Transferring material into or out of the cart now: the cart
    /// must not move.</summary>
    Transferring = 1,

    /// <summary>Done at this stop; the cart may move again.</summary>
    Done = 2,
}

/// <summary>How a cancelled haul ends.</summary>
internal enum HaulCancelDisposition
{
    Unspecified = 0,

    /// <summary>Stop where safe and stay hitched, waiting.</summary>
    StopAndWait = 1,

    /// <summary>Stop where safe, detach on suitable ground, keep the lease.
    /// </summary>
    DetachAndPark = 2,
}

/// <summary>Why a request was refused or a haul needs attention, as it
/// travels. A superset of Teamster's refusal and attention reasons plus the
/// protocol's own; unknown names from a newer minor are shown as unknown, never
/// guessed.</summary>
internal enum HaulWireReason
{
    Unspecified = 0,

    // Protocol
    UnknownOp = 1,
    MalformedRequest = 2,
    EpochMismatch = 3,
    RevisionMismatch = 4,
    DuplicateRequestDifferentPayload = 5,
    UnknownHaul = 6,

    // Availability and authority
    NoAuthority = 10,
    OtherPeersConnected = 11,
    WorkerUnavailable = 12,
    NoLease = 13,
    LeaseInvalidated = 14,
    HaulBusy = 15,

    /// <summary>Authority was lost mid-haul (C2; mirrors Teamster's attention reason).</summary>
    AuthorityLost = 16,

    // Cart state
    CartDestroyed = 20,
    CartUnloaded = 21,
    OwnershipLost = 22,
    CartInUse = 23,
    BrakeEngaged = 24,
    OtherJointOnClient = 25,
    CartTipped = 26,
    PlayerTookOver = 27,
    JointBroke = 28,
    MassNotCurrent = 29,

    // Route and motion
    NoRoute = 40,
    TooSteep = 41,
    TooNarrow = 42,
    ForbiddenDoor = 43,
    Water = 44,
    UnsupportedGap = 45,
    OutsideLoadedArea = 46,
    Wedged = 47,
    ApproachTimedOut = 48,
    HitchFailed = 49,
    RendezvousTimedOut = 50,
    UnsafeParking = 51,
    WorkerBodyLost = 52,

    // C2
    WorkerBodyDuplicated = 53,
    PausedByPlayer = 54,
}
