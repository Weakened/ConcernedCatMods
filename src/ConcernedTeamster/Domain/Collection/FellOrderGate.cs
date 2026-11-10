using System.Collections.Generic;
using TheConcernedCat.Workers;
using TheConcernedCat.Workers.Felling;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>Why <c>ct_collect fell</c> was refused before anything was looked
/// at. Zero means it was not (#381 item 5, Gunnar's half of `DECISIONS.md` D19).
///
/// Everything here is about the switches, Gunnar and the order. Everything about
/// the <i>tree</i> - which one, the area, ownership, the ward, the clearances,
/// his reach and his axe - is <see cref="SmallTreeFelling"/>'s, re-measured by the
/// felling port in the frame of the hit.</summary>
internal enum FellRefusal
{
    /// <summary>Nothing refused.</summary>
    None = 0,

    /// <summary>Teamster's master switch or the collection switch is off.</summary>
    CollectionOff = 1,

    /// <summary><c>Workers/GunnarFellsSmallTrees</c> is off (the default). A
    /// switch of its own: a player who let him pick stones up has not thereby let
    /// him cut anything down.</summary>
    FellingOff = 2,

    WorldIsGoingAway = 3,

    /// <summary>The work-authority rule refused (`DECISIONS.md` D3).</summary>
    WorkRefused = 4,

    SeamUnavailable = 5,

    /// <summary>His last inventory change could not be written to his own
    /// network object, so he is handed nothing more - the Wood a fell would give
    /// him included (D15's containment).</summary>
    WorkerRecordUnwritable = 6,

    WorkerRecordUnreadable = 7,

    NoWorker = 8,

    /// <summary>A pick, a deposit, a take, a tour or a haul is in flight.</summary>
    AlreadyWorking = 9,

    /// <summary>An interrupted collection tour is open (#379).</summary>
    InterruptedTourOpen = 10,

    /// <summary>No collection area was set in this world load. D19 fells only
    /// inside an explicit work area, and Gunnar's only one is the area the player
    /// set with <c>ct_collect area</c>: there is no default circle to fall back
    /// to.</summary>
    NoArea = 11,
}

internal readonly struct FellRequest
{
    public FellRequest(
        bool collectionEnabled,
        bool fellingEnabled,
        bool worldIsUp,
        WorkAuthorityVerdict authority,
        bool seamAvailable,
        bool workerPresent,
        bool workerRecordUnwritable,
        bool workerRecordUnreadable,
        bool transferInFlight,
        bool interruptedTourOpen,
        bool areaChosen)
    {
        CollectionEnabled = collectionEnabled;
        FellingEnabled = fellingEnabled;
        WorldIsUp = worldIsUp;
        Authority = authority;
        SeamAvailable = seamAvailable;
        WorkerPresent = workerPresent;
        WorkerRecordUnwritable = workerRecordUnwritable;
        WorkerRecordUnreadable = workerRecordUnreadable;
        TransferInFlight = transferInFlight;
        InterruptedTourOpen = interruptedTourOpen;
        AreaChosen = areaChosen;
    }

    public bool CollectionEnabled { get; }

    public bool FellingEnabled { get; }

    public bool WorldIsUp { get; }

    public WorkAuthorityVerdict Authority { get; }

    public bool SeamAvailable { get; }

    public bool WorkerPresent { get; }

    public bool WorkerRecordUnwritable { get; }

    public bool WorkerRecordUnreadable { get; }

    public bool TransferInFlight { get; }

    public bool InterruptedTourOpen { get; }

    public bool AreaChosen { get; }
}

/// <summary>The order gate of one felling hit (#381 item 5). Game-free; the
/// runtime reads the facts. It decides only whether the felling port may be
/// asked: the port then re-measures every D19 site rule through
/// <see cref="SmallTreeFelling.CheckSite"/> in the frame of the hit.
///
/// <b>Defaults refuse.</b> <c>default(FellRequest)</c> is refused as collection
/// off.</summary>
internal static class FellOrderGate
{
    public static FellRefusal Evaluate(in FellRequest request)
    {
        if (!request.CollectionEnabled)
        {
            return FellRefusal.CollectionOff;
        }

        if (!request.FellingEnabled)
        {
            return FellRefusal.FellingOff;
        }

        if (!request.WorldIsUp)
        {
            return FellRefusal.WorldIsGoingAway;
        }

        if (request.Authority != WorkAuthorityVerdict.Granted)
        {
            return FellRefusal.WorkRefused;
        }

        if (!request.SeamAvailable)
        {
            return FellRefusal.SeamUnavailable;
        }

        if (request.WorkerRecordUnwritable)
        {
            return FellRefusal.WorkerRecordUnwritable;
        }

        if (request.WorkerRecordUnreadable)
        {
            return FellRefusal.WorkerRecordUnreadable;
        }

        if (!request.WorkerPresent)
        {
            return FellRefusal.NoWorker;
        }

        if (request.TransferInFlight)
        {
            return FellRefusal.AlreadyWorking;
        }

        if (request.InterruptedTourOpen)
        {
            return FellRefusal.InterruptedTourOpen;
        }

        return request.AreaChosen ? FellRefusal.None : FellRefusal.NoArea;
    }

    public static string Describe(FellRefusal refusal, WorkAuthorityVerdict authority)
    {
        switch (refusal)
        {
            case FellRefusal.None:
                return "He may fell.";
            case FellRefusal.CollectionOff:
                return "Gunnar's collection is off. Felling a small tree needs Workers/GunnarCollectionEnabled and " +
                    "Workers/GunnarFellsSmallTrees.";
            case FellRefusal.FellingOff:
                return "Felling is off. Turn on Workers/GunnarFellsSmallTrees to let him fell a small beech or a small " +
                    "fir inside the area you set, with an axe he carries.";
            case FellRefusal.WorldIsGoingAway:
                return "The world is going away, so nothing is felled.";
            case FellRefusal.WorkRefused:
                return WorkAuthorityPolicy.Describe(authority);
            case FellRefusal.SeamUnavailable:
                return "This build of the game does not offer everything Gunnar's work needs, so none of it is " +
                    "called. The log says which member was missing.";
            case FellRefusal.WorkerRecordUnwritable:
                return "Gunnar is here, but the game could not write down what he is carrying on his body, so he is " +
                    "handed nothing more - not even the wood a tree would give him. Nothing is felled.";
            case FellRefusal.WorkerRecordUnreadable:
                return "Gunnar is here, but his body has not been able to read what it is carrying, so he is inert. " +
                    "If he has just appeared, try again in a moment.";
            case FellRefusal.NoWorker:
                return "Gunnar is not here. Bring him into the world first.";
            case FellRefusal.AlreadyWorking:
                return "He is already doing something. Let it finish, or stop it (ct_collect stop).";
            case FellRefusal.InterruptedTourOpen:
                return GunnarTourPlan.OpenPlanRefusal;
            case FellRefusal.NoArea:
                return "He fells only inside the area you set, and none is set in this world load. Stand where he " +
                    "should work and run ct_collect area [radius] first.";
            default:
                return "He may not fell now.";
        }
    }

    /// <summary>The site rule's refusal, in Gunnar's words. The shared rule's
    /// own sentences name Thorstein and Foreman's switch.</summary>
    public static string DescribeSite(FellSiteRefusal refusal)
    {
        switch (refusal)
        {
            case FellSiteRefusal.FeatureOff:
                return "felling is turned off (Workers/GunnarFellsSmallTrees)";
            case FellSiteRefusal.NoAuthority:
                return "work is not authorised here";
            case FellSiteRefusal.NoExplicitArea:
                return "he fells only inside the area you set with ct_collect area";
            case FellSiteRefusal.OutsideArea:
                return "that tree is outside the area you set";
            case FellSiteRefusal.NotOwnedHere:
                return "that tree is not owned by this game, so he leaves it alone";
            case FellSiteRefusal.WardDenied:
                return "a ward protects that tree";
            case FellSiteRefusal.PlayerTooClose:
                return "someone is within 15 m of that tree - stand further back";
            case FellSiteRefusal.BuiltPieceTooClose:
                return "something you built is within 15 m of that tree";
            case FellSiteRefusal.WorkerOutOfReach:
                return "Gunnar is not standing at that tree";
            case FellSiteRefusal.NoUsableAxe:
                return "Gunnar carries no usable axe of his own (have him take one from a chest you marked for " +
                    "taking: ct_collect source, then ct_collect take <axe>)";
            case FellSiteRefusal.AxeTierTooLow:
                return "his axe is not good enough for that tree";
            default:
                return "not allowed";
        }
    }
}

/// <summary>One item the felling hit spawned beside the tree, as the felling
/// port read it in the same call.</summary>
internal readonly struct FelledDrop
{
    public FelledDrop(bool isWood, bool ownedHere, int stack)
    {
        IsWood = isWood;
        OwnedHere = ownedHere;
        Stack = stack;
    }

    /// <summary>Its prefab is the game's own Wood item.</summary>
    public bool IsWood { get; }

    /// <summary>This process owns its network object, so taking it is this
    /// process's to do.</summary>
    public bool OwnedHere { get; }

    public int Stack { get; }
}

/// <summary>How one attempt to take a felled drop came out, as measured.
/// </summary>
internal readonly struct FelledTakeStep
{
    public FelledTakeStep(int credited, bool uncertain)
    {
        Credited = credited;
        Uncertain = uncertain;
    }

    /// <summary>Units of Wood that measurably arrived in him.</summary>
    public int Credited { get; }

    /// <summary>The measurement does not say what happened. Nothing more is
    /// taken after one of these.</summary>
    public bool Uncertain { get; }
}

/// <summary>What the take after one fell did, all of it measured.</summary>
internal readonly struct FelledTake
{
    public FelledTake(int taken, int leftOnGround, bool uncertain)
    {
        Taken = taken;
        LeftOnGround = leftOnGround;
        Uncertain = uncertain;
    }

    /// <summary>Units of Wood that measurably arrived in him.</summary>
    public int Taken { get; }

    /// <summary>Drops the hit spawned that he did not take.</summary>
    public int LeftOnGround { get; }

    /// <summary>A take did not add up, and nothing after it was tried.</summary>
    public bool Uncertain { get; }
}

/// <summary>D19's second grant for Gunnar: only the Wood his own hit dropped, by
/// instance, in the same call - and of that, only what he can carry. Game-free.
///
/// <b>Wood only</b> (lead default 3 of Thorstein's half, adopted unchanged):
/// resin and fir cones are not what collection gathers, and lie on the ground.
/// <b>Whole drops only</b>: a drop that does not fit in what is left of the carry
/// limit is left where it fell, never split. <b>Only what arrived</b>: the
/// credit is the measured change in his own count, never the stack's size.
/// </summary>
internal static class FelledWoodTake
{
    /// <summary>The game's own Wood item prefab.</summary>
    public const string WoodPrefab = "Wood";

    /// <summary>The drops to try to take, in the order the hit spawned them,
    /// into <paramref name="chosen"/> as indexes; returns how many are left on
    /// the ground.</summary>
    public static int Choose(IReadOnlyList<FelledDrop> drops, int room, List<int> chosen)
    {
        chosen.Clear();
        if (drops == null)
        {
            return 0;
        }

        int left = room < 0 ? 0 : room;
        for (int index = 0; index < drops.Count; index++)
        {
            FelledDrop drop = drops[index];
            if (!drop.IsWood || !drop.OwnedHere || drop.Stack < 1 || drop.Stack > left)
            {
                continue;
            }

            chosen.Add(index);
            left -= drop.Stack;
        }

        return drops.Count - chosen.Count;
    }

    /// <summary>Reads one take from what was counted around it. A take the game
    /// refused with nothing moved is not uncertain: the drop is still on the
    /// ground. Anything that does not add up is, and is credited only with what
    /// measurably arrived, never more than the drop held.</summary>
    public static FelledTakeStep Credit(bool threw, bool added, int before, int after, int stack, bool dropGone)
    {
        int arrived = after - before;
        if (!threw && !added && arrived == 0 && !dropGone)
        {
            return new FelledTakeStep(0, uncertain: false);
        }

        if (!threw && added && arrived == stack && dropGone)
        {
            return new FelledTakeStep(stack, uncertain: false);
        }

        int credited = arrived < 0 ? 0 : arrived > stack ? stack : arrived;
        return new FelledTakeStep(credited, uncertain: true);
    }
}
