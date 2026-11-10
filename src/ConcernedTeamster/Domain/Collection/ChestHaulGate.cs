using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>Why a chest-to-chest haul (#381 item 4) does not start. Asked in
/// this order, so the sentence is the one a player can act on first.</summary>
internal enum ChestHaulRefusal
{
    None = 0,

    CollectionOff = 1,

    TakeOff = 2,

    /// <summary><c>Workers/GunnarChestHaulEnabled</c> is off (the default).</summary>
    HaulOff = 3,

    WorldIsGoingAway = 4,

    WorkRefused = 5,

    SeamUnavailable = 6,

    WorkerRecordUnwritable = 7,

    WorkerRecordUnreadable = 8,

    NoWorker = 9,

    AlreadyWorking = 10,

    InterruptedTourOpen = 11,

    /// <summary>A cart is assigned to him: the hauling runtime owns his ticks
    /// while it does, and a manual haul never walks him off a cart.</summary>
    CartAssigned = 12,

    /// <summary>What he carries could not be read.</summary>
    CarriedUnreadable = 18,

    /// <summary>He already carries some of the cargo. A haul delivers only what
    /// it measured taking, and the reconcile counts his cargo against that, so
    /// it starts from none.</summary>
    AlreadyCarryingCargo = 19,
}

internal readonly struct ChestHaulRequest
{
    public ChestHaulRequest(
        bool collectionEnabled,
        bool takeEnabled,
        bool haulEnabled,
        bool worldIsUp,
        WorkAuthorityVerdict authority,
        bool seamAvailable,
        bool workerPresent,
        bool workerRecordUnwritable,
        bool workerRecordUnreadable,
        bool somethingInFlight,
        bool interruptedTourOpen,
        bool cartAssigned,
        bool carriedReadable,
        bool carryingCargo)
    {
        CollectionEnabled = collectionEnabled;
        TakeEnabled = takeEnabled;
        HaulEnabled = haulEnabled;
        WorldIsUp = worldIsUp;
        Authority = authority;
        SeamAvailable = seamAvailable;
        WorkerPresent = workerPresent;
        WorkerRecordUnwritable = workerRecordUnwritable;
        WorkerRecordUnreadable = workerRecordUnreadable;
        SomethingInFlight = somethingInFlight;
        InterruptedTourOpen = interruptedTourOpen;
        CartAssigned = cartAssigned;
        CarriedReadable = carriedReadable;
        CarryingCargo = carryingCargo;
    }

    public bool CollectionEnabled { get; }

    public bool TakeEnabled { get; }

    public bool HaulEnabled { get; }

    public bool WorldIsUp { get; }

    public WorkAuthorityVerdict Authority { get; }

    public bool SeamAvailable { get; }

    public bool WorkerPresent { get; }

    public bool WorkerRecordUnwritable { get; }

    public bool WorkerRecordUnreadable { get; }

    public bool SomethingInFlight { get; }

    public bool InterruptedTourOpen { get; }

    public bool CartAssigned { get; }

    public bool CarriedReadable { get; }

    public bool CarryingCargo { get; }
}

/// <summary>The start gate of a chest-to-chest haul: everything about Gunnar,
/// the switches and what else is going on. The two containers, the filter and
/// the loading order are <see cref="HaulRequestGate"/>'s, asked next - the one
/// answer to whether a manual haul may start. Game-free; the adapter reads the
/// facts. It decides only whether a haul may <b>start</b>: every
/// take and every unload is gated again, in the frame of the move, by the D20
/// and D15 gates and the pinned ports' own permit mint.
///
/// <b>Defaults refuse.</b> <c>default(ChestHaulRequest)</c> is refused as
/// collection off.</summary>
internal static class ChestHaulGate
{
    public static ChestHaulRefusal Evaluate(in ChestHaulRequest request)
    {
        if (!request.CollectionEnabled)
        {
            return ChestHaulRefusal.CollectionOff;
        }

        if (!request.TakeEnabled)
        {
            return ChestHaulRefusal.TakeOff;
        }

        if (!request.HaulEnabled)
        {
            return ChestHaulRefusal.HaulOff;
        }

        if (!request.WorldIsUp)
        {
            return ChestHaulRefusal.WorldIsGoingAway;
        }

        if (request.Authority != WorkAuthorityVerdict.Granted)
        {
            return ChestHaulRefusal.WorkRefused;
        }

        if (!request.SeamAvailable)
        {
            return ChestHaulRefusal.SeamUnavailable;
        }

        if (request.WorkerRecordUnwritable)
        {
            return ChestHaulRefusal.WorkerRecordUnwritable;
        }

        if (request.WorkerRecordUnreadable)
        {
            return ChestHaulRefusal.WorkerRecordUnreadable;
        }

        if (!request.WorkerPresent)
        {
            return ChestHaulRefusal.NoWorker;
        }

        if (request.SomethingInFlight)
        {
            return ChestHaulRefusal.AlreadyWorking;
        }

        if (request.InterruptedTourOpen)
        {
            return ChestHaulRefusal.InterruptedTourOpen;
        }

        if (request.CartAssigned)
        {
            return ChestHaulRefusal.CartAssigned;
        }

        if (!request.CarriedReadable)
        {
            return ChestHaulRefusal.CarriedUnreadable;
        }

        return request.CarryingCargo ? ChestHaulRefusal.AlreadyCarryingCargo : ChestHaulRefusal.None;
    }

    public static string Describe(ChestHaulRefusal refusal, WorkAuthorityVerdict authority)
    {
        switch (refusal)
        {
            case ChestHaulRefusal.None:
                return "The haul can start.";
            case ChestHaulRefusal.CollectionOff:
                return "Gunnar's collection is off. A haul needs Workers/GunnarCollectionEnabled, " +
                    "Workers/GunnarChestTakeEnabled and Workers/GunnarChestHaulEnabled.";
            case ChestHaulRefusal.TakeOff:
                return "Taking from a chest is off. A haul needs Workers/GunnarChestTakeEnabled as well as " +
                    "Workers/GunnarChestHaulEnabled.";
            case ChestHaulRefusal.HaulOff:
                return "Hauling from chest to chest is off. Turn on Workers/GunnarChestHaulEnabled to let him walk " +
                    "what you name from the chest you chose to take from to the chest you chose to put it in.";
            case ChestHaulRefusal.WorldIsGoingAway:
                return "The world is going away, so nothing is moved.";
            case ChestHaulRefusal.WorkRefused:
                return WorkAuthorityPolicy.Describe(authority);
            case ChestHaulRefusal.SeamUnavailable:
                return "This build of the game does not offer everything the haul needs, so none of it is " +
                    "called. The log says which member was missing.";
            case ChestHaulRefusal.WorkerRecordUnwritable:
                return "Gunnar is here, but the game could not write down what he is carrying on his body, so " +
                    "he is handed nothing more. No haul starts.";
            case ChestHaulRefusal.WorkerRecordUnreadable:
                return "Gunnar is here, but his body has not been able to read what it is carrying, so he is " +
                    "handed nothing. If he has just appeared, try again in a moment.";
            case ChestHaulRefusal.NoWorker:
                return "Gunnar is not here. Bring him into the world first.";
            case ChestHaulRefusal.AlreadyWorking:
                return "He is already doing something. Let it finish, or stop it (ct_collect stop).";
            case ChestHaulRefusal.InterruptedTourOpen:
                return GunnarTourPlan.OpenPlanRefusal;
            case ChestHaulRefusal.CartAssigned:
                return "A cart is assigned to him, and a haul from chest to chest is on foot: it never walks him " +
                    "away from a cart. Release it first (ct_haul release).";
            case ChestHaulRefusal.CarriedUnreadable:
                return "What Gunnar is carrying could not be read, so no haul starts.";
            case ChestHaulRefusal.AlreadyCarryingCargo:
                return "He is already carrying some of what you named. Put it down first (ct_collect deposit " +
                    "<item>): a haul delivers only what it measured taking.";
            default:
                return "The haul cannot start.";
        }
    }
}
