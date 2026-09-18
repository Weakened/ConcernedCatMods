using System.Collections.Generic;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.ConcernedSteward.Domain.Upkeep;

/// <summary>An inventory the Steward may read and move items between.
///
/// <b>Nothing here returns a success flag the caller is expected to believe.</b>
/// <see cref="MoveTo"/> returns <c>void</c> on purpose: the audit's first
/// finding is that vanilla's own <c>UseItem</c> answers <c>true</c> for a
/// refusal, and the lesson generalises. The upkeep loop counts both sides
/// before and after and decides from the deltas, which is the same rule the
/// settlement custody executor arrived at
/// (<c>src/Shared/Settlement/Custody/TransferExecutor.cs</c>, CONTRACTS.md
/// section 5.2) — a known shape rather than a new one.
///
/// Implementations answer counts; they never throw for a game reason.</summary>
internal interface IItemStorePort
{
    /// <summary>For evidence and for the player: "the depot", "the Steward".
    /// A phrase that reads inside a sentence, not a noun.</summary>
    string Describe { get; }

    /// <summary>Whether this store exists, is loaded, is owned by this process
    /// and may be written now. False for anything unestablished.</summary>
    bool IsAvailable { get; }

    /// <summary>The distinct item names this store currently holds.
    ///
    /// Read for one question only: does the depot stock what a given fire
    /// burns? A fire that wants something the marked chest does not have is not
    /// a fault and not a target, and answering that before a trip is planned is
    /// what stops the Steward walking to a chest for nothing.
    ///
    /// Empty when the store could not be read — which, through
    /// <see cref="FuelTargetSelector.Classify"/>, makes every fire
    /// <see cref="FuelTargetStatus.WrongFuel"/> and therefore ineligible. That
    /// is the fail-closed direction.</summary>
    IReadOnlyCollection<string> ItemNames { get; }

    /// <summary>How many units of <paramref name="fuelItemName"/> are here.
    ///
    /// <b>Counted the way vanilla matches.</b> <c>Fireplace.UseItem</c> selects
    /// on <c>m_shared.m_name</c> and nothing else, while
    /// <c>Inventory.CountItems</c> additionally filters on <c>m_worldLevel</c>.
    /// Measuring with one predicate and mutating with the other is how a
    /// conservation argument develops a hole, so implementations must match
    /// vanilla's: name only. Negative means the count could not be
    /// established.</summary>
    int Count(string fuelItemName);

    /// <summary>Whether this store could take <paramref name="count"/> more.
    /// Negative means unanswerable.</summary>
    int RoomFor(string fuelItemName, int count);

    /// <summary>Moves up to <paramref name="count"/> units into
    /// <paramref name="destination"/>, through the game's own move so the moved
    /// item instances and their metadata survive.
    ///
    /// Returns nothing. The caller measures.</summary>
    void MoveTo(IItemStorePort destination, string fuelItemName, int count);
}

/// <summary>How one attempt to put a unit into a fire ended, decided from the
/// measured deltas of the worker's hands and the fire's own fuel level.
/// <see cref="Unavailable"/> is zero.</summary>
internal enum FeedOutcome
{
    /// <summary>Not established. Treated as uncertain.</summary>
    Unavailable = 0,

    /// <summary>One unit left the worker and the fire gained fuel. The wood is
    /// burned: gone from the world, legitimately, by the game's own hand.
    /// </summary>
    Accepted = 1,

    /// <summary>Nothing left the worker and the fire gained nothing. The fire
    /// declined — full, or no longer taking fuel. <b>No wood was spent</b>, so
    /// this is an ordinary answer and not a failure.</summary>
    Declined = 2,

    /// <summary>One unit left the worker and the fire gained nothing.
    ///
    /// This is the unowned-network-object case the audit names, caught after
    /// the fact: vanilla removed the item and then its own owner gate threw the
    /// fuel away. The unit is recorded as unaccounted and <b>never</b>
    /// recreated — the loop refuses to feed an unowned fire precisely so this
    /// stays unreachable, and if it is reached anyway, somebody needs to know
    /// rather than have it quietly balanced out.</summary>
    Lost = 3,

    /// <summary>The deltas do not add up: more than one unit moved, the fire
    /// gained without the worker losing, or a count could not be read. Recorded
    /// with the evidence; nothing is replayed and nothing is compensated.
    /// </summary>
    Uncertain = 4,
}

/// <summary>What one fire looked like across one feed attempt.</summary>
internal readonly struct FeedMeasurement
{
    public FeedMeasurement(
        FeedOutcome outcome,
        int carriedBefore,
        int carriedAfter,
        float fuelBefore,
        float fuelAfter,
        string evidence)
    {
        Outcome = outcome;
        CarriedBefore = carriedBefore;
        CarriedAfter = carriedAfter;
        FuelBefore = fuelBefore;
        FuelAfter = fuelAfter;
        Evidence = evidence ?? string.Empty;
    }

    public FeedOutcome Outcome { get; }

    public int CarriedBefore { get; }

    public int CarriedAfter { get; }

    public float FuelBefore { get; }

    public float FuelAfter { get; }

    /// <summary>What was observed, in one sentence, for a player and a
    /// reviewer.</summary>
    public string Evidence { get; }

    /// <summary>Units that left the worker's hands. Exactly one on
    /// <see cref="FeedOutcome.Accepted"/> and on <see cref="FeedOutcome.Lost"/>.
    /// </summary>
    public int Spent => CarriedBefore - CarriedAfter;

    public float Gained => FuelAfter - FuelBefore;
}

/// <summary>The fires, behind a seam.
///
/// <b>Named for fuel rather than for fireplaces</b> because the audit found
/// <c>Smelter</c> and <c>CookingStation</c> carry the identical shape —
/// <c>m_fuelItem</c>, <c>m_maxFuel</c>, <c>ZDOVars.s_fuel</c>, an owner-gated
/// <c>RPC_AddFuel</c>. The second and third implementations are a known shape,
/// not a guess. <b>#340 implements fireplaces only</b>; nothing here is built
/// out for the others.</summary>
internal interface IFuelTargetPort
{
    /// <summary>Every fuel target inside <paramref name="settlementCentre"/> and
    /// <paramref name="settlementRadius"/>, as the adapter can see them now.
    ///
    /// The area is passed in rather than read from anywhere, so this seam
    /// cannot accidentally become a world sweep. An implementation that cannot
    /// see all of the area — a zone not loaded, no world up — returns what it
    /// can and lets the domain refuse the rest on its own rules.</summary>
    IReadOnlyList<FuelTargetObservation> Survey(SitePoint settlementCentre, float settlementRadius);

    /// <summary>Re-reads one fire. Returns false when it no longer exists, is
    /// not loaded, or cannot be identified as the one named.
    ///
    /// Called immediately before feeding: everything the scan established could
    /// have changed during the walk, and the walk is the longest part of the
    /// job.</summary>
    bool TryObserve(FuelTargetKey key, out FuelTargetObservation observation);

    /// <summary>Puts <b>one</b> unit of the fire's own fuel item into it,
    /// through vanilla's own <c>Fireplace.UseItem</c>, and reports what the
    /// measurements showed.
    ///
    /// One unit, because that is the granularity vanilla's own path works at,
    /// and because a measurement per unit is what makes partial acceptance
    /// exact rather than estimated.
    ///
    /// The implementation must read the fuel level and the worker's count
    /// immediately before and immediately after the vanilla call, inside one
    /// synchronous step — vanilla's routed RPC dispatches inline when this
    /// process owns the object, and its two-second fuel decay cannot run in the
    /// middle of a call on a single-threaded engine. So the two reads bracket
    /// exactly one mutation and nothing else.</summary>
    FeedMeasurement FeedOneUnit(FuelTargetKey key, IItemStorePort carrier);
}

/// <summary>Where the Steward's body is and where it is going.
///
/// The movement itself is the shared <c>WorkerMovementPlanner</c> driving
/// vanilla's own motor; this seam only carries the two things the upkeep loop
/// needs to know. Nothing here can set a position: the loop asks for a
/// destination and reads a status, and there is no member that would let it
/// write a transform, a velocity or a force.</summary>
internal interface IStewardMotionPort
{
    /// <summary>True when there is a body to move, it is loaded, and this
    /// process owns it.</summary>
    bool IsPresent { get; }

    SitePoint Position { get; }

    /// <summary>Walk there. Repeating the same destination is a no-op, so a
    /// loop that re-states its goal every tick cannot reset the path
    /// budget.</summary>
    void WalkTo(SitePoint destination, float arrivalTolerance);

    /// <summary>Stop. Idempotent.</summary>
    void Stop();

    /// <summary>Where the walk stands, decided by distance and never by the
    /// engine's own "did I stop" answer (<c>BaseAI.MoveTo</c> returns true for
    /// stopped, including when the path failed).</summary>
    WorkerWalkStatus WalkStatus { get; }

    /// <summary>Why the walk was given up on, when it was.</summary>
    WorkerDeferralReason DeferredReason { get; }
}

/// <summary>Where a walk stands. Mirrors the Foreman runtime's own enum rather
/// than sharing it: it lives in that product, and products do not reference
/// each other.</summary>
internal enum WorkerWalkStatus
{
    /// <summary>No destination. Zero, so an unanswered status is never
    /// "arrived".</summary>
    Idle = 0,

    Walking = 1,

    Arrived = 2,

    /// <summary>Given up on, for the reason the motion port names.</summary>
    Deferred = 3,
}
