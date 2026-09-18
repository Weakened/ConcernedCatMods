using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedForeman.Domain.Settlement;
using TheConcernedCat.ConcernedForeman.Runtime.Custody;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Register;
using TheConcernedCat.Settlement.Tools;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Settlement;

/// <summary>Owns the settlement runtime's live state, and is the only thing
/// that decides whether a worker may act.
///
/// <b>Authority, in one place.</b> Work — a worker body, a pickup, a transfer —
/// runs only under the slice's rule (DECISIONS.md D3): opted in, a world, the
/// host, not dedicated, and nobody else connected. Any clause failing is a
/// refusal with a reason. Marking ground keeps its original, looser rule
/// (opted in, host): it moves nothing.
///
/// The runtime holds <b>one</b> worker identity, Thorstein, whose body is
/// persistent (D9): spawned once, stamped with its key, re-bound by that key
/// whenever its ground loads, and never despawned while it carries anything.
/// </summary>
internal sealed class SettlementRuntime
{
    private readonly ForemanSettlementSettings _settings;
    private readonly Action<string> _log;
    private readonly WorkerSitePolicy _sitePolicy = new WorldSitePolicy();
    private readonly SettlementRecords _records;

    /// <summary>#286: measures the settlement's housing from the real beds.
    /// Read-only.</summary>
    private readonly WorldHousing _housing;
    private readonly DesignationTools _designations;
    private readonly ForemanCustodyRuntime _custody;

    private ForemanWorkerAI? _worker;

    internal SettlementRuntime(ForemanSettlementSettings settings, Action<string> log)
    {
        _settings = settings;
        _log = log;
        _records = new SettlementRecords(log);
        _custody = new ForemanCustodyRuntime(_records, WorkAuthority, log);
        // #286: housing is measured on demand from the real beds, so the answer
        // is never older than the question. The area comes from the caller that
        // already opened the register — asking for it a second time here gave a
        // transient world-identity failure a way to be reported as "your
        // settlement houses nobody".
        _housing = new WorldHousing(_sitePolicy, log);

        _designations = new DesignationTools(
            HasAuthority, DescribeMissingAuthority, _records, new CustodyTools(this, _custody, _records),
            area => _housing.Measure(area));

        WorkerBody.Loaded = OnWorkerBodyLoaded;
        WorkerBody.Died = (body, dropped, where) => _custody.OnWorkerDied(body, dropped, where);
        WorkerBody.ErrorLog = log;
    }

    /// <summary>The custody runtime collection and cooperation consume
    /// (<c>ICustodyRuntime</c>).</summary>
    internal ForemanCustodyRuntime Custody => _custody;

    /// <summary>CF-SET-004's command surface.</summary>
    internal string ExecuteSettlement(string[]? args) => _designations.Execute(args);

    /// <summary>Plugin start: the worker prefab is built as soon as vanilla
    /// prefabs exist, and the world-save hook is subscribed.</summary>
    internal void Install()
    {
        ForemanWorkerPrefab.Install(_settings.WorkerBaseCreature.Value, _log);
        ForemanCustodyRuntime.InstallSaveHook();
    }

    /// <summary>The designation authority answer: opted in and the host. Every
    /// clause is a refusal, never an assumption.</summary>
    internal bool HasAuthority()
    {
        if (!_settings.SettlementRuntimeEnabled.Value)
        {
            return false;
        }

        ZNet net = ZNet.instance;
        return net != null && net.IsServer();
    }

    /// <summary>The work authority answer (D3), asked before every mutation.
    /// </summary>
    internal WorkAuthorityVerdict WorkAuthority()
    {
        ZNet net = ZNet.instance;
        int peers;
        try
        {
            peers = net == null ? -1 : net.GetPeers().Count;
        }
        catch (Exception)
        {
            peers = -1;
        }

        return WorkAuthorityPolicy.Evaluate(new WorkAuthorityFacts(
            _settings.SettlementRuntimeEnabled.Value,
            worldLoaded: net != null && ZNetScene.instance != null,
            isServer: net != null && net.IsServer(),
            isDedicated: net != null && net.IsDedicated(),
            connectedPeers: peers));
    }

    internal bool HasWorkAuthority() => WorkAuthority() == WorkAuthorityVerdict.Granted;

    internal string Execute(string[]? args)
    {
        string subcommand = (args != null && args.Length > 0)
            ? args[0].ToLowerInvariant()
            : "status";

        return subcommand switch
        {
            "status" => Status(),
            "spawn" => Spawn(),
            "goto" => GoTo(args),
            "stop" => Stop(),
            "despawn" => Despawn(),
            _ => "Unknown subcommand. Try: status, spawn, goto <x> <z>, stop, despawn.",
        };
    }

    /// <summary>The bound worker body, re-found by key when its ground has
    /// been unloaded and loaded again.</summary>
    internal ForemanWorkerAI? Worker
    {
        get
        {
            if (_worker == null)
            {
                WorkerBody? body = WorkerBody.FindLive(_custody.WorkerKey.Value);
                if (body != null)
                {
                    Bind(body.GetComponent<ForemanWorkerAI>());
                }
            }

            return _worker;
        }
    }

    private string Status()
    {
        string authority = HasWorkAuthority()
            ? "granted (opted in, host, nobody else connected)"
            : DescribeMissingAuthority();

        ForemanWorkerAI? worker = Worker;
        WorkerBodyCensus? census = _custody.Census;
        string body = census == null
            ? string.Empty
            : census.IsDuplicated
                ? " TWO BODIES carry this worker's identity; neither is used until one is removed by hand."
                : census.IsMissing && worker == null ? " No worker body in this world." : string.Empty;

        if (worker == null)
        {
            return
                $"Settlement runtime: {(_settings.SettlementRuntimeEnabled.Value ? "on" : "off")}. " +
                $"Authority: {authority}. No worker in loaded ground." + body + " " +
                $"Prefab: {(ForemanWorkerPrefab.IsReady ? "ready" : ForemanWorkerPrefab.LastFailure ?? "not built")}.";
        }

        string goalText = worker.IsDeferred
            ? $"deferred ({Describe(worker.DeferredReason)})"
            : "active";
        WorkerBody? live = worker.GetComponent<WorkerBody>();

        return
            $"Settlement runtime: {(_settings.SettlementRuntimeEnabled.Value ? "on" : "off")}. " +
            $"Authority: {authority}. " +
            $"Worker at {Format(worker.transform.position)}, goal {goalText}, carrying " +
            $"{(live == null ? 0 : live.ItemCount).ToString(CultureInfo.InvariantCulture)} item stack(s). " +
            $"Path requests so far: {worker.TotalPathRequests}." + body +
            (worker.IsFaulted ? " WORKER FAULTED and is inert; see the log." : string.Empty);
    }

    internal string DescribeMissingAuthority()
    {
        return "refused (" + WorkAuthorityPolicy.Describe(WorkAuthority()) + ")";
    }

    private string Spawn()
    {
        if (!HasWorkAuthority())
        {
            return "Refused: " + DescribeMissingAuthority() + ".";
        }

        string key = _custody.WorkerKey.Value;
        WorkerBodyCensus census = WorldCustodyObjects.Census(key);
        if (!census.IsMissing)
        {
            // One body per identity (ARCH-01). A body in unloaded ground is
            // still his body: spawning another would make two Thorsteins.
            return census.IsDuplicated
                ? "Refused: this world already holds two bodies for Thorstein. Nothing is created or removed automatically."
                : "Thorstein already has a body in this world" +
                    (WorkerBody.FindLive(key) == null ? ", in ground that is not loaded. Go there." : ".");
        }

        Player player = Player.m_localPlayer;
        if (player == null)
        {
            return "Refused: no local player to spawn next to.";
        }

        if (!ForemanWorkerPrefab.IsReady && !ForemanWorkerPrefab.TryCreate(_settings.WorkerBaseCreature.Value, _log))
        {
            return "Refused: " + (ForemanWorkerPrefab.LastFailure ?? "the worker prefab could not be built") + ".";
        }

        Vector3 position = player.transform.position + (player.transform.forward * 3f);
        ForemanWorkerAI? worker = ForemanWorkerPrefab.Spawn(position, Quaternion.identity);
        if (worker == null)
        {
            return "Refused: that ground is not loaded.";
        }

        if (!WorkerBody.TryStamp(worker.gameObject, key))
        {
            // An identity-less body is never adopted later; say so rather than
            // leave one standing unexplained.
            _log("A spawned worker body could not be given its identity; it will not be used.");
            return "Refused: the new body could not be given its identity, so it will not be used.";
        }

        Bind(worker);
        return $"Worker spawned at {Format(position)}. It has no order, so it should do nothing at all.";
    }

    private void OnWorkerBodyLoaded(WorkerBody body)
    {
        if (body == null || !string.Equals(body.Key, _custody.WorkerKey.Value, StringComparison.Ordinal))
        {
            return;
        }

        Bind(body.GetComponent<ForemanWorkerAI>());
    }

    /// <summary>Wires a (re)created body: a body instantiated from its saved
    /// object comes back with no authority gate and no site policy, and must
    /// refuse to act until the runtime has given it both.</summary>
    private void Bind(ForemanWorkerAI? worker)
    {
        if (worker == null)
        {
            return;
        }

        WorkerBodyCensus? census = _custody.Census;
        if (census != null && census.IsDuplicated)
        {
            return;
        }

        worker.AuthorityGate = HasWorkAuthority;
        worker.UseSitePolicy(_sitePolicy);
        worker.OnDeferred = reason => _log($"Worker deferred: {Describe(reason)}");

        // A latched fault is an error, not verbose diagnostics. It is reported
        // unconditionally.
        worker.ErrorLog = _log;
        if (_settings.DebugLogging.Value)
        {
            worker.DebugLog = _log;
        }

        _worker = worker;
    }

    private string GoTo(string[]? args)
    {
        ForemanWorkerAI? worker = Worker;
        if (worker == null)
        {
            return "No worker in loaded ground. Spawn one first.";
        }

        if (args == null || args.Length < 3
            || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            || !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
        {
            return "Usage: cf_worker goto <x> <z>.";
        }

        // Probe from a fixed world ceiling, not from the worker's own height.
        const float ProbeCeiling = 5000f;
        float y = worker.transform.position.y;
        if (ZoneSystem.instance != null
            && ZoneSystem.instance.GetSolidHeight(new Vector3(x, ProbeCeiling, z), out float ground))
        {
            y = ground;
        }

        worker.SetGoal(new Vector3(x, y, z));
        return $"Worker ordered to {Format(new Vector3(x, y, z))}.";
    }

    private string Stop()
    {
        ForemanWorkerAI? worker = Worker;
        if (worker == null)
        {
            return "No worker in loaded ground.";
        }

        worker.ClearGoal();
        return "Order cleared. The worker should stop and stay stopped.";
    }

    /// <summary>Asked before the body is retired (despawned), before anything
    /// else is touched — not even his goal is cleared on a refusal. The plugin
    /// wires it to the collection runtime's <c>Modes.MayRetireBody</c>, so a
    /// body a collection job holds is never retired under that job. Unset means
    /// nothing else can hold the body. A guard that throws refuses.</summary>
    internal Func<bool>? MayRetireBody { get; set; }

    private bool MayRetire()
    {
        Func<bool>? guard = MayRetireBody;
        if (guard == null)
        {
            return true;
        }

        try
        {
            return guard();
        }
        catch (Exception e)
        {
            _log("[Settlement] The retire guard failed, so the worker was not retired: " + e.Message);
            return false;
        }
    }

    private string Despawn()
    {
        ForemanWorkerAI? worker = Worker;
        if (worker == null)
        {
            return "No worker in loaded ground.";
        }

        if (!MayRetire())
        {
            return "Refused: Thorstein is working. Pause or cancel the order first, then despawn.";
        }

        // Never while he carries anything (D9): despawning would destroy it.
        // Asked before his goal is cleared, so a refusal really does leave
        // everything as it was (review R2, m9).
        WorkerBody? body = worker.GetComponent<WorkerBody>();
        if (body == null || !body.IsLoaded || body.ItemCount > 0 || RecordSaysHeHolds())
        {
            return "Refused: he is carrying " +
                (body == null ? 0 : body.ItemCount).ToString(CultureInfo.InvariantCulture) +
                " item stack(s), or the record says he holds tools or material. Release everything first " +
                "(cf_settle takeback and cf_settle release, or deliver what he carries), then despawn.";
        }

        worker.ClearGoal();

        ZNetView view = worker.GetComponent<ZNetView>();
        if (view == null || !view.IsValid() || !view.IsOwner())
        {
            return "Could not despawn: this peer does not own that worker. " +
                "Its order has been cleared, so it will stand still.";
        }

        view.Destroy();
        _worker = null;
        return "Worker despawned.";
    }

    private bool RecordSaysHeHolds()
    {
        if (!_records.TryOpen(out SettlementRegister _, out SettlementJournal journal))
        {
            return true;
        }

        ReplayResult replay = journal.Replay();
        if (replay.Tools.HeldBy(_custody.ToolWorker).Count > 0 || replay.Tools.HasUncertainHandover(_custody.ToolWorker))
        {
            return true;
        }

        foreach (Holding holding in replay.Custody.Holdings)
        {
            if (holding.Location.Place == CustodyPlace.Worker)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A world is up: open custody for this load before anything can
    /// act.</summary>
    internal void OnWorldLoaded()
    {
        _worker = null;
        try
        {
            _custody.OnWorldLoaded();
        }
        catch (Exception exception)
        {
            _log("Settlement custody could not be opened for this world; no custody work will start: " + exception);
        }
    }

    /// <summary>Called when a world unloads, so a second world in the same
    /// session does not inherit a dead reference or the first world's records.
    /// The worker prefab stays registered: it is needed by every world.</summary>
    internal void OnWorldUnloaded()
    {
        _worker = null;
        _custody.OnWorldUnloaded();
        _records.Forget();
        _designations.Forget();
    }

    private static string Format(Vector3 point)
    {
        return string.Format(
            CultureInfo.InvariantCulture, "({0:0.#}, {1:0.#}, {2:0.#})", point.x, point.y, point.z);
    }

    /// <summary>A deferral reason as a sentence.</summary>
    private static string Describe(WorkerDeferralReason reason)
    {
        return reason switch
        {
            WorkerDeferralReason.Unreachable =>
                "it could not find a way there, and has stopped asking",
            WorkerDeferralReason.TooFar =>
                "that point is further than a worker is allowed to plan for",
            WorkerDeferralReason.Hazardous =>
                "that ground is dangerous to stand on",
            WorkerDeferralReason.NoAuthority =>
                "it is not allowed to act here",
            WorkerDeferralReason.OutsideLoadedGround =>
                "that point is outside loaded ground, and there is no offscreen work yet",
            _ => "no reason recorded",
        };
    }
}
