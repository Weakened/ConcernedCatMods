using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>Why a deposit was refused before anything was attempted. Zero means
/// it was not.
///
/// <b>Why this enum stops where it does.</b> Everything from
/// <see cref="NothingToDeposit"/> upward is about Gunnar and the room he is
/// working in - facts this product reads. Everything about the <i>container</i>
/// is <see cref="NpcContainerRefusal"/>'s, decided by the shared library's own
/// permit mint, and reported through <see cref="DepositSentences"/>. Two
/// vocabularies, because they have two different fixes and a player is owed the
/// right one: "turn the feature on" and "mark that chest" are not the same
/// sentence, and an enum that merged them would have to choose.</summary>
internal enum DepositRefusal
{
    /// <summary>Nothing refused.</summary>
    None = 0,

    /// <summary>The player has not opted in. Asked first, so somebody who never
    /// turned this on gets the sentence that says so.</summary>
    FeatureOff = 1,

    /// <summary>No world is loaded as far as the runtime's own lifecycle is
    /// concerned - including the shutdown window where the game's singletons
    /// still answer. Same reasoning as the pick order's own clause: a transfer
    /// begun there would be one whose record cannot outlive it.</summary>
    WorldIsGoingAway = 2,

    /// <summary>The work-authority rule refused: not the host, a dedicated
    /// server, or somebody else connected. Re-asked in the frame of the move,
    /// never cached from the tick that planned it (`DECISIONS.md` D3, D15).
    /// </summary>
    WorkRefused = 3,

    /// <summary>The start-up probe could not verify every game member the
    /// deposit binds, so none of them is called.</summary>
    SeamUnavailable = 4,

    /// <summary>He is standing right there, and his last inventory change could
    /// not be written to his own network object. What he holds and what is saved
    /// disagree, so moving some of it out would be moving an amount nobody can
    /// reconcile afterwards. Asked before "no worker" for the reason
    /// <see cref="CollectionOrderRefusal.WorkerRecordUnwritable"/> gives: such a
    /// body IS absent as far as <c>BoundBody</c> is concerned, and answering
    /// "Gunnar is not here" about a visible Gunnar is the falsehood that clause
    /// exists to stop.</summary>
    WorkerRecordUnwritable = 5,

    /// <summary>He is standing right there, and his body has not been able to
    /// read what it carries. Inert by construction: it never saves, so a deposit
    /// out of it would be a deposit out of an inventory nobody established.
    /// </summary>
    WorkerRecordUnreadable = 6,

    /// <summary>No worker body, or it is not alive.</summary>
    NoWorker = 7,

    /// <summary>Something else is already moving material - a pick in flight, or
    /// another deposit. One body does one thing.</summary>
    AlreadyWorking = 8,

    /// <summary>He is not carrying anything. Not a fault, and deliberately not
    /// silent: a player who ordered a deposit is owed the reason nothing
    /// happened.</summary>
    NothingToDeposit = 9,

    /// <summary>The job named no destination. Separate from the container's own
    /// <see cref="NpcContainerRefusal.NotDesignated"/> because this is reached
    /// before a container is resolved at all - and because <b>there is no
    /// nearest-chest fallback anywhere in this path</b> (`DECISIONS.md` D15).
    /// Refusing here is what makes that true rather than stated.</summary>
    NoDestination = 10,

    /// <summary>Something could not be read. Unknown refuses.</summary>
    Unreadable = 11,
}

/// <summary>What the runtime read at the moment a deposit was ordered. Every
/// field is something the game was asked this frame.
///
/// <b>A defaulted instance is refused.</b> <c>default</c> is "not opted in, no
/// world, no authority, no seam, no worker, carrying nothing, no destination",
/// which fails the first clause - so a caller that forgets a field gets a
/// refusal, never an admission.</summary>
internal readonly struct DepositRequest
{
    public DepositRequest(
        bool featureEnabled,
        bool worldIsUp,
        WorkAuthorityVerdict authority,
        bool seamAvailable,
        bool workerPresent,
        bool workerRecordUnwritable,
        bool workerRecordUnreadable,
        bool transferInFlight,
        bool carryingSomething,
        bool destinationDesignated,
        bool carriedIsReadable)
    {
        CarriedIsReadable = carriedIsReadable;
        FeatureEnabled = featureEnabled;
        WorldIsUp = worldIsUp;
        Authority = authority;
        SeamAvailable = seamAvailable;
        WorkerPresent = workerPresent;
        WorkerRecordUnwritable = workerRecordUnwritable;
        WorkerRecordUnreadable = workerRecordUnreadable;
        TransferInFlight = transferInFlight;
        CarryingSomething = carryingSomething;
        DestinationDesignated = destinationDesignated;
    }

    /// <summary>Teamster's master switch and the collection switch, both on.
    /// </summary>
    public bool FeatureEnabled { get; }

    public bool WorldIsUp { get; }

    public WorkAuthorityVerdict Authority { get; }

    public bool SeamAvailable { get; }

    public bool WorkerPresent { get; }

    public bool WorkerRecordUnwritable { get; }

    public bool WorkerRecordUnreadable { get; }

    /// <summary>Whether a pick or another deposit is already in flight.
    /// </summary>
    public bool TransferInFlight { get; }

    /// <summary>Whether he is holding anything at all, measured rather than
    /// assumed from a ledger - an empty body cannot deposit whatever the
    /// accounting believes.</summary>
    public bool CarryingSomething { get; }

    /// <summary>Whether the job named a destination container. <b>Never a
    /// nearest-chest lookup.</b></summary>
    public bool DestinationDesignated { get; }

    /// <summary>Whether what he is carrying could be read at all.
    ///
    /// <b>False is not "nothing".</b> An inventory that could not be enumerated
    /// holds an unknown amount, and reporting that as an empty Gunnar is the same
    /// falsehood the two record-state refusals exist to stop - the player is told
    /// he is carrying nothing while he is standing there holding it.
    ///
    /// <b>No default, deliberately.</b> It had one - <c>true</c> - and a review
    /// pointed out what that buys: dropping the argument at the single call site
    /// would compile, take the optimistic value, leave the whole suite green, and
    /// put the falsehood straight back. A required parameter makes that a
    /// compile error. A <c>default(DepositRequest)</c> still reads <c>false</c>
    /// here, which is the refusing direction.</summary>
    public bool CarriedIsReadable { get; }
}

/// <summary>Whether a deposit may start (#381, `DECISIONS.md` D15). The whole
/// decision, over values, so every refusal can be exercised without the game
/// running - which matters here for the same reason it matters for the pick:
/// the runtime that asks binds Unity and no test in this repository can load it.
///
/// <b>What this gate is not.</b> It is not the permission check. The player's
/// mark on the destination, this client owning it, nobody having it open, the
/// ward and the privacy setting are all the shared library's permit mint, asked
/// at the moment of the move on the container itself - because a permission
/// re-derived by a caller is a permission that can disagree with the one the
/// player set. This gate is what has to be true about <i>Gunnar and the
/// room</i> before a container is even resolved.</summary>
internal static class DepositOrderGate
{
    /// <summary>Decides. The first failure is the refusal, in the order a player
    /// would fix them: their own setting, then the room, then this build, then
    /// Gunnar, then whether there is anything to move and anywhere to move it.
    /// </summary>
    public static DepositRefusal Evaluate(in DepositRequest request)
    {
        if (!request.FeatureEnabled)
        {
            return DepositRefusal.FeatureOff;
        }

        if (!request.WorldIsUp)
        {
            return DepositRefusal.WorldIsGoingAway;
        }

        if (request.Authority != WorkAuthorityVerdict.Granted)
        {
            return DepositRefusal.WorkRefused;
        }

        if (!request.SeamAvailable)
        {
            return DepositRefusal.SeamUnavailable;
        }

        if (request.WorkerRecordUnwritable)
        {
            return DepositRefusal.WorkerRecordUnwritable;
        }

        if (request.WorkerRecordUnreadable)
        {
            return DepositRefusal.WorkerRecordUnreadable;
        }

        if (!request.WorkerPresent)
        {
            return DepositRefusal.NoWorker;
        }

        if (request.TransferInFlight)
        {
            return DepositRefusal.AlreadyWorking;
        }

        if (!request.DestinationDesignated)
        {
            return DepositRefusal.NoDestination;
        }

        if (!request.CarriedIsReadable)
        {
            // Asked before the empty check, because an unreadable inventory looks
            // exactly like an empty one from here and only one of them is true.
            return DepositRefusal.Unreadable;
        }

        return request.CarryingSomething ? DepositRefusal.None : DepositRefusal.NothingToDeposit;
    }
}

/// <summary>The sentences a player reads when a deposit does not happen, for
/// both vocabularies.
///
/// <b>Why every value gets one, including the ones "nothing can produce".</b>
/// The pick order's own sentence table is tested with exactly that property -
/// every value of the enum has a sentence of its own rather than falling through
/// to "a reason nobody recorded" - and it is the property that caught two
/// falsehoods there. The same test is written against this table.</summary>
internal static class DepositSentences
{
    /// <summary>What to tell a player about Gunnar and the room.</summary>
    public static string Describe(DepositRefusal refusal, WorkAuthorityVerdict authority)
    {
        switch (refusal)
        {
            case DepositRefusal.None:
                return "He can put it down.";
            case DepositRefusal.FeatureOff:
                return "Gunnar's collection is off. Turn on Workers/GunnarCollectionEnabled to let him " +
                    "pick things up and put them in a chest you have marked.";
            case DepositRefusal.WorldIsGoingAway:
                return "The world is going away, so nothing is moved. What he is carrying is saved with his body.";
            case DepositRefusal.WorkRefused:
                return WorkAuthorityPolicy.Describe(authority);
            case DepositRefusal.SeamUnavailable:
                return "This build of the game does not offer everything the deposit needs, so none of it " +
                    "is called. The log says which member was missing.";
            case DepositRefusal.WorkerRecordUnwritable:
                return "Gunnar is here and still holding what he picked up, but the game could not write it " +
                    "down on his body. He is handed nothing more and moves nothing out until a change does " +
                    "get written; a reload brings him back with what was last saved. Whatever the failed " +
                    "write was carrying is gone.";
            case DepositRefusal.WorkerRecordUnreadable:
                return "Gunnar is here, but his body has not been able to read what it is carrying, so it " +
                    "moves nothing and will not write an empty inventory over what he holds. If he has just " +
                    "appeared, try again in a moment; otherwise the log says what could not be read.";
            case DepositRefusal.NoWorker:
                return "Gunnar is not here. Bring him into the world first.";
            case DepositRefusal.AlreadyWorking:
                return "He is already moving something. Let that finish, or cancel it.";
            case DepositRefusal.NothingToDeposit:
                return "He is not carrying anything.";
            case DepositRefusal.NoDestination:
                return "No chest was chosen. Pick the one he should use - he never looks for the nearest one.";
            case DepositRefusal.Unreadable:
                return "What Gunnar is carrying could not be read, so nothing was moved. He is not " +
                    "empty - the amount is unknown, which is a different thing - and nothing is " +
                    "assumed about it. The log says what could not be read.";
            default:
                return "The deposit was refused for a reason nobody recorded, so nothing was moved.";
        }
    }

    /// <summary>What to tell a player about the container itself. The library
    /// decides which refusal applies; this product owns the wording, because the
    /// fix is always something in <i>this</i> product's terms - a mark set with
    /// Teamster's own key, a chest Teamster's own runtime resolved.</summary>
    public static string Describe(NpcContainerRefusal refusal)
    {
        switch (refusal)
        {
            case NpcContainerRefusal.None:
                return "The chest allows it.";
            case NpcContainerRefusal.Gone:
                return "That chest is not there any more, or its contents could not be read. He keeps what " +
                    "he is carrying.";
            case NpcContainerRefusal.NotThisContainer:
                return "That is not the chest that was chosen - something else stands there now. He keeps " +
                    "what he is carrying, and nothing is put into the wrong chest.";
            case NpcContainerRefusal.NotDesignated:
                return "No chest was chosen for this job. He never looks for the nearest one.";
            case NpcContainerRefusal.NotEnabled:
                return "You have not opened that chest to Gunnar. Look at it and press the container key " +
                    "until it says he may put things in.";
            case NpcContainerRefusal.UseNotAllowed:
                return "That chest is marked for taking only. Cycle it to deposit, or to both, before he " +
                    "can put anything in it.";
            case NpcContainerRefusal.NotOwnedHere:
                return "This machine does not own that chest right now, and a write it does not own is " +
                    "thrown away without telling anyone. He keeps what he is carrying.";
            case NpcContainerRefusal.InUse:
                return "Somebody has that chest open, and what is put in now is overwritten when their " +
                    "window closes. He waits rather than losing it.";
            case NpcContainerRefusal.WardDenied:
                return "A guard stone refuses that chest. He keeps what he is carrying.";
            case NpcContainerRefusal.PrivacyDenied:
                return "That chest's own privacy setting refuses. He keeps what he is carrying.";
            case NpcContainerRefusal.OutOfReach:
                return "He is not close enough to that chest yet.";
            default:
                return "The chest refused for a reason nobody recorded, so nothing was moved.";
        }
    }
}
