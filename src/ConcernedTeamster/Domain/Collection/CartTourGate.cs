using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>Why a cart-assisted tour, or one load of it into the cart, was
/// refused (#381, owner decision 2026-10-31, `DECISIONS.md` <b>D22</b>). Zero
/// means it was not. The order is the order a player would fix things in: their
/// own settings, the room, this build, Gunnar, the cart, and then the job's own
/// inputs.</summary>
internal enum CartTourRefusal
{
    None = 0,

    /// <summary>Teamster's master switch, collection, hauling or the cart tour's
    /// own switch is off. All four are needed: a cart is assigned through the
    /// hauling runtime, and a player who let him pick stones up, or pull a
    /// cart, has not thereby let him fill one.</summary>
    FeatureOff = 1,
    WorldIsGoingAway = 2,
    WorkRefused = 3,
    SeamUnavailable = 4,
    WorkerRecordUnwritable = 5,
    WorkerRecordUnreadable = 6,
    NoWorker = 7,

    /// <summary>A pick, a deposit, a take or another tour is already moving
    /// material. One body does one thing.</summary>
    AlreadyWorking = 8,

    /// <summary>No cart is assigned to him (<c>ct_haul assign</c> and
    /// <c>confirm</c>), or the assignment ended. <b>There is no nearest-cart
    /// fallback</b>: the cart is the one the player assigned, or there is none.
    /// </summary>
    NoCart = 9,

    /// <summary>The assignment now names a different cart from the one the tour
    /// began with. Never followed: a tour fills the cart it was started for.
    /// </summary>
    CartChanged = 10,

    /// <summary>The assigned cart does not resolve in this world load.</summary>
    CartNotHere = 11,

    /// <summary>The hauling runtime is doing something with the cart - a haul,
    /// a leg, a hold, a stop, a park - or somebody else holds his identity.
    /// Loading is for a cart standing still with nothing happening.</summary>
    CartBusy = 12,

    /// <summary>The cart is hitched - to him, to the player, to anybody - or its
    /// network object says it is. A hitched cart is in use, and a write into a
    /// container in use is lost or fights the person using it.</summary>
    CartHitched = 13,

    /// <summary>Somebody has the cart's container open.</summary>
    CartOpen = 14,

    /// <summary>The cart has moved from where it stood when the tour began. A
    /// container that moved fails closed (D15's rule), and a cart that rolls or
    /// is pushed is not the parked cart he was told to fill.</summary>
    CartMoved = 15,

    /// <summary>No area was set, or it was set in an earlier world load.</summary>
    NoArea = 16,

    /// <summary>The cart does not stand inside the area he is to collect. A
    /// cart-assisted tour fills a cart parked where the work is; walking every
    /// load to a cart somewhere else is a chest tour with a worse chest.</summary>
    CartOutsideArea = 17,

    /// <summary>What he carries could not be read.</summary>
    CarriedUnreadable = 18,

    /// <summary>He is already carrying what collecting yields. Only what
    /// <b>this</b> tour picks goes into the cart, so a load he brought with him
    /// is put in a chest first (<c>ct_collect deposit</c>, or a chest tour).
    /// </summary>
    AlreadyCarrying = 19,

    /// <summary>What he can carry could not be read, or is nothing.</summary>
    NoCapacity = 20,

    /// <summary>A load was asked for and he carries nothing this tour picked.
    /// Not a fault: there is nothing to put in the cart.</summary>
    NothingToLoad = 21,
}

/// <summary>The cart assigned to Gunnar, as the hauling executor last observed
/// it, copied into values (#381, D22). Read from the executor's own getters and
/// its frame observation, which runs every rendered frame while a cart is
/// assigned, so this reads no cart of its own. A defaulted value is "no cart".
/// </summary>
internal readonly struct AssignedCartFacts
{
    public AssignedCartFacts(
        bool leaseHeld,
        CartKey cart,
        bool resolved,
        bool idle,
        bool identityFree,
        bool hitched,
        bool containerOpen,
        WorkPoint position,
        WorkPoint standAt)
    {
        LeaseHeld = leaseHeld;
        Cart = cart;
        Resolved = resolved;
        Idle = idle;
        IdentityFree = identityFree;
        Hitched = hitched;
        ContainerOpen = containerOpen;
        Position = position;
        StandAt = standAt;
    }

    /// <summary>A lease names a cart for him.</summary>
    public bool LeaseHeld { get; }

    /// <summary>The lease's cart key, epoch included. Empty without a lease.
    /// Compared, never resolved by name.</summary>
    public CartKey Cart { get; }

    /// <summary>The leased cart resolved on the executor's last frame.</summary>
    public bool Resolved { get; }

    /// <summary>The executor is doing nothing with the cart: phase Ready, no
    /// haul, no leg, no joint, no hold, no release still pending, no stop
    /// pending. His identity is asked separately (<see cref="IdentityFree"/>),
    /// because the tour itself holds it once it starts.</summary>
    public bool Idle { get; }

    /// <summary>Nobody holds his identity.</summary>
    public bool IdentityFree { get; }

    /// <summary>A joint holds the cart, or the cart's network object carries
    /// vanilla's attach flag, or the executor believes it is attached.</summary>
    public bool Hitched { get; }

    /// <summary>Somebody has the cart's container open.</summary>
    public bool ContainerOpen { get; }

    /// <summary>Where the cart stood.</summary>
    public WorkPoint Position { get; }

    /// <summary>Where a puller stands at the handle. He loads standing here,
    /// never walking into the cart's body (D21's reason: walking into an
    /// unbraked cart shoves it).</summary>
    public WorkPoint StandAt { get; }

    /// <summary>Copies what the executor's getters answer, and nothing else.
    /// The same reading <see cref="CartUpkeepFacts.Read"/> makes for the idle
    /// hammer, without its identity clause, which is a separate fact here.
    /// </summary>
    public static AssignedCartFacts Read(HaulExecutor? executor)
    {
        if (executor == null)
        {
            return default;
        }

        CartLease? lease = executor.ActiveLease;
        if (lease == null)
        {
            return default;
        }

        CartObservation cart = executor.LastCart;
        return new AssignedCartFacts(
            leaseHeld: true,
            cart: lease.Cart,
            resolved: cart.Resolved,
            idle: IsIdle(executor),
            identityFree: executor.Modes.JobId == null,
            hitched: executor.Attached || cart.HasJoint || cart.AttachFlag,
            containerOpen: cart.ContainerOpen,
            position: cart.CartPosition,
            standAt: cart.ApproachPoint);
    }

    /// <summary>A lease is held and the executor is doing nothing with the
    /// cart: phase Ready, no haul, no leg, no joint, no hold, no release still
    /// pending, no stop pending. Allocation-free, because the worker tick asks
    /// it twenty times a second.</summary>
    public static bool IsIdle(HaulExecutor? executor) =>
        executor != null &&
        executor.ActiveLease != null &&
        executor.Phase == HaulPhase.Ready &&
        executor.HaulId.Length == 0 &&
        !executor.Attached &&
        !executor.HoldingCart &&
        !executor.StillHolding &&
        executor.PendingStop == HaulStopIntent.Unspecified;
}

/// <summary>What the runtime read at the moment a cart-assisted tour was asked
/// for. A defaulted instance is refused on its first clause.</summary>
internal readonly struct CartTourRequest
{
    public CartTourRequest(
        bool cartTourEnabled,
        bool worldIsUp,
        WorkAuthorityVerdict authority,
        bool seamAvailable,
        bool workerRecordUnwritable,
        bool workerRecordUnreadable,
        bool workerPresent,
        bool somethingInFlight,
        AssignedCartFacts cart,
        bool areaSetThisWorld,
        bool cartInsideArea,
        bool carriedIsReadable,
        bool carryingCollected,
        int capacityUnits)
    {
        CartTourEnabled = cartTourEnabled;
        WorldIsUp = worldIsUp;
        Authority = authority;
        SeamAvailable = seamAvailable;
        WorkerRecordUnwritable = workerRecordUnwritable;
        WorkerRecordUnreadable = workerRecordUnreadable;
        WorkerPresent = workerPresent;
        SomethingInFlight = somethingInFlight;
        Cart = cart;
        AreaSetThisWorld = areaSetThisWorld;
        CartInsideArea = cartInsideArea;
        CarriedIsReadable = carriedIsReadable;
        CarryingCollected = carryingCollected;
        CapacityUnits = capacityUnits;
    }

    /// <summary>The master switch, collection, hauling and the cart tour's own
    /// switch, all four.</summary>
    public bool CartTourEnabled { get; }

    public bool WorldIsUp { get; }

    public WorkAuthorityVerdict Authority { get; }

    public bool SeamAvailable { get; }

    public bool WorkerRecordUnwritable { get; }

    public bool WorkerRecordUnreadable { get; }

    public bool WorkerPresent { get; }

    public bool SomethingInFlight { get; }

    public AssignedCartFacts Cart { get; }

    public bool AreaSetThisWorld { get; }

    public bool CartInsideArea { get; }

    public bool CarriedIsReadable { get; }

    public bool CarryingCollected { get; }

    public int CapacityUnits { get; }
}

/// <summary>What the runtime read in the frame of one load into the cart. A
/// defaulted instance is refused on its first clause.</summary>
internal readonly struct CartLoadRequest
{
    public CartLoadRequest(
        bool cartTourEnabled,
        bool worldIsUp,
        WorkAuthorityVerdict authority,
        bool seamAvailable,
        bool workerRecordUnwritable,
        bool workerRecordUnreadable,
        bool workerPresent,
        bool transferInFlight,
        CartTourRefusal cartWatch,
        bool carriedIsReadable,
        bool somethingToLoad)
    {
        CartTourEnabled = cartTourEnabled;
        WorldIsUp = worldIsUp;
        Authority = authority;
        SeamAvailable = seamAvailable;
        WorkerRecordUnwritable = workerRecordUnwritable;
        WorkerRecordUnreadable = workerRecordUnreadable;
        WorkerPresent = workerPresent;
        TransferInFlight = transferInFlight;
        CartWatch = cartWatch;
        CarriedIsReadable = carriedIsReadable;
        SomethingToLoad = somethingToLoad;
    }

    public bool CartTourEnabled { get; }

    public bool WorldIsUp { get; }

    public WorkAuthorityVerdict Authority { get; }

    public bool SeamAvailable { get; }

    public bool WorkerRecordUnwritable { get; }

    public bool WorkerRecordUnreadable { get; }

    public bool WorkerPresent { get; }

    /// <summary>Something other than the tour itself is moving material.</summary>
    public bool TransferInFlight { get; }

    /// <summary><see cref="CartTourGate.Watch"/>'s verdict on the cart, read
    /// in this frame.</summary>
    public CartTourRefusal CartWatch { get; }

    public bool CarriedIsReadable { get; }

    /// <summary>He carries something this tour picked.</summary>
    public bool SomethingToLoad { get; }
}

/// <summary>Whether a cart-assisted tour may begin, whether it may go on, and
/// whether one load may go into the cart (#381, D22). Game-free, over values.
///
/// <b>What it is not.</b> The cart's own access - this client owning it, nobody
/// having it open, the ward, the privacy setting, the reach, the cart still
/// being the leased one and still where it stood - is asked again at the moment
/// of every move by the pinned <c>GunnarCartLoadPort</c>, through the shared
/// permit mint. This decides whether starting, going on, or trying makes sense
/// at all.</summary>
internal static class CartTourGate
{
    /// <summary>How far the cart may have moved since the tour began and still
    /// be the cart he was told to fill. A parked cart settles; a pushed or
    /// rolling one goes further than this, and the tour stops with what he holds
    /// still in him.</summary>
    public const float MostDriftMetres = 1f;

    public static CartTourRefusal EvaluateStart(in CartTourRequest request)
    {
        CartTourRefusal common = Common(
            request.CartTourEnabled,
            request.WorldIsUp,
            request.Authority,
            request.SeamAvailable,
            request.WorkerRecordUnwritable,
            request.WorkerRecordUnreadable,
            request.WorkerPresent,
            request.SomethingInFlight);
        if (common != CartTourRefusal.None)
        {
            return common;
        }

        AssignedCartFacts cart = request.Cart;
        if (!cart.LeaseHeld || cart.Cart.IsEmpty)
        {
            return CartTourRefusal.NoCart;
        }

        if (!cart.Resolved)
        {
            return CartTourRefusal.CartNotHere;
        }

        if (!cart.Idle || !cart.IdentityFree)
        {
            return CartTourRefusal.CartBusy;
        }

        if (cart.Hitched)
        {
            return CartTourRefusal.CartHitched;
        }

        if (cart.ContainerOpen)
        {
            return CartTourRefusal.CartOpen;
        }

        if (!request.AreaSetThisWorld)
        {
            return CartTourRefusal.NoArea;
        }

        if (!request.CartInsideArea)
        {
            return CartTourRefusal.CartOutsideArea;
        }

        if (!request.CarriedIsReadable)
        {
            return CartTourRefusal.CarriedUnreadable;
        }

        if (request.CarryingCollected)
        {
            return CartTourRefusal.AlreadyCarrying;
        }

        return request.CapacityUnits > 0 ? CartTourRefusal.None : CartTourRefusal.NoCapacity;
    }

    /// <summary>Whether the cart is still the parked cart the tour began with.
    /// Asked every frame and every worker tick while a cart tour runs, and again
    /// in the frame of every load. <see cref="CartTourRefusal.None"/> means go
    /// on; anything else stops the tour with what he holds still in him.
    ///
    /// The container being open is not a reason to stop the tour here: the load
    /// itself refuses it, and the tour then stops on that refusal, which names
    /// the right fix.</summary>
    public static CartTourRefusal Watch(in AssignedCartFacts cart, CartKey startCart, WorkPoint startPosition)
    {
        if (!cart.LeaseHeld || cart.Cart.IsEmpty)
        {
            return CartTourRefusal.NoCart;
        }

        if (startCart.IsEmpty || !cart.Cart.Equals(startCart))
        {
            return CartTourRefusal.CartChanged;
        }

        if (!cart.Resolved)
        {
            return CartTourRefusal.CartNotHere;
        }

        if (!cart.Idle)
        {
            return CartTourRefusal.CartBusy;
        }

        if (cart.Hitched)
        {
            return CartTourRefusal.CartHitched;
        }

        float drift = cart.Position.HorizontalDistanceTo(startPosition);
        if (float.IsNaN(drift) || drift > MostDriftMetres
            || float.IsNaN(cart.Position.VerticalDistanceTo(startPosition))
            || cart.Position.VerticalDistanceTo(startPosition) > MostDriftMetres)
        {
            return CartTourRefusal.CartMoved;
        }

        return CartTourRefusal.None;
    }

    public static CartTourRefusal EvaluateLoad(in CartLoadRequest request)
    {
        CartTourRefusal common = Common(
            request.CartTourEnabled,
            request.WorldIsUp,
            request.Authority,
            request.SeamAvailable,
            request.WorkerRecordUnwritable,
            request.WorkerRecordUnreadable,
            request.WorkerPresent,
            request.TransferInFlight);
        if (common != CartTourRefusal.None)
        {
            return common;
        }

        if (request.CartWatch != CartTourRefusal.None)
        {
            return request.CartWatch;
        }

        if (!request.CarriedIsReadable)
        {
            return CartTourRefusal.CarriedUnreadable;
        }

        return request.SomethingToLoad ? CartTourRefusal.None : CartTourRefusal.NothingToLoad;
    }

    private static CartTourRefusal Common(
        bool enabled,
        bool worldIsUp,
        WorkAuthorityVerdict authority,
        bool seamAvailable,
        bool recordUnwritable,
        bool recordUnreadable,
        bool workerPresent,
        bool inFlight)
    {
        if (!enabled)
        {
            return CartTourRefusal.FeatureOff;
        }

        if (!worldIsUp)
        {
            return CartTourRefusal.WorldIsGoingAway;
        }

        if (authority != WorkAuthorityVerdict.Granted)
        {
            return CartTourRefusal.WorkRefused;
        }

        if (!seamAvailable)
        {
            return CartTourRefusal.SeamUnavailable;
        }

        if (recordUnwritable)
        {
            return CartTourRefusal.WorkerRecordUnwritable;
        }

        if (recordUnreadable)
        {
            return CartTourRefusal.WorkerRecordUnreadable;
        }

        if (!workerPresent)
        {
            return CartTourRefusal.NoWorker;
        }

        return inFlight ? CartTourRefusal.AlreadyWorking : CartTourRefusal.None;
    }

    /// <summary>The sentence for every refusal, including the ones "nothing can
    /// produce".</summary>
    public static string Describe(CartTourRefusal refusal, WorkAuthorityVerdict authority)
    {
        switch (refusal)
        {
            case CartTourRefusal.None:
                return "He can go on.";
            case CartTourRefusal.FeatureOff:
                return "Filling a cart is off. It needs Workers/GunnarCartTourEnabled, and also " +
                    "Workers/GunnarCollectionEnabled and Workers/GunnarHaulingEnabled.";
            case CartTourRefusal.WorldIsGoingAway:
                return "The world is going away, so nothing starts. What he is carrying is saved with his body.";
            case CartTourRefusal.WorkRefused:
                return WorkAuthorityPolicy.Describe(authority);
            case CartTourRefusal.SeamUnavailable:
                return "This build of the game does not offer everything collecting needs, so none of it " +
                    "is called. The log says which member was missing.";
            case CartTourRefusal.WorkerRecordUnwritable:
                return "Gunnar is here and still holding what he picked up, but the game could not write it " +
                    "down on his body. He is handed nothing more until a change does get written.";
            case CartTourRefusal.WorkerRecordUnreadable:
                return "Gunnar is here, but his body has not been able to read what it is carrying, so he " +
                    "starts nothing. If he has just appeared, try again in a moment.";
            case CartTourRefusal.NoWorker:
                return "Gunnar is not here. Bring him into the world first.";
            case CartTourRefusal.AlreadyWorking:
                return "He is already picking something up, putting something away, or on a tour. Let that " +
                    "finish, or cancel it.";
            case CartTourRefusal.NoCart:
                return "No cart is assigned to him. Point at the cart, run 'ct_haul assign' and then " +
                    "'ct_haul confirm'. He never chooses a cart himself.";
            case CartTourRefusal.CartChanged:
                return "The cart assigned to him is not the one this tour was filling, so he stopped.";
            case CartTourRefusal.CartNotHere:
                return "The cart assigned to him is not loaded in the world, or no longer exists.";
            case CartTourRefusal.CartBusy:
                return "He is hauling, parking or holding the cart, or has another job. Filling the cart is " +
                    "for a cart standing still with nothing happening.";
            case CartTourRefusal.CartHitched:
                return "The cart is hitched to somebody. He fills only a cart standing unhitched.";
            case CartTourRefusal.CartOpen:
                return "Somebody has the cart open. Close it first.";
            case CartTourRefusal.CartMoved:
                return "The cart has moved from where it stood when the tour began, so he stopped. What he " +
                    "holds stays in him.";
            case CartTourRefusal.NoArea:
                return "No area was set. Stand in the middle of where he should collect and run " +
                    "'ct_collect area [radius]'.";
            case CartTourRefusal.CartOutsideArea:
                return "The cart stands outside the area you set. Park it inside the area, or set the area " +
                    "around it.";
            case CartTourRefusal.CarriedUnreadable:
                return "What Gunnar is carrying could not be read, so nothing starts.";
            case CartTourRefusal.AlreadyCarrying:
                return "He is already carrying stone or wood. Only what this tour picks goes into the cart, " +
                    "so put that in a chest first ('ct_collect deposit').";
            case CartTourRefusal.NoCapacity:
                return "What he can carry could not be read, or he can carry nothing more, so he starts " +
                    "nothing rather than carrying an amount nobody established.";
            case CartTourRefusal.NothingToLoad:
                return "He carries nothing this tour picked, so nothing goes into the cart.";
            default:
                return "Refused for a reason nobody recorded, so nothing happened.";
        }
    }
}

/// <summary>What to tell a player about the cart's container itself, when the
/// shared permit mint refused a load into it (D22). The library decides which
/// refusal applies; the wording is this product's, in its own terms.</summary>
internal static class CartLoadSentences
{
    public static string Describe(NpcContainerRefusal refusal)
    {
        switch (refusal)
        {
            case NpcContainerRefusal.None:
                return "It can go in.";
            case NpcContainerRefusal.Gone:
                return "The cart, or its container, is not there any more.";
            case NpcContainerRefusal.NotThisContainer:
                return "That is not the cart assigned to him, or it is not where it stood when the tour began.";
            case NpcContainerRefusal.NotDesignated:
            case NpcContainerRefusal.NotEnabled:
            case NpcContainerRefusal.UseNotAllowed:
                return "The cart is no longer assigned to him and standing idle, so nothing goes in it.";
            case NpcContainerRefusal.NotOwnedHere:
                return "This game does not control the cart right now, and a write into it would be lost.";
            case NpcContainerRefusal.InUse:
                return "Somebody has the cart open, or it is hitched.";
            case NpcContainerRefusal.WardDenied:
                return "A ward refuses him the cart.";
            case NpcContainerRefusal.PrivacyDenied:
                return "The cart's container is private to somebody else.";
            case NpcContainerRefusal.OutOfReach:
                return "He is not standing at the cart's handle.";
            default:
                return "The cart refused for a reason nobody recorded, so nothing went in.";
        }
    }
}
