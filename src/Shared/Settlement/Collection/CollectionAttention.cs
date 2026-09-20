namespace TheConcernedCat.Settlement.Collection;

/// <summary>The one actionable reason a collection order is paused or needs
/// attention (COOP-04). Every value has a player sentence and, where one
/// exists, a resolution control. Zero is a bug.</summary>
internal enum CollectionAttentionReason
{
    Unspecified = 0,

    // Authority and readiness
    NoAuthority = 1,
    OtherPeersConnected = 2,
    WorkerNotRecruited = 3,
    ToolMissing = 4,
    ToolBroken = 5,
    ToolHandoverUncertain = 6,
    JournalReadOnly = 7,

    /// <summary>Nothing could establish whose identity Thorstein is, so no job
    /// may hold him and nothing may move him.
    ///
    /// <b>Not <see cref="WorkerBodyLost"/>.</b> His body may be standing right in
    /// front of the player. What is missing is the shared NPC runtime's record of
    /// who he is, which is decided once at startup and never changes during a
    /// session. Reported as itself because the two have different causes and
    /// completely different fixes.</summary>
    WorkerIdentityUnknown = 8,

    // Scope and sources
    ScopeInvalid = 10,
    ScopeChanged = 11,
    ScopeUnloaded = 12,
    NoEligibleSources = 13,
    SourcesExhausted = 14,
    SourceUnreachable = 15,
    SurveyIncomplete = 16,

    // Carrying and delivery
    CarryFull = 20,
    NoReturnSpace = 21,
    DestinationFull = 22,
    DestinationUnavailable = 23,
    DestinationAccessDenied = 24,
    DestinationStale = 25,

    // Custody
    TransferUncertain = 30,
    ReconciliationMismatch = 31,
    PlayerRemovedMaterial = 32,
    WorkerBodyLost = 33,
    WorkerBodyDuplicated = 34,

    // Cooperation
    HaulerUnavailable = 40,
    HaulerNeedsAttention = 41,
    RendezvousTimedOut = 42,
    CartLeaseLost = 43,

    // Player (C2)

    /// <summary>The player paused the order; nothing is wrong.</summary>
    PausedByPlayer = 50,
}
