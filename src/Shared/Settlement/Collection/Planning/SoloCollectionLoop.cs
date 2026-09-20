using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Tools;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;

namespace TheConcernedCat.Settlement.Collection.Planning;

/// <summary>What the loop is doing inside its order state.</summary>
internal enum CollectionPhase
{
    Unspecified = 0,

    /// <summary>No order.</summary>
    Idle = 1,

    Surveying = 2,

    /// <summary>Choosing and reserving the next source.</summary>
    Selecting = 3,

    WalkingToSource = 4,

    WalkingToDestination = 5,

    Depositing = 6,

    /// <summary>The cooperative delivery holds the order.</summary>
    WithHauler = 7,

    /// <summary>Holding everything for the player.</summary>
    Holding = 8,

    /// <summary>Paused, needs attention, or ended.</summary>
    Stopped = 9,
}

internal enum ControlOutcome
{
    Unspecified = 0,
    Done = 1,

    /// <summary>Already so; nothing changed.</summary>
    Unchanged = 2,

    Refused = 3,
}

internal readonly struct ControlResult
{
    public ControlResult(ControlOutcome outcome, string message)
    {
        Outcome = outcome;
        Message = message ?? string.Empty;
    }

    public ControlOutcome Outcome { get; }

    public string Message { get; }
}

/// <summary>Thorstein's solo collection loop (GATHER-01..06, CONTRACTS.md §4
/// and §7) as a game-free state machine over <see cref="CollectionOrderStates"/>.
///
/// <b>The cycle.</b> Accept → survey → select and reserve a source → walk to it
/// → revalidate → pick it with the game's own pick and take exactly the drops
/// that pick spawned → repeat until a carry checkpoint → deliver (walk to the
/// chosen chest and deposit through the custody executor) or hold for the
/// player → complete. With a hauler, the checkpoint hands the order to the
/// cooperative delivery and takes it back when told to collect more.
///
/// <b>Who owns what.</b> This loop moves nobody itself: it asks
/// <see cref="ICollectionMotion"/>, which only obeys the job holding the
/// worker's <see cref="IActorModeHold"/>. It writes no inventory: picks go
/// through <see cref="ISourcePickupPort"/>, deposits through the custody
/// executor, records through custody, journal first. Progress is always read
/// back from the custody view, never kept here, so a unit is never counted
/// twice and never counted from an estimate.
///
/// <b>Safety rules the code keeps.</b>
/// <list type="bullet">
/// <item>Authority is asked at every checkpoint, and every checkpoint is in the
/// same tick as the mutation it guards.</item>
/// <item>The scope is revalidated before a reservation, before a pickup and
/// before a delivery leg. Changed, unloaded and invalid each pause with their
/// own reason; nothing falls back or widens.</item>
/// <item>Every transition goes through the contract table. A transition that
/// resumes work must be journaled first; one that stops work happens even when
/// the journal cannot take it, because stopping is always safe.</item>
/// <item>Uncertain outcomes are never retried: the order needs attention with
/// the evidence kept. Refusals retry with a ceiling and a doubling backoff.
/// </item>
/// <item>A full or unavailable destination pauses with the materials retained;
/// no other chest is ever chosen.</item>
/// </list></summary>
internal sealed class SoloCollectionLoop
{
    private readonly CollectionParameters _parameters;
    private readonly WorkerKey _workerKey;
    private readonly WorkerId _workerId;
    private readonly IActorModeHold _modes;
    private readonly SourceReservationBook _reservations;
    private readonly ICollectionMotion _motion;
    private readonly ICollectionCustody _custody;
    private readonly ISourcePickupPort _pickup;
    private readonly ISurveyProbe _probe;
    private readonly ICollectionWorld _world;
    private readonly ICollectionCooperation? _cooperation;
    private readonly CollectionRequestIds _requestIds;
    private readonly Action<string>? _log;

    /// <summary>Sources this order will not try again: unreachable, refused.
    /// Kept for the whole order so a failure cannot oscillate.</summary>
    private readonly HashSet<SourceKey> _failed = new HashSet<SourceKey>();

    /// <summary>Sources of the current snapshot already picked, or held by
    /// another order. Cleared with a new snapshot, which sees the truth.</summary>
    private readonly HashSet<SourceKey> _spentThisSnapshot = new HashSet<SourceKey>();

    private readonly BoundedRetry _walkRetry;
    private readonly BoundedRetry _depositRetry;

    private CollectionOrderDefinition? _order;
    private string _jobId = string.Empty;
    private CollectionOrderState _pausedFrom;
    private SurveyScheduler? _survey;
    private SurveySnapshot? _snapshot;
    private int _surveyRevision;
    private int _fruitlessSurveys;
    private SourceObservation? _target;
    private PhaseDeadline? _legDeadline;
    private float _lastReadinessAt = float.NegativeInfinity;
    private bool _tripOpen;
    private float _lastTickAt;

    public SoloCollectionLoop(
        CollectionParameters parameters,
        WorkerKey workerKey,
        WorkerId workerId,
        IActorModeHold modes,
        SourceReservationBook reservations,
        ICollectionMotion motion,
        ICollectionCustody custody,
        ISourcePickupPort pickup,
        ISurveyProbe probe,
        ICollectionWorld world,
        ICollectionCooperation? cooperation,
        CollectionRequestIds requestIds,
        Action<string>? log = null)
    {
        _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        if (workerKey.IsEmpty || workerId.IsEmpty)
        {
            throw new ArgumentException("A loop drives one named worker.");
        }

        _workerKey = workerKey;
        _workerId = workerId;
        _modes = modes ?? throw new ArgumentNullException(nameof(modes));
        if (!_modes.Worker.Equals(workerKey))
        {
            throw new ArgumentException("The actor-mode owner belongs to a different worker.", nameof(modes));
        }

        _reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
        _motion = motion ?? throw new ArgumentNullException(nameof(motion));
        _custody = custody ?? throw new ArgumentNullException(nameof(custody));
        _pickup = pickup ?? throw new ArgumentNullException(nameof(pickup));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _cooperation = cooperation;
        _requestIds = requestIds ?? throw new ArgumentNullException(nameof(requestIds));
        _log = log;
        _walkRetry = new BoundedRetry(parameters.WalkMaxFailures, parameters.WalkFirstRetrySeconds, parameters.WalkMaxRetrySeconds);
        _depositRetry = new BoundedRetry(
            parameters.TransferMaxFailures, parameters.TransferFirstRetrySeconds, parameters.TransferMaxRetrySeconds);
    }

    public CollectionOrderDefinition? Order => _order;

    /// <summary>Unspecified while there is no order.</summary>
    public CollectionOrderState State { get; private set; }

    /// <summary>The one actionable reason while Paused or NeedsAttention;
    /// Unspecified otherwise, and also when the player paused it.</summary>
    public CollectionAttentionReason Reason { get; private set; }

    public CollectionPhase Phase { get; private set; } = CollectionPhase.Idle;

    public bool PausedByPlayer { get; private set; }

    public SourceObservation? Target => _target;

    public SurveySnapshot? Snapshot => _snapshot;

    public SurveyAccounting? LastAccounting { get; private set; }

    public SitePoint LastWorkerPosition { get; private set; }

    public int Picks { get; private set; }

    public int Trips { get; private set; }

    public int SurveysRun { get; private set; }

    /// <summary>Transitions that happened but could not be journaled (only ever
    /// stopping ones).</summary>
    public int UnrecordedTransitions { get; private set; }

    /// <summary>Transitions the contract table refused. Always zero unless
    /// there is a bug.</summary>
    public int IllegalTransitionsAttempted { get; private set; }

    public string LastEvidence { get; private set; } = string.Empty;

    public bool HasActiveOrder => _order != null && !CollectionOrderStates.IsTerminal(State);

    private bool IsStopped =>
        State == CollectionOrderState.Paused || State == CollectionOrderState.NeedsAttention ||
        CollectionOrderStates.IsTerminal(State);

    private CustodyLocation WorkerLocation => new CustodyLocation(CustodyPlace.Worker, _workerKey.Value, Guid.Empty);

    // --- Acceptance --------------------------------------------------------------------------------------

    /// <summary>Accepts an order (GATHER-01): every check first, then the
    /// worker's identity is held, then the acceptance is journaled, and only
    /// then does any work start.</summary>
    /// <param name="defaultCirclePreviewed">The player was shown this exact
    /// default circle before giving the order (GATHER-02).</param>
    /// <param name="yieldPerPick">Units one pick gives per resource under the
    /// world's resource rate.</param>
    public CollectionIntakeRefusal Accept(
        CollectionOrderDefinition order, bool defaultCirclePreviewed,
        IReadOnlyDictionary<CollectedResource, int> yieldPerPick, float now)
    {
        if (order == null)
        {
            throw new ArgumentNullException(nameof(order));
        }

        if (!order.Worker.Equals(_workerId))
        {
            return CollectionIntakeRefusal.WrongWorker;
        }

        bool destinationResolved = true;
        CollectionAttentionReason destinationRefusal = CollectionAttentionReason.Unspecified;
        if (order.Delivery.Kind == DeliveryKind.Container)
        {
            destinationResolved = _custody.TryResolveDestination(order.Delivery, out _, out destinationRefusal);
        }

        bool haulerAvailable = order.Participation != ParticipationMode.WithHauler ||
            (_cooperation != null && _cooperation.IsAvailable(out _));

        var facts = new CollectionIntakeFacts(
            authority: _world.EvaluateAuthority(),
            custodyAvailable: true,
            journalWritable: _custody.IsWritable,
            // The record's answer, not only this session's: after a reload the
            // loop may not have adopted the order yet, and "another order is
            // active" is the truth the player needs rather than a refusal from
            // the journal further down (B1).
            anotherOrderActive: HasActiveOrder || RecordHoldsAnotherOrder(order),
            workerBusy: _modes.JobId != null && !_modes.IsHeldBy(order.Order.Value),
            workerPresent: _motion.IsPresent,
            readiness: _world.AssessReadiness(order.Worker),
            scope: ScopeCheckpoint.Revalidate(order.Scope, _world.ObserveScope(order.Scope), _world.IsLoaded),
            defaultCirclePreviewed: defaultCirclePreviewed,
            yieldPerPick: yieldPerPick,
            destinationResolved: destinationResolved,
            destinationRefusal: destinationRefusal,
            workerCarryWeight: _parameters.WorkerCarryWeight,
            unitWeight: _world.UnitWeight,
            haulerAvailable: haulerAvailable);

        CollectionIntakeRefusal refusal = CollectionIntake.Check(order, facts);
        if (refusal != CollectionIntakeRefusal.Unspecified)
        {
            return refusal;
        }

        string jobId = order.Order.Value;

        // Only a GRANT starts the order. This used to look for RefusedBusy
        // alone, which was true of the one implementation that existed at the
        // time and is a defect now that the mode can come from Concerned NPC's
        // arbiter: that answers Unspecified for an identity it does not track -
        // a registration refused at load, a library that failed to come up - and
        // "not RefusedBusy" would have read that as permission. The order would
        // then run with nothing holding the worker, so his body could be retired
        // out from under it and every WalkTo would be silently refused by a
        // motion port that obeys only the holder. Asking the positive question
        // also survives the next outcome anybody adds.
        if (!ActorModeGrants.IsGranted(_modes.Enter(ActorMode.Surveying, jobId)))
        {
            return CollectionIntakeRefusal.WorkerBusy;
        }

        if (!_custody.RecordAccepted(order))
        {
            _modes.Release(jobId);
            return CollectionIntakeRefusal.NotRecorded;
        }

        StartOrder(order, now);
        return CollectionIntakeRefusal.Unspecified;
    }

    private bool RecordHoldsAnotherOrder(CollectionOrderDefinition order)
    {
        return _custody.TryRecoverOrder(order.Worker, out CollectionOrderDefinition? recorded, out _) &&
            recorded != null && !recorded.Order.Equals(order.Order);
    }

    /// <summary>C2 (B1): adopts the non-terminal order the record kept across a
    /// reload, in the state the record gives it. Nothing is journaled here —
    /// custody already recorded the state on load — and nothing resumes by
    /// itself: the order is stopped, its identity is held so nothing else takes
    /// the worker, and the player can see it, rebind it or cancel it.
    ///
    /// <b>Why adoption alone matters.</b> Without it every command that could
    /// end the order answers "there is no order" while the record says there is
    /// one, so the material in the worker's body cannot be released and the body
    /// cannot be retired — and the uninstall procedure would then delete both.
    /// </summary>
    public bool AdoptRecovered(float now)
    {
        if (_order != null && !CollectionOrderStates.IsTerminal(State))
        {
            return false;
        }

        if (!_custody.TryRecoverOrder(_workerId, out CollectionOrderDefinition? recovered, out CollectionOrderState recordedState) ||
            recovered == null || CollectionOrderStates.IsTerminal(recordedState))
        {
            return false;
        }

        string jobId = recovered.Order.Value;
        if (_modes.JobId != null && !_modes.IsHeldBy(jobId))
        {
            // Another job holds the worker; adopting would take him from it.
            return false;
        }

        StartOrder(recovered, now);
        Adopted = true;
        _pausedFrom = CollectionOrderState.Surveying;

        // The record's own state, kept as it stands: custody decided it during
        // its load (Paused with a reason, or NeedsAttention).
        State = recordedState == CollectionOrderState.NeedsAttention
            ? CollectionOrderState.NeedsAttention
            : CollectionOrderState.Paused;
        Reason = RecoveredReason(recovered);
        Phase = CollectionPhase.Stopped;
        _modes.Enter(ActorMode.Paused, jobId);
        return true;
    }

    /// <summary>What an adopted order is stopped for. The record keeps the
    /// reason it was paused with and the recovery seam does not hand it over,
    /// so this states only what this build can establish now: an uncertain
    /// transfer, "the record and the world disagree", or the stale snapshot -
    /// and the status line points at the record for the rest.</summary>
    private CollectionAttentionReason RecoveredReason(CollectionOrderDefinition recovered)
    {
        if (_custody.View.HasUncertainTransfer(recovered.Order))
        {
            return CollectionAttentionReason.TransferUncertain;
        }

        if (State == CollectionOrderState.NeedsAttention)
        {
            return CollectionAttentionReason.ReconciliationMismatch;
        }

        return recovered.Delivery.Kind == DeliveryKind.Container
            ? CollectionAttentionReason.DestinationStale
            : CollectionAttentionReason.ScopeChanged;
    }

    /// <summary>True while the adopted order's work area and delivery still
    /// belong to a previous world load: neither resolves now, so nothing may
    /// resume until a person confirms a rebind.</summary>
    public bool NeedsRebind
    {
        get
        {
            CollectionOrderDefinition? order = _order;
            if (order == null)
            {
                return false;
            }

            Guid epoch = _custody.WorldLoadEpoch;
            if (epoch == Guid.Empty)
            {
                return true;
            }

            return order.Scope.WorldLoadEpoch != epoch ||
                (order.Delivery.Kind == DeliveryKind.Container && order.Delivery.WorldLoadEpoch != epoch);
        }
    }

    /// <summary>True when this order came from the record rather than from a
    /// player's `start` in this session.</summary>
    public bool Adopted { get; private set; }

    /// <summary>C2: the player confirms where the order works and where it
    /// delivers, both snapshotted in this world load. Custody checks the epochs
    /// and that the kinds are unchanged; quotas, progress and custody are
    /// untouched.</summary>
    public ControlResult Rebind(WorkScope scope, DeliveryTarget delivery, float now)
    {
        CollectionOrderDefinition? order = _order;
        if (order == null || CollectionOrderStates.IsTerminal(State))
        {
            return new ControlResult(ControlOutcome.Refused, "There is no active collection order.");
        }

        if (scope == null)
        {
            return new ControlResult(ControlOutcome.Refused, "No work area could be established to rebind to.");
        }

        if (scope.Source != order.Scope.Source)
        {
            return new ControlResult(
                ControlOutcome.Refused,
                "A rebind keeps the same kind of work area (" + order.Scope.Source + "). Cancel the order and give a new one.");
        }

        if (delivery.Kind != order.Delivery.Kind)
        {
            return new ControlResult(
                ControlOutcome.Refused,
                "A rebind keeps the same kind of delivery (" + order.Delivery.Kind + "). Cancel the order and give a new one.");
        }

        if (!_custody.RecordRebound(order.Order, scope, delivery))
        {
            return new ControlResult(
                ControlOutcome.Refused, "The rebind could not be written to the settlement record; nothing changed.");
        }

        _order = new CollectionOrderDefinition(
            order.Order, order.Worker, order.Quotas, scope, delivery, order.Participation, order.IssuedByCharacter);
        _snapshot = null;
        _failed.Clear();
        _spentThisSnapshot.Clear();
        if (State == CollectionOrderState.Paused &&
            (Reason == CollectionAttentionReason.DestinationStale || Reason == CollectionAttentionReason.ScopeChanged))
        {
            Reason = CollectionAttentionReason.Unspecified;
        }

        return new ControlResult(
            ControlOutcome.Done,
            "Rebound to " + scope.AnchorDescription + " and " +
            (delivery.Kind == DeliveryKind.HoldForPlayer ? "holding for you" : "that chest") +
            ". Nothing he carries changed. Resume when you are ready.");
    }

    private void StartOrder(CollectionOrderDefinition order, float now)
    {
        _order = order;
        _jobId = order.Order.Value;
        Adopted = false;
        State = CollectionOrderState.Accepted;
        Reason = CollectionAttentionReason.Unspecified;
        Phase = CollectionPhase.Surveying;
        PausedByPlayer = false;
        _pausedFrom = CollectionOrderState.Unspecified;
        _failed.Clear();
        _spentThisSnapshot.Clear();
        _survey = null;
        _snapshot = null;
        _fruitlessSurveys = 0;
        _target = null;
        _legDeadline = null;
        _tripOpen = false;
        _lastReadinessAt = now;
        _lastTickAt = now;
        _walkRetry.Reset();
        _depositRetry.Reset();
        Picks = 0;
        Trips = 0;
        SurveysRun = 0;
        UnrecordedTransitions = 0;
        LastEvidence = string.Empty;
        LastWorkerPosition = _motion.Position;
    }

    // --- The worker tick ---------------------------------------------------------------------------------

    /// <summary>One worker tick. The only place the loop mutates anything, so
    /// every mutation runs inside the worker's own simulation step.</summary>
    public void Tick(float now)
    {
        _lastTickAt = now;
        if (_order == null || IsStopped)
        {
            return;
        }

        if (_motion.IsPresent)
        {
            LastWorkerPosition = _motion.Position;
        }

        switch (State)
        {
            case CollectionOrderState.Accepted:
                BeginSurvey(now);
                break;
            case CollectionOrderState.Surveying:
                StepSurvey(now);
                break;
            case CollectionOrderState.Collecting:
                StepCollecting(now);
                break;
            case CollectionOrderState.Delivering:
                StepDelivering(now);
                break;
            case CollectionOrderState.WaitingForHauler:
                StepHauler(now);
                break;
            case CollectionOrderState.HoldingForPlayer:
                StepHolding(now);
                break;
        }
    }

    /// <summary>Called outside the worker tick, where nothing is mutated. When
    /// the worker's own tick has stopped arriving, his body is not simulated
    /// here (unloaded, destroyed, faulted, owned elsewhere) and the order stops
    /// rather than claiming to work.</summary>
    public void Supervise(float now)
    {
        if (_order == null || IsStopped)
        {
            return;
        }

        if (now - _lastTickAt < _parameters.AbsentWorkerGraceSeconds)
        {
            return;
        }

        StopForAbsentBody(now);
    }

    /// <summary>The world went away. In-memory state is dropped without writing:
    /// the journal keeps the order's last recorded state, and reservations and
    /// actor modes do not survive a reload by contract.</summary>
    public void Abandon()
    {
        if (_order != null && _modes.IsHeldBy(_jobId))
        {
            _modes.Release(_jobId);
        }

        _order = null;
        _jobId = string.Empty;
        State = CollectionOrderState.Unspecified;
        Reason = CollectionAttentionReason.Unspecified;
        Phase = CollectionPhase.Idle;
        _survey = null;
        _snapshot = null;
        _target = null;
    }

    // --- Player controls ---------------------------------------------------------------------------------

    public ControlResult Pause(float now)
    {
        if (_order == null || CollectionOrderStates.IsTerminal(State))
        {
            return new ControlResult(ControlOutcome.Refused, "There is no active collection order.");
        }

        if (State == CollectionOrderState.Paused)
        {
            return new ControlResult(ControlOutcome.Unchanged, "It is already paused.");
        }

        if (State == CollectionOrderState.NeedsAttention)
        {
            return new ControlResult(
                ControlOutcome.Refused, "It has already stopped: " + CollectionSentences.Describe(Reason));
        }

        _pausedFrom = State;
        if (!Transition(CollectionOrderState.Paused, CollectionAttentionReason.PausedByPlayer, now))
        {
            return new ControlResult(ControlOutcome.Refused, "It could not be paused; that is a bug.");
        }

        PausedByPlayer = true;
        return new ControlResult(ControlOutcome.Done, "Paused. He keeps what he carries and stands still.");
    }

    public ControlResult Resume(float now)
    {
        if (_order == null || CollectionOrderStates.IsTerminal(State))
        {
            return new ControlResult(ControlOutcome.Refused, "There is no active collection order.");
        }

        if (State == CollectionOrderState.NeedsAttention)
        {
            if (_custody.View.HasUncertainTransfer(_order.Order))
            {
                return new ControlResult(
                    ControlOutcome.Refused,
                    "It still needs attention: " + CollectionSentences.Describe(CollectionAttentionReason.TransferUncertain));
            }

            // A person looked and resumed: that is the resolution the contract
            // allows out of NeedsAttention. The condition is checked again below
            // and stops the order again if it still holds.
            if (!Transition(CollectionOrderState.Paused, Reason, now))
            {
                return new ControlResult(ControlOutcome.Refused, "It could not be resumed; that is a bug.");
            }
        }

        if (State != CollectionOrderState.Paused)
        {
            return new ControlResult(ControlOutcome.Unchanged, "It is already working.");
        }

        if (NeedsRebind)
        {
            // C2 (B1): the order's work area and chest were chosen in a
            // previous world load, where their keys meant something. Nothing
            // resumes on a stale key, and no other chest is ever substituted.
            return new ControlResult(
                ControlOutcome.Refused,
                _order!.Delivery.Kind == DeliveryKind.Container
                    ? "This order was given before the world was reloaded. Look at the chest it should deliver to and " +
                        "run cf_collect rebind, or cancel it."
                    : "This order was given before the world was reloaded. Run cf_collect rebind to set its work area " +
                        "again, or cancel it.");
        }

        PausedByPlayer = false;
        CollectionOrderState target = ResumeTarget();
        switch (target)
        {
            case CollectionOrderState.HoldingForPlayer:
                if (Transition(CollectionOrderState.HoldingForPlayer, CollectionAttentionReason.Unspecified, now))
                {
                    Phase = CollectionPhase.Holding;
                }

                break;

            case CollectionOrderState.WaitingForHauler:
                if (_cooperation == null || !_cooperation.IsAvailable(out _))
                {
                    Stop(CollectionOrderState.Paused, CollectionAttentionReason.HaulerUnavailable, now);
                }
                else if (PassCheckpoint(now, Checkpoint.Hauler) &&
                    Transition(CollectionOrderState.WaitingForHauler, CollectionAttentionReason.Unspecified, now))
                {
                    Phase = CollectionPhase.WithHauler;
                }

                break;

            case CollectionOrderState.Delivering:
                if (PassCheckpoint(now, Checkpoint.DeliveryLeg) &&
                    Transition(CollectionOrderState.Delivering, CollectionAttentionReason.Unspecified, now))
                {
                    _walkRetry.Reset();
                    _depositRetry.Reset();
                    IssueWalkToDestination(now);
                }

                break;

            default:
                BeginSurvey(now);
                break;
        }

        return State == CollectionOrderState.Paused || State == CollectionOrderState.NeedsAttention
            ? new ControlResult(ControlOutcome.Refused, "It cannot resume yet: " + CollectionSentences.Describe(Reason))
            : new ControlResult(ControlOutcome.Done, "Resumed: " + CollectionSentences.Describe(State) + ".");
    }

    /// <summary>Ends the order. Not a refund: what was delivered stays
    /// delivered, and what he carries stays with him, reported.</summary>
    public ControlResult Cancel(float now)
    {
        if (_order == null || CollectionOrderStates.IsTerminal(State))
        {
            return new ControlResult(ControlOutcome.Refused, "There is no active collection order.");
        }

        CollectionOrderDefinition order = _order;
        if (_modes.IsHeldBy(_jobId))
        {
            _modes.Enter(ActorMode.Recovering, _jobId);
        }

        _motion.Stop(_jobId);
        ReleaseTarget();
        _reservations.ReleaseAll(order.Order);

        if (order.Participation == ParticipationMode.WithHauler && _cooperation != null)
        {
            _cooperation.Cancel(order, detachAndPark: true);
        }

        if (!Transition(CollectionOrderState.Cancelled, CollectionAttentionReason.Unspecified, now))
        {
            return new ControlResult(ControlOutcome.Refused, "It could not be cancelled; that is a bug.");
        }

        return new ControlResult(
            ControlOutcome.Done,
            "Cancelled. Delivered material stays delivered; anything he carries stays with him until it is moved.");
    }

    // --- Survey ------------------------------------------------------------------------------------------

    private void BeginSurvey(float now)
    {
        if (!PassCheckpoint(now, Checkpoint.Survey))
        {
            return;
        }

        if (State != CollectionOrderState.Surveying &&
            !Transition(CollectionOrderState.Surveying, CollectionAttentionReason.Unspecified, now))
        {
            return;
        }

        _motion.Stop(_jobId);
        ReleaseTarget();
        _survey = new SurveyScheduler(
            _parameters, _order!.Scope, _probe, ++_surveyRevision, SurveyProvenance.SoloForeman, now);
        SurveysRun++;
        Phase = CollectionPhase.Surveying;
    }

    private void StepSurvey(float now)
    {
        if (_survey == null)
        {
            BeginSurvey(now);
            return;
        }

        _survey.Step(now);
        if (!_survey.IsComplete)
        {
            return;
        }

        _snapshot = _survey.Snapshot;
        LastAccounting = _survey.Accounting;
        _survey = null;
        _spentThisSnapshot.Clear();

        OrderProgress progress = ReadProgress();
        bool collectable = CountCollectable(progress, out int failedButAvailable) > 0;
        if (collectable)
        {
            _fruitlessSurveys = 0;
        }

        if (collectable || progress.AnyCarried || progress.AllNeedsCovered)
        {
            if (Transition(CollectionOrderState.Collecting, CollectionAttentionReason.Unspecified, now))
            {
                Phase = CollectionPhase.Selecting;
            }

            return;
        }

        Stop(CollectionOrderState.Paused, EmptySurveyReason(progress, failedButAvailable), now);
    }

    private int CountCollectable(OrderProgress progress, out int failedButAvailable)
    {
        failedButAvailable = 0;
        int collectable = 0;
        if (_snapshot == null)
        {
            return 0;
        }

        foreach (SourceObservation source in _snapshot.Sources)
        {
            if (source.Availability != SourceAvailability.Available || progress.Remaining(source.Yields) <= 0)
            {
                continue;
            }

            if (_failed.Contains(source.Key))
            {
                failedButAvailable++;
            }
            else
            {
                collectable++;
            }
        }

        return collectable;
    }

    /// <summary>The honest reason a survey found nothing to collect: an
    /// incomplete look is not an empty area, unloaded ground is not exhausted
    /// ground, and exhausted is not "never had any".</summary>
    private CollectionAttentionReason EmptySurveyReason(OrderProgress progress, int failedButAvailable)
    {
        SurveyAccounting? accounting = LastAccounting;
        if (accounting == null || accounting.TruncatedByBudget)
        {
            return CollectionAttentionReason.SurveyIncomplete;
        }

        if (accounting.UnloadedCells > 0)
        {
            return CollectionAttentionReason.ScopeUnloaded;
        }

        bool unknown = false;
        bool exhausted = false;
        foreach (ResourceQuota quota in _order!.Quotas)
        {
            if (progress.Remaining(quota.Resource) <= 0)
            {
                continue;
            }

            unknown |= accounting.Unknown(quota.Resource) > 0;
            exhausted |= accounting.Exhausted(quota.Resource) > 0;
        }

        if (unknown)
        {
            return CollectionAttentionReason.SurveyIncomplete;
        }

        if (failedButAvailable > 0)
        {
            return CollectionAttentionReason.SourceUnreachable;
        }

        return exhausted ? CollectionAttentionReason.SourcesExhausted : CollectionAttentionReason.NoEligibleSources;
    }

    // --- Collecting --------------------------------------------------------------------------------------

    private void StepCollecting(float now)
    {
        switch (Phase)
        {
            case CollectionPhase.WalkingToSource:
                StepWalkingToSource(now);
                break;
            default:
                Phase = CollectionPhase.Selecting;
                StepSelecting(now);
                break;
        }
    }

    private void StepSelecting(float now)
    {
        CollectionOrderDefinition order = _order!;
        if (_snapshot == null || now - _snapshot.TakenAt >= _parameters.SnapshotMaxAgeSeconds)
        {
            BeginSurvey(now);
            return;
        }

        if (!PassCheckpoint(now, Checkpoint.Reservation))
        {
            return;
        }

        OrderProgress progress = ReadProgress();
        if (progress.AllDelivered)
        {
            BeginDelivery(now, progress);
            return;
        }

        if (!_custody.TryResolveWorker(out IInventoryPort? worker, out CollectionAttentionReason workerRefusal) || worker == null)
        {
            Stop(CollectionOrderState.Paused, WorkerReason(workerRefusal), now);
            return;
        }

        IInventoryPort? destination = null;
        bool soloToContainer = order.Participation == ParticipationMode.Solo && order.Delivery.Kind == DeliveryKind.Container;
        if (soloToContainer)
        {
            // Return space is established before collecting more. A chest that
            // cannot be resolved cannot be returned to, so nothing more is
            // picked; what he carries stays with him.
            if (!_custody.TryResolveDestination(order.Delivery, out destination, out CollectionAttentionReason refusal) ||
                destination == null)
            {
                Stop(CollectionOrderState.Paused, DestinationReason(refusal), now);
                return;
            }
        }

        List<ResourceNeed> needs = BuildNeeds(order, progress, worker, destination);
        SelectionResult result = CollectionTargetSelector.Select(
            _parameters, _snapshot.Sources, needs, _motion.Position,
            soloToContainer ? order.Delivery.Position : (SitePoint?)null, IsExcluded);

        switch (result.Outcome)
        {
            case SelectionOutcome.Chosen:
                Reserve(result.Source!, now);
                break;

            case SelectionOutcome.AllNeedsCovered:
                BeginDelivery(now, progress);
                break;

            case SelectionOutcome.CarryFull:
                if (progress.AnyCarried)
                {
                    BeginDelivery(now, progress);
                }
                else
                {
                    // Full of something that is not this order's: nothing to
                    // deliver would make room.
                    Stop(CollectionOrderState.Paused, CollectionAttentionReason.CarryFull, now);
                }

                break;

            case SelectionOutcome.NoReturnSpace:
                if (progress.AnyCarried)
                {
                    BeginDelivery(now, progress);
                }
                else
                {
                    Stop(CollectionOrderState.Paused, CollectionAttentionReason.DestinationFull, now);
                }

                break;

            default:
                if (progress.AnyCarried)
                {
                    BeginDelivery(now, progress);
                }
                else if (_fruitlessSurveys < _parameters.MaxFruitlessSurveys)
                {
                    _fruitlessSurveys++;
                    BeginSurvey(now);
                }
                else
                {
                    CountCollectable(progress, out int failedButAvailable);
                    Stop(CollectionOrderState.Paused, EmptySurveyReason(progress, failedButAvailable), now);
                }

                break;
        }
    }

    private List<ResourceNeed> BuildNeeds(
        CollectionOrderDefinition order, OrderProgress progress, IInventoryPort worker, IInventoryPort? destination)
    {
        float carriedWeight = 0f;
        foreach (ResourceQuota quota in order.Quotas)
        {
            float unit = _world.UnitWeight(quota.Resource);
            if (unit > 0f)
            {
                carriedWeight += Math.Max(0, worker.Count(MaterialItem.Of(quota.Resource))) * unit;
            }
        }

        var needs = new List<ResourceNeed>(order.Quotas.Count);
        foreach (ResourceQuota quota in order.Quotas)
        {
            MaterialItem item = MaterialItem.Of(quota.Resource);
            int remaining = progress.Remaining(quota.Resource);
            int probe = Math.Max(1, remaining);
            int carryRoom = CarryPlanner.CarryRoomUnits(
                _parameters.WorkerCarryWeight, carriedWeight, _world.UnitWeight(quota.Resource), worker.CanAccept(item, probe));

            int returnRoom = ResourceNeed.Unlimited;
            if (destination != null)
            {
                int carried = progress.Carried(quota.Resource);
                returnRoom = CarryPlanner.ReturnRoomUnits(destination.CanAccept(item, carried + probe), carried);
            }

            needs.Add(new ResourceNeed(quota.Resource, remaining, carryRoom, returnRoom));
        }

        return needs;
    }

    /// <summary>Failed for this order, or already spent in this snapshot. A
    /// source held by another order is learned when its reservation is refused
    /// and is spent for the rest of the snapshot.</summary>
    private bool IsExcluded(SourceKey key) => _failed.Contains(key) || _spentThisSnapshot.Contains(key);

    private void Reserve(SourceObservation source, float now)
    {
        switch (_reservations.Reserve(source.Key, _order!.Order))
        {
            case ReservationOutcome.Reserved:
            case ReservationOutcome.AlreadySatisfied:
                _target = source;
                _tripOpen = true;
                _walkRetry.Reset();
                IssueWalkToTarget(now);
                break;

            case ReservationOutcome.HeldByAnotherOrder:
                _spentThisSnapshot.Add(source.Key);
                break;

            default:
                // A key from another world load: survey again.
                _snapshot = null;
                break;
        }
    }

    private void IssueWalkToTarget(float now)
    {
        if (_target == null)
        {
            Phase = CollectionPhase.Selecting;
            return;
        }

        if (!_motion.WalkTo(_target.Key.Position, _parameters.SourceArrivalToleranceMetres, _jobId))
        {
            StopForAbsentBody(now);
            return;
        }

        Phase = CollectionPhase.WalkingToSource;
        _legDeadline = new PhaseDeadline(now, _parameters.WalkLegDeadlineSeconds);
    }

    private void StepWalkingToSource(float now)
    {
        if (_target == null || !_reservations.IsHeldBy(_target.Key, _order!.Order))
        {
            ReleaseTarget();
            Phase = CollectionPhase.Selecting;
            return;
        }

        if (!_motion.IsPresent)
        {
            StopForAbsentBody(now);
            return;
        }

        if (_walkRetry.IsWaiting(now))
        {
            return;
        }

        switch (_motion.Status)
        {
            case CollectionWalkStatus.Arrived:
                PickTarget(now);
                break;

            case CollectionWalkStatus.Deferred:
                OnWalkToSourceFailed(now, _motion.LastDeferral);
                break;

            case CollectionWalkStatus.Walking:
                if (_legDeadline.HasValue && _legDeadline.Value.IsExpired(now))
                {
                    OnWalkToSourceFailed(now, WorkerDeferralReason.Unreachable);
                }

                break;

            default:
                // Nobody is walking: a backoff ended, or something cleared the
                // goal. Ask again; the retry ceiling bounds how often.
                IssueWalkToTarget(now);
                break;
        }
    }

    private void OnWalkToSourceFailed(float now, WorkerDeferralReason reason)
    {
        if (reason == WorkerDeferralReason.NoAuthority)
        {
            Stop(CollectionOrderState.Paused, CollectionAttentionReason.NoAuthority, now);
            return;
        }

        _motion.Stop(_jobId);
        RetryDecision decision = _walkRetry.RecordFailure(now);
        bool deterministic = reason == WorkerDeferralReason.TooFar || reason == WorkerDeferralReason.Hazardous;
        if (decision.GiveUp || deterministic)
        {
            SourceKey abandoned = _target!.Key;
            _failed.Add(abandoned);
            ReleaseTarget();
            _walkRetry.Reset();
            Phase = CollectionPhase.Selecting;
            LastEvidence = "Gave up walking to " + abandoned + " (" + reason + ").";
            _log?.Invoke(LastEvidence);
        }
    }

    private void PickTarget(float now)
    {
        CollectionOrderDefinition order = _order!;
        SourceObservation target = _target!;

        if (!PassCheckpoint(now, Checkpoint.Pickup))
        {
            return;
        }

        if (!(_motion.Position.HorizontalDistanceTo(target.Key.Position) <= _parameters.PickupReachMetres))
        {
            // "Arrived" by the walk's tolerance but not within reach: count it
            // as a failed approach, never pick from further away.
            OnWalkToSourceFailed(now, WorkerDeferralReason.Unreachable);
            return;
        }

        if (!_custody.TryResolveWorker(out IInventoryPort? worker, out CollectionAttentionReason workerRefusal) || worker == null)
        {
            Stop(CollectionOrderState.Paused, WorkerReason(workerRefusal), now);
            return;
        }

        OrderProgress progress = ReadProgress();
        IInventoryPort? destination = null;
        bool soloToContainer = order.Participation == ParticipationMode.Solo && order.Delivery.Kind == DeliveryKind.Container;
        if (soloToContainer &&
            (!_custody.TryResolveDestination(order.Delivery, out destination, out CollectionAttentionReason refusal) ||
                destination == null))
        {
            Stop(CollectionOrderState.Paused, DestinationReason(refusal), now);
            return;
        }

        // Capacity and return space are asked again at the instant of the
        // pick: things changed during the walk.
        List<ResourceNeed> needs = BuildNeeds(order, progress, worker, destination);
        int yield = Math.Max(1, target.EstimatedYield);
        foreach (ResourceNeed need in needs)
        {
            if (need.Resource == target.Yields &&
                (yield > need.Remaining || yield > need.CarryRoom || yield > need.ReturnRoom))
            {
                ReleaseTarget();
                Phase = CollectionPhase.Selecting;
                return;
            }
        }

        PickupResult result = _pickup.TryPick(target.Key, order.Order);
        _reservations.Release(target.Key, order.Order);
        _spentThisSnapshot.Add(target.Key);
        _target = null;
        _legDeadline = null;
        _walkRetry.Reset();
        Phase = CollectionPhase.Selecting;

        switch (result.Outcome)
        {
            case PickupOutcome.Picked:
                TakeDrops(result, worker, now);
                break;

            case PickupOutcome.Refused:
                // One attempt per reservation (CONTRACTS.md §7): next source.
                _failed.Add(target.Key);
                LastEvidence = "Pick refused at " + target.Key + ": " + result.Reason;
                break;

            default:
                LastEvidence = "Pick uncertain at " + target.Key + ": " + result.Reason;
                Stop(
                    CollectionOrderState.NeedsAttention,
                    _custody.IsWritable ? CollectionAttentionReason.TransferUncertain : CollectionAttentionReason.JournalReadOnly,
                    now);
                break;
        }
    }

    /// <summary>Takes every drop the pick spawned, in the same tick. Each take
    /// is attempted even after one fails, so no traced drop is left guarded
    /// and forgotten; the pickup port restores an untaken drop to an ordinary
    /// world item.</summary>
    private void TakeDrops(PickupResult result, IInventoryPort worker, float now)
    {
        Picks++;
        int expected = 0;
        int taken = 0;
        foreach (SpawnedDrop drop in result.Drops)
        {
            expected += drop.Count;
            taken += Math.Max(0, Math.Min(drop.Count, _pickup.TryTakeDrop(drop, worker)));
        }

        LastEvidence = "Picked " + taken + " of " + expected + " traced unit(s).";

        if (_custody.View.HasUncertainTransfer(_order!.Order))
        {
            Stop(CollectionOrderState.NeedsAttention, CollectionAttentionReason.TransferUncertain, now);
            return;
        }

        if (expected == 0)
        {
            // A pick that reports success with nothing traced is not a success.
            Stop(CollectionOrderState.NeedsAttention, CollectionAttentionReason.TransferUncertain, now);
            return;
        }

        if (taken < expected)
        {
            Stop(CollectionOrderState.NeedsAttention, CollectionAttentionReason.CarryFull, now);
        }
    }

    // --- Delivering --------------------------------------------------------------------------------------

    /// <summary>The carry checkpoint: hand to the hauler, hold for the player,
    /// or walk the load to the chest.</summary>
    private void BeginDelivery(float now, OrderProgress progress)
    {
        CollectionOrderDefinition order = _order!;
        ReleaseTarget();
        _tripOpen = false;

        if (order.Participation == ParticipationMode.WithHauler)
        {
            if (_cooperation == null || !_cooperation.IsAvailable(out _))
            {
                Stop(CollectionOrderState.Paused, CollectionAttentionReason.HaulerUnavailable, now);
                return;
            }

            if (Transition(CollectionOrderState.WaitingForHauler, CollectionAttentionReason.Unspecified, now))
            {
                Phase = CollectionPhase.WithHauler;
            }

            return;
        }

        if (order.Delivery.Kind == DeliveryKind.HoldForPlayer)
        {
            if (!progress.AllNeedsCovered)
            {
                // Acceptance made sure the whole order fits on his back, so this
                // is his inventory refusing, not the budget.
                Stop(CollectionOrderState.Paused, CollectionAttentionReason.CarryFull, now);
                return;
            }

            if (Transition(CollectionOrderState.HoldingForPlayer, CollectionAttentionReason.Unspecified, now))
            {
                _motion.Stop(_jobId);
                Phase = CollectionPhase.Holding;
            }

            return;
        }

        if (!progress.AnyCarried && !progress.AllDelivered)
        {
            // Nothing is carried and nothing is left to collect, yet it is not
            // all delivered: units are on the ground or in a cart. A person
            // decides; the loop does not invent where they are.
            Stop(CollectionOrderState.NeedsAttention, CollectionAttentionReason.ReconciliationMismatch, now);
            return;
        }

        if (!PassCheckpoint(now, Checkpoint.DeliveryLeg))
        {
            return;
        }

        if (!_custody.TryResolveDestination(order.Delivery, out _, out CollectionAttentionReason refusal))
        {
            Stop(CollectionOrderState.Paused, DestinationReason(refusal), now);
            return;
        }

        if (!Transition(CollectionOrderState.Delivering, CollectionAttentionReason.Unspecified, now))
        {
            return;
        }

        _walkRetry.Reset();
        _depositRetry.Reset();
        if (progress.AnyCarried)
        {
            IssueWalkToDestination(now);
        }
        else
        {
            Phase = CollectionPhase.Depositing;
        }
    }

    private void IssueWalkToDestination(float now)
    {
        if (!_motion.WalkTo(_order!.Delivery.Position, _parameters.DeliveryArrivalToleranceMetres, _jobId))
        {
            StopForAbsentBody(now);
            return;
        }

        Phase = CollectionPhase.WalkingToDestination;
        _legDeadline = new PhaseDeadline(now, _parameters.WalkLegDeadlineSeconds);
    }

    private void StepDelivering(float now)
    {
        if (Phase == CollectionPhase.Depositing)
        {
            StepDeposit(now);
            return;
        }

        Phase = CollectionPhase.WalkingToDestination;
        if (!_motion.IsPresent)
        {
            StopForAbsentBody(now);
            return;
        }

        if (_walkRetry.IsWaiting(now))
        {
            return;
        }

        switch (_motion.Status)
        {
            case CollectionWalkStatus.Arrived:
                _legDeadline = null;
                Phase = CollectionPhase.Depositing;
                break;

            case CollectionWalkStatus.Deferred:
                OnWalkToDestinationFailed(now, _motion.LastDeferral);
                break;

            case CollectionWalkStatus.Walking:
                if (_legDeadline.HasValue && _legDeadline.Value.IsExpired(now))
                {
                    OnWalkToDestinationFailed(now, WorkerDeferralReason.Unreachable);
                }

                break;

            default:
                IssueWalkToDestination(now);
                break;
        }
    }

    private void OnWalkToDestinationFailed(float now, WorkerDeferralReason reason)
    {
        if (reason == WorkerDeferralReason.NoAuthority)
        {
            Stop(CollectionOrderState.Paused, CollectionAttentionReason.NoAuthority, now);
            return;
        }

        _motion.Stop(_jobId);
        RetryDecision decision = _walkRetry.RecordFailure(now);
        if (decision.GiveUp || reason == WorkerDeferralReason.TooFar || reason == WorkerDeferralReason.Hazardous)
        {
            // He cannot get to the chest. He keeps what he carries; no other
            // chest is ever substituted.
            Stop(CollectionOrderState.Paused, CollectionAttentionReason.DestinationUnavailable, now);
        }
    }

    private void StepDeposit(float now)
    {
        CollectionOrderDefinition order = _order!;
        if (!PassCheckpoint(now, Checkpoint.Deposit))
        {
            return;
        }

        if (_depositRetry.IsWaiting(now))
        {
            return;
        }

        OrderProgress progress = ReadProgress();
        if (!progress.AnyCarried)
        {
            if (progress.AllDelivered)
            {
                Transition(CollectionOrderState.Completed, CollectionAttentionReason.Unspecified, now);
                return;
            }

            // This load is delivered; what is still needed means another trip,
            // over a fresh survey.
            Trips++;
            _snapshot = null;
            _walkRetry.Reset();
            _depositRetry.Reset();
            if (Transition(CollectionOrderState.Collecting, CollectionAttentionReason.Unspecified, now))
            {
                Phase = CollectionPhase.Selecting;
            }

            return;
        }

        if (!_custody.TryResolveWorker(out IInventoryPort? worker, out CollectionAttentionReason workerRefusal) || worker == null)
        {
            Stop(CollectionOrderState.Paused, WorkerReason(workerRefusal), now);
            return;
        }

        if (!_custody.TryResolveDestination(order.Delivery, out IInventoryPort? destination, out CollectionAttentionReason refusal) ||
            destination == null)
        {
            Stop(CollectionOrderState.Paused, DestinationReason(refusal), now);
            return;
        }

        if (!(_motion.Position.HorizontalDistanceTo(order.Delivery.Position) <= _parameters.DeliveryArrivalToleranceMetres + 0.5f))
        {
            // Pushed away since arriving: walk back rather than deposit at a
            // distance.
            IssueWalkToDestination(now);
            return;
        }

        foreach (ResourceQuota quota in order.Quotas)
        {
            int carried = progress.Carried(quota.Resource);
            if (carried <= 0)
            {
                continue;
            }

            MaterialItem item = MaterialItem.Of(quota.Resource);
            int count = Math.Min(carried, Math.Max(0, worker.Count(item)));
            if (count <= 0)
            {
                // The record says he carries it and his inventory says he does
                // not. Nothing is moved on a claim.
                LastEvidence = "Recorded as carried: " + carried + " " + item + "; in his inventory: " + worker.Count(item) + ".";
                Stop(CollectionOrderState.NeedsAttention, CollectionAttentionReason.ReconciliationMismatch, now);
                return;
            }

            if (destination.CanAccept(item, count) <= 0)
            {
                Stop(CollectionOrderState.Paused, CollectionAttentionReason.DestinationFull, now);
                return;
            }

            var intent = new TransferIntent(
                _requestIds.Next(order.Order, "deposit"), order.Order, WorkerLocation,
                new CustodyLocation(CustodyPlace.Destination, order.Delivery.ContainerKey, order.Delivery.WorldLoadEpoch),
                item, count, _custody.View.Revision);

            TransferReceipt receipt = _custody.Executor.Execute(intent, worker, destination);
            LastEvidence = "Deposit " + intent.Request + ": " + receipt.Outcome + ", " + receipt.Accepted + " of " + count +
                " " + item + (receipt.Evidence.Length > 0 ? " (" + receipt.Evidence + ")" : string.Empty);

            switch (receipt.Outcome)
            {
                case TransferOutcome.Completed:
                case TransferOutcome.AlreadySatisfied:
                    _depositRetry.Reset();
                    break;

                case TransferOutcome.Partial:
                    // What fitted is delivered; the rest stays with him.
                    Stop(CollectionOrderState.Paused, CollectionAttentionReason.DestinationFull, now);
                    break;

                case TransferOutcome.Refused:
                case TransferOutcome.Stale:
                    if (_depositRetry.RecordFailure(now).GiveUp)
                    {
                        Stop(
                            CollectionOrderState.Paused,
                            destination.CanAccept(item, 1) <= 0
                                ? CollectionAttentionReason.DestinationFull
                                : CollectionAttentionReason.DestinationUnavailable,
                            now);
                    }

                    break;

                default:
                    // Uncertain, or a request id reused with another payload:
                    // never retried, never compensated.
                    Stop(CollectionOrderState.NeedsAttention, CollectionAttentionReason.TransferUncertain, now);
                    break;
            }

            // One transfer per tick.
            return;
        }
    }

    // --- With the hauler, holding ------------------------------------------------------------------------

    private void StepHauler(float now)
    {
        CollectionOrderDefinition order = _order!;
        if (_cooperation == null)
        {
            Stop(CollectionOrderState.Paused, CollectionAttentionReason.HaulerUnavailable, now);
            return;
        }

        if (!PassCheckpoint(now, Checkpoint.Hauler))
        {
            return;
        }

        CollectionHandOff step = _cooperation.Tick(order, now, out CollectionAttentionReason reason);
        switch (step)
        {
            case CollectionHandOff.Working:
                break;

            case CollectionHandOff.CollectMore:
                _snapshot = null;
                if (Transition(CollectionOrderState.Collecting, CollectionAttentionReason.Unspecified, now))
                {
                    Phase = CollectionPhase.Selecting;
                }

                break;

            case CollectionHandOff.Delivered:
                // Completion is decided from custody, not from the claim: the
                // deposit phase completes only when the view says delivered.
                if (Transition(CollectionOrderState.Delivering, CollectionAttentionReason.Unspecified, now))
                {
                    Phase = CollectionPhase.Depositing;
                }

                break;

            case CollectionHandOff.NeedsAttention:
                Stop(
                    CollectionOrderState.NeedsAttention,
                    reason == CollectionAttentionReason.Unspecified ? CollectionAttentionReason.HaulerNeedsAttention : reason,
                    now);
                break;

            default:
                Stop(
                    CollectionOrderState.Paused,
                    reason == CollectionAttentionReason.Unspecified ? CollectionAttentionReason.HaulerUnavailable : reason,
                    now);
                break;
        }
    }

    private void StepHolding(float now)
    {
        Phase = CollectionPhase.Holding;
        if (ReadProgress().AllDelivered)
        {
            Transition(CollectionOrderState.Completed, CollectionAttentionReason.Unspecified, now);
        }
    }

    // --- Checkpoints and transitions ---------------------------------------------------------------------

    private enum Checkpoint
    {
        Survey,
        Reservation,
        Pickup,
        DeliveryLeg,
        Deposit,
        Hauler,
    }

    /// <summary>The safe-checkpoint questions. False means the order has
    /// already been stopped with the reason.</summary>
    private bool PassCheckpoint(float now, Checkpoint checkpoint)
    {
        CollectionOrderDefinition order = _order!;

        WorkAuthorityVerdict authority = _world.EvaluateAuthority();
        if (authority != WorkAuthorityVerdict.Granted)
        {
            Stop(
                CollectionOrderState.Paused,
                authority == WorkAuthorityVerdict.OtherPeersConnected
                    ? CollectionAttentionReason.OtherPeersConnected
                    : CollectionAttentionReason.NoAuthority,
                now);
            return false;
        }

        if (!_custody.IsWritable)
        {
            Stop(CollectionOrderState.Paused, CollectionAttentionReason.JournalReadOnly, now);
            return false;
        }

        if (_custody.View.HasUncertainTransfer(order.Order))
        {
            Stop(CollectionOrderState.NeedsAttention, CollectionAttentionReason.TransferUncertain, now);
            return false;
        }

        if (!_motion.IsPresent)
        {
            StopForAbsentBody(now);
            return false;
        }

        if (checkpoint != Checkpoint.Deposit && checkpoint != Checkpoint.Hauler)
        {
            switch (ScopeCheckpoint.Revalidate(order.Scope, _world.ObserveScope(order.Scope), _world.IsLoaded))
            {
                case ScopeCheck.Valid:
                    break;
                case ScopeCheck.Changed:
                    Stop(CollectionOrderState.Paused, CollectionAttentionReason.ScopeChanged, now);
                    return false;
                case ScopeCheck.Unloaded:
                    Stop(CollectionOrderState.Paused, CollectionAttentionReason.ScopeUnloaded, now);
                    return false;
                default:
                    Stop(CollectionOrderState.Paused, CollectionAttentionReason.ScopeInvalid, now);
                    return false;
            }
        }

        // Readiness is for collecting more: asked at every trip start and at
        // least every ReadinessRecheckSeconds while collecting. A load already
        // picked is still delivered, because delivering uses no tool.
        bool askReadiness = checkpoint == Checkpoint.Survey ||
            (checkpoint == Checkpoint.Reservation &&
                (!_tripOpen || now - _lastReadinessAt >= _parameters.ReadinessRecheckSeconds));
        if (askReadiness)
        {
            ReadinessVerdict verdict = _world.AssessReadiness(order.Worker);
            _lastReadinessAt = now;
            if (!verdict.IsReady)
            {
                Stop(CollectionOrderState.Paused, CollectionIntake.AttentionFor(verdict), now);
                return false;
            }
        }

        return true;
    }

    private void StopForAbsentBody(float now)
    {
        if (_order == null || IsStopped)
        {
            return;
        }

        if (!_world.IsLoaded(LastWorkerPosition))
        {
            // His ground unloaded with him on it: the body is saved with the
            // world, not lost.
            Stop(CollectionOrderState.Paused, CollectionAttentionReason.ScopeUnloaded, now);
            return;
        }

        Stop(
            ReadProgress().AnyCarried ? CollectionOrderState.NeedsAttention : CollectionOrderState.Paused,
            CollectionAttentionReason.WorkerBodyLost,
            now);
    }

    /// <summary>Stops the order in Paused or NeedsAttention with one reason.
    /// NeedsAttention is never downgraded to Paused here: only a person does
    /// that.</summary>
    private void Stop(CollectionOrderState to, CollectionAttentionReason reason, float now)
    {
        if (_order == null || CollectionOrderStates.IsTerminal(State))
        {
            return;
        }

        if (State == CollectionOrderState.NeedsAttention)
        {
            return;
        }

        if (State == to)
        {
            Reason = reason;
            return;
        }

        if (State != CollectionOrderState.Paused)
        {
            _pausedFrom = State;
        }

        if (to == CollectionOrderState.Paused)
        {
            PausedByPlayer = false;
        }

        Transition(to, reason, now);
    }

    private bool Transition(CollectionOrderState to, CollectionAttentionReason reason, float now)
    {
        CollectionOrderDefinition order = _order!;
        CollectionOrderState from = State;
        if (from == to)
        {
            return true;
        }

        if (!CollectionOrderStates.CanTransition(from, to))
        {
            IllegalTransitionsAttempted++;
            _log?.Invoke("Refused an illegal collection transition " + from + " -> " + to + "; that is a bug.");
            return false;
        }

        bool stopping = to == CollectionOrderState.Paused || to == CollectionOrderState.NeedsAttention ||
            CollectionOrderStates.IsTerminal(to);
        CollectionAttentionReason recordedReason =
            to == CollectionOrderState.Paused || to == CollectionOrderState.NeedsAttention
                ? reason
                : CollectionAttentionReason.Unspecified;

        if (!_custody.RecordTransition(order.Order, from, to, recordedReason))
        {
            if (!stopping)
            {
                // Resuming work that the record cannot show is not allowed.
                UnrecordedTransitions++;
                if (from == CollectionOrderState.Paused)
                {
                    Reason = CollectionAttentionReason.JournalReadOnly;
                }
                else
                {
                    _pausedFrom = from;
                    ApplyState(CollectionOrderState.Paused, CollectionAttentionReason.JournalReadOnly);
                }

                return false;
            }

            UnrecordedTransitions++;
        }

        ApplyState(to, recordedReason);
        return true;
    }

    private void ApplyState(CollectionOrderState to, CollectionAttentionReason reason)
    {
        State = to;
        Reason = reason;

        bool stopping = to == CollectionOrderState.Paused || to == CollectionOrderState.NeedsAttention ||
            CollectionOrderStates.IsTerminal(to);
        if (stopping)
        {
            // Stop the body while the job still holds it, then let go of what
            // an idle order must not hold.
            _motion.Stop(_jobId);
            ReleaseTarget();
            _reservations.ReleaseAll(_order!.Order);
            _survey = null;
            _legDeadline = null;
            Phase = CollectionOrderStates.IsTerminal(to) ? CollectionPhase.Idle : CollectionPhase.Stopped;
        }

        switch (to)
        {
            case CollectionOrderState.Surveying:
                _modes.Enter(ActorMode.Surveying, _jobId);
                break;
            case CollectionOrderState.Collecting:
            case CollectionOrderState.Delivering:
            case CollectionOrderState.WaitingForHauler:
            case CollectionOrderState.HoldingForPlayer:
                _modes.Enter(ActorMode.Working, _jobId);
                break;
            case CollectionOrderState.Paused:
            case CollectionOrderState.NeedsAttention:
                _modes.Enter(ActorMode.Paused, _jobId);
                break;
            case CollectionOrderState.Completed:
            case CollectionOrderState.Cancelled:
                _modes.Release(_jobId);
                break;
        }
    }

    private CollectionOrderState ResumeTarget()
    {
        switch (_pausedFrom)
        {
            case CollectionOrderState.HoldingForPlayer:
                return CollectionOrderState.HoldingForPlayer;
            case CollectionOrderState.WaitingForHauler:
                return CollectionOrderState.WaitingForHauler;
            case CollectionOrderState.Delivering:
                return ReadProgress().AnyCarried ? CollectionOrderState.Delivering : CollectionOrderState.Surveying;
            default:
                return CollectionOrderState.Surveying;
        }
    }

    private void ReleaseTarget()
    {
        if (_target != null && _order != null)
        {
            _reservations.Release(_target.Key, _order.Order);
        }

        _target = null;
    }

    private static CollectionAttentionReason DestinationReason(CollectionAttentionReason refusal)
    {
        switch (refusal)
        {
            case CollectionAttentionReason.DestinationFull:
            case CollectionAttentionReason.DestinationAccessDenied:
            case CollectionAttentionReason.DestinationStale:
            case CollectionAttentionReason.DestinationUnavailable:
                return refusal;
            default:
                return CollectionAttentionReason.DestinationUnavailable;
        }
    }

    private static CollectionAttentionReason WorkerReason(CollectionAttentionReason refusal) =>
        refusal == CollectionAttentionReason.Unspecified ? CollectionAttentionReason.WorkerBodyLost : refusal;

    // --- Progress ----------------------------------------------------------------------------------------

    private OrderProgress ReadProgress()
    {
        CollectionOrderDefinition order = _order!;
        var progress = new ResourceProgress[order.Quotas.Count];
        for (int index = 0; index < order.Quotas.Count; index++)
        {
            ResourceQuota quota = order.Quotas[index];
            progress[index] = _custody.View.ProgressFor(order, quota.Resource)
                ?? new ResourceProgress(quota.Resource, quota.Requested);
        }

        return new OrderProgress(progress);
    }

    /// <summary>Per-resource progress read from custody, with the questions the
    /// loop asks of it.</summary>
    private sealed class OrderProgress
    {
        private readonly ResourceProgress[] _resources;

        public OrderProgress(ResourceProgress[] resources)
        {
            _resources = resources;
        }

        public bool AnyCarried
        {
            get
            {
                foreach (ResourceProgress resource in _resources)
                {
                    if (resource.Carried > 0)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public bool AllNeedsCovered
        {
            get
            {
                foreach (ResourceProgress resource in _resources)
                {
                    if (resource.StillToCollect > 0)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public bool AllDelivered
        {
            get
            {
                foreach (ResourceProgress resource in _resources)
                {
                    if (!resource.IsDelivered)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public int Remaining(CollectedResource resource)
        {
            foreach (ResourceProgress progress in _resources)
            {
                if (progress.Resource == resource)
                {
                    return progress.StillToCollect;
                }
            }

            return 0;
        }

        public int Carried(CollectedResource resource)
        {
            foreach (ResourceProgress progress in _resources)
            {
                if (progress.Resource == resource)
                {
                    return progress.Carried;
                }
            }

            return 0;
        }
    }

    // --- Status ------------------------------------------------------------------------------------------

    /// <summary>A multi-line report for the console and the log: state, the one
    /// reason, progress per resource from custody, and what the last survey saw.
    /// Estimates are labelled as estimates.</summary>
    public string Describe()
    {
        if (_order == null)
        {
            return "No collection order.";
        }

        var text = new StringBuilder();
        text.Append("Order ").Append(_order.Order.Value).Append(": ").Append(CollectionSentences.Describe(State));
        if (State == CollectionOrderState.Paused && PausedByPlayer)
        {
            text.Append(" (by you)");
        }

        text.Append(", phase ").Append(Phase).Append('.');
        if (State == CollectionOrderState.Paused || State == CollectionOrderState.NeedsAttention)
        {
            if (Reason != CollectionAttentionReason.Unspecified)
            {
                text.AppendLine().Append("  Why: ").Append(CollectionSentences.Describe(Reason));
            }
        }

        if (Adopted)
        {
            text.AppendLine().Append("  Taken up again from the settlement record after a reload; ")
                .Append("cf_settle status has the record's own reason.");
        }

        if (NeedsRebind)
        {
            text.AppendLine().Append("  This order was given before the world was reloaded. Its work area and ")
                .Append(_order.Delivery.Kind == DeliveryKind.Container ? "chest have" : "work area has")
                .Append(" to be chosen again (cf_collect rebind) before it can go on. What he carries is unchanged, ")
                .Append("and cf_collect cancel always works.");
        }

        text.AppendLine().Append("  Work area: ").Append(_order.Scope.Source).Append(" around ")
            .Append(_order.Scope.AnchorDescription).Append(", radius ")
            .Append(_order.Scope.RadiusMetres.ToString("0.#", CultureInfo.InvariantCulture)).Append(" m.");
        if (State == CollectionOrderState.HoldingForPlayer)
        {
            // The order ends when the record shows the materials handed over,
            // which is the settlement's own handover act, not something the
            // loop can do to itself. Say so plainly, with the way out.
            text.AppendLine().Append("  He is holding everything for you. It stays on him until the settlement record ")
                .Append("shows it handed over; cf_collect cancel ends the order and leaves what he carries with him, ")
                .Append("ready to be taken back.");
        }

        text.AppendLine().Append("  Delivery: ")
            .Append(_order.Delivery.Kind == DeliveryKind.HoldForPlayer ? "hold for you" : "chest " + _order.Delivery.ContainerKey)
            .Append("; ").Append(_order.Participation == ParticipationMode.Solo ? "solo" : "with the hauler").Append('.');

        foreach (ResourceQuota quota in _order.Quotas)
        {
            ResourceProgress progress = _custody.View.ProgressFor(_order, quota.Resource)
                ?? new ResourceProgress(quota.Resource, quota.Requested);
            text.AppendLine().Append("  ").Append(quota.Resource).Append(": requested ").Append(progress.Requested)
                .Append(", carried ").Append(progress.Carried)
                .Append(", in cart ").Append(progress.InCart)
                .Append(", delivered ").Append(progress.Delivered)
                .Append(", handed over ").Append(progress.HandedOver)
                .Append(", on the ground ").Append(progress.OnGround)
                .Append(", lost ").Append(progress.Lost)
                .Append(", still to collect ").Append(progress.StillToCollect);
            if (_target != null && _target.Yields == quota.Resource)
            {
                text.Append(" (reserved estimate ").Append(_target.EstimatedYield).Append(", not counted)");
            }
        }

        SurveyAccounting? accounting = LastAccounting;
        if (accounting != null)
        {
            text.AppendLine().Append("  Last solo survey: ")
                .Append(accounting.LoadedCells).Append(" of ").Append(accounting.TotalCells).Append(" cells loaded");
            if (accounting.TruncatedByBudget)
            {
                text.Append(", CUT SHORT by its budget");
            }

            foreach (ResourceQuota quota in _order.Quotas)
            {
                text.Append("; ").Append(quota.Resource).Append(" sources available ").Append(accounting.Available(quota.Resource))
                    .Append(", exhausted ").Append(accounting.Exhausted(quota.Resource))
                    .Append(", inaccessible ").Append(accounting.Inaccessible(quota.Resource))
                    .Append(", unknown ").Append(accounting.Unknown(quota.Resource));
            }

            text.Append("; look-alikes rejected ").Append(accounting.RejectedNotNatural).Append('.');
        }

        text.AppendLine().Append("  Picks ").Append(Picks).Append(", trips delivered ").Append(Trips)
            .Append(", surveys ").Append(SurveysRun).Append('.');
        if (UnrecordedTransitions > 0)
        {
            text.Append(" ").Append(UnrecordedTransitions).Append(" stop(s) could not be written to the record.");
        }

        if (LastEvidence.Length > 0)
        {
            text.AppendLine().Append("  Last: ").Append(LastEvidence);
        }

        return text.ToString();
    }
}
