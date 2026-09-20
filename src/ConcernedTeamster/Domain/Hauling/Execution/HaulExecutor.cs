using System;
using TheConcernedCat.ConcernedTeamster.Domain.Workers;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;

/// <summary>Everything the executor acts through. A bundle, so one executor per
/// world load can be built from the same ports.</summary>
internal sealed class HaulExecutorPorts
{
    public HaulExecutorPorts(
        IPullerBody body,
        ICartHitchSeam seam,
        ICartRoutePlanner planner,
        IHaulMotionMonitor monitor,
        IHaulAuthority authority,
        IHaulClock clock,
        IHaulExecutionLog log,
        IHaulNavigation? navigation = null)
    {
        Navigation = navigation;
        Body = body ?? throw new ArgumentNullException(nameof(body));
        Seam = seam ?? throw new ArgumentNullException(nameof(seam));
        Planner = planner ?? throw new ArgumentNullException(nameof(planner));
        Monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        Authority = authority ?? throw new ArgumentNullException(nameof(authority));
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public IPullerBody Body { get; }

    public ICartHitchSeam Seam { get; }

    public ICartRoutePlanner Planner { get; }

    public IHaulMotionMonitor Monitor { get; }

    public IHaulAuthority Authority { get; }

    public IHaulClock Clock { get; }

    public IHaulExecutionLog Log { get; }

    /// <summary>The navigation calls beyond the C1 seams, or null to work on
    /// the contract interfaces alone.</summary>
    public IHaulNavigation? Navigation { get; }
}

/// <summary>What a stop request asks for. Zero is unspecified.</summary>
internal enum HaulStopIntent
{
    Unspecified = 0,

    /// <summary>Stop where safe and stay hitched, waiting.</summary>
    StopAndWait = 1,

    /// <summary>Stop where safe and detach on suitable ground.</summary>
    DetachAndPark = 2,
}

/// <summary>What releasing the lease did. Zero is unspecified.</summary>
internal enum LeaseReleaseOutcome
{
    Unspecified = 0,
    Released = 1,

    /// <summary>Gunnar is hitched: he stops, detaches on suitable ground and
    /// then releases the lease.</summary>
    Pending = 2,

    NoLease = 3,

    /// <summary>The ground is not suitable to leave the cart on; nothing was
    /// released and the haul needs attention.</summary>
    RefusedUnsafeParking = 4,
}

/// <summary>What retiring Gunnar's body did. Zero is unspecified.</summary>
internal enum BodyRetirementOutcome
{
    Unspecified = 0,
    Retired = 1,

    /// <summary>A haul holds Gunnar; bodies are retired only while resting.
    /// </summary>
    RefusedBusy = 2,

    /// <summary>A cart's joint still holds the body and would not let go: a
    /// jointed body is never destroyed (review R-313 B1).</summary>
    RefusedStillHitched = 3,
}

/// <summary>Refuses and reports any phase change the contract table does not
/// list (CONTRACTS.md §2.1). The executor never forces a transition.</summary>
internal static class HaulTransitionGuard
{
    public static bool Allows(HaulPhase from, HaulPhase to, IHaulExecutionLog log)
    {
        if (HaulPhases.CanTransition(from, to))
        {
            return true;
        }

        log?.Bug("Illegal haul transition " + from + " -> " + to + " refused.");
        return false;
    }
}

/// <summary>Gunnar's cart lifecycle (CART-01..04, CART-06), executed.
///
/// <b>Shape.</b> Two entry points and a handful of commands. <see cref="ObserveFrame"/>
/// runs every rendered frame and does nothing but read signals and end control
/// the moment it must: authority lost, a peer connected, the body gone, the
/// cart gone, the joint broken or taken, a brake on. <see cref="Tick"/> runs at
/// the worker's 20 Hz and makes progress: approach, align, hitch, pull, recover,
/// stop, wait, detach. The commands are the <see cref="IHaulService"/> semantics
/// of CONTRACTS.md §3.2 plus assigning and releasing the lease.
///
/// <b>Rules it adds to the phase table.</b> Every phase change goes through
/// <see cref="HaulPhases.CanTransition"/> and an illegal one is refused and
/// reported as a bug. Detach comes before everything else on every teardown
/// path, with the identity in Recovering first. A hitched haul reaches Ready or
/// Unassigned only through Detaching. Unloading moves nothing. A vanished joint
/// is never re-hitched within the attempt. Every asynchronous phase has a
/// deadline or a bounded retry, and giving up means stopping and reporting one
/// reason, never escalating.
///
/// <b>Revision</b> increments on every phase, lease, reason or haul change and
/// never on a position update.</summary>
internal sealed class HaulExecutor
{
    private const float ApproachArrivalRadiusMetres = 0.25f;

    /// <summary>Within this (flat) of the standing point Gunnar is steered
    /// straight at it rather than pathfound.</summary>
    private const float ApproachFineRadiusMetres = 1.5f;

    private readonly HaulExecutorPorts _ports;
    private readonly WorkerKey _worker;
    private readonly HaulLimits _limits;
    private readonly HaulExecutionLimits _execution;
    private readonly CartStillnessTracker _stillness;
    private readonly AttentionThrottle _attentionLog = new AttentionThrottle(30f);
    private readonly AttentionThrottle _releaseLog = new AttentionThrottle(10f);

    private BoundedRetry _approachRetry;
    private BoundedRetry _hitchRetry;
    private BoundedRetry _recoveryRetry;
    private PhaseDeadline _deadline;
    private float _noPathSince = float.NaN;
    private float _massWaitStartedAt = float.NaN;
    private float _recoveryRetryAt;
    private float _detachReleasedAt = float.NaN;
    private float _lastWorkerTickAt = float.NaN;
    private float _unloadTouchedAt = float.NaN;
    private float _hitchReadySince = float.NaN;
    private bool _legArrived;
    private bool _cartMeasured;
    private bool _releaseLeaseAfterDetach;

    /// <summary>The cart Gunnar's joint is on, set at a verified attach and
    /// cleared only when a release ends it (review R-313 M1). It outlives the
    /// lease on purpose: the joint is released through this, never through the
    /// lease.</summary>
    private CartKey? _attachedCart;

    /// <summary>The last frame whose signals said the joint was Gunnar's, for
    /// attributing a vanished joint from vanilla's own evidence.</summary>
    private CartObservation? _lastHeld;

    /// <summary>The §2.7 hold: control ended for this reason and Gunnar is
    /// holding the cart until it can be left safely. Unspecified when not
    /// holding.</summary>
    private HaulAttentionReason _holdReason;

    private string _holdDetail = string.Empty;
    private HaulStopIntent _pendingStop;
    private HaulAttentionReason _pendingAttention;
    private string _pendingAttentionDetail = string.Empty;

    public HaulExecutor(
        HaulExecutorPorts ports,
        WorkerKey worker,
        Guid worldLoadEpoch,
        int startingRevision,
        HaulLimits limits,
        HaulExecutionLimits execution,
        IWorkerIdentityAuthority identityAuthority)
    {
        _ports = ports ?? throw new ArgumentNullException(nameof(ports));
        if (worker.IsEmpty)
        {
            throw new ArgumentException("An executor needs a worker.", nameof(worker));
        }

        _worker = worker;
        _limits = (limits ?? throw new ArgumentNullException(nameof(limits))).Validate();
        _execution = (execution ?? throw new ArgumentNullException(nameof(execution))).Validate();
        Leases = new CartLeaseBook(worldLoadEpoch);
        Modes = new WorkerIdentityHold(worker, identityAuthority);
        Revision = Math.Max(0, startingRevision);
        _stillness = new CartStillnessTracker(_limits);
        _approachRetry = NewRecoveryRetry();
        _hitchRetry = NewHitchRetry();
        _recoveryRetry = NewRecoveryRetry();
    }

    /// <summary>One lease book per world load (DECISIONS.md D6).</summary>
    public CartLeaseBook Leases { get; }

    /// <summary>Gunnar's identity, and what it is doing. The hold behind it
    /// belongs to the shared runtime's arbiter, not to this product
    /// (<see cref="WorkerIdentityHold"/>), so a haul and a collection round
    /// cannot both have him.</summary>
    public WorkerIdentityHold Modes { get; }

    public WorkerKey Worker => _worker;

    public Guid WorldLoadEpoch => Leases.WorldLoadEpoch;

    public HaulPhase Phase { get; private set; } = HaulPhase.Unassigned;

    public int Revision { get; private set; }

    /// <summary>The one reason, while Paused or NeedsAttention.</summary>
    public HaulAttentionReason Attention { get; private set; }

    /// <summary>The exact failed condition behind <see cref="Attention"/>.
    /// </summary>
    public string AttentionDetail { get; private set; } = string.Empty;

    /// <summary>Empty when no haul is active.</summary>
    public string HaulId { get; private set; } = string.Empty;

    public HaulLegRequest? Leg { get; private set; }

    public CartRoutePlan? Plan { get; private set; }

    /// <summary>Gunnar believes he holds the joint: verified at attach and
    /// confirmed by every later frame's signals. True for exactly as long as
    /// <see cref="AttachedCart"/> names a cart, whatever the lease says.
    /// </summary>
    public bool Attached => _attachedCart.HasValue;

    /// <summary>The cart the joint is on, or null when Gunnar holds nothing.
    /// </summary>
    public CartKey? AttachedCart => _attachedCart;

    /// <summary>A release did not take (the cart still reports the joint, or it
    /// answered "not ours" for a joint the last frame said was Gunnar's): it is
    /// retried every frame, and nothing retires the body meanwhile (review
    /// R-313 M1).</summary>
    public bool StillHolding { get; private set; }

    /// <summary>Gunnar stopped because authority was lost or a peer connected,
    /// and is holding the cart until the ground under it is parkable
    /// (CONTRACTS.md §2.7). Unspecified when he is not.</summary>
    public HaulAttentionReason HoldingCartBecause => _holdReason;

    public bool HoldingCart => _holdReason != HaulAttentionReason.Unspecified;

    /// <summary>Why the §2.7 hold started, in words, for the status line.
    /// </summary>
    public string HoldingCartDetail => _holdDetail;

    public HaulStopIntent PendingStop => _pendingStop;

    public HitchRefusal LastHitchRefusal { get; private set; }

    public string LastHitchDetail { get; private set; } = string.Empty;

    public int HitchFailures => _hitchRetry.Failures;

    public int RecoveryFailures => _recoveryRetry.Failures;

    public HaulCommandDetail LastCommandDetail { get; private set; }

    public PullerBodyFacts LastBody { get; private set; }

    public CartObservation LastCart { get; private set; }

    public CartLease? ActiveLease =>
        Leases.TryGetActiveForWorker(_worker, out CartLease? lease) ? lease : null;

    /// <summary>The seam exists and Gunnar's body can work now.</summary>
    public bool WorkerAvailable
    {
        get
        {
            if (!_ports.Seam.IsAvailable)
            {
                return false;
            }

            PullerBodyFacts body = _ports.Body.Read();
            return body.Present && !body.Faulted && !body.Dead;
        }
    }

    public bool CartStill => _stillness.IsStill(_ports.Clock.Now);

    public HaulSnapshot Snapshot
    {
        get
        {
            bool still = CartStill;
            CartObservation cart = LastCart;
            bool upright = cart.Resolved && cart.UpDot >= _limits.MinUprightDot;
            bool arrived = Phase == HaulPhase.Waiting && _legArrived && still;
            WorkPoint? cartPosition = cart.Resolved ? cart.CartPosition : (WorkPoint?)null;
            WorkPoint? workerPosition = LastBody.Present ? LastBody.Position : (WorkPoint?)null;
            return new HaulSnapshot(HaulId, Phase, Revision, Attention, Attached, still, upright, arrived, cartPosition, workerPosition);
        }
    }

    // ------------------------------------------------------------------
    // Lease commands
    // ------------------------------------------------------------------

    /// <summary>Assigns the player's explicitly selected and confirmed cart to
    /// Gunnar (CART-01): validates, then lets the lease book decide one lease
    /// per worker and per cart.</summary>
    public AssignmentVerdict AssignCart(string leaseId, CartKey cart, CartAssignmentFacts facts)
    {
        if (string.IsNullOrEmpty(leaseId))
        {
            throw new ArgumentException("A lease id is required.", nameof(leaseId));
        }

        WorkAuthorityVerdict authority = _ports.Authority.Evaluate();
        CartKey? key = cart.IsEmpty ? (CartKey?)null : cart;
        AssignmentVerdict verdict = CartAssignmentValidator.Evaluate(
            authority, WorkerAvailable, facts, key, Leases, _worker, _limits, _execution);
        if (verdict.Outcome != AssignmentOutcome.Assigned)
        {
            return verdict;
        }

        if (Attached || HaulId.Length > 0)
        {
            return AssignmentVerdict.Refuse(CartAssignmentRefusal.WorkerBusy, "Gunnar is still busy with the previous haul");
        }

        AssignmentVerdict fromBook = CartAssignmentValidator.FromLeaseOutcome(Leases.Assign(leaseId, _worker, cart));
        if (fromBook.Outcome != AssignmentOutcome.Assigned)
        {
            return fromBook;
        }

        if (Phase == HaulPhase.NeedsAttention)
        {
            // An old attention from a lease that already ended; the new lease
            // replaces it.
            Transition(HaulPhase.Unassigned);
        }

        if (Phase == HaulPhase.Unassigned)
        {
            Transition(HaulPhase.Ready);
        }
        else
        {
            Revision++;
        }

        _ports.Log.Info("Cart " + cart + " assigned to Gunnar under lease " + leaseId + ".");
        MeasureLeasedCart(cart);
        return fromBook;
    }

    /// <summary>The player takes the cart back. Hitched: Gunnar stops, parks on
    /// suitable ground, detaches, and only then the lease ends.</summary>
    public LeaseReleaseOutcome ReleaseLease()
    {
        CartLease? lease = ActiveLease;
        if (lease == null)
        {
            return LeaseReleaseOutcome.NoLease;
        }

        if (Attached)
        {
            _releaseLeaseAfterDetach = true;
            RequestStop(HaulStopIntent.DetachAndPark);
            if (Phase == HaulPhase.NeedsAttention && Attention == HaulAttentionReason.UnsafeParking)
            {
                _releaseLeaseAfterDetach = false;
                return LeaseReleaseOutcome.RefusedUnsafeParking;
            }

            return Phase == HaulPhase.Unassigned ? LeaseReleaseOutcome.Released : LeaseReleaseOutcome.Pending;
        }

        _ports.Body.Stop();
        Leases.Release(lease.LeaseId);
        OnLeaseEnded();
        _ports.Log.Info("Lease " + lease.LeaseId + " released by the player.");
        ReturnToUnassigned();
        return LeaseReleaseOutcome.Released;
    }

    // ------------------------------------------------------------------
    // IHaulService commands (CONTRACTS.md §3.2)
    // ------------------------------------------------------------------

    public HaulCommandResult RequestLeg(HaulLegRequest request, int expectedRevision)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        float now = _ports.Clock.Now;
        if (expectedRevision != Revision)
        {
            return Answer(HaulCommandOutcome.Stale, HaulAttentionReason.Unspecified, HaulCommandDetail.StaleRevision);
        }

        WorkAuthorityVerdict authority = _ports.Authority.Evaluate();
        if (authority != WorkAuthorityVerdict.Granted)
        {
            return Answer(
                HaulCommandOutcome.Unavailable,
                authority == WorkAuthorityVerdict.OtherPeersConnected ? HaulAttentionReason.OtherPeersConnected : HaulAttentionReason.AuthorityLost,
                HaulCommandDetail.NoAuthority);
        }

        PullerBodyFacts body = _ports.Body.Read();
        LastBody = body;
        if (!_ports.Seam.IsAvailable || !body.Present || body.Faulted || body.Dead)
        {
            return Answer(
                HaulCommandOutcome.Unavailable,
                !_ports.Seam.IsAvailable ? HaulAttentionReason.HitchFailed : body.Duplicated ? HaulAttentionReason.WorkerBodyDuplicated : HaulAttentionReason.WorkerBodyLost,
                HaulCommandDetail.WorkerUnavailable);
        }

        CartLease? lease = ActiveLease;
        if (lease == null)
        {
            return Answer(HaulCommandOutcome.Rejected, HaulAttentionReason.LeaseInvalidated, HaulCommandDetail.NoLease);
        }

        if (!string.Equals(lease.LeaseId, request.LeaseId, StringComparison.Ordinal))
        {
            return Answer(HaulCommandOutcome.Rejected, HaulAttentionReason.LeaseInvalidated, HaulCommandDetail.LeaseMismatch);
        }

        bool sameHaul = HaulId.Length > 0 && string.Equals(HaulId, request.HaulId, StringComparison.Ordinal);
        bool fromReady = Phase == HaulPhase.Ready && !Attached && (HaulId.Length == 0 || sameHaul);
        bool fromWaiting = Phase == HaulPhase.Waiting && Attached && sameHaul;
        bool identityFree = Modes.JobId == null || Modes.IsHeldBy(request.HaulId);
        if ((!fromReady && !fromWaiting) || !identityFree)
        {
            return Answer(
                HaulCommandOutcome.Rejected,
                HaulAttentionReason.Unspecified,
                HaulPhases.ForbidsMotion(Phase) ? HaulCommandDetail.MotionForbidden : HaulCommandDetail.HaulBusy);
        }

        // Revalidate the lease's cart before any motion leg (CONTRACTS.md §2.2).
        CartObservation cart = _ports.Seam.Observe(lease.Cart);
        LastCart = cart;

        // A hitched Gunnar is judged from the joint first, so the joint is
        // released before anything ends the lease (review R-313 M1).
        if (fromWaiting)
        {
            SignalVerdict signals = JointSignalClassifier.Classify(authority, cart, body, _limits, _lastHeld);
            if (!signals.Healthy)
            {
                if (!IsAlreadyAsked(signals))
                {
                    LoseControl(signals, lease);
                }

                return Answer(HaulCommandOutcome.Rejected, signals.Reason, HaulCommandDetail.HitchUnhealthy);
            }

            _lastHeld = cart;
        }

        if (!cart.CapabilityOk)
        {
            // A seam that failed mid-call knows nothing about the cart; the
            // lease does not end as destroyed (review R-313 m2).
            return Answer(HaulCommandOutcome.Unavailable, HaulAttentionReason.HitchFailed, HaulCommandDetail.WorkerUnavailable);
        }

        if (!cart.Resolved)
        {
            HaulAttentionReason gone = cart.RecordExists ? HaulAttentionReason.CartUnloaded : HaulAttentionReason.CartDestroyed;
            InvalidateLease(lease, cart.RecordExists ? LeaseInvalidation.CartUnloaded : LeaseInvalidation.CartDestroyed);
            if (HaulId.Length > 0)
            {
                ChainToAttention(gone, "the leased cart no longer resolves");
            }
            else
            {
                ReturnToUnassigned();
            }

            return Answer(HaulCommandOutcome.Rejected, gone, HaulCommandDetail.CartUnavailable);
        }

        if ((_ports.Navigation != null && !MeasureLeasedCart(lease.Cart)) || !TryGetFootprint(cart, out CartFootprint footprint))
        {
            return Answer(HaulCommandOutcome.Rejected, HaulAttentionReason.NoRoute, HaulCommandDetail.FootprintUnknown);
        }

        // Hitched, Gunnar holds the handle where he stands; not yet hitched, he
        // will hold it at the approach point. Either way the first turn is
        // predicted from the cart's real heading.
        WorkPoint puller = fromWaiting ? body.Position : cart.ApproachPoint;
        CartRoutePlan plan = PlanLeg(
            new CartRouteRequest(cart.CartPosition, request.Target, footprint, cart.ExpectedMassKg, Revision + 1), puller, now);
        if (plan.Verdict == CartRouteVerdict.BudgetExhausted)
        {
            return Answer(HaulCommandOutcome.Unavailable, HaulAttentionReason.NoRoute, HaulCommandDetail.PlannerBudgetExhausted);
        }

        if (!plan.IsSuitable)
        {
            return Answer(HaulCommandOutcome.Rejected, MapRouteVerdict(plan.Verdict), HaulCommandDetail.RouteRefused);
        }

        // Accepted: one haul per Gunnar, all per-leg state fresh.
        HaulId = request.HaulId;
        Leg = request;
        Plan = plan;
        ResetLegState(now);
        if (fromWaiting)
        {
            Transition(HaulPhase.Pulling);
        }
        else
        {
            Transition(HaulPhase.Approaching);
            _deadline = new PhaseDeadline(now, _limits.ApproachTimeoutSeconds);
        }

        _ports.Log.Info(FormattableString.Invariant(
            $"Gunnar haul {HaulId}: leg to {request.Target} accepted ({plan.LengthMetres:0.#} m, steepest {plan.SteepestGradeRatio:0.###})."));
        return Answer(HaulCommandOutcome.Accepted, HaulAttentionReason.Unspecified, HaulCommandDetail.Accepted);
    }

    public HaulCommandResult AcknowledgeWait(string haulId, int expectedRevision, bool transferring)
    {
        float now = _ports.Clock.Now;
        if (expectedRevision != Revision)
        {
            return Answer(HaulCommandOutcome.Stale, HaulAttentionReason.Unspecified, HaulCommandDetail.StaleRevision);
        }

        if (HaulId.Length == 0 || !string.Equals(HaulId, haulId, StringComparison.Ordinal))
        {
            return Answer(HaulCommandOutcome.Rejected, HaulAttentionReason.Unspecified, HaulCommandDetail.UnknownHaul);
        }

        // C4 §3.2: the consumer speaking keeps its hold on the cart alive.
        TouchUnloadHold(haulId);
        if (transferring)
        {
            WorkAuthorityVerdict authority = _ports.Authority.Evaluate();
            if (authority != WorkAuthorityVerdict.Granted)
            {
                return Answer(
                    HaulCommandOutcome.Unavailable,
                    authority == WorkAuthorityVerdict.OtherPeersConnected ? HaulAttentionReason.OtherPeersConnected : HaulAttentionReason.AuthorityLost,
                    HaulCommandDetail.NoAuthority);
            }

            if (Phase != HaulPhase.Waiting || !Attached || !_stillness.IsStill(now))
            {
                return Answer(HaulCommandOutcome.Rejected, HaulAttentionReason.Unspecified, HaulCommandDetail.NotWaitingStill);
            }

            _ports.Body.Stop();
            Transition(HaulPhase.Unloading);

            // The hold starts now and lasts only as long as the consumer keeps
            // saying it is there (C4 §3.2).
            _unloadTouchedAt = now;
            return Answer(HaulCommandOutcome.Accepted, HaulAttentionReason.Unspecified, HaulCommandDetail.Accepted);
        }

        if (Phase != HaulPhase.Unloading)
        {
            return Answer(HaulCommandOutcome.Rejected, HaulAttentionReason.Unspecified, HaulCommandDetail.NotUnloading);
        }

        _unloadTouchedAt = float.NaN;
        Transition(HaulPhase.Waiting);
        HaulStopIntent pending = _pendingStop;
        if (pending != HaulStopIntent.Unspecified)
        {
            // A cancel that arrived during the transfer completes now.
            _pendingStop = HaulStopIntent.Unspecified;
            RequestStop(pending);
        }

        return Answer(HaulCommandOutcome.Accepted, HaulAttentionReason.Unspecified, HaulCommandDetail.Accepted);
    }

    public HaulCommandResult Cancel(string haulId, bool detachAndPark)
    {
        if (HaulId.Length == 0 || !string.Equals(HaulId, haulId, StringComparison.Ordinal))
        {
            return Answer(HaulCommandOutcome.Rejected, HaulAttentionReason.Unspecified, HaulCommandDetail.UnknownHaul);
        }

        HaulStopIntent intent = detachAndPark ? HaulStopIntent.DetachAndPark : HaulStopIntent.StopAndWait;
        if (Phase == HaulPhase.Unloading)
        {
            // Never refused while unloading, but it waits for the consumer's
            // Done, or for the hold's own deadline (C4 §3.2): the cart must not
            // move out from under a transfer.
            TouchUnloadHold(haulId);
            _pendingStop = intent;
            return Answer(HaulCommandOutcome.Accepted, HaulAttentionReason.Unspecified, HaulCommandDetail.Pending);
        }

        if (intent == HaulStopIntent.StopAndWait && IsStoppedPhase(Phase))
        {
            // C4 §3.2: a haul that has already stopped is asked to stop. Nothing
            // to do: no transition, no pending intent, and the reason stands.
            return Answer(HaulCommandOutcome.Accepted, HaulAttentionReason.Unspecified, HaulCommandDetail.Accepted);
        }

        RequestStop(intent);
        return Answer(HaulCommandOutcome.Accepted, HaulAttentionReason.Unspecified, HaulCommandDetail.Accepted);
    }

    // ------------------------------------------------------------------
    // Teardown
    // ------------------------------------------------------------------

    /// <summary>Releases control before the world, the plugin or the runtime
    /// goes away (DECISIONS.md D4): motor stopped, identity in Recovering,
    /// joint released, and only then the lease ends. The body is never retired
    /// here.</summary>
    public void Teardown(string why, LeaseInvalidation invalidation)
    {
        if (invalidation == LeaseInvalidation.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(invalidation), "A teardown needs a reason.");
        }

        CartLease? lease = ActiveLease;
        try
        {
            _ports.Body.Stop();
            if (HaulId.Length > 0)
            {
                EnterIdentity(ActorMode.Recovering, HaulId);
            }
        }
        finally
        {
            // The release happens even when stopping the motor or the identity
            // threw (review R-313 m9), and through the attached cart rather than
            // the lease (M1).
            EndHold();
            ReleaseAttached("before teardown: " + why);
        }

        _pendingStop = HaulStopIntent.Unspecified;
        _releaseLeaseAfterDetach = false;
        if (lease != null)
        {
            InvalidateLease(lease, invalidation);
        }

        if (HaulId.Length > 0)
        {
            ChainToAttention(HaulAttentionReason.LeaseInvalidated, why);
        }
        else if (Phase != HaulPhase.Unassigned)
        {
            ReturnToUnassigned();
        }

        ReleaseIdentityIfIdle();
    }

    /// <summary>Retires Gunnar's body. Only while no haul holds him, and the
    /// joint (should one exist at all) is released first, always. A joint that
    /// will not let go refuses the retirement: a body a cart is jointed to is
    /// never destroyed (review R-313 B1).</summary>
    public BodyRetirementOutcome RetireBody()
    {
        if (HaulId.Length > 0 || !Modes.MayRetireBody)
        {
            return BodyRetirementOutcome.RefusedBusy;
        }

        CartLease? lease = ActiveLease;
        try
        {
            _ports.Body.Stop();
        }
        finally
        {
            EndHold();
            ReleaseAttached("retiring Gunnar's body");
        }

        if (Attached)
        {
            _ports.Log.Warning("Gunnar's body was not retired: a cart still holds his joint.");
            return BodyRetirementOutcome.RefusedStillHitched;
        }

        if (lease != null)
        {
            InvalidateLease(lease, LeaseInvalidation.WorkerBodyLost);
            ReturnToUnassigned();
        }

        _ports.Body.Retire();
        _ports.Log.Info("Gunnar's body was retired.");
        return BodyRetirementOutcome.Retired;
    }

    // ------------------------------------------------------------------
    // Per-frame signals
    // ------------------------------------------------------------------

    /// <summary>Every rendered frame: reads the signals and ends control the
    /// moment it must. Makes no progress of its own.</summary>
    public void ObserveFrame()
    {
        float now = _ports.Clock.Now;
        CartLease? lease = ActiveLease;
        if (lease == null && !Attached && HaulId.Length == 0 && Phase != HaulPhase.NeedsAttention)
        {
            return;
        }

        WorkAuthorityVerdict authority = _ports.Authority.Evaluate();
        PullerBodyFacts body = EffectiveBody(now);

        // The cart watched is the lease's, or the one Gunnar is still holding:
        // a joint outlives its lease and is released through its own key.
        CartObservation cart = ReadCart(lease != null ? lease.Cart : _attachedCart, now);

        if (Attached)
        {
            if (StillHolding)
            {
                // A release that did not take is retried every frame until it
                // does (review R-313 M1); nothing else progresses meanwhile.
                StopIfMoving(body);
                ReleaseAttached("retrying a release that did not take");
                return;
            }

            if (HoldingCart)
            {
                TickHold(now, body, cart, lease);
                return;
            }

            SignalVerdict verdict = lease == null
                ? SignalVerdict.End(HaulAttentionReason.LeaseInvalidated, true, LeaseInvalidation.Unspecified, "the lease ended while hitched")
                : JointSignalClassifier.Classify(authority, cart, body, _limits, _lastHeld);
            if (verdict.Healthy)
            {
                _lastHeld = cart;

                // Unloading holds the cart hitched, so the hold's own deadline
                // is watched from here (C4 §3.2).
                if (Phase == HaulPhase.Unloading && ConsumerWentQuiet(now))
                {
                    EndUnloadHold(now);
                }
            }
            else if (!IsAlreadyAsked(verdict))
            {
                LoseControl(verdict, lease);
            }

            return;
        }

        // A detach that has started always completes (C4 §7): the joint is
        // already gone and only the settle window is left, so nothing here
        // interrupts it.
        bool detaching = Phase == HaulPhase.Detaching;
        if (!detaching && authority == WorkAuthorityVerdict.OtherPeersConnected)
        {
            if (HaulId.Length > 0 && Phase != HaulPhase.Paused && Phase != HaulPhase.NeedsAttention)
            {
                _ports.Body.Stop();
                ChainToPaused(HaulAttentionReason.OtherPeersConnected, "a peer connected");
            }

            return;
        }

        if (!detaching && authority != WorkAuthorityVerdict.Granted)
        {
            if (lease != null)
            {
                _ports.Body.Stop();
                InvalidateLease(lease, LeaseInvalidation.AuthorityLost);
                if (HaulId.Length > 0)
                {
                    ChainToAttention(HaulAttentionReason.AuthorityLost, "work authority is " + authority);
                }
                else
                {
                    ReturnToUnassigned();
                }
            }

            return;
        }

        if (!detaching && Phase == HaulPhase.Paused && Attention == HaulAttentionReason.OtherPeersConnected)
        {
            // Nobody else is connected any more: the haul is held, ready for
            // its next leg, but nothing moves until one is requested.
            Transition(HaulPhase.Ready);
        }

        if (lease != null && !cart.CapabilityOk)
        {
            // The seam itself failed: nothing is known about the cart, so its
            // lease does not end as destroyed or unloaded (review R-313 m2).
            _ports.Body.Stop();
            if (HaulId.Length > 0)
            {
                ChainToAttention(HaulAttentionReason.HitchFailed, "the cart seam stopped working; nothing about the cart could be read");
            }

            return;
        }

        if (lease != null && !cart.Resolved)
        {
            _ports.Body.Stop();
            HaulAttentionReason gone = cart.RecordExists ? HaulAttentionReason.CartUnloaded : HaulAttentionReason.CartDestroyed;
            InvalidateLease(lease, cart.RecordExists ? LeaseInvalidation.CartUnloaded : LeaseInvalidation.CartDestroyed);
            if (HaulId.Length > 0)
            {
                ChainToAttention(gone, "the leased cart no longer resolves");
            }
            else
            {
                ReturnToUnassigned();
            }

            return;
        }

        if (lease != null && HaulId.Length > 0 && IsProgressPhase(Phase) && !IsWorking(body))
        {
            // Nothing keeps walking without a body to command (review R-313 B1).
            _ports.Body.Stop();
            InvalidateLease(lease, LeaseInvalidation.WorkerBodyLost);
            ChainToAttention(
                body.Duplicated ? HaulAttentionReason.WorkerBodyDuplicated : HaulAttentionReason.WorkerBodyLost,
                body.Duplicated ? "more than one body carries Gunnar's identity" : "Gunnar's body is not working");
            return;
        }

        if (detaching && Phase == HaulPhase.Detaching && lease != null)
        {
            // The settle is checked every frame, together with the body and
            // lease checks, so it cannot stall when the worker tick stops
            // (review R-313 m1, C4 §7).
            TickDetaching(now, cart, lease);
            return;
        }

        if (Phase == HaulPhase.Unloading && ConsumerWentQuiet(now))
        {
            EndUnloadHold(now);
        }
    }

    private bool ConsumerWentQuiet(float now) =>
        !float.IsNaN(_unloadTouchedAt) && now - _unloadTouchedAt >= _limits.RendezvousTimeoutSeconds;

    /// <summary>C4 §3.2: the consumer stopped saying it is there, so the hold on
    /// the cart ends here rather than lasting forever. A cancel that arrived
    /// during the transfer completes now.</summary>
    private void EndUnloadHold(float now)
    {
        _unloadTouchedAt = float.NaN;
        HaulStopIntent pending = _pendingStop;
        _pendingStop = HaulStopIntent.Unspecified;
        _ports.Body.Stop();
        ChainToAttention(
            HaulAttentionReason.RendezvousTimedOut,
            FormattableString.Invariant(
                $"nothing asked about the hold for {_limits.RendezvousTimeoutSeconds:0} s while the cart was held for a transfer"));
        if (pending != HaulStopIntent.Unspecified)
        {
            RequestStop(pending);
        }
    }

    /// <summary>C4 §3.2 hold liveness: the cooperating consumer says it is still
    /// there. Every <c>getHaul</c>, <c>acknowledgeWait</c> and <c>cancelHaul</c>
    /// naming this haul keeps an Unloading hold alive.</summary>
    public bool TouchUnloadHold(string haulId)
    {
        if (Phase != HaulPhase.Unloading || HaulId.Length == 0 ||
            !string.Equals(HaulId, haulId, StringComparison.Ordinal))
        {
            return false;
        }

        _unloadTouchedAt = _ports.Clock.Now;
        return true;
    }

    // ------------------------------------------------------------------
    // 20 Hz progress
    // ------------------------------------------------------------------

    /// <summary>Gunnar's worker tick (20 Hz): progress in the current phase.
    /// </summary>
    public void Tick()
    {
        float now = _ports.Clock.Now;
        _lastWorkerTickAt = now;
        CartLease? lease = ActiveLease;
        if (lease == null)
        {
            // No lease is no licence to keep walking (review R-313 B1). Any
            // joint still held is the frame observer's business, every frame.
            StopIfMoving(EffectiveBody(now));
            return;
        }

        WorkAuthorityVerdict authority = _ports.Authority.Evaluate();
        PullerBodyFacts body = EffectiveBody(now);
        CartObservation cart = ReadCart(lease.Cart, now);

        if (Attached)
        {
            if (StillHolding || HoldingCart)
            {
                // A release that did not take, or the §2.7 hold: the frame
                // observer owns both, and no progress happens under either.
                StopIfMoving(body);
                return;
            }

            SignalVerdict verdict = JointSignalClassifier.Classify(authority, cart, body, _limits, _lastHeld);
            if (verdict.Healthy)
            {
                _lastHeld = cart;
            }
            else if (!IsAlreadyAsked(verdict))
            {
                LoseControl(verdict, lease);
                return;
            }
        }

        switch (Phase)
        {
            case HaulPhase.Approaching:
                TickApproaching(now, authority, body, cart);
                break;
            case HaulPhase.Hitching:
                TickHitching(now, authority, body, cart, lease);
                break;
            case HaulPhase.Pulling:
                TickPulling(now, authority, body, cart);
                break;
            case HaulPhase.Recovering:
                TickRecovering(now, authority, body, cart);
                break;
            case HaulPhase.Stopping:
                TickStopping(now, body, cart, lease);
                break;
            case HaulPhase.Waiting:
            case HaulPhase.Unloading:
                StopIfMoving(body);
                break;
            case HaulPhase.Detaching:
                TickDetaching(now, cart, lease);
                break;
            default:
                // Ready, Paused, NeedsAttention, Unassigned: nothing moves.
                StopIfMoving(body);
                break;
        }
    }

    private void TickApproaching(float now, WorkAuthorityVerdict authority, PullerBodyFacts body, CartObservation cart)
    {
        if (authority != WorkAuthorityVerdict.Granted || !cart.Resolved || !IsWorking(body))
        {
            StopIfMoving(body);
            return;
        }

        if (_deadline.IsExpired(now))
        {
            _ports.Body.Stop();
            ChainToAttention(
                HaulAttentionReason.ApproachTimedOut,
                FormattableString.Invariant($"Gunnar did not reach the handle within {_limits.ApproachTimeoutSeconds:0} s"));
            return;
        }

        if (_approachRetry.IsWaiting(now))
        {
            StopIfMoving(body);
            return;
        }

        float reach = _limits.HitchReachFraction * cart.DetachDistanceMetres;
        if (cart.DetachDistanceMetres > 0f && cart.HitchDistanceMetres <= reach)
        {
            _noPathSince = float.NaN;
            StopIfMoving(body);
            if (HeadingErrorDegrees(body, cart) <= _execution.AlignHeadingToleranceDegrees)
            {
                if (Transition(HaulPhase.Hitching))
                {
                    TickHitching(now, authority, body, cart, ActiveLease);
                }

                return;
            }

            _ports.Body.Face(cart.HeadingX, cart.HeadingZ);
            return;
        }

        bool nearStandingPoint = body.Position.HorizontalDistanceTo(cart.ApproachPoint) <= ApproachFineRadiusMetres;
        if (!nearStandingPoint && body.MotorCommanded && !body.HasPath)
        {
            if (float.IsNaN(_noPathSince))
            {
                _noPathSince = now;
            }
            else if (now - _noPathSince >= _execution.NoPathGraceSeconds)
            {
                _noPathSince = float.NaN;
                _ports.Body.Stop();
                RetryDecision decision = _approachRetry.RecordFailure(now);
                if (decision.GiveUp)
                {
                    ChainToAttention(HaulAttentionReason.NoRoute, "Gunnar found no path to the cart's handle");
                }

                return;
            }
        }
        else
        {
            _noPathSince = float.NaN;
        }

        // The pathfinder stops a walker up to half a metre short, and the hitch
        // needs him within 60 % of the cart's own detach distance of the handle
        // (0.6 m for a detach distance of 1 m): the last stretch is steered
        // straight at the standing point, re-checked every tick.
        if (nearStandingPoint)
        {
            _ports.Body.SteerToward(cart.ApproachPoint);
            return;
        }

        _ports.Body.WalkTo(cart.ApproachPoint, ApproachArrivalRadiusMetres);
    }

    private void TickHitching(float now, WorkAuthorityVerdict authority, PullerBodyFacts body, CartObservation cart, CartLease? lease)
    {
        if (lease == null || _hitchRetry.IsWaiting(now))
        {
            return;
        }

        StopIfMoving(body);

        // CART-03 on every attempt, not only at the handover from Approaching
        // (review R-313 m6): Gunnar faces the way the cart points, and the cart
        // itself is at rest. Neither spends an attempt until the wait runs out.
        string? notReady = HitchReadiness(body, cart, out bool facing);
        if (notReady != null)
        {
            if (float.IsNaN(_hitchReadySince))
            {
                _hitchReadySince = now;
            }

            if (now - _hitchReadySince < _execution.StoppingTimeoutSeconds)
            {
                if (!facing)
                {
                    _ports.Body.Face(cart.HeadingX, cart.HeadingZ);
                }

                return;
            }

            _hitchReadySince = float.NaN;
            HandleHitchRefusal(now, facing ? HitchRefusal.InUse : HitchRefusal.OutOfReach, notReady, cart, lease);
            return;
        }

        _hitchReadySince = float.NaN;
        HitchVerdict verdict = HitchPreconditions.Evaluate(
            _ports.Seam.IsAvailable, authority, lease.IsActive, cart, body, _limits, _execution);
        if (!verdict.Allowed)
        {
            HandleHitchRefusal(now, verdict.Refusal, verdict.Detail, cart, lease);
            return;
        }

        _massWaitStartedAt = float.NaN;
        AttachResult result = _ports.Seam.AttachAndVerify(lease.Cart);
        if (!result.Attached)
        {
            HandleHitchRefusal(now, result.Refusal, result.Detail, cart, lease);
            return;
        }

        _attachedCart = lease.Cart;
        StillHolding = false;

        // The joint is one frame old; what it looked like under load is what
        // the next healthy frame records.
        _lastHeld = null;
        LastHitchRefusal = HitchRefusal.Unspecified;
        LastHitchDetail = string.Empty;
        _hitchRetry.Reset();
        _ports.Monitor.Reset();
        _ports.Log.Info("Gunnar hitched to cart " + lease.Cart + " (joint verified).");
        Transition(HaulPhase.Pulling);
    }

    private void HandleHitchRefusal(float now, HitchRefusal refusal, string detail, CartObservation cart, CartLease lease)
    {
        LastHitchRefusal = refusal;
        LastHitchDetail = detail;
        switch (refusal)
        {
            case HitchRefusal.SeamUnavailable:
                ChainToAttention(HaulAttentionReason.HitchFailed, detail);
                return;
            case HitchRefusal.NoAuthority:
                // ObserveFrame ends control with the precise reason.
                return;
            case HitchRefusal.LeaseNotActive:
                ChainToAttention(HaulAttentionReason.LeaseInvalidated, detail);
                return;
            case HitchRefusal.CartGone:
                if (!cart.Resolved)
                {
                    InvalidateLease(lease, cart.RecordExists ? LeaseInvalidation.CartUnloaded : LeaseInvalidation.CartDestroyed);
                    ChainToAttention(cart.RecordExists ? HaulAttentionReason.CartUnloaded : HaulAttentionReason.CartDestroyed, detail);
                }
                else
                {
                    InvalidateLease(lease, LeaseInvalidation.CartDestroyed);
                    ChainToAttention(HaulAttentionReason.HitchFailed, detail);
                }

                return;
            case HitchRefusal.NotOwnedHere:
                InvalidateLease(lease, LeaseInvalidation.OwnershipLost);
                ChainToAttention(HaulAttentionReason.OwnershipLost, detail);
                return;
            case HitchRefusal.Braked:
                ChainToAttention(HaulAttentionReason.BrakeEngaged, detail);
                return;
            case HitchRefusal.NotUpright:
                ChainToAttention(HaulAttentionReason.CartTipped, detail);
                return;
            case HitchRefusal.MassNotCurrent:
                if (float.IsNaN(_massWaitStartedAt))
                {
                    _massWaitStartedAt = now;
                }

                if (now - _massWaitStartedAt < _limits.MassSettleSeconds)
                {
                    // Vanilla refreshes an owned cart's mass every 5 s: wait,
                    // without spending an attempt.
                    return;
                }

                _massWaitStartedAt = float.NaN;
                break;
            case HitchRefusal.PullerBodyInvalid:
                _ports.Body.Calibrate();
                break;
        }

        RetryDecision decision = _hitchRetry.RecordFailure(now);
        if (decision.GiveUp)
        {
            ChainToAttention(
                HaulAttentionReason.HitchFailed,
                FormattableString.Invariant($"{decision.Failures} hitch attempts refused; last: {refusal} ({detail})"));
            return;
        }

        if (refusal == HitchRefusal.OutOfReach)
        {
            Transition(HaulPhase.Approaching);
            _deadline = new PhaseDeadline(now, _limits.ApproachTimeoutSeconds);
        }
    }

    private void TickPulling(float now, WorkAuthorityVerdict authority, PullerBodyFacts body, CartObservation cart)
    {
        if (authority != WorkAuthorityVerdict.Granted || !Attached)
        {
            _ports.Body.Stop();
            return;
        }

        if (Plan == null || Leg == null)
        {
            _ports.Log.Bug("Pulling without a plan or a leg.");
            StopThenAttend(HaulAttentionReason.NoRoute, "no plan");
            return;
        }

        _ports.Monitor.Sample(now, body.Position, cart.CartPosition, body.MotorCommanded);
        if (cart.BreakForceNewtons > 0f && cart.JointForceNewtons >= _execution.JointStrainRatio * cart.BreakForceNewtons)
        {
            RememberStall(body, cart, now);
            BeginRecovery(now, FormattableString.Invariant(
                $"the hitch strained to {cart.JointForceNewtons:0} N of {cart.BreakForceNewtons:0} N"));
            return;
        }

        HaulMotion motion = _ports.Monitor.Current;
        if (motion == HaulMotion.Stalled || motion == HaulMotion.Wedged)
        {
            RememberStall(body, cart, now);
            BeginRecovery(now, "motion judged " + motion);
            return;
        }

        // The running plan is checked against the world again at the planner's
        // own interval; a plan that no longer holds has ended.
        IHaulNavigation? navigation = _ports.Navigation;
        if (navigation != null && navigation.NeedsRefresh(Plan, now))
        {
            CartRoutePlan refreshed = navigation.Refresh(Plan, body.Position, cart.CartPosition, now);
            if (!refreshed.IsSuitable)
            {
                BeginRecovery(now, "the route no longer holds (" + refreshed.Verdict + ")");
                return;
            }

            Plan = refreshed;
        }

        HaulSteering steering = Steer(Plan, body.Position, cart.CartPosition);
        if (steering.Status == HaulSteeringStatus.Finished)
        {
            Arrive(now);
            return;
        }

        if (steering.Goal == null)
        {
            BeginRecovery(
                now,
                steering.Status == HaulSteeringStatus.LeftCorridor
                    ? "Gunnar or the cart left the corridor the route verified"
                    : "the route is no longer valid from here");
            return;
        }

        SteeringGoal goal = steering.Goal.Value;
        if (goal.IsFinalStop && body.Position.HorizontalDistanceTo(goal.Target) <= Leg.ArrivalRadiusMetres)
        {
            Arrive(now);
            return;
        }

        _ports.Body.SteerToward(goal.Target);
    }

    private void Arrive(float now)
    {
        _legArrived = true;
        _ports.Body.Stop();
        _deadline = new PhaseDeadline(now, _execution.StoppingTimeoutSeconds);
        Transition(HaulPhase.Stopping);
    }

    private void RememberStall(PullerBodyFacts body, CartObservation cart, float now)
    {
        if (_ports.Navigation != null && Leg != null && body.Position.IsFinite && cart.CartPosition.IsFinite)
        {
            _ports.Navigation.RememberStall(Leg.Target, cart.CartPosition, body.Position, now);
        }
    }

    private HaulSteering Steer(CartRoutePlan plan, WorkPoint puller, WorkPoint cart)
    {
        if (_ports.Navigation != null)
        {
            return _ports.Navigation.Steer(plan, puller, cart);
        }

        SteeringGoal? goal = _ports.Planner.NextGoal(plan, puller, cart);
        return new HaulSteering(goal == null ? HaulSteeringStatus.LeftCorridor : HaulSteeringStatus.Following, goal);
    }

    private CartRoutePlan PlanLeg(CartRouteRequest request, WorkPoint? puller, float now) =>
        _ports.Navigation != null ? _ports.Navigation.Plan(request, puller, now) : _ports.Planner.Plan(request, now);

    private bool TryGetFootprint(CartObservation cart, out CartFootprint footprint)
    {
        CartFootprint? measured = _ports.Navigation?.Footprint;
        if (measured.HasValue)
        {
            footprint = measured.Value;
            return true;
        }

        return cart.TryGetFootprint(out footprint);
    }

    /// <summary>Measures the leased cart for navigation once per lease. Every
    /// later leg of the same lease keeps what the planner learned about it.
    /// </summary>
    private bool MeasureLeasedCart(CartKey cart)
    {
        if (_ports.Navigation == null || _cartMeasured)
        {
            return true;
        }

        _cartMeasured = _ports.Navigation.UseCart(cart);
        if (!_cartMeasured)
        {
            _ports.Log.Warning("Cart " + cart + " could not be measured for route planning; no leg is planned for it until it can be.");
        }

        return _cartMeasured;
    }

    private void BeginRecovery(float now, string why)
    {
        _ports.Body.Stop();
        RetryDecision decision = _recoveryRetry.RecordFailure(now);
        if (decision.GiveUp)
        {
            StopThenAttend(
                HaulAttentionReason.Wedged,
                FormattableString.Invariant($"no progress after {decision.Failures - 1} recoveries; last: {why}"));
            return;
        }

        _recoveryRetryAt = decision.RetryAt;
        _ports.Monitor.Reset();
        _ports.Log.Info("Gunnar haul " + HaulId + ": recovering (" + why + ").");
        Transition(HaulPhase.Recovering);
    }

    private void TickRecovering(float now, WorkAuthorityVerdict authority, PullerBodyFacts body, CartObservation cart)
    {
        StopIfMoving(body);
        if (authority != WorkAuthorityVerdict.Granted || now < _recoveryRetryAt || Leg == null)
        {
            return;
        }

        // The one manoeuvre verified safe: plan again from where the cart
        // actually stands. Nothing pushes, reverses or repositions anything.
        if (!TryGetFootprint(cart, out CartFootprint footprint))
        {
            StopThenAttend(HaulAttentionReason.NoRoute, "the cart's footprint could not be measured");
            return;
        }

        CartRoutePlan plan = PlanLeg(
            new CartRouteRequest(cart.CartPosition, Leg.Target, footprint, cart.ExpectedMassKg, Revision), body.Position, now);
        if (plan.Verdict == CartRouteVerdict.BudgetExhausted)
        {
            RetryDecision decision = _recoveryRetry.RecordFailure(now);
            if (decision.GiveUp)
            {
                StopThenAttend(HaulAttentionReason.Wedged, "the route planner stayed out of budget while recovering");
            }
            else
            {
                _recoveryRetryAt = decision.RetryAt;
            }

            return;
        }

        if (!plan.IsSuitable)
        {
            StopThenAttend(MapRouteVerdict(plan.Verdict), "the route from here was refused: " + plan.Verdict);
            return;
        }

        Plan = plan;
        _ports.Monitor.Reset();
        Transition(HaulPhase.Pulling);
    }

    private void StopThenAttend(HaulAttentionReason reason, string detail)
    {
        _ports.Body.Stop();
        _pendingAttention = reason;
        _pendingAttentionDetail = detail;
        if (Phase != HaulPhase.Stopping)
        {
            _deadline = new PhaseDeadline(_ports.Clock.Now, _execution.StoppingTimeoutSeconds);
            if (!Transition(HaulPhase.Stopping))
            {
                ChainToAttention(reason, detail);
            }
        }
    }

    private void TickStopping(float now, PullerBodyFacts body, CartObservation cart, CartLease lease)
    {
        StopIfMoving(body);
        if (_stillness.IsStill(now))
        {
            if (_pendingAttention != HaulAttentionReason.Unspecified)
            {
                HaulAttentionReason reason = _pendingAttention;
                string detail = _pendingAttentionDetail;
                _pendingAttention = HaulAttentionReason.Unspecified;
                _pendingAttentionDetail = string.Empty;
                ChainToAttention(reason, detail);
                return;
            }

            if (_pendingStop == HaulStopIntent.DetachAndPark)
            {
                BeginDetaching(now, cart, lease);
                return;
            }

            _pendingStop = HaulStopIntent.Unspecified;
            Transition(HaulPhase.Waiting);
            return;
        }

        if (_deadline.IsExpired(now))
        {
            HaulAttentionReason reason = _pendingAttention != HaulAttentionReason.Unspecified
                ? _pendingAttention
                : HaulAttentionReason.UnsafeParking;
            _pendingAttention = HaulAttentionReason.Unspecified;
            _pendingStop = HaulStopIntent.Unspecified;
            ChainToAttention(reason, "the cart would not come to rest");
        }
    }

    private void BeginDetaching(float now, CartObservation cart, CartLease? lease)
    {
        _pendingStop = HaulStopIntent.Unspecified;

        // Through the joint's own cart, so a detach still works when the lease
        // has already ended (review R-313 M1).
        CartKey? key = _attachedCart ?? (lease != null ? lease.Cart : (CartKey?)null);
        if (!key.HasValue)
        {
            return;
        }

        ParkingGround ground = _ports.Seam.ReadGround(key.Value);
        ParkingDecision parking = ParkingJudge.Evaluate(ground, cart, _stillness.IsStill(now), _limits, _execution);
        if (!parking.Safe)
        {
            _releaseLeaseAfterDetach = false;
            ChainToAttention(HaulAttentionReason.UnsafeParking, parking.Detail);
            return;
        }

        if (!Transition(HaulPhase.Detaching))
        {
            return;
        }

        // Recovering first: nothing may interrupt a detach in progress.
        EnterIdentity(ActorMode.Recovering, HaulId.Length > 0 ? HaulId : lease != null ? lease.LeaseId : "detach-" + key.Value);
        ReleaseResult released = ReleaseAttached("detaching on suitable ground");
        _detachReleasedAt = now;
        _stillness.Reset();
        if (released == ReleaseResult.NotOurs)
        {
            _releaseLeaseAfterDetach = false;
            ChainToAttention(HaulAttentionReason.PlayerTookOver, "the joint belonged to another puller");
        }
        else if (released == ReleaseResult.StillAttached)
        {
            _releaseLeaseAfterDetach = false;
            ChainToAttention(HaulAttentionReason.HitchFailed, "the cart's detach did not release the joint");
        }
    }

    private void TickDetaching(float now, CartObservation cart, CartLease? lease)
    {
        if (float.IsNaN(_detachReleasedAt))
        {
            _detachReleasedAt = now;
        }

        if (now - _detachReleasedAt < _execution.DetachSettleSeconds)
        {
            if (cart.Resolved && !(cart.SpeedMetresPerSecond <= _execution.RollAwaySpeedMetresPerSecond))
            {
                _releaseLeaseAfterDetach = false;
                ChainToAttention(
                    HaulAttentionReason.UnsafeParking,
                    FormattableString.Invariant($"the cart started rolling at {cart.SpeedMetresPerSecond:0.##} m/s after Gunnar let go"));
            }

            return;
        }

        _detachReleasedAt = float.NaN;
        if (!Transition(HaulPhase.Ready))
        {
            return;
        }

        ClearHaul();
        if (_releaseLeaseAfterDetach && lease != null)
        {
            _releaseLeaseAfterDetach = false;
            Leases.Release(lease.LeaseId);
            OnLeaseEnded();
            _ports.Log.Info("Lease " + lease.LeaseId + " released after detaching.");
            ReturnToUnassigned();
        }
    }

    // ------------------------------------------------------------------
    // Stops, pauses and attention
    // ------------------------------------------------------------------

    private void RequestStop(HaulStopIntent intent)
    {
        float now = _ports.Clock.Now;
        CartLease? lease = ActiveLease;
        switch (Phase)
        {
            case HaulPhase.Ready:
                ClearHaul();
                break;

            case HaulPhase.Approaching:
                _ports.Body.Stop();
                Transition(HaulPhase.Ready);
                ClearHaul();
                break;

            case HaulPhase.Hitching:
                // Between attempts no joint exists: Detaching releases nothing.
                _ports.Body.Stop();
                if (Transition(HaulPhase.Detaching) && Transition(HaulPhase.Ready))
                {
                    ClearHaul();
                }

                break;

            case HaulPhase.Pulling:
            case HaulPhase.Recovering:
                _ports.Body.Stop();
                _pendingStop = intent;
                _deadline = new PhaseDeadline(now, _execution.StoppingTimeoutSeconds);
                Transition(HaulPhase.Stopping);
                break;

            case HaulPhase.Stopping:
                _pendingStop = intent;
                break;

            case HaulPhase.Waiting:
                if (intent == HaulStopIntent.DetachAndPark)
                {
                    BeginDetaching(now, ReadCart(WatchedCart(lease), now), lease);
                }

                break;

            case HaulPhase.Unloading:
                _pendingStop = intent;
                break;

            case HaulPhase.Detaching:
                break;

            case HaulPhase.Paused:
            case HaulPhase.NeedsAttention:
                if (Attached)
                {
                    if (HoldingCart)
                    {
                        // CONTRACTS.md §2.7: he is already letting go the moment
                        // the cart can be left; a command makes no ground safer.
                        break;
                    }

                    if (intent == HaulStopIntent.DetachAndPark)
                    {
                        BeginDetaching(now, ReadCart(WatchedCart(lease), now), lease);
                    }
                    else if (Phase == HaulPhase.Paused)
                    {
                        Transition(HaulPhase.Waiting);
                    }
                }
                else
                {
                    _ports.Body.Stop();
                    Transition(lease == null ? HaulPhase.Unassigned : HaulPhase.Ready);
                    ClearHaul();
                }

                break;
        }
    }

    /// <summary>A person has already been asked about a cart Gunnar still holds
    /// (a hard lean, or the §2.7 hold); the same condition next frame changes
    /// nothing, so the reason and the revision stay put.</summary>
    private bool IsAlreadyAsked(SignalVerdict verdict) =>
        verdict.StillHeld && (Phase == HaulPhase.NeedsAttention || (verdict.Pause && Phase == HaulPhase.Paused));

    // ------------------------------------------------------------------
    // The joint (review R-313 M1; CONTRACTS.md §2.5, §2.7)
    // ------------------------------------------------------------------

    /// <summary>Releases any joint Gunnar holds, for a runtime going away on a
    /// path the executor cannot observe. Safe to call when he holds nothing.
    /// </summary>
    public ReleaseResult ReleaseJointNow(string why)
    {
        EndHold();
        return ReleaseAttached(why);
    }

    /// <summary>Releases the joint through the cart it is on, whatever the lease
    /// says. The cart is forgotten only when the release ends the joint
    /// (Released, NoJoint, NoCart, or "not ours" for a joint the last frame did
    /// not call Gunnar's). Anything else latches <see cref="StillHolding"/>:
    /// the release is retried every frame and the body is not retired until it
    /// takes.</summary>
    private ReleaseResult ReleaseAttached(string why)
    {
        CartKey? key = _attachedCart;
        if (!key.HasValue)
        {
            StillHolding = false;
            return ReleaseResult.NoJoint;
        }

        CartKey cart = key.Value;
        ReleaseResult released = _ports.Seam.ReleaseJoint(cart);
        bool lastSaidOurs = LastCart.HasJoint && LastCart.JointConnectedToPuller;
        bool ended =
            released == ReleaseResult.Released ||
            released == ReleaseResult.NoJoint ||
            released == ReleaseResult.NoCart ||
            (released == ReleaseResult.NotOurs && !lastSaidOurs);
        if (ended)
        {
            ForgetAttached();
            _ports.Log.Info("Gunnar let go of cart " + cart + " (" + why + "): " + released + ".");
            return released;
        }

        StillHolding = true;
        if (_releaseLog.ShouldNotify("release:" + released, _ports.Clock.Now))
        {
            _ports.Log.Warning(
                "Gunnar could not let go of cart " + cart + " (" + why + "): " + released +
                ". He keeps trying every frame, and his body is not retired while a cart holds it.");
        }

        return released;
    }

    private void ForgetAttached()
    {
        _attachedCart = null;
        _lastHeld = null;
        StillHolding = false;
    }

    private void EnterHold(SignalVerdict verdict)
    {
        _holdReason = verdict.Reason;
        _holdDetail = verdict.Detail;
    }

    private void EndHold()
    {
        _holdReason = HaulAttentionReason.Unspecified;
        _holdDetail = string.Empty;
    }

    /// <summary>The lost-authority hold (CONTRACTS.md §2.7, C4). Gunnar has
    /// stopped and still holds the cart: every frame the safety checks that do
    /// not depend on authority keep running, and the moment the cart is still,
    /// upright and on parkable ground he lets go. The phase and the reason never
    /// change here, and nothing resumes the haul, even when authority returns.
    /// </summary>
    private void TickHold(float now, PullerBodyFacts body, CartObservation cart, CartLease? lease)
    {
        StopIfMoving(body);
        SignalVerdict verdict = JointSignalClassifier.Classify(
            WorkAuthorityVerdict.Granted, cart, body, _limits, _lastHeld);
        if (!verdict.Healthy)
        {
            if (verdict.StillHeld)
            {
                // A leaning cart, or nothing new: he goes on holding it.
                return;
            }

            // The player took it, the brake went on, the body or the cart went
            // away: that ending replaces the hold, with its own reason.
            EndHold();
            LoseControl(verdict, lease);
            return;
        }

        _lastHeld = cart;
        ParkingDecision parking = ParkingJudge.Evaluate(
            _ports.Seam.ReadGround(_attachedCart!.Value), cart, _stillness.IsStill(now), _limits, _execution);
        if (!parking.Safe)
        {
            if (_attentionLog.ShouldNotify("hold:" + _holdReason, now))
            {
                _ports.Log.Info(
                    "Gunnar is holding cart " + _attachedCart.Value + " where he stopped (" + _holdReason + "): " +
                    parking.Detail + ", so letting go would set it rolling.");
            }

            return;
        }

        HaulAttentionReason why = _holdReason;
        ReleaseAttached("the cart can be left where it stands (" + why + ")");
        if (Attached)
        {
            return;
        }

        EndHold();
        _stillness.Reset();
        if (_releaseLeaseAfterDetach && lease != null)
        {
            _releaseLeaseAfterDetach = false;
            Leases.Release(lease.LeaseId);
            OnLeaseEnded();
            _ports.Log.Info("Lease " + lease.LeaseId + " released once Gunnar could put the cart down.");
        }
    }

    /// <summary>The cart being watched: the lease's, or the one Gunnar still
    /// holds after the lease ended.</summary>
    private CartKey? WatchedCart(CartLease? lease) => lease != null ? lease.Cart : _attachedCart;

    /// <summary>Why this is not a moment to hitch (CART-03), or null when it is.
    /// </summary>
    private string? HitchReadiness(PullerBodyFacts body, CartObservation cart, out bool facing)
    {
        facing = HeadingErrorDegrees(body, cart) <= _execution.AlignHeadingToleranceDegrees;
        if (!cart.Resolved)
        {
            // Nothing is known about the cart; D4 answers with the real reason.
            facing = true;
            return null;
        }

        if (!facing)
        {
            return FormattableString.Invariant(
                $"Gunnar is {HeadingErrorDegrees(body, cart):0} degrees off the way the cart points");
        }

        if (!(cart.SpeedMetresPerSecond <= _limits.StillSpeedMetresPerSecond))
        {
            return FormattableString.Invariant($"the cart is rolling at {cart.SpeedMetresPerSecond:0.##} m/s");
        }

        return null;
    }

    private void LoseControl(SignalVerdict verdict, CartLease? lease)
    {
        try
        {
            _ports.Body.Stop();
            if (HaulId.Length > 0)
            {
                EnterIdentity(ActorMode.Recovering, HaulId);
            }
        }
        finally
        {
            // The joint is dealt with even if stopping the motor or claiming the
            // identity threw (review R-313 m9), and through the cart it is on,
            // never through the lease (M1).
            if (verdict.ReleaseJoint)
            {
                ReleaseAttached(verdict.Reason.ToString());
            }
            else if (verdict.HoldsCart)
            {
                EnterHold(verdict);
            }
            else if (!verdict.StillHeld)
            {
                // The joint is another body's now: Gunnar holds nothing.
                ForgetAttached();
            }
        }

        _pendingStop = HaulStopIntent.Unspecified;
        _pendingAttention = HaulAttentionReason.Unspecified;
        _releaseLeaseAfterDetach = false;
        _unloadTouchedAt = float.NaN;
        if (verdict.InvalidatesLease && lease != null)
        {
            InvalidateLease(lease, verdict.Invalidation);
        }

        if (verdict.Pause)
        {
            ChainToPaused(verdict.Reason, verdict.Detail);
        }
        else
        {
            ChainToAttention(verdict.Reason, verdict.Detail);
        }
    }

    private void ChainToAttention(HaulAttentionReason reason, string detail)
    {
        if (reason == HaulAttentionReason.Unspecified)
        {
            _ports.Log.Bug("Attention without a reason.");
            reason = HaulAttentionReason.HitchFailed;
        }

        if (Phase == HaulPhase.Unassigned)
        {
            return;
        }

        if (Phase == HaulPhase.NeedsAttention)
        {
            if (Attention != reason)
            {
                Attention = reason;
                AttentionDetail = detail ?? string.Empty;
                Revision++;
                ReportAttention();
            }

            return;
        }

        Transition(HaulPhase.NeedsAttention, reason, detail);
    }

    private void ChainToPaused(HaulAttentionReason reason, string detail)
    {
        switch (Phase)
        {
            case HaulPhase.Ready:
            case HaulPhase.Approaching:
            case HaulPhase.Waiting:
            case HaulPhase.Unloading:
                Transition(HaulPhase.Paused, reason, detail);
                break;
            case HaulPhase.Hitching:
                if (Transition(HaulPhase.Approaching))
                {
                    Transition(HaulPhase.Paused, reason, detail);
                }

                break;
            case HaulPhase.Pulling:
            case HaulPhase.Stopping:
            case HaulPhase.Recovering:
                if (Transition(HaulPhase.Detaching) && Transition(HaulPhase.Ready))
                {
                    Transition(HaulPhase.Paused, reason, detail);
                }

                break;
            case HaulPhase.Detaching:
                if (Transition(HaulPhase.Ready))
                {
                    Transition(HaulPhase.Paused, reason, detail);
                }

                break;
            case HaulPhase.Paused:
                if (Attention != reason)
                {
                    Attention = reason;
                    AttentionDetail = detail ?? string.Empty;
                    Revision++;
                }

                break;
            default:
                // NeedsAttention outranks a pause; Unassigned has nothing to hold.
                break;
        }
    }

    private void ReturnToUnassigned()
    {
        switch (Phase)
        {
            case HaulPhase.Unassigned:
                break;
            case HaulPhase.Ready:
            case HaulPhase.NeedsAttention:
                Transition(HaulPhase.Unassigned);
                break;
            case HaulPhase.Approaching:
            case HaulPhase.Paused:
                if (Transition(HaulPhase.Ready))
                {
                    Transition(HaulPhase.Unassigned);
                }

                break;
            case HaulPhase.Hitching:
            case HaulPhase.Detaching:
                if ((Phase == HaulPhase.Detaching || Transition(HaulPhase.Detaching)) && Transition(HaulPhase.Ready))
                {
                    Transition(HaulPhase.Unassigned);
                }

                break;
            default:
                // A hitched phase never skips Detaching; attention first.
                ChainToAttention(HaulAttentionReason.LeaseInvalidated, "the lease ended while a haul was running");
                return;
        }

        ClearHaul();
    }

    // ------------------------------------------------------------------
    // State helpers
    // ------------------------------------------------------------------

    private bool Transition(HaulPhase to, HaulAttentionReason reason = HaulAttentionReason.Unspecified, string detail = "")
    {
        if (!HaulTransitionGuard.Allows(Phase, to, _ports.Log))
        {
            return false;
        }

        HaulPhase from = Phase;
        Phase = to;
        bool holdsReason = to == HaulPhase.NeedsAttention || to == HaulPhase.Paused;
        Attention = holdsReason ? reason : HaulAttentionReason.Unspecified;
        AttentionDetail = holdsReason ? (detail ?? string.Empty) : string.Empty;
        Revision++;
        if (to != HaulPhase.Stopping && to != HaulPhase.Pulling && to != HaulPhase.Recovering)
        {
            _pendingAttention = HaulAttentionReason.Unspecified;
        }

        SyncActorMode();
        if (to == HaulPhase.NeedsAttention)
        {
            ReportAttention();
        }
        else
        {
            _ports.Log.Info("Gunnar haul " + (HaulId.Length > 0 ? HaulId : "-") + ": " + from + " -> " + to +
                (holdsReason ? " (" + reason + ")" : string.Empty) + ".");
        }

        return true;
    }

    private void ReportAttention()
    {
        string key = Attention.ToString();
        if (_attentionLog.ShouldNotify(key, _ports.Clock.Now))
        {
            _ports.Log.Warning("Gunnar needs attention: " + Attention + " - " + AttentionDetail);
        }
    }

    private string _heldIdentityJob = string.Empty;

    private ActorModeOutcome EnterIdentity(ActorMode mode, string job)
    {
        ActorModeOutcome result = Modes.Enter(mode, job);
        if (result == ActorModeOutcome.Entered || result == ActorModeOutcome.AlreadyInMode)
            _heldIdentityJob = job;
        return result;
    }

    private void SyncActorMode()
    {
        if (HaulId.Length == 0)
        {
            ReleaseIdentityIfIdle();
            return;
        }

        ActorMode desired;
        switch (Phase)
        {
            case HaulPhase.Approaching:
            case HaulPhase.Hitching:
            case HaulPhase.Pulling:
            case HaulPhase.Stopping:
            case HaulPhase.Waiting:
            case HaulPhase.Unloading:
                desired = ActorMode.Working;
                break;
            case HaulPhase.Detaching:
            case HaulPhase.Recovering:
                desired = ActorMode.Recovering;
                break;
            default:
                desired = ActorMode.Paused;
                break;
        }

        if (EnterIdentity(desired, HaulId) == ActorModeOutcome.RefusedBusy)
        {
            _ports.Log.Bug("Another job holds Gunnar's identity while haul " + HaulId + " runs.");
        }
    }

    private void ReleaseIdentityIfIdle()
    {
        if (HaulId.Length == 0 && _heldIdentityJob.Length > 0)
        {
            Modes.Release(_heldIdentityJob);
            _heldIdentityJob = string.Empty;
        }
    }

    private void ClearHaul()
    {
        bool hadHaul = HaulId.Length > 0;
        HaulId = string.Empty;
        Leg = null;
        Plan = null;
        _legArrived = false;
        _pendingStop = HaulStopIntent.Unspecified;
        _pendingAttention = HaulAttentionReason.Unspecified;
        ReleaseIdentityIfIdle();
        if (hadHaul)
        {
            Revision++;
        }
    }

    private void InvalidateLease(CartLease lease, LeaseInvalidation reason)
    {
        if (Leases.Invalidate(lease.LeaseId, reason) == LeaseOutcome.Invalidated)
        {
            Revision++;
            OnLeaseEnded();
            _ports.Log.Info("Lease " + lease.LeaseId + " on cart " + lease.Cart + " ended: " + reason + ".");
        }
    }

    /// <summary>Navigation forgets the cart whenever its lease ends, however
    /// it ends.</summary>
    private void OnLeaseEnded()
    {
        _cartMeasured = false;
        _ports.Navigation?.ReleaseCart();
    }

    private void ResetLegState(float now)
    {
        _approachRetry = NewRecoveryRetry();
        _hitchRetry = NewHitchRetry();
        _recoveryRetry = NewRecoveryRetry();
        _noPathSince = float.NaN;
        _massWaitStartedAt = float.NaN;
        _detachReleasedAt = float.NaN;
        _hitchReadySince = float.NaN;
        _recoveryRetryAt = 0f;
        _legArrived = false;
        _pendingStop = HaulStopIntent.Unspecified;
        _pendingAttention = HaulAttentionReason.Unspecified;
        _lastWorkerTickAt = now;
        LastHitchRefusal = HitchRefusal.Unspecified;
        LastHitchDetail = string.Empty;
        _ports.Monitor.Reset();
    }

    private BoundedRetry NewHitchRetry() =>
        new BoundedRetry(_limits.MaxHitchAttempts, _limits.RecoveryBackoffSeconds, _limits.RecoveryBackoffMaxSeconds);

    /// <summary>A recovery budget of N allows N recoveries: the (N+1)th failure
    /// gives up.</summary>
    private BoundedRetry NewRecoveryRetry() =>
        new BoundedRetry(_limits.MaxRecoveryAttempts + 1, _limits.RecoveryBackoffSeconds, _limits.RecoveryBackoffMaxSeconds);

    private HaulCommandResult Answer(HaulCommandOutcome outcome, HaulAttentionReason reason, HaulCommandDetail detail)
    {
        LastCommandDetail = detail;
        string wire = WireDetail(detail);
        return new HaulCommandResult(
            outcome, reason, Revision, string.Equals(wire, reason.ToString(), StringComparison.Ordinal) ? string.Empty : wire);
    }

    /// <summary>The C2 <see cref="HaulCommandResult.Detail"/>: the wire reason name
    /// for a protocol answer no attention reason can say, empty otherwise.
    /// </summary>
    internal static string WireDetail(HaulCommandDetail detail)
    {
        switch (detail)
        {
            case HaulCommandDetail.StaleRevision:
                return "RevisionMismatch";
            case HaulCommandDetail.WorkerUnavailable:
                return "WorkerUnavailable";
            case HaulCommandDetail.NoLease:
                return "NoLease";
            case HaulCommandDetail.HaulBusy:
            case HaulCommandDetail.MotionForbidden:
            case HaulCommandDetail.NotWaitingStill:
            case HaulCommandDetail.NotUnloading:
                return "HaulBusy";
            case HaulCommandDetail.UnknownHaul:
                return "UnknownHaul";
            default:
                return string.Empty;
        }
    }

    private PullerBodyFacts EffectiveBody(float now)
    {
        PullerBodyFacts body = _ports.Body.Read();
        if (HaulId.Length > 0 && IsProgressPhase(Phase) && !float.IsNaN(_lastWorkerTickAt) &&
            now - _lastWorkerTickAt > _execution.WorkerTickStaleSeconds)
        {
            // The worker's own tick has gone quiet: not owned, disabled or
            // faulted without saying so. A body that is not ticking is not
            // working.
            body.Present = false;
        }

        LastBody = body;
        return body;
    }

    private CartObservation ReadCart(CartKey? key, float now)
    {
        if (!key.HasValue)
        {
            _stillness.Reset();
            return default;
        }

        CartObservation cart = _ports.Seam.Observe(key.Value);
        LastCart = cart;
        if (cart.Resolved)
        {
            _stillness.Observe(now, cart.SpeedMetresPerSecond);
        }
        else
        {
            _stillness.Reset();
        }

        return cart;
    }

    private void StopIfMoving(PullerBodyFacts body)
    {
        if (body.MotorCommanded)
        {
            _ports.Body.Stop();
        }
    }

    private static bool IsWorking(PullerBodyFacts body) => body.Present && !body.Faulted && !body.Dead;

    /// <summary>C4 §3.2: a haul that is not moving and holds no intent to move.
    /// </summary>
    private static bool IsStoppedPhase(HaulPhase phase) =>
        phase == HaulPhase.Ready || phase == HaulPhase.Waiting ||
        phase == HaulPhase.Paused || phase == HaulPhase.NeedsAttention;

    private static bool IsProgressPhase(HaulPhase phase)
    {
        switch (phase)
        {
            case HaulPhase.Approaching:
            case HaulPhase.Hitching:
            case HaulPhase.Pulling:
            case HaulPhase.Stopping:
            case HaulPhase.Recovering:
            case HaulPhase.Waiting:
            case HaulPhase.Unloading:
            case HaulPhase.Detaching:
                return true;
            default:
                return false;
        }
    }

    internal static float HeadingErrorDegrees(PullerBodyFacts body, CartObservation cart)
    {
        double forward = Math.Sqrt((body.ForwardX * body.ForwardX) + (body.ForwardZ * body.ForwardZ));
        double heading = Math.Sqrt((cart.HeadingX * cart.HeadingX) + (cart.HeadingZ * cart.HeadingZ));
        if (!(forward > 1e-4) || !(heading > 1e-4))
        {
            return 180f;
        }

        double cosine = ((body.ForwardX * cart.HeadingX) + (body.ForwardZ * cart.HeadingZ)) / (forward * heading);
        cosine = Math.Max(-1d, Math.Min(1d, cosine));
        return (float)(Math.Acos(cosine) * 180d / Math.PI);
    }

    internal static HaulAttentionReason MapRouteVerdict(CartRouteVerdict verdict)
    {
        switch (verdict)
        {
            case CartRouteVerdict.TooSteep:
                return HaulAttentionReason.TooSteep;
            case CartRouteVerdict.TooNarrow:
                return HaulAttentionReason.TooNarrow;
            case CartRouteVerdict.ForbiddenDoor:
                return HaulAttentionReason.ForbiddenDoor;
            case CartRouteVerdict.Water:
                return HaulAttentionReason.Water;
            case CartRouteVerdict.UnsupportedGap:
                return HaulAttentionReason.UnsupportedGap;
            case CartRouteVerdict.OutsideLoadedArea:
                return HaulAttentionReason.OutsideLoadedArea;
            case CartRouteVerdict.UnsafeStop:
                return HaulAttentionReason.UnsafeParking;
            default:
                return HaulAttentionReason.NoRoute;
        }
    }
}
