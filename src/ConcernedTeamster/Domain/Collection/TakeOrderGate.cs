using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>Why a take from a chest was refused before anything was attempted.
/// Zero means it was not (#381, owner decision 2026-10-31, `DECISIONS.md` D20).
///
/// <b>The same two vocabularies as the deposit, for the same reason.</b>
/// Everything here is about Gunnar, the room and the order - facts this product
/// reads. Everything about the <i>container</i> is
/// <see cref="NpcContainerRefusal"/>'s, decided by the shared library's permit
/// mint at the moment of the move and reported through
/// <see cref="TakeSentences"/>.</summary>
internal enum TakeRefusal
{
    /// <summary>Nothing refused.</summary>
    None = 0,

    /// <summary>Teamster's master switch or the collection switch is off. Asked
    /// first, so somebody who never turned collection on is told that.</summary>
    CollectionOff = 1,

    /// <summary>The take's own switch (<c>Workers/GunnarChestTakeEnabled</c>) is
    /// off. A separate switch because a separate grant: a player who let him
    /// pick stones up and put them in a chest has not thereby let him take
    /// anything out of one.</summary>
    TakeOff = 2,

    /// <summary>No world is loaded as far as the runtime's own lifecycle is
    /// concerned.</summary>
    WorldIsGoingAway = 3,

    /// <summary>The work-authority rule refused (`DECISIONS.md` D3). Re-asked in
    /// the frame of the move, never cached.</summary>
    WorkRefused = 4,

    /// <summary>The start-up probe could not verify every member the runtime
    /// binds.</summary>
    SeamUnavailable = 5,

    /// <summary>His last inventory change could not be written to his own
    /// network object. Handing him more would be handing him an amount nobody
    /// can reconcile, which is the containment D15 already applies. Asked before
    /// "no worker" for the reason the deposit gate gives.</summary>
    WorkerRecordUnwritable = 6,

    /// <summary>His body has not been able to read what it carries, so it is
    /// inert and is handed nothing.</summary>
    WorkerRecordUnreadable = 7,

    /// <summary>No worker body, or it is not alive.</summary>
    NoWorker = 8,

    /// <summary>A pick, a deposit, a tour or another take is in flight.</summary>
    AlreadyWorking = 9,

    /// <summary>An interrupted collection tour is open (#379). A take would
    /// change his load under a plan that did not announce it.</summary>
    InterruptedTourOpen = 10,

    /// <summary>No source chest was chosen. <b>There is no nearest-chest
    /// fallback</b>: with nothing designated there is nothing to fall back to.
    /// </summary>
    NoSource = 11,

    /// <summary>The order named no item. A filter nobody filled is not
    /// permission to take everything.</summary>
    NoCargoNamed = 12,

    /// <summary>The game has no item of that name, so there is nothing it could
    /// mean. Refused rather than guessed at.</summary>
    UnknownCargo = 13,

    /// <summary>The order asked for fewer than one unit.</summary>
    NothingWanted = 14,

    /// <summary>What he already carries, or what one unit weighs, could not be
    /// read, so how much more fits is unknown. Unknown refuses.</summary>
    CarryUnreadable = 15,

    /// <summary>He has no room left under the carry limit for even one unit of
    /// what was asked for.</summary>
    NoCarryRoom = 16,
}

/// <summary>What the runtime read at the moment a take was ordered. Every field
/// is something the game was asked this frame.
///
/// <b>A defaulted instance is refused.</b> <c>default</c> fails the first
/// clause, so a caller that forgets a field gets a refusal, never an admission.
/// </summary>
internal readonly struct TakeRequest
{
    public TakeRequest(
        bool collectionEnabled,
        bool takeEnabled,
        bool worldIsUp,
        WorkAuthorityVerdict authority,
        bool seamAvailable,
        bool workerPresent,
        bool workerRecordUnwritable,
        bool workerRecordUnreadable,
        bool transferInFlight,
        bool interruptedTourOpen,
        bool sourceDesignated,
        string? cargo,
        bool cargoKnown,
        int unitsWanted,
        int? unitsThatFit)
    {
        CollectionEnabled = collectionEnabled;
        TakeEnabled = takeEnabled;
        WorldIsUp = worldIsUp;
        Authority = authority;
        SeamAvailable = seamAvailable;
        WorkerPresent = workerPresent;
        WorkerRecordUnwritable = workerRecordUnwritable;
        WorkerRecordUnreadable = workerRecordUnreadable;
        TransferInFlight = transferInFlight;
        InterruptedTourOpen = interruptedTourOpen;
        SourceDesignated = sourceDesignated;
        Cargo = cargo ?? string.Empty;
        CargoKnown = cargoKnown;
        UnitsWanted = unitsWanted;
        UnitsThatFit = unitsThatFit;
    }

    /// <summary>Teamster's master switch and the collection switch, both on.
    /// </summary>
    public bool CollectionEnabled { get; }

    /// <summary><c>Workers/GunnarChestTakeEnabled</c>.</summary>
    public bool TakeEnabled { get; }

    public bool WorldIsUp { get; }

    public WorkAuthorityVerdict Authority { get; }

    public bool SeamAvailable { get; }

    public bool WorkerPresent { get; }

    public bool WorkerRecordUnwritable { get; }

    public bool WorkerRecordUnreadable { get; }

    public bool TransferInFlight { get; }

    /// <summary>Whether an interrupted tour's plan is open (#379).</summary>
    public bool InterruptedTourOpen { get; }

    /// <summary>Whether the player chose a source chest. <b>Never a nearest-chest
    /// lookup.</b></summary>
    public bool SourceDesignated { get; }

    /// <summary>The one item prefab the player named. Exact; there is no
    /// wildcard and no "everything".</summary>
    public string Cargo { get; }

    /// <summary>Whether the game has an item of that name.</summary>
    public bool CargoKnown { get; }

    public int UnitsWanted { get; }

    /// <summary>How many units of <see cref="Cargo"/> still fit under the carry
    /// limit, or <c>null</c> when that could not be read.</summary>
    public int? UnitsThatFit { get; }
}

/// <summary>Whether a take from a chest may start (#381, `DECISIONS.md` D20).
/// The whole decision, over values, so every refusal can be exercised without
/// the game running.
///
/// <b>What this gate is not.</b> It is not the permission check. The player's
/// <c>Take</c> or <c>Both</c> mark on the source, this client owning it, nobody
/// having it open, the ward, the privacy setting and the reach are all the
/// shared library's permit mint, asked by the take port on the container itself
/// at the moment of the move.</summary>
internal static class TakeOrderGate
{
    /// <summary>The longest item prefab name an order may carry. Vanilla's are
    /// far shorter; a longer one is not a name anybody typed on purpose.</summary>
    public const int LongestCargoName = 64;

    /// <summary>Decides. The first failure is the refusal, in the order a player
    /// would fix them.</summary>
    public static TakeRefusal Evaluate(in TakeRequest request)
    {
        if (!request.CollectionEnabled)
        {
            return TakeRefusal.CollectionOff;
        }

        if (!request.TakeEnabled)
        {
            return TakeRefusal.TakeOff;
        }

        if (!request.WorldIsUp)
        {
            return TakeRefusal.WorldIsGoingAway;
        }

        if (request.Authority != WorkAuthorityVerdict.Granted)
        {
            return TakeRefusal.WorkRefused;
        }

        if (!request.SeamAvailable)
        {
            return TakeRefusal.SeamUnavailable;
        }

        if (request.WorkerRecordUnwritable)
        {
            return TakeRefusal.WorkerRecordUnwritable;
        }

        if (request.WorkerRecordUnreadable)
        {
            return TakeRefusal.WorkerRecordUnreadable;
        }

        if (!request.WorkerPresent)
        {
            return TakeRefusal.NoWorker;
        }

        if (request.TransferInFlight)
        {
            return TakeRefusal.AlreadyWorking;
        }

        if (request.InterruptedTourOpen)
        {
            return TakeRefusal.InterruptedTourOpen;
        }

        if (!request.SourceDesignated)
        {
            return TakeRefusal.NoSource;
        }

        if (!IsCargoName(request.Cargo))
        {
            return TakeRefusal.NoCargoNamed;
        }

        if (!request.CargoKnown)
        {
            return TakeRefusal.UnknownCargo;
        }

        if (request.UnitsWanted < 1)
        {
            return TakeRefusal.NothingWanted;
        }

        if (!request.UnitsThatFit.HasValue)
        {
            return TakeRefusal.CarryUnreadable;
        }

        return request.UnitsThatFit.Value >= 1 ? TakeRefusal.None : TakeRefusal.NoCarryRoom;
    }

    /// <summary>How many units the take may ask the chest for: what was wanted,
    /// never more than fits under the carry limit. The chest's own count and his
    /// slots are measured by the port; this is only the product's discipline.
    /// </summary>
    public static int UnitsToAsk(int unitsWanted, int unitsThatFit)
    {
        if (unitsWanted < 1 || unitsThatFit < 1)
        {
            return 0;
        }

        return unitsWanted < unitsThatFit ? unitsWanted : unitsThatFit;
    }

    /// <summary>Whether a typed name can be an item prefab: non-empty, bounded,
    /// and nothing but letters, digits and the underscore vanilla's prefab names
    /// use. A wildcard, a space or a path is refused rather than interpreted.
    /// </summary>
    public static bool IsCargoName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name!.Length > LongestCargoName)
        {
            return false;
        }

        for (int index = 0; index < name.Length; index++)
        {
            char letter = name[index];
            bool plain = (letter >= 'a' && letter <= 'z')
                || (letter >= 'A' && letter <= 'Z')
                || (letter >= '0' && letter <= '9')
                || letter == '_';
            if (!plain)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>What a take did.</summary>
internal enum TakeOutcome
{
    Unspecified = 0,

    /// <summary>Everything asked for arrived in him.</summary>
    Taken = 1,

    /// <summary>Some of it arrived and the rest is still in the chest. Never
    /// deleted.</summary>
    PartlyTaken = 2,

    /// <summary>The chest refused, or there was nothing to take. Both
    /// inventories are as they were.</summary>
    Refused = 3,

    /// <summary>Whether it moved could not be established. Nothing is credited,
    /// nothing is retried and nothing is put right automatically; a person
    /// looks.</summary>
    Uncertain = 4,
}

/// <summary>What one take actually moved, measured on both sides.</summary>
internal readonly struct TakeResult
{
    public TakeResult(TakeOutcome outcome, IReadOnlyList<KeyValuePair<string, int>>? moved, string detail = "")
    {
        Outcome = outcome;
        Moved = moved ?? Array.Empty<KeyValuePair<string, int>>();
        Detail = detail ?? string.Empty;
    }

    public TakeOutcome Outcome { get; }

    /// <summary>Per item, how many units were <b>observed</b> to arrive in him -
    /// never how many were asked for.</summary>
    public IReadOnlyList<KeyValuePair<string, int>> Moved { get; }

    public string Detail { get; }
}

/// <summary>The sentences a player reads when a take does not happen, for both
/// vocabularies. Every value of both enums has a sentence of its own, which a
/// test asserts.</summary>
internal static class TakeSentences
{
    /// <summary>What to tell a player about Gunnar, the room and the order.
    /// </summary>
    public static string Describe(TakeRefusal refusal, WorkAuthorityVerdict authority)
    {
        switch (refusal)
        {
            case TakeRefusal.None:
                return "He can take it.";
            case TakeRefusal.CollectionOff:
                return "Gunnar's collection is off. Turn on Workers/GunnarCollectionEnabled first; taking from " +
                    "a chest also needs Workers/GunnarChestTakeEnabled.";
            case TakeRefusal.TakeOff:
                return "Taking from a chest is off. Turn on Workers/GunnarChestTakeEnabled to let him take " +
                    "from a chest you have marked for taking.";
            case TakeRefusal.WorldIsGoingAway:
                return "The world is going away, so nothing is moved.";
            case TakeRefusal.WorkRefused:
                return WorkAuthorityPolicy.Describe(authority);
            case TakeRefusal.SeamUnavailable:
                return "This build of the game does not offer everything the take needs, so none of it is " +
                    "called. The log says which member was missing.";
            case TakeRefusal.WorkerRecordUnwritable:
                return "Gunnar is here, but the game could not write down what he is carrying on his body, so " +
                    "he is handed nothing more until a change does get written. Nothing was taken.";
            case TakeRefusal.WorkerRecordUnreadable:
                return "Gunnar is here, but his body has not been able to read what it is carrying, so he is " +
                    "handed nothing. If he has just appeared, try again in a moment.";
            case TakeRefusal.NoWorker:
                return "Gunnar is not here. Bring him into the world first.";
            case TakeRefusal.AlreadyWorking:
                return "He is already moving something. Let that finish, or cancel it.";
            case TakeRefusal.InterruptedTourOpen:
                return GunnarTourPlan.OpenPlanRefusal;
            case TakeRefusal.NoSource:
                return "No chest was chosen to take from. Look at it and run ct_collect source - he never " +
                    "looks for the nearest one.";
            case TakeRefusal.NoCargoNamed:
                return "Name the one item he should take, exactly as the game names it (for example Stone). " +
                    "He never takes everything.";
            case TakeRefusal.UnknownCargo:
                return "The game has no item by that name. Use the name the game itself uses (for example " +
                    "Stone, Wood or CopperOre). Nothing was taken.";
            case TakeRefusal.NothingWanted:
                return "Ask for at least one.";
            case TakeRefusal.CarryUnreadable:
                return "What he is already carrying could not be weighed, so how much more he can carry is " +
                    "unknown. Nothing was taken.";
            case TakeRefusal.NoCarryRoom:
                return "He cannot carry any more of that. Nothing was taken.";
            default:
                return "The take was refused for a reason nobody recorded, so nothing was moved.";
        }
    }

    /// <summary>What to tell a player about the source container itself.
    /// </summary>
    public static string Describe(NpcContainerRefusal refusal)
    {
        switch (refusal)
        {
            case NpcContainerRefusal.None:
                return "The chest allows it.";
            case NpcContainerRefusal.Gone:
                return "That chest is not there any more, or its contents could not be read. Nothing was taken.";
            case NpcContainerRefusal.NotThisContainer:
                return "That is not the chest that was chosen - something else stands there now. Nothing is " +
                    "taken from the wrong chest.";
            case NpcContainerRefusal.NotDesignated:
                return "No chest was chosen to take from. He never looks for the nearest one.";
            case NpcContainerRefusal.NotEnabled:
                return "You have not opened that chest to Gunnar. Look at it and press the container key " +
                    "until it says he may take from it.";
            case NpcContainerRefusal.UseNotAllowed:
                return "That chest is marked for putting things in only. Cycle it to take, or to both, before " +
                    "he can take anything out of it.";
            case NpcContainerRefusal.NotOwnedHere:
                return "This machine does not own that chest right now, so nothing is taken from it.";
            case NpcContainerRefusal.InUse:
                return "Somebody has that chest open. He waits rather than taking from under them.";
            case NpcContainerRefusal.WardDenied:
                return "A guard stone refuses that chest. Nothing was taken.";
            case NpcContainerRefusal.PrivacyDenied:
                return "That chest's own privacy setting refuses. Nothing was taken.";
            case NpcContainerRefusal.OutOfReach:
                return "He is not close enough to that chest yet.";
            default:
                return "The chest refused for a reason nobody recorded, so nothing was taken.";
        }
    }
}
