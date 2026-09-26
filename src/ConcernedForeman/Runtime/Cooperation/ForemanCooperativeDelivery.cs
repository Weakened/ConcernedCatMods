using System;
using System.Globalization;
using TheConcernedCat.ConcernedForeman.Runtime.Work;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Cooperation;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;
using UnityEngine;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedForeman.Runtime.Cooperation;

/// <summary>Foreman's <see cref="ICooperativeDelivery"/> (#317): the game-side
/// shell around the game-free <see cref="CooperativeDeliveryLoop"/>, over the
/// haul capability discovered from Teamster, Thorstein's worker motion (agent
/// C) and the custody runtime (agent D).
///
/// <b>How the collection loop uses it.</b> It ticks this only at its own carry
/// checkpoint and then every frame while the answer is Working; CollectMore
/// hands Thorstein back. So the first tick after a hand-back is a hand-over,
/// and a tick on a stopped run means the order was resumed.
///
/// <b>Fails closed.</b> Anything thrown inside a tick is caught and latched: the
/// delivery answers Paused / HaulerUnavailable for the rest of the session and
/// logs once. It never lets an exception into the game's update loop.</summary>
internal sealed class ForemanCooperativeDelivery : ICooperativeDelivery
{
    private readonly IHaulEndpointSource _endpoints;
    private readonly ICustodyRuntime _custody;
    private readonly Func<float> _clock;
    private readonly Action<string> _log;
    private readonly string _consumerVersion;
    private readonly CooperationLimits _limits = CooperationLimits.Default;
    private readonly AttentionThrottle _notices = new AttentionThrottle(30f);

    private Func<IWorkerMotion?> _motion = () => null;
    private HaulClient? _client;
    private CooperativeDeliveryLoop? _loop;
    private CooperationStep _lastStep;
    private float _stoppedAt = float.NegativeInfinity;
    private int _loggedRevision = -1;
    private bool _faulted;

    private float _availabilityAt = float.NegativeInfinity;
    private CooperationAvailability _availability;
    private CollectionAttentionReason _availabilityReason;

    internal ForemanCooperativeDelivery(
        IHaulEndpointSource endpoints, ICustodyRuntime custody, Func<float> clock, Action<string> log, string consumerVersion)
    {
        _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
        _custody = custody ?? throw new ArgumentNullException(nameof(custody));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _consumerVersion = consumerVersion;
    }

    /// <summary>The running loop, for the order panel. Null before the first
    /// cooperative order of the world session.</summary>
    internal CooperativeDeliveryLoop? ActiveRun => _loop;

    internal CooperationAvailability LastAvailability => _availability;

    internal string LastAvailabilityDetail { get; private set; } = string.Empty;

    internal bool IsFaulted => _faulted;

    /// <summary>The collection runtime is built after this delivery (it takes
    /// it as a constructor argument), so Thorstein's motion is bound after.
    /// </summary>
    internal void BindMotion(Func<IWorkerMotion?> motion) =>
        _motion = motion ?? throw new ArgumentNullException(nameof(motion));

    private HaulClient Client => _client ??= new HaulClient(_endpoints, _limits, _consumerVersion);

    public bool IsAvailable(out CollectionAttentionReason reason)
    {
        reason = CollectionAttentionReason.HaulerUnavailable;
        if (_faulted)
        {
            return false;
        }

        try
        {
            // An order already cooperating keeps its hauler: Gunnar busy with
            // this order's own haul is not "busy".
            if (_loop != null && !_loop.IsTerminal &&
                _loop.Phase != CooperationPhase.Paused && _loop.Phase != CooperationPhase.NeedsAttention)
            {
                reason = CollectionAttentionReason.Unspecified;
                return true;
            }

            float now = _clock();
            if (now - _availabilityAt >= _limits.PollIntervalSeconds || now < _availabilityAt)
            {
                _availabilityAt = now;
                _availability = CooperationAvailabilityProbe.Evaluate(Client, now, out _availabilityReason, out string detail);
                LastAvailabilityDetail = detail;
            }

            reason = _availabilityReason;
            return _availability == CooperationAvailability.Available;
        }
        catch (Exception exception)
        {
            Fault("checking availability", exception);
            return false;
        }
    }

    public CooperativeStep Tick(CollectionOrderDefinition order, float now, out CollectionAttentionReason reason)
    {
        reason = CollectionAttentionReason.HaulerUnavailable;
        if (_faulted || order == null)
        {
            return CooperativeStep.Paused;
        }

        try
        {
            if (_loop == null || !_loop.Order.Order.Equals(order.Order))
            {
                if (_loop != null && !_loop.IsTerminal)
                {
                    _loop.Cancel(detachAndPark: false, now);
                }

                _loop = new CooperativeDeliveryLoop(
                    order, Client, new MotionWorker(() => _motion(), order.Order.Value), new RuntimeCustody(_custody),
                    _limits, Guid.NewGuid());
                _lastStep = CooperationStep.Unspecified;
                _loggedRevision = -1;
            }

            if ((_loop.Phase == CooperationPhase.Paused || _loop.Phase == CooperationPhase.NeedsAttention) &&
                now - _stoppedAt >= _limits.PollIntervalSeconds)
            {
                // The collection loop stops ticking a stopped run; ticking again
                // means the order was resumed.
                _loop.Resume(now);
            }

            if (_lastStep != CooperationStep.Working)
            {
                _loop.HandOver();
            }

            CooperationTick tick = _loop.Tick(now);
            _lastStep = tick.Step;
            if (tick.Step == CooperationStep.Paused || tick.Step == CooperationStep.NeedsAttention)
            {
                _stoppedAt = now;
            }

            Report(tick, now);
            reason = tick.Reason;
            return Map(tick.Step);
        }
        catch (Exception exception)
        {
            Fault("running the cooperative order", exception);
            return CooperativeStep.Paused;
        }
    }

    public void Cancel(CollectionOrderDefinition order, bool detachAndPark)
    {
        try
        {
            if (_loop != null && order != null && _loop.Order.Order.Equals(order.Order))
            {
                _loop.Cancel(detachAndPark, _clock());
                _log("Cooperative order \"" + order.Order.Value + "\" cancelled: " + _loop.LastCancelOutcome);
            }
        }
        catch (Exception exception)
        {
            Fault("cancelling the cooperative order", exception);
        }
    }

    /// <summary>The player paused the order from the panel: Gunnar stops where
    /// he is and the cart is released from any hold.</summary>
    internal void PauseForPlayer()
    {
        try
        {
            _loop?.Pause(_clock());
            _stoppedAt = _clock();
        }
        catch (Exception exception)
        {
            Fault("pausing the cooperative order", exception);
        }
    }

    /// <summary>A world went away: the run, its provider epoch and its lease
    /// belonged to it. Nothing is cancelled across the boundary - Teamster's
    /// own world unload ends Gunnar's haul.</summary>
    internal void OnWorldUnloaded()
    {
        _loop = null;
        _client = null;
        _lastStep = CooperationStep.Unspecified;
        _availabilityAt = float.NegativeInfinity;
        _availability = CooperationAvailability.Unspecified;
    }

    private static CooperativeStep Map(CooperationStep step)
    {
        switch (step)
        {
            case CooperationStep.Working:
                return CooperativeStep.Working;
            case CooperationStep.CollectMore:
                return CooperativeStep.CollectMore;
            case CooperationStep.Delivered:
                return CooperativeStep.Delivered;
            case CooperationStep.NeedsAttention:
                return CooperativeStep.NeedsAttention;
            default:
                return CooperativeStep.Paused;
        }
    }

    private void Report(CooperationTick tick, float now)
    {
        if (tick.PlanRevision == _loggedRevision)
        {
            return;
        }

        _loggedRevision = tick.PlanRevision;
        bool stopped = tick.Step == CooperationStep.Paused || tick.Step == CooperationStep.NeedsAttention;
        string key = tick.Phase + "/" + tick.Reason;
        if (stopped ? _notices.ShouldNotify(key, now) : _notices.ShouldNotify("phase/" + tick.Phase, now))
        {
            _log(
                "Cooperative order: " + tick.Phase +
                (tick.Reason != CollectionAttentionReason.Unspecified ? " (" + tick.Reason + ")" : string.Empty) +
                (tick.Detail.Length > 0 ? " - " + tick.Detail : string.Empty));
        }
    }

    private void Fault(string what, Exception exception)
    {
        if (_faulted)
        {
            return;
        }

        _faulted = true;
        try
        {
            _log(
                "Cooperative delivery failed while " + what + " and is off for this session; collection orders " +
                "can still run solo. " + SafeFailure.Brief(exception));
        }
        catch (Exception)
        {
            // Nothing left to do: the delivery is already latched off.
        }
    }

    /// <summary>Thorstein through the collection agent's worker motion, always
    /// in the name of the order that holds his actor mode.</summary>
    private sealed class MotionWorker : ICooperationWorker
    {
        private readonly Func<IWorkerMotion?> _motion;
        private readonly string _jobId;

        public MotionWorker(Func<IWorkerMotion?> motion, string jobId)
        {
            _motion = motion;
            _jobId = jobId;
        }

        public bool IsPresent => _motion()?.IsPresent ?? false;

        public SitePoint Position
        {
            get
            {
                IWorkerMotion? motion = _motion();
                if (motion == null)
                {
                    return default;
                }

                Vector3 position = motion.Position;
                return new SitePoint(position.x, position.y, position.z);
            }
        }

        public CooperationWalkStatus WalkStatus
        {
            get
            {
                switch (_motion()?.Status ?? WorkerWalkStatus.Idle)
                {
                    case WorkerWalkStatus.Walking:
                        return CooperationWalkStatus.Walking;
                    case WorkerWalkStatus.Arrived:
                        return CooperationWalkStatus.Arrived;
                    case WorkerWalkStatus.Deferred:
                        return CooperationWalkStatus.Deferred;
                    default:
                        return CooperationWalkStatus.Idle;
                }
            }
        }

        public bool WalkTo(SitePoint point, float tolerance)
        {
            IWorkerMotion? motion = _motion();
            return motion != null && motion.WalkTo(new Vector3(point.X, point.Y, point.Z), tolerance, _jobId);
        }

        public void Stop() => _motion()?.Stop(_jobId);
    }

    /// <summary>The custody runtime as the loop sees it, with the custody
    /// locations the runtime and the collection loop already use: Thorstein's
    /// pack by his worker key, a cart by its session key in the provider
    /// epoch, a chest by the delivery target's key and epoch.</summary>
    private sealed class RuntimeCustody : ICooperationCustody
    {
        private readonly ICustodyRuntime _runtime;

        public RuntimeCustody(ICustodyRuntime runtime)
        {
            _runtime = runtime;
        }

        public IMaterialCustodyView View => _runtime.View;

        public ITransferExecutor Executor => _runtime.Executor;

        public bool IsWritable => _runtime.IsWritable;

        public CustodyLocation WorkerLocation => new CustodyLocation(CustodyPlace.Worker, WorkerKey.Thorstein.Value, Guid.Empty);

        public CustodyLocation CartLocation(string cartSessionKey, Guid providerEpoch) =>
            new CustodyLocation(CustodyPlace.Cart, cartSessionKey, providerEpoch);

        public CustodyLocation DestinationLocation(DeliveryTarget target) =>
            new CustodyLocation(CustodyPlace.Destination, target.ContainerKey, target.WorldLoadEpoch);

        public bool TryResolveWorker(out IInventoryPort? port, out CollectionAttentionReason refusal) =>
            _runtime.TryResolveWorker(WorkerKey.Thorstein, out port, out refusal);

        public bool TryResolveCart(string cartSessionKey, Guid providerEpoch, out IInventoryPort? port, out CollectionAttentionReason refusal) =>
            _runtime.TryResolveCart(cartSessionKey, providerEpoch, out port, out refusal);

        public bool TryResolveContainer(DeliveryTarget target, out IInventoryPort? port, out CollectionAttentionReason refusal) =>
            _runtime.TryResolveContainer(target, out port, out refusal);

        /// <summary>Use the custody runtime's canonical request-id scheme so
        /// cooperative transfers and solo transfers share one idempotence rule.</summary>
        public RequestId NextTransferId(OrderId order) =>
            CustodyIds.ForTransfer(order, _runtime.View);

        public bool RecordCartBaseline(OrderId order, string leaseId, string cartSessionKey, Guid providerEpoch) =>
            _runtime.RecordCartBaseline(order, leaseId, cartSessionKey, providerEpoch);
    }
}
