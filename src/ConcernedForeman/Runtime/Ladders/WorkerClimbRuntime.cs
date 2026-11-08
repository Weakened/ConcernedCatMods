using System;
using System.Globalization;
using TheConcernedCat.ConcernedForeman.Domain.Ladders;
using TheConcernedCat.ConcernedForeman.Runtime.Settlement;
using TheConcernedCat.ConcernedForeman.Runtime.Work;
using TheConcernedCat.Diagnostics;
using TheConcernedCat.Workers;
using TheConcernedCat.Workers.Traversal;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Ladders;

/// <summary>#334: <c>cf_worker climb</c> - the explicit order that sends
/// Thorstein's worker body along a route that may climb a Concerned Ladders
/// ladder, and the game half of <see cref="WorkerClimbOrder"/>.
///
/// <b>Off unless two switches are on.</b> <c>Ladders/NpcClimbing</c> (false by
/// default) and the settlement runtime's opt-in, read through the same
/// work-authority rule every worker mutation asks: the host, not dedicated, with
/// nobody else connected. Either one off refuses the order, and either one going
/// off ends it.
///
/// <b>What it binds.</b> The ONE body bound when the order is given, a
/// <see cref="WorkerLadderReceiver"/> made for that body, vanilla's navigation
/// mesh as the walk evidence, and <see cref="WorkerClimbMotor"/> as the only
/// thing that may place the body while it is on a ladder. Thorstein's actor
/// mode is held through Concerned NPC's arbiter for the whole order. Nothing
/// here writes a position itself.</summary>
internal sealed class WorkerClimbRuntime
{
    private readonly SettlementRuntime _settlement;
    private readonly LadderSettings _ladders;
    private readonly Action<string> _log;
    private readonly WorkerSitePolicy _site = new WorldSitePolicy();
    private readonly WorkerClimbOrder _order;

    internal WorkerClimbRuntime(
        SettlementRuntime settlement, IActorModeHold modes, LadderSettings ladders, Action<string> log)
    {
        _settlement = settlement ?? throw new ArgumentNullException(nameof(settlement));
        _ladders = ladders ?? throw new ArgumentNullException(nameof(ladders));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _order = new WorkerClimbOrder(modes, new Gates(this), log);
    }

    internal WorkerClimbOrder Order => _order;

    /// <summary><c>cf_worker climb here | &lt;x&gt; &lt;y&gt; &lt;z&gt; | cancel | status</c>.
    /// <paramref name="args"/>[0] is "climb".</summary>
    internal string Execute(string[] args)
    {
        string verb = args.Length > 1 ? args[1].ToLowerInvariant() : "status";
        switch (verb)
        {
            case "status":
                return _order.Describe() + " Ladders/NpcClimbing is " + (_ladders.NpcClimbing.Value ? "on" : "off") + ".";
            case "cancel":
                if (!_order.IsActive)
                {
                    return "No climb order is running.";
                }

                _order.Cancel("cancelled by the player");
                return _order.Describe();
            case "here":
                Player player = Player.m_localPlayer;
                if (player == null)
                {
                    return "Refused: no local player to stand at.";
                }

                Vector3 feet = player.transform.position;
                return Start(new WorkPoint(feet.x, feet.y, feet.z));
            default:
                if (args.Length < 4
                    || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                    || !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                    || !float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                {
                    return "Usage: cf_worker climb here | <x> <y> <z> | cancel | status. Stand where he should go " +
                        "and use \"here\"; he walks, and climbs a Concerned Ladders ladder if that is the way.";
                }

                return Start(new WorkPoint(x, y, z));
        }
    }

    /// <summary>Once a frame from the plugin.</summary>
    internal void Update(float now) => _order.Supervise(now);

    /// <summary>The world is going away: the order ends and the body is given
    /// back before the scene drops it.</summary>
    internal void OnWorldUnloaded()
    {
        _order.Cancel("the world is unloading");
        _order.Supervise(Time.time);
    }

    /// <summary>The plugin is being torn down.</summary>
    internal void Shutdown()
    {
        _order.Cancel("the plugin is shutting down");
        _order.Supervise(Time.time);
    }

    private string Start(WorkPoint target)
    {
        if (!_ladders.NpcClimbing.Value)
        {
            return "Refused: worker ladder climbing is off (Ladders/NpcClimbing = false, the default).";
        }

        ForemanWorkerAI? ai = _settlement.Worker;
        if (ai == null)
        {
            return "Refused: Thorstein has no worker body in loaded ground (cf_worker spawn).";
        }

        var capabilities = new TraversalCapabilities(_ladders.NpcClimbing.Value);
        capabilities.Grant(WorkerKey.Thorstein, TraversalCapability.Ladders);
        WorkerLadderReceiver receiver;
        try
        {
            receiver = new WorkerLadderReceiver(ai, capabilities, SiteAllowed, _log);
        }
        catch (Exception exception)
        {
            return "Refused: his body cannot be measured for a ladder (" + SafeFailure.Brief(exception) + ").";
        }

        return _order.Start(
            target,
            new Body(ai, () => _settlement.Worker, receiver),
            receiver,
            new WorkerNavMeshWalkProbe(() => Time.time),
            capabilities,
            Time.time);
    }

    private bool SiteAllowed(WorkPoint point)
    {
        var at = new Vector3(point.X, point.Y, point.Z);
        return _site.IsInLoadedGround(at) && !_site.IsHazardous(at);
    }

    private sealed class Gates : IWorkerClimbGates
    {
        private readonly WorkerClimbRuntime _runtime;

        internal Gates(WorkerClimbRuntime runtime)
        {
            _runtime = runtime;
        }

        public bool NpcClimbing => _runtime._ladders.NpcClimbing.Value;

        public WorkAuthorityVerdict Authority => _runtime._settlement.WorkAuthority();
    }

    /// <summary>The body the order was given, and only that one.</summary>
    private sealed class Body : IWorkerClimbBody
    {
        private readonly ForemanWorkerAI _ai;
        private readonly Func<ForemanWorkerAI?> _current;
        private readonly WorkerLadderReceiver _receiver;
        private readonly Humanoid? _humanoid;
        private Action<ForemanWorkerAI, float>? _installed;
        private WorkPoint _walkingTo;
        private bool _walking;

        internal Body(ForemanWorkerAI ai, Func<ForemanWorkerAI?> current, WorkerLadderReceiver receiver)
        {
            _ai = ai;
            _current = current;
            _receiver = receiver;
            _humanoid = ai.GetComponent<Humanoid>();
        }

        public bool IsCurrent
        {
            get
            {
                if (_ai == null || _ai.IsFaulted || !_ai.IsOwnedAndValid || _humanoid == null || _humanoid.IsDead())
                {
                    return false;
                }

                ForemanWorkerAI? current = _current();
                return current != null && ReferenceEquals(current, _ai);
            }
        }

        public WorkPoint Position
        {
            get
            {
                if (_ai == null)
                {
                    return default;
                }

                Vector3 at = _ai.transform.position;
                return new WorkPoint(at.x, at.y, at.z);
            }
        }

        public bool WalkToward(WorkPoint target, float arrivalTolerance)
        {
            if (!IsCurrent || _ai.IsClimbing)
            {
                return false;
            }

            // The planner giving up on THIS leg's goal is a blocked walk. A
            // deferral left over from an earlier, different goal is not: a new
            // goal is a new question, and SetJobGoal asks it afresh.
            if (_walking && _walkingTo.Equals(target) && _ai.WalkStatus == WorkerWalkStatus.Deferred)
            {
                return false;
            }

            _walking = _ai.SetJobGoal(new Vector3(target.X, target.Y, target.Z), arrivalTolerance);
            _walkingTo = target;
            return _walking;
        }

        public void StopWalking()
        {
            _walking = false;
            if (_ai != null && !_ai.IsFaulted)
            {
                _ai.ClearJobGoal();
            }
        }

        public IWorkerLadderMotor? TrySuspendGroundMotor(WorkerKey worker, Guid bodyEpoch) =>
            IsCurrent ? WorkerClimbMotor.TrySuspend(_ai, worker, bodyEpoch, _receiver.IsMoveClear) : null;

        public bool IsSupportedLanding(WorkPoint target) => IsCurrent && _receiver.IsLandingSupported(target);

        public void Attach(Action<float> tick)
        {
            if (_ai == null)
            {
                return;
            }

            _installed = (_, dt) => tick(dt);
            _ai.RouteTick = _installed;
        }

        public void Detach()
        {
            if (_ai != null && _installed != null && _ai.RouteTick == _installed)
            {
                _ai.RouteTick = null;
            }

            _installed = null;
        }
    }
}
