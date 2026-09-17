using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Logging;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>Gunnar's worker runtime (#313): the driver that owns his body
/// binding, the attach seam, one haul executor per world load and the
/// <see cref="IHaulService"/> everything else talks to.
///
/// <b>Two clocks.</b> Every rendered frame (<see cref="Update"/>) it follows the
/// world's lifecycle, keeps the body census and binding current and lets the
/// executor read the joint's signals, so a lost joint, a brake, a peer or a lost
/// cart ends control within a frame. Progress (approach, hitch, pull, stop,
/// detach) runs in Gunnar's own 20 Hz worker tick. A body the runtime has not
/// bound does nothing at all.
///
/// <b>Faults.</b> Nothing escapes into the game's loops: the worker tick latches
/// in <see cref="TeamsterWorkerAI"/>, and a fault here latches the runtime,
/// releases any joint Gunnar holds and leaves every other Teamster feature
/// running.
///
/// <b>Teardown</b> releases the joint first on every path it can observe: the
/// setting switched off or authority lost (through the executor's signals), the
/// game shutting down, the world going away, and the plugin being destroyed.
/// </summary>
internal sealed class GunnarHaulingRuntime : MonoBehaviour, IHaulClock, IHaulExecutionLog
{
    private readonly HaulLimits _limits = HaulLimits.Default.Validate();
    private readonly HaulExecutionLimits _execution = HaulExecutionLimits.Default.Validate();
    private readonly CartSelectionBook _selections = new CartSelectionBook();
    private readonly List<ZDO> _censusBuffer = new List<ZDO>();
    private readonly HashSet<ZDOID> _persistedBodies = new HashSet<ZDOID>();
    private readonly HashSet<ZDOID> _distinctBodies = new HashSet<ZDOID>();
    private readonly Predicate<ZDOID> _recordGone = id => ZDOMan.instance == null || ZDOMan.instance.GetZDO(id) == null;
    private float _nextBindingAt;

    private TeamsterSettings _settings = null!;
    private ManualLogSource _log = null!;
    private TeamsterWorkerBody _body = null!;
    private VagonHitchSeam _seam = null!;
    private GunnarWorkAuthority _authority = null!;
    private ICartRoutePlanner _planner = null!;
    private IHaulMotionMonitor _monitor = null!;
    private CartTelemetryPump? _pump;
    private HaulExecutor? _executor;
    private Guid _epoch;
    private bool _worldUp;
    private bool _censusComplete;
    private int _censusIndex;
    private WorkerBodyStatus _bodyStatus = WorkerBodyStatus.Searching;
    private int _leaseCounter;
    private int _haulCounter;
    private bool _faulted;
    private bool _evidence;
    private float _nextEvidenceAt;

    internal GunnarHaulService Service { get; private set; } = null!;

    public float Now => Time.time;

    /// <summary>The entry point: registers the worker prefab for every session,
    /// adds the runtime to the plugin's object and registers <c>ct_haul</c>.
    /// </summary>
    internal static GunnarHaulingRuntime Install(GameObject host, TeamsterSettings settings, ManualLogSource log)
    {
        TeamsterWorkerPrefab.Install(settings.WorkerBaseCreature.Value, message => log.LogInfo(message));
        GunnarHaulingRuntime runtime = host.AddComponent<GunnarHaulingRuntime>();
        runtime.Initialize(settings, log);
        var command = new HaulConsoleCommand(runtime);
        VanillaConsoleCommands.Register(command, log);
        log.LogInfo(VanillaConsoleCommands.Describe(new[] { command.Name }));
        return runtime;
    }

    /// <summary>The reverse of <see cref="Install"/>, for plugin teardown.
    /// </summary>
    internal static void Uninstall(GunnarHaulingRuntime? runtime)
    {
        if (runtime != null)
        {
            runtime.Shutdown("the plugin is being removed");
            UnityEngine.Object.Destroy(runtime);
        }

        TeamsterWorkerPrefab.Uninstall();
    }

    private void Initialize(TeamsterSettings settings, ManualLogSource log)
    {
        _settings = settings;
        _log = log;
        _body = new TeamsterWorkerBody(_execution);
        _seam = new VagonHitchSeam(_body, EngagedBrakeCartId, _execution);
        _authority = new GunnarWorkAuthority(settings);
        _planner = new StraightLinePlaceholderPlanner(new StraightSegmentProbe(LeasedCartObservation), _limits, _execution);
        _monitor = new PlaceholderHaulMotionMonitor(_limits);
        Service = new GunnarHaulService(_authority);
        TeamsterWorkerAI.TickHandler = OnWorkerTick;
        TeamsterWorkerAI.ErrorLog = message => _log.LogError(message);

        _log.LogInfo(
            "Gunnar's hauling is " + (settings.GunnarHaulingEnabled.Value ? "ENABLED" : "off (the default)") +
            "; pull strength " + settings.GunnarPullStrength.Value + ", worker body from '" + TeamsterWorkerPrefab.BaseCreature + "'.");
        if (_seam.IsAvailable)
        {
            _log.LogInfo("Gunnar's cart seam is available: " + _seam.Probe.VerifiedMembers.Count + " game members verified.");
        }
        else
        {
            _log.LogWarning(
                "Gunnar's cart seam is UNAVAILABLE, so he will not haul: " + _seam.UnavailableDetail +
                ". Cart telemetry and the parking brake are unaffected.");
        }
    }

    private void Update()
    {
        if (_faulted)
        {
            return;
        }

        try
        {
            FrameTick();
        }
        catch (Exception exception)
        {
            _faulted = true;
            _log.LogError("Gunnar's hauling runtime faulted and is now off for this session: " + exception);
            EmergencyRelease();
        }
    }

    private void OnDestroy()
    {
        Shutdown("the plugin is shutting down");
        if (TeamsterWorkerAI.TickHandler == (Action<TeamsterWorkerAI>)OnWorkerTick)
        {
            TeamsterWorkerAI.TickHandler = null;
        }
    }

    private void FrameTick()
    {
        bool worldUp = ZNetScene.instance != null && ZNet.instance != null && ZDOMan.instance != null;
        if (worldUp && !_worldUp)
        {
            OnWorldLoaded();
        }
        else if (!worldUp && _worldUp)
        {
            OnWorldUnloaded();
        }

        _worldUp = worldUp;
        if (!worldUp || _executor == null)
        {
            return;
        }

        if (Game.instance != null && Game.instance.IsShuttingDown())
        {
            Shutdown("the game is shutting down");
            return;
        }

        AdvanceCensus();
        RefreshBinding();
        _executor.ObserveFrame();
        LogEvidenceIfDue();
    }

    private void OnWorkerTick(TeamsterWorkerAI ai)
    {
        if (_faulted || _executor == null || ai != _body.Bound)
        {
            return;
        }

        _executor.Tick();
    }

    private void OnWorldLoaded()
    {
        _epoch = Guid.NewGuid();
        _censusComplete = false;
        _censusIndex = 0;
        _persistedBodies.Clear();
        _bodyStatus = WorkerBodyStatus.Searching;
        _selections.Clear();
        _seam.Forget();
        _body.Bind(null);
        _pump = null;
        var ports = new HaulExecutorPorts(_body, _seam, _planner, _monitor, _authority, this, this);
        _executor = new HaulExecutor(ports, WorkerKey.Gunnar, _epoch, Service.NextStartingRevision, _limits, _execution);
        Service.Bind(_executor);
        _log.LogInfo("Gunnar's hauling: a world loaded (epoch " + _epoch.ToString("N", CultureInfo.InvariantCulture).Substring(0, 8) + "); leases start empty.");
    }

    private void OnWorldUnloaded()
    {
        Shutdown("the world unloaded");
    }

    /// <summary>Detach first, then forget the world.</summary>
    private void Shutdown(string why)
    {
        HaulExecutor? executor = _executor;
        _executor = null;
        if (executor != null)
        {
            try
            {
                executor.Teardown(why, LeaseInvalidation.WorldReloaded);
            }
            catch (Exception exception)
            {
                _log.LogWarning("Gunnar's teardown could not complete cleanly (" + why + "): " + exception.Message);
            }

            Service.Unbind();
        }

        _body.Bind(null);
        _seam.Forget();
        _selections.Clear();
    }

    private void EmergencyRelease()
    {
        try
        {
            CartLease? lease = _executor?.ActiveLease;
            if (lease != null && _executor!.Attached)
            {
                _seam.ReleaseJoint(lease.Cart);
            }

            _body.Stop();
            Service.Unbind();
            _executor = null;
        }
        catch
        {
            // The runtime is already off; nothing more may escape.
        }
    }

    // ------------------------------------------------------------------
    // Body census and binding (CONTRACTS.md §2.6)
    // ------------------------------------------------------------------

    private void AdvanceCensus()
    {
        if (_censusComplete || ZDOMan.instance == null)
        {
            return;
        }

        _censusBuffer.Clear();
        bool done = ZDOMan.instance.GetAllZDOsWithPrefabIterative(TeamsterWorkerPrefab.PrefabName, _censusBuffer, ref _censusIndex);
        for (int index = 0; index < _censusBuffer.Count; index++)
        {
            ZDO record = _censusBuffer[index];
            if (record != null && string.Equals(record.GetString("tcc.worker.key", string.Empty), WorkerKey.Gunnar.Value, StringComparison.Ordinal))
            {
                _persistedBodies.Add(record.m_uid);
            }
        }

        _censusBuffer.Clear();
        if (done)
        {
            _censusComplete = true;
            _log.LogInfo("Gunnar's body census: " + _persistedBodies.Count + " saved bod" + (_persistedBodies.Count == 1 ? "y" : "ies") + " in this world.");
        }
    }

    private void RefreshBinding()
    {
        // Four times a second is far faster than bodies load or unload, and it
        // keeps the census bookkeeping off the per-frame path. A body that
        // vanishes mid-haul is caught every frame by the executor's own signals.
        if (Time.time < _nextBindingAt && _body.Bound != null)
        {
            return;
        }

        _nextBindingAt = Time.time + 0.25f;
        _persistedBodies.RemoveWhere(_recordGone);
        int loaded = 0;
        TeamsterWorkerAI? candidate = null;
        HashSet<ZDOID> distinct = _distinctBodies;
        distinct.Clear();
        distinct.UnionWith(_persistedBodies);
        IReadOnlyList<TeamsterWorkerAI> live = TeamsterWorkerAI.Live;
        for (int index = 0; index < live.Count; index++)
        {
            TeamsterWorkerAI body = live[index];
            if (body == null || !string.Equals(body.Identity, WorkerKey.Gunnar.Value, StringComparison.Ordinal))
            {
                continue;
            }

            ZNetView view = body.GetComponent<ZNetView>();
            if (view == null || !view.IsValid())
            {
                continue;
            }

            loaded++;
            candidate = body;
            distinct.Add(view.GetZDO().m_uid);
        }

        WorkerBodyStatus status = WorkerBodyCensus.Decide(_censusComplete, distinct.Count, loaded, candidate != null && candidate.IsFaulted);
        if (status != _bodyStatus)
        {
            _log.LogInfo("Gunnar's body: " + status + " - " + WorkerBodyCensus.Describe(status));
            _bodyStatus = status;
        }

        TeamsterWorkerAI? bind = status == WorkerBodyStatus.Bound || status == WorkerBodyStatus.Faulted ? candidate : null;
        if (bind != _body.Bound)
        {
            _body.Bind(bind);
        }

        // A body loaded with the world binds before the player exists; D5
        // calibration waits for a player to measure, never for a guess.
        if (_body.Bound != null && float.IsNaN(_body.CalibratedMassKg) && Player.m_localPlayer != null &&
            (_executor == null || !_executor.Attached))
        {
            _body.Calibrate();
        }
    }

    // ------------------------------------------------------------------
    // Ports
    // ------------------------------------------------------------------

    public void Info(string message) => _log.LogInfo(message);

    public void Warning(string message) => _log.LogWarning(message);

    public void Bug(string message) => _log.LogError("[bug] " + message);

    private string? EngagedBrakeCartId()
    {
        if (_pump == null)
        {
            _pump = GetComponent<CartTelemetryPump>();
        }

        return _pump != null ? _pump.Brake?.EngagedCartId : null;
    }

    private CartObservation? LeasedCartObservation()
    {
        CartLease? lease = _executor?.ActiveLease;
        return lease == null ? (CartObservation?)null : _seam.Observe(lease.Cart);
    }

    // ------------------------------------------------------------------
    // ct_haul (a development aid; the visible controls are agent E's)
    // ------------------------------------------------------------------

    internal string Execute(string[]? args)
    {
        string sub = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "status";
        switch (sub)
        {
            case "status": return Status();
            case "seam": return Seam();
            case "spawn": return Spawn();
            case "retire": return Retire();
            case "assign": return Assign();
            case "confirm": return Confirm();
            case "release": return Release();
            case "go": return Go(args);
            case "stop": return Stop(detach: false);
            case "detach": return Stop(detach: true);
            case "evidence": return Evidence(args);
            default:
                return "Unknown subcommand. Try: status, seam, spawn, assign, confirm, go <x> <z> [radius], stop, detach, release, retire, evidence on|off.";
        }
    }

    private string Status()
    {
        var text = new StringBuilder();
        WorkAuthorityVerdict authority = _authority.Evaluate();
        text.Append("Authority: ").Append(authority).Append(" - ").Append(WorkAuthorityPolicy.Describe(authority)).AppendLine();
        text.Append("Seam: ").Append(_seam.IsAvailable ? "available" : "UNAVAILABLE (" + _seam.UnavailableDetail + ")");
        text.Append(". Prefab: ").Append(TeamsterWorkerPrefab.IsReady ? "registered" : TeamsterWorkerPrefab.LastFailure).AppendLine();
        text.Append("Body: ").Append(_bodyStatus).Append(" - ").Append(WorkerBodyCensus.Describe(_bodyStatus)).AppendLine();
        HaulExecutor? executor = _executor;
        if (executor == null)
        {
            text.Append("No world is loaded.");
            return text.ToString();
        }

        CartLease? lease = executor.ActiveLease;
        text.Append("Lease: ").Append(lease == null ? "none" : lease.LeaseId + " on cart " + lease.Cart).AppendLine();
        text.Append("Phase: ").Append(executor.Phase).Append(" (revision ").Append(executor.Revision).Append(')');
        if (executor.Attention != HaulAttentionReason.Unspecified)
        {
            text.Append(", attention ").Append(executor.Attention).Append(": ").Append(executor.AttentionDetail);
        }

        text.AppendLine();
        text.Append("Haul: ").Append(executor.HaulId.Length == 0 ? "none" : executor.HaulId);
        if (executor.Leg != null)
        {
            text.Append(" to ").Append(executor.Leg.Target).Append(" within ").Append(Format(executor.Leg.ArrivalRadiusMetres)).Append(" m");
        }

        text.Append(". Attached: ").Append(executor.Attached ? "yes" : "no");
        if (executor.LastHitchRefusal != HitchRefusal.Unspecified)
        {
            text.Append(". Last hitch refusal: ").Append(executor.LastHitchRefusal).Append(" (").Append(executor.LastHitchDetail).Append(") - ")
                .Append(HaulRefusalSentences.Describe(executor.LastHitchRefusal));
        }

        text.Append(". Hitch failures ").Append(executor.HitchFailures).Append(", recoveries ").Append(executor.RecoveryFailures).AppendLine();
        text.Append(DescribeEvidence(executor));
        return text.ToString();
    }

    private string Seam()
    {
        var text = new StringBuilder();
        text.Append("Probe: ").Append(_seam.Probe.VerifiedMembers.Count).Append(" verified");
        if (!_seam.IsAvailable)
        {
            text.Append("; ").Append(_seam.UnavailableDetail);
        }

        text.AppendLine();
        text.Append("Time.fixedDeltaTime ").Append(Format(Time.fixedDeltaTime)).AppendLine();
        Player player = Player.m_localPlayer;
        if (player != null)
        {
            Rigidbody playerBody = player.GetComponent<Rigidbody>();
            text.Append("Player body: mass ").Append(Format(playerBody.mass)).Append(" kg, base ").Append(Format(player.m_originalMass))
                .Append(" kg, constraints ").Append(playerBody.constraints).Append(", kinematic ").Append(playerBody.isKinematic)
                .Append(", layer ").Append(LayerMask.LayerToName(player.gameObject.layer)).AppendLine();
        }

        Rigidbody? gunnar = _body.Rigidbody;
        if (gunnar != null)
        {
            Character character = gunnar.GetComponent<Character>();
            text.Append("Gunnar body: mass ").Append(Format(gunnar.mass)).Append(" kg, base ").Append(Format(character.m_originalMass))
                .Append(" kg, calibrated ").Append(Format(_body.CalibratedMassKg)).Append(" kg, constraints ").Append(gunnar.constraints)
                .Append(" (player's rotation ").Append(_body.PlayerRotationConstraints).Append("), kinematic ").Append(gunnar.isKinematic)
                .Append(", gravity ").Append(gunnar.useGravity).Append(", collisions ").Append(gunnar.detectCollisions)
                .Append(", scale ").Append(gunnar.transform.lossyScale).AppendLine();
        }

        Vagon? cart = HoveredCart() ?? LeasedCart();
        if (cart != null)
        {
            Transform attach = cart.m_attachPoint;
            float massSum = 0f;
            Rigidbody[] bodies = cart.m_bodies ?? new Rigidbody[0];
            foreach (Rigidbody body in bodies)
            {
                massSum += body != null ? body.mass : 0f;
            }

            text.Append("Cart ").Append(cart.m_nview.GetZDO().m_uid).Append(": detachDistance ").Append(Format(cart.m_detachDistance))
                .Append(", breakForce ").Append(Format(cart.m_breakForce)).Append(", spring ").Append(Format(cart.m_spring))
                .Append(", damper ").Append(Format(cart.m_springDamping)).Append(", baseMass ").Append(Format(cart.m_baseMass))
                .Append(", itemWeightMassFactor ").Append(Format(cart.m_itemWeightMassFactor)).Append(", playerExtraPullMass ")
                .Append(Format(cart.m_playerExtraPullMass)).Append(", attachOffset ").Append(cart.m_attachOffset)
                .Append(", attachPoint parent is root ").Append(attach != null && attach.parent == cart.transform)
                .Append(", bodies ").Append(bodies.Length).Append(" weighing ").Append(Format(massSum)).Append(" kg").AppendLine();
        }
        else
        {
            text.Append("Point at a cart (or assign one) to read its live constants.");
        }

        return text.ToString();
    }

    private string Spawn()
    {
        WorkAuthorityVerdict authority = _authority.Evaluate();
        if (authority != WorkAuthorityVerdict.Granted)
        {
            return "Refused: " + WorkAuthorityPolicy.Describe(authority);
        }

        if (!WorkerBodyCensus.MaySpawn(_bodyStatus))
        {
            return "Refused: " + WorkerBodyCensus.Describe(_bodyStatus);
        }

        Player player = Player.m_localPlayer;
        if (player == null)
        {
            return "Refused: no local player to bring Gunnar to.";
        }

        Vector3 position = player.transform.position + (player.transform.forward * 3f);
        if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(position, out float ground))
        {
            position.y = ground;
        }

        TeamsterWorkerAI? body = TeamsterWorkerPrefab.Spawn(position, Quaternion.LookRotation(-player.transform.forward), out string failure);
        if (body == null)
        {
            return "Refused: " + failure + ".";
        }

        _persistedBodies.Add(body.GetComponent<ZNetView>().GetZDO().m_uid);
        return "Gunnar arrived at " + Format(position) + ". He stays in this world until you retire him.";
    }

    private string Retire()
    {
        if (_executor == null)
        {
            return "No world is loaded.";
        }

        if (_bodyStatus == WorkerBodyStatus.Duplicated)
        {
            Player player = Player.m_localPlayer;
            GameObject? hover = player != null ? player.GetHoverObject() : null;
            TeamsterWorkerAI? pointed = hover != null ? hover.GetComponentInParent<TeamsterWorkerAI>() : null;
            if (pointed == null || pointed == _body.Bound)
            {
                return "There is more than one Gunnar: point at the extra one and run retire again.";
            }

            ZNetView view = pointed.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner())
            {
                return "Refused: this game does not own that body.";
            }

            _persistedBodies.Remove(view.GetZDO().m_uid);
            view.Destroy();
            return "The extra Gunnar was retired.";
        }

        BodyRetirementOutcome outcome = _executor.RetireBody();
        return outcome == BodyRetirementOutcome.Retired
            ? "Gunnar was retired; his body left the world."
            : "Refused: Gunnar is busy with a haul. Stop or detach it and resolve any attention first.";
    }

    private string Assign()
    {
        WorkAuthorityVerdict authority = _authority.Evaluate();
        if (authority != WorkAuthorityVerdict.Granted)
        {
            return HaulRefusalSentences.Describe(CartAssignmentRefusal.NoAuthority);
        }

        Vagon? cart = HoveredCart();
        ZNetView? view = cart != null ? cart.m_nview : null;
        if (cart == null || view == null || !view.IsValid() || _executor == null)
        {
            return HaulRefusalSentences.Describe(CartAssignmentRefusal.AmbiguousSelection);
        }

        var key = new CartKey(view.GetZDO().m_uid.ToString(), _epoch);
        _seam.Remember(key, cart);
        _selections.Propose(key, cart.m_name, Time.time);
        CartObservation observation = _seam.Observe(key);
        return string.Format(
            CultureInfo.InvariantCulture,
            "Selected cart {0} ({1:0.#} kg of cargo in {2} stacks). Type 'ct_haul confirm' within {3:0} s to assign it to Gunnar.",
            key, observation.CargoWeightKg, observation.CargoStacks, _execution.SelectionConfirmSeconds);
    }

    private string Confirm()
    {
        if (_executor == null)
        {
            return "No world is loaded.";
        }

        if (!_selections.TryConfirm(_epoch, Time.time, _execution, out PendingCartSelection selection, out CartAssignmentRefusal refusal))
        {
            return "Not assigned: " + HaulRefusalSentences.Describe(refusal);
        }

        CartAssignmentFacts facts = _seam.ReadAssignmentFacts(selection.Cart, _epoch);
        string leaseId = "gunnar-lease-" + (++_leaseCounter).ToString(CultureInfo.InvariantCulture);
        AssignmentVerdict verdict = _executor.AssignCart(leaseId, selection.Cart, facts);
        switch (verdict.Outcome)
        {
            case AssignmentOutcome.Assigned:
                return "Cart " + selection.Cart + " is assigned to Gunnar (lease " + leaseId + "). Use 'ct_haul go <x> <z>' to haul it.";
            case AssignmentOutcome.AlreadyAssigned:
                return "That cart is already Gunnar's.";
            default:
                return "Not assigned: " + HaulRefusalSentences.Describe(verdict.Refusal) + " (" + verdict.Detail + ")";
        }
    }

    private string Release()
    {
        if (_executor == null)
        {
            return "No world is loaded.";
        }

        switch (_executor.ReleaseLease())
        {
            case LeaseReleaseOutcome.Released:
                return "Gunnar let the cart go; it is no longer assigned.";
            case LeaseReleaseOutcome.Pending:
                return "Gunnar is stopping to park and unhitch; the cart is released once he has let go.";
            case LeaseReleaseOutcome.RefusedUnsafeParking:
                return "Not released: the ground there is not safe to leave the cart on (" + _executor.AttentionDetail + "). Take the cart yourself or move it first.";
            default:
                return "Gunnar has no cart assigned.";
        }
    }

    private string Go(string[]? args)
    {
        HaulExecutor? executor = _executor;
        if (executor == null)
        {
            return "No world is loaded.";
        }

        if (args == null || args.Length < 3 ||
            !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
        {
            return "Usage: ct_haul go <x> <z> [arrival radius]. The placeholder planner accepts only a clear straight run ahead of the cart on flat open ground.";
        }

        float radius = _execution.DefaultArrivalRadiusMetres;
        if (args.Length >= 4 && float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) && parsed > 0f)
        {
            radius = parsed;
        }

        CartLease? lease = executor.ActiveLease;
        if (lease == null)
        {
            return "Assign a cart first: point at it, 'ct_haul assign', then 'ct_haul confirm'.";
        }

        float y = 0f;
        if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(new Vector3(x, 0f, z), out float ground))
        {
            y = ground;
        }

        string haulId = executor.HaulId.Length > 0
            ? executor.HaulId
            : "standalone-" + (++_haulCounter).ToString(CultureInfo.InvariantCulture);
        var request = new HaulLegRequest(haulId, string.Empty, lease.LeaseId, true, new WorkPoint(x, y, z), radius);
        HaulCommandResult result = Service.RequestLeg(request, Service.Snapshot.Revision);
        return DescribeResult("Leg " + haulId, result);
    }

    private string Stop(bool detach)
    {
        HaulExecutor? executor = _executor;
        if (executor == null || executor.HaulId.Length == 0)
        {
            return "Gunnar is not hauling.";
        }

        HaulCommandResult result = Service.Cancel(executor.HaulId, detach);
        return DescribeResult(detach ? "Detach and park" : "Stop and wait", result);
    }

    private string Evidence(string[]? args)
    {
        _evidence = args != null && args.Length > 1 && string.Equals(args[1], "on", StringComparison.OrdinalIgnoreCase);
        _nextEvidenceAt = 0f;
        return "Evidence log " + (_evidence ? "ON: one line per second in the BepInEx log while a cart is assigned." : "off.");
    }

    private string DescribeResult(string what, HaulCommandResult result)
    {
        string text = what + ": " + result.Outcome;
        if (result.Reason != HaulAttentionReason.Unspecified)
        {
            text += " (" + result.Reason + ")";
        }

        return text + ", detail " + Service.LastCommandDetail + ", revision " + result.Revision + ".";
    }

    private void LogEvidenceIfDue()
    {
        if (!_evidence || _executor == null || _executor.ActiveLease == null || Time.time < _nextEvidenceAt)
        {
            return;
        }

        _nextEvidenceAt = Time.time + 1f;
        _log.LogInfo("Gunnar evidence: phase " + _executor.Phase + " rev " + _executor.Revision + "; " + DescribeEvidence(_executor).Replace(Environment.NewLine, "; "));
    }

    private string DescribeEvidence(HaulExecutor executor)
    {
        CartLease? lease = executor.ActiveLease;
        PullerBodyFacts body = _body.Read();
        var text = new StringBuilder();
        text.Append("Gunnar ").Append(body.Present ? Format(body.Position) : "absent").Append(" speed ").Append(Format(body.SpeedMetresPerSecond))
            .Append(" m/s, mass ").Append(Format(body.BodyMassKg)).Append(" kg (base ").Append(Format(body.BaseMassKg)).Append(", calibrated ")
            .Append(Format(body.CalibratedMassKg)).Append(")");
        if (lease == null)
        {
            return text.ToString();
        }

        CartObservation cart = _seam.Observe(lease.Cart);
        if (!cart.Resolved)
        {
            return text.Append(Environment.NewLine).Append("Cart ").Append(lease.Cart).Append(cart.RecordExists ? " is not loaded" : " no longer exists").ToString();
        }

        Vagon? vagon = _seam.Resolve(lease.Cart);
        string owner = vagon != null && vagon.m_nview != null && vagon.m_nview.IsValid()
            ? vagon.m_nview.GetZDO().GetOwner().ToString(CultureInfo.InvariantCulture) + (vagon.m_nview.GetZDO().GetOwner() == ZDOMan.GetSessionID() ? " (this session)" : " (NOT this session)")
            : "unknown";
        text.Append(Environment.NewLine).Append("Cart ").Append(Format(cart.CartPosition)).Append(" speed ").Append(Format(cart.SpeedMetresPerSecond))
            .Append(" m/s, upright ").Append(Format(cart.UpDot)).Append(", owner ").Append(owner)
            .Append(", joint ").Append(cart.HasJoint ? (cart.JointConnectedToPuller ? "connected to Gunnar" : cart.JointConnectedToLocalPlayer ? "connected to the player" : "connected elsewhere") : "none")
            .Append(", attach flag ").Append(cart.AttachFlag).Append(", joint force ").Append(Format(cart.JointForceNewtons)).Append(" N of ")
            .Append(Format(cart.BreakForceNewtons)).Append(", hitch distance ").Append(Format(cart.HitchDistanceMetres)).Append(" m of ")
            .Append(Format(cart.DetachDistanceMetres)).Append(", body mass ").Append(Format(cart.BodyMassSumKg)).Append(" kg (load says ")
            .Append(Format(cart.ExpectedMassKg)).Append("), cargo ").Append(cart.CargoStacks).Append(" stacks ").Append(Format(cart.CargoWeightKg))
            .Append(" kg, brake ").Append(cart.BrakeEngaged || cart.RootFrozen);
        return text.ToString();
    }

    private Vagon? HoveredCart()
    {
        Player player = Player.m_localPlayer;
        GameObject? hover = player != null ? player.GetHoverObject() : null;
        return hover != null ? hover.GetComponentInParent<Vagon>() : null;
    }

    private Vagon? LeasedCart()
    {
        CartLease? lease = _executor?.ActiveLease;
        return lease != null ? _seam.Resolve(lease.Cart) : null;
    }

    private static string Format(float value) =>
        float.IsNaN(value) ? "n/a" : value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Format(Vector3 value) => new WorkPoint(value.x, value.y, value.z).ToString();

    private static string Format(WorkPoint value) => value.ToString();
}
