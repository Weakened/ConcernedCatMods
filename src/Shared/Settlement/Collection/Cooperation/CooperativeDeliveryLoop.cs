using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TheConcernedCat.Interop.Haul;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;

namespace TheConcernedCat.Settlement.Collection.Cooperation;

/// <summary>One cooperative collection order's delivery with Gunnar and a
/// leased cart (COOP-01..03, CONTRACTS.md §3, §7), game-free.
///
/// <b>What it owns and what it does not.</b> Thorstein's collection order stays
/// with the collection loop, which calls <see cref="Tick"/> while the order is
/// WithHauler and follows the returned step. This loop owns only the
/// cooperation: which rendezvous to propose, when to take Thorstein to the
/// cart, the transfer hold, the haul legs and the checkpoints. It moves no body
/// (Thorstein walks through <see cref="ICooperationWorker"/>, Gunnar through the
/// provider), it moves no material except through the custody executor, and it
/// credits nothing by bookkeeping.
///
/// <b>The cycle.</b> Connecting (handshake, lease, cart, destination) - Staging
/// (Gunnar brings the cart to a rendezvous while Thorstein collects) -
/// AwaitingLoad (the cart waits, still, until Thorstein's load reaches a
/// checkpoint) - ApproachingCart - Loading (hold acknowledged at the current
/// revision, worker to cart through the executor, hold released) - a capacity or
/// quota checkpoint - Hauling (Gunnar to the chest, Thorstein alongside) -
/// Unloading (hold, cart to container directly, or carried across when the chest
/// is out of reach) - back to Staging, or Completing when everything requested
/// is delivered.
///
/// <b>Nobody waits forever.</b> Every waiting phase has a deadline of
/// <see cref="CooperationLimits.RendezvousTimeoutSeconds"/>; waiting at the
/// rendezvous has a progress watchdog of the same length. Expiry stops Gunnar
/// where he is (StopAndWait) and ends in NeedsAttention RendezvousTimedOut.
///
/// <b>Losing Gunnar or the cart</b> (§3.3, COOP-03): no further transfer starts,
/// the hold is released if it can be, and the cart's custody location is
/// reconciled against the cart's actual contents before the run pauses. Cargo
/// stays physical; nothing is moved into Thorstein's inventory by bookkeeping.
/// </summary>
internal sealed class CooperativeDeliveryLoop
{
    private readonly CollectionOrderDefinition _order;
    private readonly HaulClient _client;
    private readonly ICooperationWorker _worker;
    private readonly ICooperationCustody _custody;
    private readonly CooperationLimits _limits;
    private readonly HaulRequestIds _ids;
    private readonly BoundedRetry _transferRefusals;

    // What this run has established about the provider, the lease and the cart.
    private bool _connected;
    private int _epochGeneration;
    private Guid _providerEpoch;
    private string _leaseId = string.Empty;
    private string _cartKey = string.Empty;
    private int _haulRevision;
    private SitePoint? _cartPosition;
    private bool _haulStarted;
    private bool _baselineRecorded;

    // Legs.
    private IReadOnlyList<SitePoint> _candidates = Array.Empty<SitePoint>();
    private int _candidateIndex;
    private RequestHaulMessage? _pendingLeg;
    private bool _legAccepted;

    // The transfer hold.
    private AcknowledgeWaitMessage? _pendingHold;
    private AcknowledgeWaitMessage? _pendingRelease;
    private bool _holdActive;
    private bool _holdUncertain;
    private int _holdRevision;

    // Completing.
    private CancelHaulMessage? _pendingCancel;

    // Unloading.
    private UnloadStep _unloadStep;
    private bool _destinationBlocked;
    private bool _cartBlocked;
    private int _lastLoadMoved;

    // Waiting.
    private PhaseDeadline? _deadline;
    private int _watchedCommitted;
    private SitePoint? _walkTarget;
    private bool _commandingWorker;

    public CooperativeDeliveryLoop(
        CollectionOrderDefinition order,
        HaulClient client,
        ICooperationWorker worker,
        ICooperationCustody custody,
        CooperationLimits limits,
        Guid runNonce)
    {
        _order = order ?? throw new ArgumentNullException(nameof(order));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        _custody = custody ?? throw new ArgumentNullException(nameof(custody));
        _limits = (limits ?? throw new ArgumentNullException(nameof(limits))).Validate();
        _ids = new HaulRequestIds(order.Order, runNonce);
        _transferRefusals = new BoundedRetry(limits.MaxTransferRefusals, limits.TransferRetrySeconds, limits.TransferRetryMaxSeconds);
        Phase = CooperationPhase.Connecting;
    }

    private enum UnloadStep
    {
        Unspecified = 0,
        AtCart = 1,
        CarryToChest = 2,
        AtChest = 3,
        ReturnToCart = 4,
    }

    private enum Checkpoint
    {
        Unspecified = 0,
        Collect = 1,
        LoadNow = 2,
        HaulNow = 3,
        Complete = 4,
    }

    public CollectionOrderDefinition Order => _order;

    public CooperationPhase Phase { get; private set; }

    public CollectionAttentionReason Reason { get; private set; }

    public string Detail { get; private set; } = string.Empty;

    /// <summary>Increments on every phase, reason or rendezvous change.</summary>
    public int PlanRevision { get; private set; }

    /// <summary>The meeting point last proposed to Gunnar.</summary>
    public SitePoint? Rendezvous { get; private set; }

    /// <summary>Increments whenever the proposed meeting point changes.</summary>
    public int RendezvousRevision { get; private set; }

    public string HaulId => _ids.HaulId;

    public string LeaseId => _leaseId;

    public string CartSessionKey => _cartKey;

    public Guid ProviderEpoch => _providerEpoch;

    /// <summary>Unloads that finished at the destination in this run.</summary>
    public int DeliveryTrips { get; private set; }

    /// <summary>The cart is held still for a transfer this run asked for.
    /// </summary>
    public bool HoldActive => _holdActive;

    public bool PausedByPlayer { get; private set; }

    public bool IsTerminal => Phase == CooperationPhase.Completed || Phase == CooperationPhase.Cancelled;

    private CooperationStep StepForStoppedPhase()
    {
        switch (Phase)
        {
            case CooperationPhase.Completed:
                return CooperationStep.Delivered;
            case CooperationPhase.Cancelled:
                return CooperationStep.Cancelled;
            case CooperationPhase.Paused:
                return CooperationStep.Paused;
            default:
                return CooperationStep.NeedsAttention;
        }
    }

    /// <summary>What happened to the last cancel sent on the player's behalf,
    /// for the panel: empty when none was needed.</summary>
    public string LastCancelOutcome { get; private set; } = string.Empty;

    public CooperationTick Tick(float now)
    {
        switch (Phase)
        {
            case CooperationPhase.Completed:
            case CooperationPhase.Cancelled:
            case CooperationPhase.Paused:
            case CooperationPhase.NeedsAttention:
                // A hold whose release never got through is retried even after
                // the run stopped or ended: a held cart is safe, but Gunnar can
                // finish nothing (not even a cancel) until it is released.
                if (_holdActive)
                {
                    TryReleaseHoldOnce(now);
                }

                return Emit(StepForStoppedPhase());
        }

        // Connecting re-establishes all of this itself; everywhere else a reload
        // or a silent provider means the haul this run relied on is gone.
        if (_connected && Phase != CooperationPhase.Connecting)
        {
            if (_client.EpochGeneration != _epochGeneration)
            {
                return Reconcile(CollectionAttentionReason.CartLeaseLost, "Concerned Teamster's world was reloaded.", now, providerEpochLost: true);
            }

            if (_client.IsProviderLost)
            {
                return Reconcile(CollectionAttentionReason.HaulerUnavailable, "Concerned Teamster stopped answering.", now);
            }
        }

        switch (Phase)
        {
            case CooperationPhase.Connecting:
                return TickConnecting(now);
            case CooperationPhase.Staging:
                return TickStaging(now);
            case CooperationPhase.AwaitingLoad:
                return TickAwaitingLoad(now);
            case CooperationPhase.ApproachingCart:
                return TickApproachingCart(now);
            case CooperationPhase.Loading:
                return TickLoading(now);
            case CooperationPhase.Hauling:
                return TickHauling(now);
            case CooperationPhase.Unloading:
                return TickUnloading(now);
            case CooperationPhase.Completing:
                return TickCompleting(now);
            default:
                return Attention(CollectionAttentionReason.HaulerNeedsAttention, "The cooperative run reached an unknown step.");
        }
    }

    /// <summary>The player paused the order: release any hold, stop Gunnar
    /// where he is if he is moving for this order, and hold.</summary>
    public void Pause(float now)
    {
        if (IsTerminal || Phase == CooperationPhase.Paused)
        {
            return;
        }

        TryReleaseHoldOnce(now);
        if (_haulStarted && _legAccepted &&
            (Phase == CooperationPhase.Staging || Phase == CooperationPhase.Hauling))
        {
            TrySendCancelOnce(HaulCancelDisposition.StopAndWait, now);
        }

        StopWorker();
        SetPhase(CooperationPhase.Paused, CollectionAttentionReason.Unspecified, "Paused.");
        PausedByPlayer = true;
    }

    /// <summary>Resumes a paused run, or one whose attention was resolved: the
    /// run starts again from the handshake and re-derives where it is from the
    /// provider and from custody.</summary>
    public bool Resume(float now)
    {
        if (Phase != CooperationPhase.Paused && Phase != CooperationPhase.NeedsAttention)
        {
            return false;
        }

        PausedByPlayer = false;
        _pendingLeg = null;
        _pendingHold = null;
        _pendingRelease = null;
        _pendingCancel = null;
        _legAccepted = false;
        _deadline = null;
        _transferRefusals.Reset();
        _client.ResetFailures();
        SetPhase(CooperationPhase.Connecting, CollectionAttentionReason.Unspecified, string.Empty);
        return true;
    }

    /// <summary>Ends the run for the player (COOP-04 cancel): no transfer is
    /// left half-done - transfers are synchronous inside a tick - the hold is
    /// released, and the haul is cancelled with the chosen disposition. Not a
    /// refund: delivered material stays delivered, cart material stays in the
    /// cart and carried material stays with Thorstein.</summary>
    public void Cancel(bool detachAndPark, float now)
    {
        if (IsTerminal)
        {
            return;
        }

        TryReleaseHoldOnce(now);
        if (_connected)
        {
            // Also when this run never sent a leg: a previous run of the same
            // order may have left Gunnar waiting under this order's haul. An
            // unknown haul is simply refused.
            TrySendCancelOnce(detachAndPark ? HaulCancelDisposition.DetachAndPark : HaulCancelDisposition.StopAndWait, now);
        }

        StopWorker();
        SetPhase(CooperationPhase.Cancelled, CollectionAttentionReason.Unspecified, "Cancelled.");
    }

    // --- Connecting ------------------------------------------------------------------------------

    private CooperationTick TickConnecting(float now)
    {
        if (_order.Delivery.Kind != DeliveryKind.Container)
        {
            return Attention(
                CollectionAttentionReason.DestinationUnavailable,
                "A cooperative order delivers to a chest; hold-for-player orders run without Gunnar.");
        }

        if (!_custody.IsWritable)
        {
            return PauseFor(CollectionAttentionReason.JournalReadOnly, "The settlement record cannot be written.");
        }

        HaulCall<HelloReply> hello = _client.Hello(now);
        if (!hello.Succeeded)
        {
            if (hello.Outcome == HaulCallOutcome.NoAnswer && !_client.IsProviderLost &&
                _client.DiscoveryStatus == TheConcernedCat.Interop.CapabilityStatus.Available)
            {
                return Emit(CooperationStep.CollectMore);
            }

            return PauseFor(CollectionAttentionReason.HaulerUnavailable, "Concerned Teamster is not answering (" + Explain(hello) + ").");
        }

        HelloReply handshake = hello.Reply!;
        if (handshake.ProviderEpoch == Guid.Empty)
        {
            return PauseFor(CollectionAttentionReason.HaulerUnavailable, "Concerned Teamster has no world loaded.");
        }

        if (handshake.Authority == WorkAuthorityVerdict.OtherPeersConnected)
        {
            return PauseFor(CollectionAttentionReason.OtherPeersConnected, WorkAuthorityPolicy.Describe(handshake.Authority));
        }

        if (handshake.Authority != WorkAuthorityVerdict.Granted)
        {
            return PauseFor(CollectionAttentionReason.HaulerUnavailable, "Gunnar may not haul: " + WorkAuthorityPolicy.Describe(handshake.Authority));
        }

        if (!handshake.WorkerAvailable)
        {
            return PauseFor(CollectionAttentionReason.HaulerUnavailable, "Gunnar cannot work right now.");
        }

        if (!handshake.HasLease)
        {
            return PauseFor(CollectionAttentionReason.HaulerUnavailable, "No cart is assigned to Gunnar.");
        }

        HaulCall<DescribeLeaseReply> described = _client.DescribeLease(now);
        if (!described.Succeeded)
        {
            if (described.Outcome == HaulCallOutcome.NoAnswer && !_client.IsProviderLost)
            {
                return Emit(CooperationStep.CollectMore);
            }

            if (described.Outcome == HaulCallOutcome.Stale)
            {
                return Emit(CooperationStep.CollectMore);
            }

            return PauseFor(CooperationReasons.ForHaul(described.Reason), "The cart lease could not be read (" + Explain(described) + ").");
        }

        DescribeLeaseReply lease = described.Reply!;
        bool sameCart = _connected && handshake.ProviderEpoch == _providerEpoch &&
            string.Equals(lease.LeaseId, _leaseId, StringComparison.Ordinal) &&
            string.Equals(lease.CartSessionKey, _cartKey, StringComparison.Ordinal);

        if (!lease.CartUpright)
        {
            return Attention(CollectionAttentionReason.HaulerNeedsAttention, "The assigned cart is not upright.");
        }

        if (lease.Phase == HaulWirePhase.NeedsAttention || lease.Phase == HaulWirePhase.Paused)
        {
            return PauseFor(CollectionAttentionReason.HaulerNeedsAttention, "Gunnar is stopped (" + lease.Phase + "); see his panel in Concerned Teamster.");
        }

        if (sameCart && lease.Phase == HaulWirePhase.Unloading && (_holdActive || _holdUncertain))
        {
            // A hold this run asked for may still be on: release it first.
            _holdActive = true;
            _holdRevision = lease.Revision;
            TryReleaseHoldOnce(now);
            return Emit(CooperationStep.Working);
        }

        if (CooperationReasons.IsBusy(lease.Phase))
        {
            return PauseFor(CollectionAttentionReason.HaulerUnavailable, "Gunnar is busy with another haul.");
        }

        int inCart = LedgerTotal(CustodyPlace.Cart);
        if (inCart > 0 && !sameCart)
        {
            return Attention(
                CollectionAttentionReason.CartLeaseLost,
                "The record shows " + DescribeLedger(CustodyPlace.Cart) +
                " in a cart this run cannot vouch for (reassigned or reloaded). Confirm where it is before continuing.");
        }

        if (!sameCart)
        {
            _baselineRecorded = false;
            _haulStarted = false;
        }

        _connected = true;
        _providerEpoch = handshake.ProviderEpoch;
        _epochGeneration = _client.EpochGeneration;
        _leaseId = lease.LeaseId;
        _cartKey = lease.CartSessionKey;
        _haulRevision = lease.Revision;
        if (lease.CartPosition.HasValue)
        {
            _cartPosition = ToSite(lease.CartPosition.Value);
        }

        switch (EvaluateCheckpoint())
        {
            case Checkpoint.Complete:
                return EnterCompleting(now);
            default:
                return inCart > 0 ? EnterHauling(now) : EnterStaging(now, restartCandidates: true);
        }
    }

    // --- Staging and waiting at the rendezvous ----------------------------------------------------

    private CooperationTick EnterStaging(float now, bool restartCandidates)
    {
        if (restartCandidates || _candidates.Count == 0)
        {
            _candidates = RendezvousPlanner.Candidates(
                _order.Scope, _order.Delivery.Position, _cartPosition, _limits.MaxRendezvousCandidates);
            _candidateIndex = 0;
        }

        SetPhase(CooperationPhase.Staging, CollectionAttentionReason.Unspecified, string.Empty);
        StartLeg(HaulLegPurpose.ToRendezvous, _candidates[_candidateIndex], now);
        return Emit(StagingStep());
    }

    private CooperationTick TickStaging(float now)
    {
        if (!_legAccepted)
        {
            _pendingLeg ??= RebuildLeg(HaulLegPurpose.ToRendezvous, _candidates[_candidateIndex]);
            CooperationTick? stop = SendLeg(now);
            if (stop != null)
            {
                return stop;
            }

            if (!_legAccepted)
            {
                return DeadlineOr(now, "Gunnar starting toward the meeting point", StagingStep());
            }
        }

        if (!TryPollHaul(now, out GetHaulReply? haul, out CooperationTick? ended))
        {
            return ended ?? DeadlineOr(now, "Gunnar bringing the cart to the meeting point", StagingStep());
        }

        if (IsWaitingStill(haul!) && haul!.Arrived)
        {
            SetPhase(CooperationPhase.AwaitingLoad, CollectionAttentionReason.Unspecified, string.Empty);
            _deadline = new PhaseDeadline(now, _limits.RendezvousTimeoutSeconds);
            _watchedCommitted = CommittedTotal();
            return TickAwaitingLoad(now);
        }

        return DeadlineOr(now, "Gunnar bringing the cart to the meeting point", StagingStep());
    }

    /// <summary>While the cart travels, Thorstein collects - unless his load is
    /// already ready, in which case he waits for the cart at the meeting point.
    /// </summary>
    private CooperationStep StagingStep()
    {
        if (EvaluateCheckpoint() != Checkpoint.LoadNow || !Rendezvous.HasValue)
        {
            ReleaseWorker();
            return CooperationStep.CollectMore;
        }

        WalkWorker(RendezvousPlanner.StandOff(Rendezvous.Value, _worker.Position, _limits.CartStandOffMetres));
        return CooperationStep.Working;
    }

    private CooperationTick TickAwaitingLoad(float now)
    {
        if (!TryPollHaul(now, out GetHaulReply? haul, out CooperationTick? ended))
        {
            if (ended != null)
            {
                return ended;
            }
        }

        switch (EvaluateCheckpoint())
        {
            case Checkpoint.Complete:
                return EnterCompleting(now);
            case Checkpoint.HaulNow:
                return EnterHauling(now);
            case Checkpoint.LoadNow:
                SetPhase(CooperationPhase.ApproachingCart, CollectionAttentionReason.Unspecified, string.Empty);
                _deadline = new PhaseDeadline(now, _limits.RendezvousTimeoutSeconds);
                return TickApproachingCart(now);
        }

        ReleaseWorker();
        int committed = CommittedTotal();
        if (committed != _watchedCommitted)
        {
            _watchedCommitted = committed;
            _deadline = new PhaseDeadline(now, _limits.RendezvousTimeoutSeconds);
        }

        return DeadlineOr(now, "new material for the waiting cart", CooperationStep.CollectMore);
    }

    private CooperationTick TickApproachingCart(float now)
    {
        if (!TryPollHaul(now, out GetHaulReply? haul, out CooperationTick? ended))
        {
            return ended ?? DeadlineOr(now, "Thorstein reaching the cart", CooperationStep.Working);
        }

        if (!IsWaitingStill(haul!) || !_cartPosition.HasValue)
        {
            return DeadlineOr(now, "the cart standing still for loading", CooperationStep.Working);
        }

        SitePoint cart = _cartPosition.Value;
        if (_worker.Position.HorizontalDistanceTo(cart) <= _limits.CartReachMetres)
        {
            SetPhase(CooperationPhase.Loading, CollectionAttentionReason.Unspecified, string.Empty);
            _deadline = new PhaseDeadline(now, _limits.RendezvousTimeoutSeconds);
            return TickLoading(now);
        }

        if (_commandingWorker && _worker.WalkStatus == CooperationWalkStatus.Deferred)
        {
            return RestageForUnreachableCart(now);
        }

        WalkWorker(RendezvousPlanner.StandOff(cart, _worker.Position, _limits.CartStandOffMetres));
        return DeadlineOr(now, "Thorstein reaching the cart", CooperationStep.Working);
    }

    private CooperationTick RestageForUnreachableCart(float now)
    {
        StopWorker();
        if (_candidateIndex + 1 >= _candidates.Count)
        {
            return Attention(
                CollectionAttentionReason.RendezvousTimedOut,
                "Thorstein could not find a way to the cart at any meeting point.");
        }

        _candidateIndex++;
        SetPhase(CooperationPhase.Staging, CollectionAttentionReason.Unspecified, string.Empty);
        StartLeg(HaulLegPurpose.ToRendezvous, _candidates[_candidateIndex], now);
        return Emit(CooperationStep.Working);
    }

    // --- Loading ------------------------------------------------------------------------------------

    private CooperationTick TickLoading(float now)
    {
        if (!_holdActive)
        {
            CooperationTick? stop = AcquireHold(now);
            if (stop != null)
            {
                return stop;
            }

            if (!_holdActive)
            {
                return DeadlineOr(now, "the cart being held still for loading", CooperationStep.Working);
            }
        }

        if (!_custody.TryResolveWorker(out IInventoryPort? workerPort, out CollectionAttentionReason workerRefusal))
        {
            TryReleaseHoldOnce(now);
            return PauseFor(Known(workerRefusal, CollectionAttentionReason.WorkerBodyLost), "Thorstein's pack cannot be reached.");
        }

        if (!_custody.TryResolveCart(_cartKey, _providerEpoch, out IInventoryPort? cartPort, out _))
        {
            return Reconcile(CollectionAttentionReason.CartLeaseLost, "The cart's container cannot be reached.", now);
        }

        if (!_baselineRecorded)
        {
            if (!_custody.RecordCartBaseline(_order.Order, _leaseId, _cartKey, _providerEpoch))
            {
                TryReleaseHoldOnce(now);
                return PauseFor(CollectionAttentionReason.JournalReadOnly, "The cart's existing cargo could not be recorded, so nothing was loaded.");
            }

            _baselineRecorded = true;
        }

        TransferBatch batch = MoveAll(
            CustodyPlace.Worker, _custody.WorkerLocation, workerPort!,
            _custody.CartLocation(_cartKey, _providerEpoch), cartPort!, now);
        CooperationTick? failed = HandleBatch(batch, now);
        if (failed != null)
        {
            return failed;
        }

        _cartBlocked = batch.Blocked;
        _lastLoadMoved = batch.Moved;
        CooperationTick? releaseStop = ReleaseHold(now);
        if (releaseStop != null)
        {
            return releaseStop;
        }

        if (_holdActive)
        {
            return DeadlineOr(now, "the cart being released after loading", CooperationStep.Working);
        }

        return AfterLoading(now);
    }

    private CooperationTick AfterLoading(float now)
    {
        int stillToCollect = StillToCollectTotal();
        int carried = LedgerTotal(CustodyPlace.Worker);
        if (_lastLoadMoved == 0 && _cartBlocked && carried > 0 && LedgerTotal(CustodyPlace.Cart) == 0)
        {
            // A cart already full of its own cargo cannot help: say so rather
            // than haul it to the chest empty of anything this order gathered.
            return PauseFor(
                CollectionAttentionReason.NoReturnSpace,
                "The cart has no room for " + DescribeLedger(CustodyPlace.Worker) + "; Thorstein keeps it.");
        }

        if (stillToCollect == 0 || _cartBlocked || carried > 0)
        {
            return EnterHauling(now);
        }

        SetPhase(CooperationPhase.AwaitingLoad, CollectionAttentionReason.Unspecified, string.Empty);
        _deadline = new PhaseDeadline(now, _limits.RendezvousTimeoutSeconds);
        _watchedCommitted = CommittedTotal();
        ReleaseWorker();
        return Emit(CooperationStep.CollectMore);
    }

    // --- Hauling --------------------------------------------------------------------------------------

    private CooperationTick EnterHauling(float now)
    {
        SetPhase(CooperationPhase.Hauling, CollectionAttentionReason.Unspecified, string.Empty);
        StartLeg(HaulLegPurpose.ToDestination, _order.Delivery.Position, now);
        return Emit(CooperationStep.Working);
    }

    private CooperationTick TickHauling(float now)
    {
        SitePoint chest = _order.Delivery.Position;
        if (!_legAccepted)
        {
            _pendingLeg ??= RebuildLeg(HaulLegPurpose.ToDestination, chest);
            CooperationTick? stop = SendLeg(now);
            if (stop != null)
            {
                return stop;
            }

            if (!_legAccepted)
            {
                return DeadlineOr(now, "Gunnar starting toward the chest", CooperationStep.Working);
            }
        }

        if (!TryPollHaul(now, out GetHaulReply? haul, out CooperationTick? ended))
        {
            return ended ?? DeadlineOr(now, "the cart reaching the chest", CooperationStep.Working);
        }

        bool cartArrived = IsWaitingStill(haul!) && haul!.Arrived && _cartPosition.HasValue;
        if (cartArrived && _worker.Position.HorizontalDistanceTo(_cartPosition!.Value) <= _limits.CartReachMetres)
        {
            SetPhase(CooperationPhase.Unloading, CollectionAttentionReason.Unspecified, string.Empty);
            _unloadStep = UnloadStep.AtCart;
            _destinationBlocked = false;
            _deadline = new PhaseDeadline(now, _limits.RendezvousTimeoutSeconds);
            return Emit(CooperationStep.Working);
        }

        if (_commandingWorker && _worker.WalkStatus == CooperationWalkStatus.Deferred)
        {
            StopWorker();
            return Attention(CollectionAttentionReason.DestinationUnavailable, "Thorstein could not find a way to the chest.");
        }

        WalkWorker(cartArrived
            ? RendezvousPlanner.StandOff(_cartPosition!.Value, _worker.Position, _limits.CartStandOffMetres)
            : RendezvousPlanner.StandOff(chest, _worker.Position, _limits.CartStandOffMetres));
        return DeadlineOr(now, "the cart reaching the chest", CooperationStep.Working);
    }

    // --- Unloading ------------------------------------------------------------------------------------

    private CooperationTick TickUnloading(float now)
    {
        switch (_unloadStep)
        {
            case UnloadStep.AtCart:
                return UnloadAtCart(now);
            case UnloadStep.CarryToChest:
                return WalkForUnloading(now, _order.Delivery.Position, UnloadStep.AtChest, "Thorstein carrying the load to the chest");
            case UnloadStep.AtChest:
                return UnloadAtChest(now);
            case UnloadStep.ReturnToCart:
                return _cartPosition.HasValue
                    ? WalkForUnloading(now, _cartPosition.Value, UnloadStep.AtCart, "Thorstein returning to the cart")
                    : Reconcile(CollectionAttentionReason.CartLeaseLost, "The cart's position is no longer known.", now);
            default:
                return Attention(CollectionAttentionReason.HaulerNeedsAttention, "Unloading reached an unknown step.");
        }
    }

    private CooperationTick UnloadAtCart(float now)
    {
        if (!_holdActive)
        {
            CooperationTick? stop = AcquireHold(now);
            if (stop != null)
            {
                return stop;
            }

            if (!_holdActive)
            {
                return DeadlineOr(now, "the cart being held still for unloading", CooperationStep.Working);
            }
        }

        if (!_custody.TryResolveCart(_cartKey, _providerEpoch, out IInventoryPort? cartPort, out _))
        {
            return Reconcile(CollectionAttentionReason.CartLeaseLost, "The cart's container cannot be reached.", now);
        }

        SitePoint chest = _order.Delivery.Position;
        bool chestInReach = _worker.Position.HorizontalDistanceTo(chest) <= _limits.CartReachMetres;
        if (chestInReach)
        {
            if (!_custody.TryResolveContainer(_order.Delivery, out IInventoryPort? containerPort, out CollectionAttentionReason refusal))
            {
                TryReleaseHoldOnce(now);
                return PauseFor(
                    Known(refusal, CollectionAttentionReason.DestinationUnavailable),
                    "The chest cannot take the load; " + DescribeLedger(CustodyPlace.Cart) + " stay in the cart.");
            }

            TransferBatch fromCart = MoveAll(
                CustodyPlace.Cart, _custody.CartLocation(_cartKey, _providerEpoch), cartPort!,
                _custody.DestinationLocation(_order.Delivery), containerPort!, now);
            CooperationTick? cartFailed = HandleBatch(fromCart, now);
            if (cartFailed != null)
            {
                return cartFailed;
            }

            _destinationBlocked |= fromCart.Blocked;
            if (_custody.TryResolveWorker(out IInventoryPort? carriedPort, out _) && LedgerTotal(CustodyPlace.Worker) > 0)
            {
                // What the cart could not take on the way rides in Thorstein's
                // pack; he is at the chest too.
                TransferBatch fromPack = MoveAll(
                    CustodyPlace.Worker, _custody.WorkerLocation, carriedPort!,
                    _custody.DestinationLocation(_order.Delivery), containerPort!, now);
                CooperationTick? packFailed = HandleBatch(fromPack, now);
                if (packFailed != null)
                {
                    return packFailed;
                }

                _destinationBlocked |= fromPack.Blocked;
            }

            CooperationTick? released = ReleaseHold(now);
            if (released != null)
            {
                return released;
            }

            return _holdActive
                ? DeadlineOr(now, "the cart being released after unloading", CooperationStep.Working)
                : AfterUnloading(now);
        }

        // The chest is out of reach from where the cart could stop: carry
        // across, as much as Thorstein can take at a time.
        if (!_custody.TryResolveWorker(out IInventoryPort? workerPort, out CollectionAttentionReason workerRefusal))
        {
            TryReleaseHoldOnce(now);
            return PauseFor(Known(workerRefusal, CollectionAttentionReason.WorkerBodyLost), "Thorstein's pack cannot be reached.");
        }

        TransferBatch toPack = LedgerTotal(CustodyPlace.Cart) > 0
            ? MoveAll(
                CustodyPlace.Cart, _custody.CartLocation(_cartKey, _providerEpoch), cartPort!,
                _custody.WorkerLocation, workerPort!, now)
            : TransferBatch.Done(0, false);
        CooperationTick? packStop = HandleBatch(toPack, now);
        if (packStop != null)
        {
            return packStop;
        }

        CooperationTick? releaseStop = ReleaseHold(now);
        if (releaseStop != null)
        {
            return releaseStop;
        }

        if (_holdActive)
        {
            return DeadlineOr(now, "the cart being released", CooperationStep.Working);
        }

        if (LedgerTotal(CustodyPlace.Worker) == 0)
        {
            return LedgerTotal(CustodyPlace.Cart) == 0
                ? AfterUnloading(now)
                : Attention(
                    CollectionAttentionReason.CarryFull,
                    "Thorstein cannot carry anything from the cart to the chest; " + DescribeLedger(CustodyPlace.Cart) + " stay in the cart.");
        }

        _unloadStep = UnloadStep.CarryToChest;
        _deadline = new PhaseDeadline(now, _limits.RendezvousTimeoutSeconds);
        return Emit(CooperationStep.Working);
    }

    private CooperationTick UnloadAtChest(float now)
    {
        if (!_custody.TryResolveContainer(_order.Delivery, out IInventoryPort? containerPort, out CollectionAttentionReason refusal))
        {
            return PauseFor(
                Known(refusal, CollectionAttentionReason.DestinationUnavailable),
                "The chest cannot take the load; Thorstein keeps what he carries.");
        }

        if (!_custody.TryResolveWorker(out IInventoryPort? workerPort, out CollectionAttentionReason workerRefusal))
        {
            return PauseFor(Known(workerRefusal, CollectionAttentionReason.WorkerBodyLost), "Thorstein's pack cannot be reached.");
        }

        TransferBatch batch = MoveAll(
            CustodyPlace.Worker, _custody.WorkerLocation, workerPort!,
            _custody.DestinationLocation(_order.Delivery), containerPort!, now);
        CooperationTick? failed = HandleBatch(batch, now, holdToRelease: false);
        if (failed != null)
        {
            return failed;
        }

        if (batch.Blocked && LedgerTotal(CustodyPlace.Worker) > 0)
        {
            _destinationBlocked = true;
            return PauseFor(
                CollectionAttentionReason.DestinationFull,
                "The chest is full; Thorstein keeps " + DescribeLedger(CustodyPlace.Worker) + " and " +
                DescribeLedger(CustodyPlace.Cart) + " stay in the cart.");
        }

        if (LedgerTotal(CustodyPlace.Cart) > 0)
        {
            _unloadStep = UnloadStep.ReturnToCart;
            _deadline = new PhaseDeadline(now, _limits.RendezvousTimeoutSeconds);
            return Emit(CooperationStep.Working);
        }

        return AfterUnloading(now);
    }

    private CooperationTick WalkForUnloading(float now, SitePoint target, UnloadStep next, string waitingFor)
    {
        if (_worker.Position.HorizontalDistanceTo(target) <= _limits.CartReachMetres)
        {
            _unloadStep = next;
            _deadline = new PhaseDeadline(now, _limits.RendezvousTimeoutSeconds);
            return Emit(CooperationStep.Working);
        }

        if (_commandingWorker && _worker.WalkStatus == CooperationWalkStatus.Deferred)
        {
            StopWorker();
            return Attention(
                CollectionAttentionReason.DestinationUnavailable,
                "Thorstein could not find a way between the cart and the chest; he keeps what he carries.");
        }

        WalkWorker(RendezvousPlanner.StandOff(target, _worker.Position, _limits.CartStandOffMetres));
        return DeadlineOr(now, waitingFor, CooperationStep.Working);
    }

    private CooperationTick AfterUnloading(float now)
    {
        DeliveryTrips++;
        if (EvaluateCheckpoint() == Checkpoint.Complete)
        {
            return EnterCompleting(now);
        }

        int remaining = LedgerTotal(CustodyPlace.Cart) + LedgerTotal(CustodyPlace.Worker);
        if (_destinationBlocked && remaining > 0)
        {
            return PauseFor(
                CollectionAttentionReason.DestinationFull,
                "The chest is full; " + DescribeLedger(CustodyPlace.Cart) + " stay in the cart and Thorstein keeps " +
                DescribeLedger(CustodyPlace.Worker) + ".");
        }

        if (LedgerTotal(CustodyPlace.Cart) > 0)
        {
            return Attention(
                CollectionAttentionReason.DestinationUnavailable,
                "The cart could not be emptied into the chest; " + DescribeLedger(CustodyPlace.Cart) + " stay in the cart.");
        }

        ReleaseWorker();
        return EnterStaging(now, restartCandidates: false);
    }

    // --- Completing -----------------------------------------------------------------------------------

    private CooperationTick EnterCompleting(float now)
    {
        SetPhase(CooperationPhase.Completing, CollectionAttentionReason.Unspecified, string.Empty);
        _deadline = new PhaseDeadline(now, _limits.RendezvousTimeoutSeconds);
        return TickCompleting(now);
    }

    private CooperationTick TickCompleting(float now)
    {
        StopWorker();
        if (!_connected)
        {
            return Complete(string.Empty);
        }

        _pendingCancel ??= new CancelHaulMessage(
            _providerEpoch, _ids.Next('c'), _ids.HaulId, HaulCancelDisposition.DetachAndPark, _haulRevision);
        HaulCall<HaulPhaseReply> call = _client.CancelHaul(_pendingCancel, now);
        switch (call.Outcome)
        {
            case HaulCallOutcome.Succeeded:
                return Complete("Gunnar is parking the cart.");
            case HaulCallOutcome.Refused when call.Reason == HaulWireReason.UnknownHaul:
                return Complete("Gunnar's haul had already ended.");
            case HaulCallOutcome.NoAnswer when !_client.IsProviderLost && !(_deadline?.IsExpired(now) ?? false):
                return Emit(CooperationStep.Working);
            default:
                // Everything is delivered: Gunnar's parking is Teamster's to
                // finish, and never holds the order hostage.
                return Complete("Gunnar could not be told to park (" + Explain(call) + "); see his panel in Concerned Teamster.");
        }
    }

    private CooperationTick Complete(string detail)
    {
        _pendingCancel = null;
        SetPhase(CooperationPhase.Completed, CollectionAttentionReason.Unspecified, detail);
        return Emit(CooperationStep.Delivered);
    }

    // --- Legs -------------------------------------------------------------------------------------------

    private void StartLeg(HaulLegPurpose purpose, SitePoint target, float now)
    {
        _pendingLeg = RebuildLeg(purpose, target);
        _legAccepted = false;
        _deadline = new PhaseDeadline(now, _limits.RendezvousTimeoutSeconds);
        if (purpose == HaulLegPurpose.ToRendezvous)
        {
            Rendezvous = target;
            RendezvousRevision++;
            PlanRevision++;
        }
    }

    private RequestHaulMessage RebuildLeg(HaulLegPurpose purpose, SitePoint target)
    {
        float radius = purpose == HaulLegPurpose.ToDestination
            ? _limits.DestinationArrivalRadiusMetres
            : _limits.RendezvousArrivalRadiusMetres;
        return new RequestHaulMessage(
            _providerEpoch, _ids.Next(purpose == HaulLegPurpose.ToDestination ? 'x' : 'r'), _haulRevision,
            _order.Order.Value, _ids.HaulId, _leaseId, purpose, ToWork(target), radius);
    }

    /// <summary>Sends the pending leg. Null when the run continues (accepted,
    /// or retrying); a tick when the run stops or moved on.</summary>
    private CooperationTick? SendLeg(float now)
    {
        RequestHaulMessage leg = _pendingLeg!;
        HaulCall<RequestHaulReply> call = _client.RequestHaul(leg, now);
        switch (call.Outcome)
        {
            case HaulCallOutcome.Succeeded:
                _legAccepted = true;
                _haulStarted = true;
                _haulRevision = call.Reply!.Revision;
                PlanRevision++;
                return null;

            case HaulCallOutcome.Refused:
                if (CooperationReasons.IsRouteRefusal(call.Reason))
                {
                    if (leg.Purpose == HaulLegPurpose.ToRendezvous && _candidateIndex + 1 < _candidates.Count)
                    {
                        _candidateIndex++;
                        StartLeg(HaulLegPurpose.ToRendezvous, _candidates[_candidateIndex], now);
                        return null;
                    }

                    return Attention(
                        CollectionAttentionReason.HaulerNeedsAttention,
                        leg.Purpose == HaulLegPurpose.ToRendezvous
                            ? "Gunnar found no cart-safe way to any meeting point (" + call.ReasonName + ")."
                            : "Gunnar found no cart-safe way to the chest (" + call.ReasonName + ").");
                }

                switch (call.Reason)
                {
                    case HaulWireReason.NoLease:
                    case HaulWireReason.LeaseInvalidated:
                        return Reconcile(CollectionAttentionReason.CartLeaseLost, "Gunnar no longer holds the cart.", now);
                    case HaulWireReason.HaulBusy:
                        return WhyBusy(now);
                    case HaulWireReason.DuplicateRequestDifferentPayload:
                        return Attention(CollectionAttentionReason.HaulerNeedsAttention, "Concerned Teamster refused a reused request id.");
                    default:
                        return PauseFor(CooperationReasons.ForHaul(call.Reason), "Gunnar refused the haul (" + Explain(call) + ").");
                }

            case HaulCallOutcome.Stale:
                if (call.Reason == HaulWireReason.RevisionMismatch && RefreshRevision(now))
                {
                    StartLeg(leg.Purpose, ToSite(leg.Target), now);
                    return null;
                }

                return call.Reason == HaulWireReason.EpochMismatch
                    ? Reconcile(CollectionAttentionReason.CartLeaseLost, "Concerned Teamster's world was reloaded.", now, providerEpochLost: true)
                    : null;

            case HaulCallOutcome.Unavailable:
                return PauseFor(CooperationReasons.ForHaul(call.Reason), "Gunnar cannot haul now (" + Explain(call) + ").");

            default:
                // No answer: the same message goes again next tick, so a leg the
                // provider did apply is answered AlreadySatisfied, not repeated.
                return _client.IsProviderLost
                    ? Reconcile(CollectionAttentionReason.HaulerUnavailable, "Concerned Teamster stopped answering.", now)
                    : null;
        }
    }

    /// <summary>A leg refused as busy: this order's own haul may have been
    /// stopped by Gunnar's side (then reconcile with his reason), may still be
    /// finishing a step (then wait), or the busy haul is someone else's.
    /// </summary>
    private CooperationTick? WhyBusy(float now)
    {
        HaulCall<GetHaulReply> own = _client.GetHaul(_ids.HaulId, now);
        if (own.Succeeded)
        {
            GetHaulReply haul = own.Reply!;
            if (!CooperationReasons.IsProviderEnded(haul.Phase))
            {
                _pendingLeg = null;
                return null;
            }

            return Reconcile(
                haul.HasAttention ? CooperationReasons.ForHaul(haul.Attention) : CollectionAttentionReason.HaulerNeedsAttention,
                "Gunnar stopped (" + (haul.HasAttention ? haul.AttentionName : haul.Phase.ToString()) + ").", now);
        }

        return PauseFor(CollectionAttentionReason.HaulerUnavailable, "Gunnar is busy with another haul.");
    }

    private bool RefreshRevision(float now)
    {
        HaulCall<DescribeLeaseReply> lease = _client.DescribeLease(now);
        if (!lease.Succeeded || lease.FromCache)
        {
            return false;
        }

        _haulRevision = lease.Reply!.Revision;
        return true;
    }

    /// <summary>Polls this order's haul. False with a tick when the haul ended
    /// under the run (reconciled); false without one when there is simply no
    /// fresh answer yet.</summary>
    private bool TryPollHaul(float now, out GetHaulReply? haul, out CooperationTick? ended)
    {
        haul = null;
        ended = null;
        HaulCall<GetHaulReply> call = _client.GetHaul(_ids.HaulId, now);
        switch (call.Outcome)
        {
            case HaulCallOutcome.Succeeded:
                haul = call.Reply!;
                _haulRevision = haul.Revision;
                if (haul.CartPosition.HasValue)
                {
                    _cartPosition = ToSite(haul.CartPosition.Value);
                }

                if (CooperationReasons.IsProviderEnded(haul.Phase))
                {
                    string why = haul.HasAttention ? haul.AttentionName : haul.Phase.ToString();
                    ended = Reconcile(
                        haul.HasAttention ? CooperationReasons.ForHaul(haul.Attention) : CollectionAttentionReason.HaulerNeedsAttention,
                        "Gunnar stopped (" + why + ").", now);
                    return false;
                }

                return true;

            case HaulCallOutcome.Refused:
                ended = Reconcile(
                    call.Reason == HaulWireReason.UnknownHaul
                        ? CollectionAttentionReason.HaulerNeedsAttention
                        : CooperationReasons.ForHaul(call.Reason),
                    "Gunnar's haul ended (" + Explain(call) + ").", now);
                return false;

            case HaulCallOutcome.Stale:
                ended = Reconcile(CollectionAttentionReason.CartLeaseLost, "Concerned Teamster's world was reloaded.", now, providerEpochLost: true);
                return false;

            case HaulCallOutcome.Unavailable:
                ended = Reconcile(CooperationReasons.ForHaul(call.Reason), "Gunnar cannot continue (" + Explain(call) + ").", now);
                return false;

            default:
                if (_client.IsProviderLost)
                {
                    ended = Reconcile(CollectionAttentionReason.HaulerUnavailable, "Concerned Teamster stopped answering.", now);
                }

                return false;
        }
    }

    private static bool IsWaitingStill(GetHaulReply haul) => haul.Phase == HaulWirePhase.Waiting && haul.CartStill;

    // --- The transfer hold ----------------------------------------------------------------------------

    /// <summary>Asks Gunnar to hold the waiting cart still, at the revision just
    /// read. Null to continue (held, or not yet); a tick to stop.</summary>
    private CooperationTick? AcquireHold(float now)
    {
        if (_pendingHold == null)
        {
            if (!TryPollHaul(now, out GetHaulReply? haul, out CooperationTick? ended))
            {
                return ended;
            }

            if (!IsWaitingStill(haul!) || !_cartPosition.HasValue)
            {
                return null;
            }

            if (_worker.Position.HorizontalDistanceTo(_cartPosition.Value) > _limits.CartReachMetres)
            {
                if (Phase == CooperationPhase.Loading)
                {
                    SetPhase(CooperationPhase.ApproachingCart, CollectionAttentionReason.Unspecified, string.Empty);
                    _deadline = new PhaseDeadline(now, _limits.RendezvousTimeoutSeconds);
                }
                else
                {
                    _unloadStep = UnloadStep.ReturnToCart;
                }

                return Emit(CooperationStep.Working);
            }

            HaulCall<DescribeLeaseReply> lease = _client.DescribeLease(now);
            if (lease.Succeeded)
            {
                DescribeLeaseReply described = lease.Reply!;
                if (!string.Equals(described.LeaseId, _leaseId, StringComparison.Ordinal) ||
                    !string.Equals(described.CartSessionKey, _cartKey, StringComparison.Ordinal))
                {
                    return Reconcile(CollectionAttentionReason.CartLeaseLost, "Gunnar's lease changed to another cart.", now);
                }

                if (!described.CartUpright)
                {
                    return Reconcile(CollectionAttentionReason.HaulerNeedsAttention, "The cart is not upright.", now);
                }
            }
            else if (lease.Outcome == HaulCallOutcome.Refused)
            {
                return Reconcile(CooperationReasons.ForHaul(lease.Reason), "Gunnar no longer holds the cart.", now);
            }
            else
            {
                return null;
            }

            _pendingHold = new AcknowledgeWaitMessage(
                _providerEpoch, _ids.Next('h'), haul!.Revision, _ids.HaulId, HaulWaitActivity.Transferring);
        }

        HaulCall<HaulPhaseReply> call = _client.AcknowledgeWait(_pendingHold, now);
        switch (call.Outcome)
        {
            case HaulCallOutcome.Succeeded:
                _holdActive = true;
                _holdUncertain = false;
                _holdRevision = call.Reply!.Revision;
                _haulRevision = call.Reply.Revision;
                _pendingHold = null;
                PlanRevision++;
                return null;

            case HaulCallOutcome.Refused when call.Reason == HaulWireReason.HaulBusy:
                // Not waiting still any more (or not yet): read again next tick.
                _pendingHold = null;
                return null;

            case HaulCallOutcome.Refused:
                _pendingHold = null;
                return Reconcile(CooperationReasons.ForHaul(call.Reason), "Gunnar refused to hold the cart (" + Explain(call) + ").", now);

            case HaulCallOutcome.Stale:
                _pendingHold = null;
                return call.Reason == HaulWireReason.EpochMismatch
                    ? Reconcile(CollectionAttentionReason.CartLeaseLost, "Concerned Teamster's world was reloaded.", now, providerEpochLost: true)
                    : null;

            case HaulCallOutcome.Unavailable:
                _pendingHold = null;
                return PauseFor(CooperationReasons.ForHaul(call.Reason), "Gunnar cannot hold the cart now (" + Explain(call) + ").");

            default:
                _holdUncertain = true;
                return _client.IsProviderLost
                    ? Reconcile(CollectionAttentionReason.HaulerUnavailable, "Concerned Teamster stopped answering.", now)
                    : null;
        }
    }

    /// <summary>Tells Gunnar the transfer is done. Null to continue (released,
    /// or retrying); a tick to stop.</summary>
    private CooperationTick? ReleaseHold(float now)
    {
        if (!_holdActive)
        {
            return null;
        }

        _pendingRelease ??= new AcknowledgeWaitMessage(
            _providerEpoch, _ids.Next('d'), _holdRevision, _ids.HaulId, HaulWaitActivity.Done);
        HaulCall<HaulPhaseReply> call = _client.AcknowledgeWait(_pendingRelease, now);
        switch (call.Outcome)
        {
            case HaulCallOutcome.Succeeded:
                _holdActive = false;
                _holdUncertain = false;
                _pendingRelease = null;
                _haulRevision = call.Reply!.Revision;
                PlanRevision++;
                return null;

            case HaulCallOutcome.Stale when call.Reason == HaulWireReason.RevisionMismatch:
                _pendingRelease = null;
                if (TryPollHaul(now, out GetHaulReply? haul, out CooperationTick? ended))
                {
                    if (haul!.Phase == HaulWirePhase.Unloading)
                    {
                        _holdRevision = haul.Revision;
                        return null;
                    }

                    _holdActive = false;
                    return null;
                }

                _holdActive = false;
                return ended;

            case HaulCallOutcome.Refused:
            case HaulCallOutcome.Stale:
                // The hold is no longer there to release (HaulBusy: not
                // unloading; UnknownHaul; a reload).
                _holdActive = false;
                _pendingRelease = null;
                return call.Reason == HaulWireReason.HaulBusy
                    ? null
                    : Reconcile(CooperationReasons.ForHaul(call.Reason), "Gunnar's hold ended (" + Explain(call) + ").", now);

            default:
                return _client.IsProviderLost
                    ? Reconcile(CollectionAttentionReason.HaulerUnavailable, "Concerned Teamster stopped answering.", now)
                    : null;
        }
    }

    private void TryReleaseHoldOnce(float now)
    {
        if (!_holdActive)
        {
            return;
        }

        _pendingRelease ??= new AcknowledgeWaitMessage(
            _providerEpoch, _ids.Next('d'), _holdRevision, _ids.HaulId, HaulWaitActivity.Done);
        HaulCall<HaulPhaseReply> call = _client.AcknowledgeWait(_pendingRelease, now);
        if (call.Outcome == HaulCallOutcome.Succeeded ||
            call.Outcome == HaulCallOutcome.Refused || call.Outcome == HaulCallOutcome.Stale)
        {
            _holdActive = false;
            _holdUncertain = false;
            _pendingRelease = null;
        }
    }

    private void TrySendCancelOnce(HaulCancelDisposition disposition, float now)
    {
        if (_providerEpoch == Guid.Empty)
        {
            LastCancelOutcome = "not sent: never connected";
            return;
        }

        var cancel = new CancelHaulMessage(_providerEpoch, _ids.Next('c'), _ids.HaulId, disposition, _haulRevision);
        HaulCall<HaulPhaseReply> call = _client.CancelHaul(cancel, now);
        if (call.Outcome == HaulCallOutcome.NoAnswer && !_client.IsProviderLost)
        {
            call = _client.CancelHaul(cancel, now);
        }

        LastCancelOutcome = call.Succeeded
            ? disposition + " accepted (" + call.Reply!.Phase + ")"
            : disposition + " not confirmed (" + Explain(call) + ")";
    }

    // --- Transfers ----------------------------------------------------------------------------------

    private readonly struct TransferBatch
    {
        private TransferBatch(TransferBatchKind kind, int moved, bool blocked, string evidence)
        {
            Kind = kind;
            Moved = moved;
            Blocked = blocked;
            Evidence = evidence;
        }

        public TransferBatchKind Kind { get; }

        public int Moved { get; }

        /// <summary>The receiving side could not take everything.</summary>
        public bool Blocked { get; }

        public string Evidence { get; }

        public static TransferBatch Done(int moved, bool blocked) => new TransferBatch(TransferBatchKind.Done, moved, blocked, string.Empty);

        public static TransferBatch Later(string evidence) => new TransferBatch(TransferBatchKind.RetryLater, 0, false, evidence);

        public static TransferBatch Failed(TransferBatchKind kind, string evidence) => new TransferBatch(kind, 0, false, evidence);
    }

    private enum TransferBatchKind
    {
        Unspecified = 0,
        Done = 1,

        /// <summary>Refused or stale, nothing moved, retries left.</summary>
        RetryLater = 2,

        /// <summary>Refused every time it was allowed to be.</summary>
        Exhausted = 3,

        /// <summary>The executor could not prove what happened.</summary>
        Uncertain = 4,

        /// <summary>The record and the actual inventory disagree.</summary>
        Mismatch = 5,
    }

    /// <summary>Moves this order's material of every quota resource from one
    /// custody place to another through the executor: the record's count,
    /// capped by what the receiver can take, never more than is actually there.
    /// Credits exactly the accepted units the receipts report.</summary>
    private TransferBatch MoveAll(
        CustodyPlace fromPlace, CustodyLocation from, IInventoryPort fromPort, CustodyLocation to, IInventoryPort toPort, float now)
    {
        if (_transferRefusals.IsWaiting(now))
        {
            return TransferBatch.Later("waiting to retry a refused transfer");
        }

        IMaterialCustodyView view = _custody.View;
        int moved = 0;
        bool blocked = false;
        foreach (ResourceQuota quota in _order.Quotas)
        {
            MaterialItem item = MaterialItem.Of(quota.Resource);
            int recorded = view.CountAt(_order.Order, fromPlace, quota.Resource);
            if (recorded <= 0)
            {
                continue;
            }

            int actual = fromPort.Count(item);
            if (actual < recorded)
            {
                return TransferBatch.Failed(
                    TransferBatchKind.Mismatch,
                    "The record shows " + Units(recorded, quota.Resource) + " in " + fromPort.Describe + " but " +
                    actual.ToString(CultureInfo.InvariantCulture) + " are there.");
            }

            int fits = toPort.CanAccept(item, recorded);
            if (fits <= 0)
            {
                blocked = true;
                continue;
            }

            int count = Math.Min(recorded, fits);
            blocked |= count < recorded;
            var intent = new TransferIntent(_ids.NextTransfer(), _order.Order, from, to, item, count, view.Revision);
            TransferReceipt receipt = _custody.Executor.Execute(intent, fromPort, toPort);
            switch (receipt.Outcome)
            {
                case TransferOutcome.Completed:
                case TransferOutcome.AlreadySatisfied:
                    moved += receipt.Accepted;
                    break;

                case TransferOutcome.Partial:
                    moved += receipt.Accepted;
                    blocked = true;
                    break;

                case TransferOutcome.Refused:
                case TransferOutcome.Stale:
                    RetryDecision decision = _transferRefusals.RecordFailure(now);
                    string refused = "Moving " + Units(count, quota.Resource) + " from " + fromPort.Describe + " to " +
                        toPort.Describe + " was " + receipt.Outcome.ToString().ToLowerInvariant() +
                        (receipt.Evidence.Length > 0 ? ": " + receipt.Evidence : ".");
                    return decision.GiveUp
                        ? TransferBatch.Failed(TransferBatchKind.Exhausted, refused)
                        : TransferBatch.Later(refused);

                default:
                    return TransferBatch.Failed(
                        TransferBatchKind.Uncertain,
                        "Moving " + Units(count, quota.Resource) + " from " + fromPort.Describe + " to " + toPort.Describe +
                        " could not be confirmed (" + receipt.Outcome + (receipt.Evidence.Length > 0 ? ": " + receipt.Evidence : string.Empty) + ").");
            }
        }

        _transferRefusals.Reset();
        return TransferBatch.Done(moved, blocked);
    }

    /// <summary>Null when the batch finished; otherwise the tick that stops
    /// the run, the hold released first so the cart is not left held.</summary>
    private CooperationTick? HandleBatch(TransferBatch batch, float now, bool holdToRelease = true)
    {
        switch (batch.Kind)
        {
            case TransferBatchKind.Done:
                return null;
            case TransferBatchKind.RetryLater:
                return Emit(CooperationStep.Working);
        }

        if (holdToRelease)
        {
            TryReleaseHoldOnce(now);
        }

        switch (batch.Kind)
        {
            case TransferBatchKind.Uncertain:
                return Attention(CollectionAttentionReason.TransferUncertain, batch.Evidence);
            case TransferBatchKind.Mismatch:
                return Attention(CollectionAttentionReason.ReconciliationMismatch, batch.Evidence);
            default:
                return _custody.IsWritable
                    ? Attention(CollectionAttentionReason.ReconciliationMismatch, batch.Evidence)
                    : PauseFor(CollectionAttentionReason.JournalReadOnly, batch.Evidence);
        }
    }

    // --- Reconciliation ---------------------------------------------------------------------------

    /// <summary>§3.3 and COOP-03: the provider or the cart went away under the
    /// run. No transfer starts; the hold is released if that can still be
    /// said; the cart's custody location is compared with the cart's actual
    /// contents; only then does the run pause (or ask for attention when the
    /// two disagree or the cart cannot be reached). Nothing is credited, moved
    /// or refunded here.</summary>
    private CooperationTick Reconcile(CollectionAttentionReason reason, string detail, float now, bool providerEpochLost = false)
    {
        SetPhase(CooperationPhase.Reconciling, CollectionAttentionReason.Unspecified, detail);
        _pendingLeg = null;
        _pendingHold = null;
        _legAccepted = false;
        TryReleaseHoldOnce(now);
        StopWorker();

        int inCart = LedgerTotal(CustodyPlace.Cart);
        if (inCart == 0)
        {
            return PauseFor(reason, detail);
        }

        // After the provider's world reloaded, the cart key is from a previous
        // world load: it may now name another object entirely, so it is never
        // resolved, and the record cannot be confirmed from here.
        IInventoryPort? cartPort = null;
        if (providerEpochLost || _cartKey.Length == 0 ||
            !_custody.TryResolveCart(_cartKey, _providerEpoch, out cartPort, out _))
        {
            return Attention(
                CollectionAttentionReason.CartLeaseLost,
                detail + " The record shows " + DescribeLedger(CustodyPlace.Cart) + " in the cart, which cannot be reached to confirm it.");
        }

        var evidence = new StringBuilder();
        foreach (ResourceQuota quota in _order.Quotas)
        {
            int recorded = _custody.View.CountAt(_order.Order, CustodyPlace.Cart, quota.Resource);
            int actual = cartPort!.Count(MaterialItem.Of(quota.Resource));
            if (recorded != actual)
            {
                evidence.Append(" The record shows ").Append(Units(recorded, quota.Resource))
                    .Append(" in the cart; it holds ").Append(actual.ToString(CultureInfo.InvariantCulture)).Append('.');
            }
        }

        if (evidence.Length > 0)
        {
            return Attention(CollectionAttentionReason.ReconciliationMismatch, detail + evidence);
        }

        return PauseFor(reason, detail + " " + DescribeLedger(CustodyPlace.Cart) + " stay in the cart.");
    }

    // --- Checkpoints ------------------------------------------------------------------------------

    private Checkpoint EvaluateCheckpoint()
    {
        IMaterialCustodyView view = _custody.View;
        bool allDelivered = true;
        int onGround = 0;
        int stillToCollect = 0;
        foreach (ResourceQuota quota in _order.Quotas)
        {
            ResourceProgress progress = view.ProgressFor(_order, quota.Resource);
            allDelivered &= progress.IsDelivered;
            onGround += progress.OnGround;
            stillToCollect += progress.StillToCollect;
        }

        if (allDelivered)
        {
            return Checkpoint.Complete;
        }

        if (onGround > 0)
        {
            // Thorstein picks up what his own pick dropped before anything else.
            return Checkpoint.Collect;
        }

        int carried = LedgerTotal(CustodyPlace.Worker);
        if (carried == 0)
        {
            return stillToCollect == 0 && LedgerTotal(CustodyPlace.Cart) > 0 ? Checkpoint.HaulNow : Checkpoint.Collect;
        }

        if (stillToCollect == 0)
        {
            return Checkpoint.LoadNow;
        }

        if (!_custody.TryResolveWorker(out IInventoryPort? pack, out _) || pack == null)
        {
            return Checkpoint.Collect;
        }

        foreach (ResourceQuota quota in _order.Quotas)
        {
            if (view.ProgressFor(_order, quota.Resource).StillToCollect > 0 &&
                pack.CanAccept(MaterialItem.Of(quota.Resource), 1) > 0)
            {
                return Checkpoint.Collect;
            }
        }

        // Full for everything still needed: take the load to the cart.
        return Checkpoint.LoadNow;
    }

    private int LedgerTotal(CustodyPlace place)
    {
        int total = 0;
        foreach (ResourceQuota quota in _order.Quotas)
        {
            total += _custody.View.CountAt(_order.Order, place, quota.Resource);
        }

        return total;
    }

    private int StillToCollectTotal()
    {
        int total = 0;
        foreach (ResourceQuota quota in _order.Quotas)
        {
            total += _custody.View.ProgressFor(_order, quota.Resource).StillToCollect;
        }

        return total;
    }

    private int CommittedTotal()
    {
        int total = 0;
        foreach (ResourceQuota quota in _order.Quotas)
        {
            total += _custody.View.ProgressFor(_order, quota.Resource).Committed;
        }

        return total;
    }

    private string DescribeLedger(CustodyPlace place)
    {
        var parts = new List<string>();
        foreach (ResourceQuota quota in _order.Quotas)
        {
            int count = _custody.View.CountAt(_order.Order, place, quota.Resource);
            if (count > 0)
            {
                parts.Add(Units(count, quota.Resource));
            }
        }

        return parts.Count == 0 ? "nothing" : string.Join(" and ", parts.ToArray());
    }

    private static string Units(int count, CollectedResource resource) =>
        count.ToString(CultureInfo.InvariantCulture) + " " + CollectedResources.ItemPrefabName(resource);

    // --- Thorstein ------------------------------------------------------------------------------------

    private void WalkWorker(SitePoint target)
    {
        bool retarget = !_walkTarget.HasValue || _walkTarget.Value.HorizontalDistanceTo(target) > 0.5f ||
            _worker.WalkStatus == CooperationWalkStatus.Idle;
        if (retarget && _worker.WalkTo(target, _limits.WalkToleranceMetres))
        {
            _walkTarget = target;
            _commandingWorker = true;
        }
    }

    private void StopWorker()
    {
        if (_commandingWorker)
        {
            _worker.Stop();
        }

        _commandingWorker = false;
        _walkTarget = null;
    }

    /// <summary>Hands Thorstein back to the collection loop.</summary>
    private void ReleaseWorker() => StopWorker();

    // --- Plumbing -----------------------------------------------------------------------------------

    private CooperationTick DeadlineOr(float now, string waitingFor, CooperationStep step)
    {
        if (_deadline.HasValue && _deadline.Value.IsExpired(now))
        {
            return TimedOut(waitingFor, now);
        }

        return Emit(step);
    }

    /// <summary>§7: a rendezvous wait that ran out stops Gunnar where he is and
    /// asks the player, with what was being waited for.</summary>
    private CooperationTick TimedOut(string waitingFor, float now)
    {
        TryReleaseHoldOnce(now);
        if (_haulStarted)
        {
            TrySendCancelOnce(HaulCancelDisposition.StopAndWait, now);
        }

        StopWorker();
        return Attention(
            CollectionAttentionReason.RendezvousTimedOut,
            "Waited " + _limits.RendezvousTimeoutSeconds.ToString("0", CultureInfo.InvariantCulture) + " s for " +
            waitingFor + ".");
    }

    private CooperationTick PauseFor(CollectionAttentionReason reason, string detail)
    {
        StopWorker();
        SetPhase(CooperationPhase.Paused, reason, detail);
        return Emit(CooperationStep.Paused);
    }

    private CooperationTick Attention(CollectionAttentionReason reason, string detail)
    {
        StopWorker();
        SetPhase(CooperationPhase.NeedsAttention, reason, detail);
        return Emit(CooperationStep.NeedsAttention);
    }

    private void SetPhase(CooperationPhase phase, CollectionAttentionReason reason, string detail)
    {
        if (Phase != phase || Reason != reason)
        {
            PlanRevision++;
        }

        Phase = phase;
        Reason = reason;
        Detail = detail ?? string.Empty;
    }

    private CooperationTick Emit(CooperationStep step) => new CooperationTick(step, Phase, Reason, Detail, PlanRevision);

    private static CollectionAttentionReason Known(CollectionAttentionReason reason, CollectionAttentionReason fallback) =>
        reason == CollectionAttentionReason.Unspecified ? fallback : reason;

    private static string Explain<TReply>(HaulCall<TReply> call)
        where TReply : class
    {
        string reason = call.ReasonName.Length > 0 ? call.ReasonName : call.Outcome.ToString();
        return call.Detail.Length > 0 ? reason + ": " + call.Detail : reason;
    }

    private static WorkPoint ToWork(SitePoint point) => new WorkPoint(point.X, point.Y, point.Z);

    private static SitePoint ToSite(WorkPoint point) => new SitePoint(point.X, point.Y, point.Z);
}
