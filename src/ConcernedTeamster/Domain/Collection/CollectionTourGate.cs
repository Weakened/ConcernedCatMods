using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>Why a collection tour was refused before it began. Zero means it
/// was not.</summary>
internal enum TourRefusal
{
    None = 0,
    FeatureOff = 1,
    WorldIsGoingAway = 2,
    WorkRefused = 3,
    SeamUnavailable = 4,
    WorkerRecordUnwritable = 5,
    WorkerRecordUnreadable = 6,
    NoWorker = 7,

    /// <summary>A pick, a deposit or another tour is already moving material.
    /// One body does one thing, and one tour runs at a time.</summary>
    AlreadyWorking = 8,

    /// <summary>A cart is assigned to him or hitched to him. A tour is on foot;
    /// walking him away from a hitched cart is the one thing the hauling
    /// decisions forbid, and moving material into a cart is not authorized.
    /// </summary>
    CartAssigned = 9,

    /// <summary>No area was set, or it was set in an earlier world load.</summary>
    NoArea = 10,

    /// <summary>No chest was chosen, or it was chosen in an earlier world load.
    /// <b>There is no nearest-chest fallback.</b></summary>
    NoDestination = 11,

    /// <summary>The chosen chest is not marked Deposit or Both. Asked again at
    /// the moment of every move; asked here too so the player finds out now
    /// rather than after a load.</summary>
    DestinationNotMarked = 12,

    /// <summary>What he can carry could not be read, or is nothing. A limit
    /// nobody could read is zero, never unbounded.</summary>
    NoCapacity = 13,
}

/// <summary>What the runtime read at the moment a tour was asked for. A
/// defaulted instance is refused on its first clause.</summary>
internal readonly struct TourRequest
{
    public TourRequest(
        bool featureEnabled,
        bool worldIsUp,
        WorkAuthorityVerdict authority,
        bool seamAvailable,
        bool workerRecordUnwritable,
        bool workerRecordUnreadable,
        bool workerPresent,
        bool somethingInFlight,
        bool cartAssigned,
        bool areaSetThisWorld,
        bool destinationSetThisWorld,
        bool destinationAllowsDeposit,
        int capacityUnits)
    {
        FeatureEnabled = featureEnabled;
        WorldIsUp = worldIsUp;
        Authority = authority;
        SeamAvailable = seamAvailable;
        WorkerRecordUnwritable = workerRecordUnwritable;
        WorkerRecordUnreadable = workerRecordUnreadable;
        WorkerPresent = workerPresent;
        SomethingInFlight = somethingInFlight;
        CartAssigned = cartAssigned;
        AreaSetThisWorld = areaSetThisWorld;
        DestinationSetThisWorld = destinationSetThisWorld;
        DestinationAllowsDeposit = destinationAllowsDeposit;
        CapacityUnits = capacityUnits;
    }

    public bool FeatureEnabled { get; }

    public bool WorldIsUp { get; }

    public WorkAuthorityVerdict Authority { get; }

    public bool SeamAvailable { get; }

    public bool WorkerRecordUnwritable { get; }

    public bool WorkerRecordUnreadable { get; }

    public bool WorkerPresent { get; }

    public bool SomethingInFlight { get; }

    public bool CartAssigned { get; }

    public bool AreaSetThisWorld { get; }

    public bool DestinationSetThisWorld { get; }

    public bool DestinationAllowsDeposit { get; }

    public int CapacityUnits { get; }
}

/// <summary>Whether a collection tour may begin (#381). Game-free, over values,
/// in the order a player would fix things: their own setting, the room, this
/// build, Gunnar, then the job's own inputs.
///
/// <b>What it is not.</b> The per-source and per-chest checks - the allowlist,
/// ownership, wards, locations, the chest's mark, ownership, in-use and reach -
/// are asked again at every pick and every move, by the classification and the
/// two ports. This decides whether starting makes sense at all.</summary>
internal static class CollectionTourGate
{
    public static TourRefusal Evaluate(in TourRequest request)
    {
        if (!request.FeatureEnabled)
        {
            return TourRefusal.FeatureOff;
        }

        if (!request.WorldIsUp)
        {
            return TourRefusal.WorldIsGoingAway;
        }

        if (request.Authority != WorkAuthorityVerdict.Granted)
        {
            return TourRefusal.WorkRefused;
        }

        if (!request.SeamAvailable)
        {
            return TourRefusal.SeamUnavailable;
        }

        if (request.WorkerRecordUnwritable)
        {
            return TourRefusal.WorkerRecordUnwritable;
        }

        if (request.WorkerRecordUnreadable)
        {
            return TourRefusal.WorkerRecordUnreadable;
        }

        if (!request.WorkerPresent)
        {
            return TourRefusal.NoWorker;
        }

        if (request.SomethingInFlight)
        {
            return TourRefusal.AlreadyWorking;
        }

        if (request.CartAssigned)
        {
            return TourRefusal.CartAssigned;
        }

        if (!request.AreaSetThisWorld)
        {
            return TourRefusal.NoArea;
        }

        if (!request.DestinationSetThisWorld)
        {
            return TourRefusal.NoDestination;
        }

        if (!request.DestinationAllowsDeposit)
        {
            return TourRefusal.DestinationNotMarked;
        }

        return request.CapacityUnits > 0 ? TourRefusal.None : TourRefusal.NoCapacity;
    }

    /// <summary>The sentence for every refusal, including the ones "nothing can
    /// produce".</summary>
    public static string Describe(TourRefusal refusal, WorkAuthorityVerdict authority)
    {
        switch (refusal)
        {
            case TourRefusal.None:
                return "He can start.";
            case TourRefusal.FeatureOff:
                return "Gunnar's collection is off. Turn on Workers/GunnarCollectionEnabled to let him " +
                    "collect loose stones and branches into a chest you have marked.";
            case TourRefusal.WorldIsGoingAway:
                return "The world is going away, so no tour starts. What he is carrying is saved with his body.";
            case TourRefusal.WorkRefused:
                return WorkAuthorityPolicy.Describe(authority);
            case TourRefusal.SeamUnavailable:
                return "This build of the game does not offer everything collecting needs, so none of it " +
                    "is called. The log says which member was missing.";
            case TourRefusal.WorkerRecordUnwritable:
                return "Gunnar is here and still holding what he picked up, but the game could not write it " +
                    "down on his body. He is handed nothing more until a change does get written; a reload " +
                    "brings him back with what was last saved. Whatever the failed write was carrying is gone.";
            case TourRefusal.WorkerRecordUnreadable:
                return "Gunnar is here, but his body has not been able to read what it is carrying, so he " +
                    "starts nothing. If he has just appeared, try again in a moment; otherwise the log says " +
                    "what could not be read.";
            case TourRefusal.NoWorker:
                return "Gunnar is not here. Bring him into the world first.";
            case TourRefusal.AlreadyWorking:
                return "He is already picking something up, putting something away, or on a tour. Let that " +
                    "finish, or cancel it.";
            case TourRefusal.CartAssigned:
                return "A cart is assigned to him. A collection tour is on foot - release the cart first " +
                    "(ct_haul release), and he will not walk away from one that is hitched.";
            case TourRefusal.NoArea:
                return "No area was set. Stand in the middle of where he should collect and run " +
                    "'ct_collect area [radius]'. He never looks further than the area you give him.";
            case TourRefusal.NoDestination:
                return "No chest was chosen. Look at the one he should use and run 'ct_collect destination' " +
                    "- he never looks for the nearest one.";
            case TourRefusal.DestinationNotMarked:
                return "The chest you chose is not marked for him to put things in. Look at it and press the " +
                    "container key until it says he may.";
            case TourRefusal.NoCapacity:
                return "What he can carry could not be read, or he can carry nothing more, so he starts " +
                    "nothing rather than carrying an amount nobody established.";
            default:
                return "The tour was refused for a reason nobody recorded, so nothing started.";
        }
    }
}
