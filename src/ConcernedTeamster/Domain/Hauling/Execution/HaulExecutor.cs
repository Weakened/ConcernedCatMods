using System;
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
        IHaulExecutionLog log)
    {
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

    private readonly HaulExecutorPorts _ports;
    private readonly WorkerKey _worker;
    private readonly HaulLimits _limits;
    private readonly HaulExecutionLimits _execution;
    private readonly CartStillnessTracker _stillness;
    private readonly AttentionThrottle _attentionLog = new AttentionThrottle(30f);

    private BoundedRetry _approachRetry;
    private BoundedRetry _hitchRetry;
    private BoundedRetry _recoveryRetry;
    private PhaseDeadline _deadline;
    private float _noPathSince = float.NaN;
    private float _massWaitStartedAt = float.NaN;
    private float _recoveryRetryAt;
    private float _detachReleasedAt = float.NaN;
    private float _lastWorkerTickAt = float.NaN;
    private bool _legArrived;
    private bool _releaseLeaseAfterDetach;
    private HaulStopIntent _pendingStop;
    private HaulAttentionReason _pendingAttention;
    private string _pendingAttentionDetail = string.Empty;

    public HaulExecutor(
        HaulExecutorPorts ports,
        WorkerKey worker,
        Guid worldLoadEpoch,
        int startingRevision,
        HaulLimits limits,
        HaulExecutionLimits execution)
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
        Modes = new ActorModeOwner(worker);
        Revision = Math.Max(0, startingRevision);
        _stillness = new CartStillnessTracker(_limits);
        _approachRetry = NewRecoveryRetry();
        _hitchRetry = NewHitchRetry();
        _recoveryRetry = NewRecoveryRetry();
    }

    /// <summary>One lease book per world load (DECISIONS.md D6).</summary>
    public CartLeaseBook Leases { get; }

    /// <summary>The single actor-mode owner of Gunnar's identity for this world
    /// load (ARCH-02).</summary>
    public ActorModeOwner Modes { get; }

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
    /// confirmed by every later frame's signals.</summary>
    public bool Attached { get; private set; }

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
                _ports.Seam.IsAvailable ? HaulAttentionReason.WorkerBodyLost : HaulAttentionReason.HitchFailed,
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

        if (fromWaiting)
        {
            SignalVerdict signals = JointSignalClassifier.Classify(authority, cart, body, _limits);
            if (!signals.Healthy)
            {
                LoseControl(signals, lease);
                return Answer(HaulCommandOutcome.Rejected, signals.Reason, HaulCommandDetail.HitchUnhealthy);
            }
        }

        if (!cart.TryGetFootprint(out CartFootprint footprint))
        {
            return Answer(HaulCommandOutcome.Rejected, HaulAttentionReason.NoRoute, HaulCommandDetail.FootprintUnknown);
        }

        CartRoutePlan plan = _ports.Planner.Plan(
            new CartRouteRequest(cart.CartPosition, request.Target, footprint, cart.ExpectedMassKg, Revision + 1), now);
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
            return Answer(HaulCommandOutcome.Accepted, HaulAttentionReason.Unspecified, HaulCommandDetail.Accepted);
        }

        if (Phase != HaulPhase.Unloading)
        {
            return Answer(HaulCommandOutcome.Rejected, HaulAttentionReason.Unspecified, HaulCommandDetail.NotUnloading);
        }

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
            // Done: the cart must not move out from under a transfer.
            _pendingStop = intent;
            return Answer(HaulCommandOutcome.Accepted, HaulAttentionReason.Unspecified, HaulCommandDetail.Pending);
        }

        if (Phase == HaulPhase.NeedsAttention && Attached && intent == HaulStopIntent.StopAndWait)
        {
            return Answer(HaulCommandOutcome.Rejected, Attention, HaulCommandDetail.CannotWaitWhileNeedingAttention);
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

        _ports.Body.Stop();
        CartLease? lease = ActiveLease;
        if (HaulId.Length > 0)
        {
            Modes.Enter(ActorMode.Recovering, HaulId);
        }

        if (Attached && lease != null)
        {
            ReleaseResult released = _ports.Seam.ReleaseJoint(lease.Cart);
            _ports.Log.Info("Gunnar let go of cart " + lease.Cart + " before teardown (" + why + "): " + released + ".");
        }

        Attached = false;
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
    /// joint (should one exist at all) is released first, always.</summary>
    public BodyRetirementOutcome RetireBody()
    {
        if (HaulId.Length > 0 || !Modes.MayRetireBody)
        {
            return BodyRetirementOutcome.RefusedBusy;
        }

        _ports.Body.Stop();
        CartLease? lease = ActiveLease;
        if (Attached && lease != null)
        {
            _ports.Seam.ReleaseJoint(lease.Cart);
        }

        Attached = false;
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
        CartObservation cart = ReadCart(lease, now);

        if (Attached)
        {
            SignalVerdict verdict = lease == null
                ? SignalVerdict.End(HaulAttentionReason.LeaseInvalidated, true, LeaseInvalidation.Unspecified, "the lease ended while hitched")
                : JointSignalClassifier.Classify(authority, cart, body, _limits);
            if (!verdict.Healthy && !IsAlreadyAsked(verdict))
            {
                LoseControl(verdict, lease);
            }

            return;
        }

        if (Phase == HaulPhase.Detaching)
        {
            return;
        }

        if (authority == WorkAuthorityVerdict.OtherPeersConnected)
        {
            if (HaulId.Length > 0 && Phase != HaulPhase.Paused && Phase != HaulPhase.NeedsAttention)
            {
                _ports.Body.Stop();
                ChainToPaused(HaulAttentionReason.OtherPeersConnected, "a peer connected");
            }

            return;
        }

        if (authority != WorkAuthorityVerdict.Granted)
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

        if (Phase == HaulPhase.Paused && Attention == HaulAttentionReason.OtherPeersConnected)
        {
            // Nobody else is connected any more: the haul is held, ready for
            // its next leg, but nothing moves until one is requested.
            Transition(HaulPhase.Ready);
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
            InvalidateLease(lease, LeaseInvalidation.WorkerBodyLost);
            ChainToAttention(HaulAttentionReason.WorkerBodyLost, "Gunnar's body is not working");
        }
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
            if (Attached)
            {
                ObserveFrame();
            }

            return;
        }

        WorkAuthorityVerdict authority = _ports.Authority.Evaluate();
        PullerBodyFacts body = EffectiveBody(now);
        CartObservation cart = ReadCart(lease, now);

        if (Attached)
        {
            SignalVerdict verdict = JointSignalClassifier.Classify(authority, cart, body, _limits);
            if (!verdict.Healthy && !IsAlreadyAsked(verdict))
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

        if (body.MotorCommanded && !body.HasPath)
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

        _ports.Body.WalkTo(cart.ApproachPoint, ApproachArrivalRadiusMetres);
    }

    private void TickHitching(float now, WorkAuthorityVerdict authority, PullerBodyFacts body, CartObservation cart, CartLease? lease)
    {
        if (lease == null || _hitchRetry.IsWaiting(now))
        {
            return;
        }

        StopIfMoving(body);
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

        Attached = true;
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
            BeginRecovery(now, FormattableString.Invariant(
                $"the hitch strained to {cart.JointForceNewtons:0} N of {cart.BreakForceNewtons:0} N"));
            return;
        }

        HaulMotion motion = _ports.Monitor.Current;
        if (motion == HaulMotion.Stalled || motion == HaulMotion.Wedged)
        {
            BeginRecovery(now, "motion judged " + motion);
            return;
        }

        SteeringGoal? next = _ports.Planner.NextGoal(Plan, body.Position, cart.CartPosition);
        if (next == null)
        {
            BeginRecovery(now, "the route is no longer valid from here");
            return;
        }

        SteeringGoal goal = next.Value;
        if (goal.IsFinalStop && body.Position.HorizontalDistanceTo(goal.Target) <= Leg.ArrivalRadiusMetres)
        {
            _legArrived = true;
            _ports.Body.Stop();
            _deadline = new PhaseDeadline(now, _execution.StoppingTimeoutSeconds);
            Transition(HaulPhase.Stopping);
            return;
        }

        _ports.Body.SteerToward(goal.Target);
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
        if (!cart.TryGetFootprint(out CartFootprint footprint))
        {
            StopThenAttend(HaulAttentionReason.NoRoute, "the cart's footprint could not be measured");
            return;
        }

        CartRoutePlan plan = _ports.Planner.Plan(
            new CartRouteRequest(cart.CartPosition, Leg.Target, footprint, cart.ExpectedMassKg, Revision), now);
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
        if (lease == null)
        {
            return;
        }

        ParkingGround ground = _ports.Seam.ReadGround(lease.Cart);
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
        Modes.Enter(ActorMode.Recovering, HaulId.Length > 0 ? HaulId : lease.LeaseId);
        ReleaseResult released = _ports.Seam.ReleaseJoint(lease.Cart);
        _ports.Log.Info("Gunnar detached from cart " + lease.Cart + " on suitable ground: " + released + ".");
        Attached = false;
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

    private void TickDetaching(float now, CartObservation cart, CartLease lease)
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
        if (_releaseLeaseAfterDetach)
        {
            _releaseLeaseAfterDetach = false;
            Leases.Release(lease.LeaseId);
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
                    BeginDetaching(now, ReadCart(lease, now), lease);
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
                    if (intent == HaulStopIntent.DetachAndPark)
                    {
                        BeginDetaching(now, ReadCart(lease, now), lease);
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
    /// (a hard lean); the same condition next frame changes nothing, so the
    /// reason and the revision stay put.</summary>
    private bool IsAlreadyAsked(SignalVerdict verdict) =>
        verdict.StillHeld && Phase == HaulPhase.NeedsAttention;

    private void LoseControl(SignalVerdict verdict, CartLease? lease)
    {
        _ports.Body.Stop();
        if (HaulId.Length > 0)
        {
            Modes.Enter(ActorMode.Recovering, HaulId);
        }

        if (verdict.ReleaseJoint && lease != null)
        {
            ReleaseResult released = _ports.Seam.ReleaseJoint(lease.Cart);
            _ports.Log.Info("Gunnar let go of cart " + lease.Cart + " (" + verdict.Reason + "): " + released + ".");
        }

        // Only a leaning cart that is still held by Gunnar keeps the joint.
        Attached = !verdict.ReleaseJoint && LastCart.HasJoint && LastCart.JointConnectedToPuller;
        _pendingStop = HaulStopIntent.Unspecified;
        _pendingAttention = HaulAttentionReason.Unspecified;
        _releaseLeaseAfterDetach = false;
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

        if (Modes.Enter(desired, HaulId) == ActorModeOutcome.RefusedBusy)
        {
            _ports.Log.Bug("Another job holds Gunnar's identity while haul " + HaulId + " runs.");
        }
    }

    private void ReleaseIdentityIfIdle()
    {
        if (HaulId.Length == 0 && Modes.JobId != null)
        {
            Modes.Release(Modes.JobId);
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
            _ports.Log.Info("Lease " + lease.LeaseId + " on cart " + lease.Cart + " ended: " + reason + ".");
        }
    }

    private void ResetLegState(float now)
    {
        _approachRetry = NewRecoveryRetry();
        _hitchRetry = NewHitchRetry();
        _recoveryRetry = NewRecoveryRetry();
        _noPathSince = float.NaN;
        _massWaitStartedAt = float.NaN;
        _detachReleasedAt = float.NaN;
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
        return new HaulCommandResult(outcome, reason, Revision);
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

    private CartObservation ReadCart(CartLease? lease, float now)
    {
        if (lease == null)
        {
            _stillness.Reset();
            return default;
        }

        CartObservation cart = _ports.Seam.Observe(lease.Cart);
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
