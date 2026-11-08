using System;
using System.Globalization;
using TheConcernedCat.Workers;
using TheConcernedCat.Workers.Traversal;

namespace TheConcernedCat.ConcernedForeman.Domain.Ladders;

/// <summary>The ladder links one order is allowed to use: the #334 receiver for
/// the one body the order was given. Its network and its checked-path traversal
/// belong together (WORKER_LADDER_RECEIVER_2026_11_01.md, "Binding procedure").
/// </summary>
internal interface IWorkerClimbLinks
{
    TraversalLinkNetwork Network { get; }

    ILadderTraversal Traversal { get; }

    WorkerLadderSurveyResult Tick(
        float deltaSeconds, bool npcClimbing, bool explicitOrder, WorkAuthorityVerdict authority,
        WorkPoint centre, float radius);

    bool RevalidateForAdmission(
        bool npcClimbing, bool explicitOrder, WorkAuthorityVerdict authority, WorkPoint centre, float radius);

    void Forget();
}

/// <summary>The ONE worker body an order was given, captured when it starts.
/// It never follows a mutable "current body" to a different body: a reload, a
/// respawn or a death makes <see cref="IsCurrent"/> false for good.</summary>
internal interface IWorkerClimbBody
{
    /// <summary>Still the settlement's bound body, valid, owned by this
    /// process, alive and not faulted.</summary>
    bool IsCurrent { get; }

    WorkPoint Position { get; }

    /// <summary>A ground walk through the worker's own budgeted vanilla motor.
    /// False when the walk was refused or the planner gave up on it.</summary>
    bool WalkToward(WorkPoint target, float arrivalTolerance);

    void StopWalking();

    /// <summary>The scoped ladder lease: vanilla's ground motor suspended and
    /// the body's own position written only by the returned motor.</summary>
    IWorkerLadderMotor? TrySuspendGroundMotor(WorkerKey worker, Guid bodyEpoch);

    bool IsSupportedLanding(WorkPoint target);

    /// <summary>Runs <paramref name="tick"/> inside the body's own simulation
    /// step, the same place the collection job's mutations run.</summary>
    void Attach(Action<float> tick);

    void Detach();
}

/// <summary>The live gates, re-read every time they are asked.</summary>
internal interface IWorkerClimbGates
{
    /// <summary><c>Ladders/NpcClimbing</c>, off by default.</summary>
    bool NpcClimbing { get; }

    /// <summary>The shared work-authority rule (D3): opted in, the host, not
    /// dedicated, nobody else connected.</summary>
    WorkAuthorityVerdict Authority { get; }
}

internal enum WorkerClimbState
{
    Idle = 0,
    Searching = 1,
    Moving = 2,

    /// <summary>The route has stopped but the ladder motor has not given the
    /// body back yet. The identity stays held until it does.</summary>
    Releasing = 3,
    Finished = 4,
}

/// <summary>#334: one explicitly ordered ladder route for Thorstein's own body.
///
/// <b>What it binds.</b> PR #501 delivered every part of a ladder route except
/// the order that drives one: the receiver, the multi-link search and the
/// runner existed and nothing called them. This is that caller, game-free so
/// that every exit can be proved here rather than read. An order:
///
/// <list type="number">
/// <item>refuses unless <c>Ladders/NpcClimbing</c> is on, the work-authority
/// rule grants, the body is the bound one and the identity can be held;</item>
/// <item>holds Thorstein's actor mode through Concerned NPC's arbiter for its
/// whole life, so presentation, collection, construction and a despawn all see
/// him as busy;</item>
/// <item>forces a fresh ladder survey at admission, searches over published
/// links and verified walk edges, then runs the route one leg at a time;</item>
/// <item>forces another fresh survey immediately before every ladder entry and
/// refuses the entry if the leg's link generation moved;</item>
/// <item>ends on completion, a blocked or refused leg, a cancel, a world
/// unload, a lost, dead or faulted body, a stalled body tick, the switch going
/// off, or authority being lost - and gives the identity back only once the
/// ladder motor has restored the body.</item>
/// </list>
///
/// No position is written here and no link is invented here. The runner owns
/// the walk/climb exclusivity, the checked traversal owns the path, and the
/// native motor owns the single position write.</summary>
internal sealed class WorkerClimbOrder
{
    /// <summary>How close the ground motor is asked to walk to a leg's end.
    /// Inside the runner's own 1 m arrival so the motor never parks short of
    /// where the runner accepts arrival (#352's lesson).</summary>
    internal const float WalkArrivalMetres = 0.5f;

    /// <summary>The search, the survey and the runner all bound a route at
    /// 64 m from where it starts.</summary>
    internal const float MaximumReachMetres = 64f;

    /// <summary>A body whose own tick has not run for this long is not being
    /// simulated by this process any more (unloaded, ownership gone, removed).
    /// </summary>
    internal const float StalledTickSeconds = 2f;

    /// <summary>About thirty seconds of unanswered walk questions at the
    /// worker's 20 Hz tick, after which the search gives up.</summary>
    internal const int MaximumPendingWalkAnswers = 600;

    private readonly IActorModeHold _modes;
    private readonly IWorkerClimbGates _gates;
    private readonly Action<string> _log;

    private IWorkerClimbBody? _body;
    private IWorkerClimbLinks? _links;
    private IWorkerWalkProbe? _walk;
    private TraversalCapabilities? _capabilities;
    private WorkerRouteSearch? _search;
    private WorkerRouteRunner? _runner;
    private WorkerRoutePlan? _plan;
    private string? _jobId;
    private Guid _epoch;
    private WorkPoint _target;
    private WorkPoint _centre;
    private float _radius;
    private bool _ticked;
    private float _lastTickSeen;
    private string _ending = string.Empty;

    internal WorkerClimbOrder(IActorModeHold modes, IWorkerClimbGates gates, Action<string> log)
    {
        _modes = modes ?? throw new ArgumentNullException(nameof(modes));
        if (!_modes.Worker.Equals(WorkerKey.Thorstein))
        {
            throw new ArgumentException("A worker climb order drives Thorstein.", nameof(modes));
        }

        _gates = gates ?? throw new ArgumentNullException(nameof(gates));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    internal WorkerClimbState State { get; private set; }

    /// <summary>What the last order ended with, for <c>cf_worker climb
    /// status</c>.</summary>
    internal string LastOutcome { get; private set; } = "No climb order has run.";

    internal bool IsActive => State == WorkerClimbState.Searching || State == WorkerClimbState.Moving ||
        State == WorkerClimbState.Releasing;

    internal string? JobId => IsActive ? _jobId : null;

    internal WorkerRouteRunner? Runner => _runner;

    internal WorkerRouteSearch? Search => _search;

    /// <summary>Takes an explicit order to walk, and climb where a published
    /// ladder is the way, to <paramref name="target"/>.</summary>
    /// <returns>One sentence for the console.</returns>
    internal string Start(
        WorkPoint target,
        IWorkerClimbBody body,
        IWorkerClimbLinks links,
        IWorkerWalkProbe walk,
        TraversalCapabilities capabilities,
        float now)
    {
        if (body == null || links == null || walk == null || capabilities == null)
        {
            throw new ArgumentNullException(body == null ? nameof(body) : links == null ? nameof(links) :
                walk == null ? nameof(walk) : nameof(capabilities));
        }

        if (IsActive)
        {
            return "Refused: a climb order is already running (cf_worker climb cancel).";
        }

        if (!_gates.NpcClimbing)
        {
            return "Refused: worker ladder climbing is off (Ladders/NpcClimbing = false, the default).";
        }

        WorkAuthorityVerdict authority = _gates.Authority;
        if (authority != WorkAuthorityVerdict.Granted)
        {
            return "Refused: " + WorkAuthorityPolicy.Describe(authority) + ".";
        }

        if (!body.IsCurrent)
        {
            return "Refused: Thorstein's worker body is not here, not this game's, or not able to act.";
        }

        var actor = TraversalActor.OfWorker(WorkerKey.Thorstein, TraversalCapability.Ladders);
        if (!capabilities.Can(actor, TraversalCapability.Ladders))
        {
            return "Refused: " + capabilities.Explain(actor, TraversalCapability.Ladders);
        }

        WorkPoint from = body.Position;
        if (!target.IsFinite || !from.IsFinite)
        {
            return "Refused: that destination is not a point in the world.";
        }

        float reach = from.HorizontalDistanceTo(target);
        if (reach > MaximumReachMetres || from.VerticalDistanceTo(target) > MaximumReachMetres)
        {
            return "Refused: that destination is more than " +
                MaximumReachMetres.ToString("0", CultureInfo.InvariantCulture) + " m from him.";
        }

        string jobId = "ladder-route:" + Guid.NewGuid().ToString("N");
        ActorModeOutcome held = _modes.Enter(ActorMode.Working, jobId);
        if (!ActorModeGrants.IsGranted(held))
        {
            return !_modes.IsIdentityKnown
                ? "Refused: Concerned NPC never accepted Thorstein, so nothing can hold him."
                : "Refused: Thorstein is busy" + (_modes.JobId == null ? "." : " (" + _modes.JobId + ").");
        }

        _centre = from;
        _radius = Math.Max(8f, Math.Min(MaximumReachMetres, reach + 8f));
        if (!links.RevalidateForAdmission(true, true, authority, _centre, _radius))
        {
            links.Forget();
            _modes.Release(jobId);
            return "Refused: no checked ladder links could be measured here (Concerned Ladders missing or " +
                "unavailable, an ambiguous or over-budget ladder layout, unloaded ground, or a body shape " +
                "that cannot be measured).";
        }

        _body = body;
        _links = links;
        _walk = walk;
        _capabilities = capabilities;
        _jobId = jobId;
        _epoch = Guid.NewGuid();
        _target = target;
        _plan = null;
        _ending = string.Empty;
        _search = new WorkerRouteSearch(
            links.Network, capabilities, actor, from, target,
            new WorkerRouteBudget(maximumPendingAnswers: MaximumPendingWalkAnswers));
        _runner = new WorkerRouteRunner(links.Network, links.Traversal, new RoutePort(this), WorkerKey.Thorstein);
        State = WorkerClimbState.Searching;
        _ticked = false;
        _lastTickSeen = now;
        body.Attach(Tick);
        return "Thorstein ordered to " + Format(target) + " (" + links.Network.Count.ToString(CultureInfo.InvariantCulture) +
            " checked ladder face(s) nearby). He works out a route first.";
    }

    /// <summary>One step, inside the body's own simulation tick. Never throws.
    /// </summary>
    internal void Tick(float deltaSeconds)
    {
        _ticked = true;
        try
        {
            switch (State)
            {
                case WorkerClimbState.Searching:
                    TickSearch(deltaSeconds);
                    break;
                case WorkerClimbState.Moving:
                    TickRoute(deltaSeconds);
                    break;
                case WorkerClimbState.Releasing:
                    TryFinishRelease();
                    break;
            }
        }
        catch (Exception exception)
        {
            Stop("the climb order faulted (" + exception.GetType().Name + ")");
        }
    }

    /// <summary>Once a frame, from the plugin, whether or not the body is
    /// ticking. This is how an order ends when the body's own tick will never
    /// run again: a death, an unload, a lost ownership, a fault.</summary>
    internal void Supervise(float now)
    {
        if (!IsActive)
        {
            return;
        }

        if (_ticked)
        {
            _ticked = false;
            _lastTickSeen = now;
        }

        try
        {
            if (State == WorkerClimbState.Releasing)
            {
                TryFinishRelease();
                return;
            }

            if (!_body!.IsCurrent)
            {
                Stop("his body is gone, dead, faulted or no longer this game's");
            }
            else if (!_gates.NpcClimbing)
            {
                Stop("Ladders/NpcClimbing was switched off");
            }
            else if (_gates.Authority != WorkAuthorityVerdict.Granted)
            {
                Stop(WorkAuthorityPolicy.Describe(_gates.Authority));
            }
            else if (now - _lastTickSeen > StalledTickSeconds || now < _lastTickSeen)
            {
                Stop("his body stopped being simulated");
            }
        }
        catch (Exception exception)
        {
            Stop("the climb supervisor faulted (" + exception.GetType().Name + ")");
        }
    }

    /// <summary>The player's cancel, a world unload or a plugin teardown.</summary>
    internal void Cancel(string reason)
    {
        if (IsActive && State != WorkerClimbState.Releasing)
        {
            Stop(reason);
        }
    }

    internal string Describe()
    {
        switch (State)
        {
            case WorkerClimbState.Searching:
                return "Climb order to " + Format(_target) + ": working out a route (" +
                    _search!.Queries.ToString(CultureInfo.InvariantCulture) + " walk check(s) so far).";
            case WorkerClimbState.Moving:
                WorkerRouteLeg leg = _plan!.Legs[Math.Min(_runner!.LegIndex, _plan.Legs.Count - 1)];
                return "Climb order to " + Format(_target) + ": leg " +
                    (_runner.LegIndex + 1).ToString(CultureInfo.InvariantCulture) + " of " +
                    _plan.Legs.Count.ToString(CultureInfo.InvariantCulture) + " (" +
                    (leg.Kind == WorkerRouteLegKind.Climb ? "ladder, " + leg.Direction : "walking") + "), " +
                    _runner.Phase + ".";
            case WorkerClimbState.Releasing:
                return "Climb order stopping (" + _ending + "): the ladder motor has not given his body back " +
                    "yet, so Thorstein stays held and nothing else may move him.";
            default:
                return LastOutcome;
        }
    }

    private bool Allowed() =>
        _gates.NpcClimbing && _gates.Authority == WorkAuthorityVerdict.Granted && _body != null && _body.IsCurrent &&
        _modes.IsHeldBy(_jobId);

    private void TickSearch(float deltaSeconds)
    {
        if (!Allowed())
        {
            Stop("its authority, switch, body or hold went away while it was planning");
            return;
        }

        WorkerLadderSurveyResult survey = _links!.Tick(
            deltaSeconds, _gates.NpcClimbing, true, _gates.Authority, _centre, _radius);
        if (survey != WorkerLadderSurveyResult.Ready)
        {
            Stop("the ladder survey became " + survey);
            return;
        }

        WorkerRouteSearchState searched = _search!.Advance(
            _walk!, _gates.Authority, _gates.NpcClimbing, explicitOrderActive: true, maximumChecksThisTick: 1);
        if (searched == WorkerRouteSearchState.Refused)
        {
            Stop("no route: " + _search.Refusal);
            return;
        }

        if (searched != WorkerRouteSearchState.Complete)
        {
            return;
        }

        _plan = _search.Plan;
        State = WorkerClimbState.Moving;
        if (_plan == null || !_runner!.Start(_plan, _jobId!))
        {
            Stop("the route could not be started (" + (_runner?.StopReason ?? WorkerRouteStop.Fault) + ")");
            return;
        }

        _log("Thorstein's route to " + Format(_target) + ": " + _plan.Legs.Count.ToString(CultureInfo.InvariantCulture) +
            " leg(s), " + _plan.Metres.ToString("0.#", CultureInfo.InvariantCulture) + " m.");
    }

    private void TickRoute(float deltaSeconds)
    {
        WorkerRouteRunner runner = _runner!;
        if (runner.Phase == WorkerRoutePhase.Walking || runner.Phase == WorkerRoutePhase.Climbing)
        {
            // Keeps the published links honest while he is on the way: a ladder
            // that is removed or rebuilt retires its link, and the runner sees a
            // changed generation and stops rather than climbing what is gone.
            _links!.Tick(deltaSeconds, _gates.NpcClimbing, true, _gates.Authority, _centre, _radius);
        }

        runner.Tick(deltaSeconds);
        if (runner.ControlsRoute)
        {
            return;
        }

        Finish(runner.Phase == WorkerRoutePhase.Complete
            ? "arrived at " + Format(_target)
            : "stopped: " + runner.StopReason);
    }

    /// <summary>Ends the order for <paramref name="reason"/>. The route is
    /// cancelled first; if the motor cannot give the body back yet, the order
    /// waits in <see cref="WorkerClimbState.Releasing"/> with the identity still
    /// held.</summary>
    private void Stop(string reason)
    {
        if (!IsActive)
        {
            return;
        }

        if (_ending.Length == 0)
        {
            _ending = reason;
        }

        WorkerRouteRunner? runner = _runner;
        if (runner != null && runner.ControlsRoute)
        {
            if (runner.Phase != WorkerRoutePhase.Restoring)
            {
                runner.Cancel();
            }

            if (runner.ControlsRoute)
            {
                State = WorkerClimbState.Releasing;
                return;
            }
        }

        Finish(_ending);
    }

    private void TryFinishRelease()
    {
        WorkerRouteRunner runner = _runner!;
        if (runner.ControlsRoute)
        {
            // Restoring: the runner retries the motor's restore without moving.
            runner.Tick(0f);
        }

        if (!runner.ControlsRoute)
        {
            Finish(_ending.Length == 0 ? "stopped: " + runner.StopReason : _ending);
        }
    }

    private void Finish(string outcome)
    {
        try
        {
            _body?.Detach();
            if (_body != null && _body.IsCurrent)
            {
                _body.StopWalking();
            }
        }
        finally
        {
            _links?.Forget();
            if (_jobId != null)
            {
                _modes.Release(_jobId);
            }

            State = WorkerClimbState.Finished;
            LastOutcome = "Last climb order: " + outcome + ".";
            _log("Thorstein's climb order ended: " + outcome + ".");
            _body = null;
            _links = null;
            _walk = null;
            _search = null;
        }
    }

    private static string Format(WorkPoint point) =>
        string.Format(CultureInfo.InvariantCulture, "({0:0.#}, {1:0.#}, {2:0.#})", point.X, point.Y, point.Z);

    /// <summary>The runner's view of this order. Every answer is re-read from
    /// the live gates and the captured body; nothing is cached as permission.
    /// </summary>
    private sealed class RoutePort : IWorkerRoutePort
    {
        private readonly WorkerClimbOrder _order;

        internal RoutePort(WorkerClimbOrder order)
        {
            _order = order;
        }

        public WorkerKey Worker => WorkerKey.Thorstein;

        public Guid BodyEpoch => _order._body != null && _order._body.IsCurrent ? _order._epoch : Guid.Empty;

        public WorkPoint Position => _order._body?.Position ?? default;

        public bool FeatureEnabled => _order._gates.NpcClimbing && _order._capabilities != null &&
            _order._capabilities.NpcClimbingEnabled;

        public bool ExplicitOrderActive =>
            (_order.State == WorkerClimbState.Searching || _order.State == WorkerClimbState.Moving) &&
            _order._ending.Length == 0;

        public bool ActorReady => _order._body != null && _order._body.IsCurrent && _order._modes.IsHeldBy(_order._jobId);

        public WorkAuthorityVerdict Authority => _order._gates.Authority;

        public IWorkerRouteHold? TryHoldRoute(string orderId) =>
            orderId == _order._jobId && _order._modes.IsHeldBy(orderId) ? new BorrowedHold(_order._modes, orderId) : null;

        public bool WalkToward(WorkPoint target, float deltaSeconds) =>
            _order._body != null && _order._body.IsCurrent && _order._body.WalkToward(target, WalkArrivalMetres);

        public void StopWalkingForRoute()
        {
            if (_order._body != null && _order._body.IsCurrent)
            {
                _order._body.StopWalking();
            }
        }

        public IWorkerLadderMotor? TrySuspendGroundMotor(WorkerKey expectedWorker, Guid expectedBodyEpoch)
        {
            IWorkerClimbBody? body = _order._body;
            IWorkerClimbLinks? links = _order._links;
            WorkerRoutePlan? plan = _order._plan;
            WorkerRouteRunner? runner = _order._runner;
            if (body == null || links == null || plan == null || runner == null ||
                !expectedWorker.Equals(WorkerKey.Thorstein) || expectedBodyEpoch != _order._epoch || !body.IsCurrent)
            {
                return null;
            }

            // A fresh survey immediately before entry: the cached publication
            // is never movement authority on its own.
            if (!links.RevalidateForAdmission(
                    _order._gates.NpcClimbing, ExplicitOrderActive, _order._gates.Authority, _order._centre, _order._radius))
            {
                return null;
            }

            // And it must still be the ladder the route was planned over.
            if (runner.LegIndex < 0 || runner.LegIndex >= plan.Legs.Count)
            {
                return null;
            }

            WorkerRouteLeg leg = plan.Legs[runner.LegIndex];
            if (leg.Kind != WorkerRouteLegKind.Climb || leg.LinkId == null ||
                !links.Network.TryGet(leg.LinkId, out _, out int generation) || generation != leg.Generation)
            {
                return null;
            }

            return body.TrySuspendGroundMotor(expectedWorker, expectedBodyEpoch);
        }

        public bool IsSupportedLanding(WorkPoint target) =>
            _order._body != null && _order._body.IsCurrent && _order._body.IsSupportedLanding(target);
    }

    /// <summary>The route borrows the order's hold: disposing it releases only
    /// itself, never the identity the order still holds.</summary>
    private sealed class BorrowedHold : IWorkerRouteHold
    {
        private readonly IActorModeHold _modes;
        private readonly string _jobId;
        private bool _disposed;

        internal BorrowedHold(IActorModeHold modes, string jobId)
        {
            _modes = modes;
            _jobId = jobId;
        }

        public bool IsActive => !_disposed && _modes.IsHeldBy(_jobId);

        public void Dispose() => _disposed = true;
    }
}
