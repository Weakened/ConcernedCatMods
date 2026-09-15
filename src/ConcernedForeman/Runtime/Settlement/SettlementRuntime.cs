using System;
using System.Globalization;
using TheConcernedCat.ConcernedForeman.Domain.Settlement;
using TheConcernedCat.Settlement.Worker;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Settlement;

/// <summary>Owns the settlement runtime's live state, and is the only thing
/// that decides whether a worker may act.
///
/// <b>Authority, in one place.</b> Three things must all be true before a worker
/// does anything: the player opted in, this peer is the host, and the world is
/// loaded. Any one of them failing is a refusal with a reason. They are gathered
/// here rather than inside the worker so that there is exactly one answer to
/// "may we act", and so that turning the runtime off in the config stops workers
/// on the next tick instead of at the next restart.
///
/// The spike deliberately holds <b>one</b> worker. Recruitment, multiple
/// workers and settlement designation are later leaves, and building them here
/// would make this one unreviewable.</summary>
internal sealed class SettlementRuntime
{
    private readonly ForemanSettlementSettings _settings;
    private readonly Action<string> _log;
    private readonly WorkerSitePolicy _sitePolicy = new WorldSitePolicy();
    private readonly SettlementRecords _records;
    private readonly DesignationTools _designations;

    private ForemanWorkerAI? _worker;

    internal SettlementRuntime(ForemanSettlementSettings settings, Action<string> log)
    {
        _settings = settings;
        _log = log;
        _records = new SettlementRecords(log);
        _designations = new DesignationTools(HasAuthority, DescribeMissingAuthority, _records);
    }

    /// <summary>CF-SET-004's command surface. It shares this object's single
    /// authority answer rather than carrying one of its own, so marking ground
    /// is refused in exactly the situations a worker refuses to act.</summary>
    internal string ExecuteSettlement(string[]? args) => _designations.Execute(args);

    /// <summary>The single authority answer. Every clause is a refusal, never an
    /// assumption: a missing <c>ZNet</c> is "no authority", not "probably solo".</summary>
    internal bool HasAuthority()
    {
        if (!_settings.SettlementRuntimeEnabled.Value)
        {
            return false;
        }

        ZNet net = ZNet.instance;
        if (net == null)
        {
            return false;
        }

        // Solo and local host are the initial targets. On a dedicated server
        // this peer is a client and owns nothing, so it refuses — the ADR's
        // "missing authority fails closed", not "take ownership anyway".
        return net.IsServer();
    }

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

    private string Status()
    {
        string authority = HasAuthority()
            ? "granted (opted in, host)"
            : DescribeMissingAuthority();

        if (_worker == null)
        {
            return
                $"Settlement runtime: {(_settings.SettlementRuntimeEnabled.Value ? "on" : "off")}. " +
                $"Authority: {authority}. No worker. " +
                $"Prefab: {(ForemanWorkerPrefab.IsReady ? "ready" : ForemanWorkerPrefab.LastFailure ?? "not built")}.";
        }

        string goalText = _worker.IsDeferred
            ? $"deferred ({Describe(_worker.DeferredReason)})"
            : "active";

        return
            $"Settlement runtime: {(_settings.SettlementRuntimeEnabled.Value ? "on" : "off")}. " +
            $"Authority: {authority}. " +
            $"Worker at {Format(_worker.transform.position)}, goal {goalText}. " +
            $"Path requests so far: {_worker.TotalPathRequests}." +
            (_worker.IsFaulted ? " WORKER FAULTED and is inert; see the log." : string.Empty);
    }

    internal string DescribeMissingAuthority()
    {
        if (!_settings.SettlementRuntimeEnabled.Value)
        {
            return "refused (the settlement runtime is off; enable it in the config)";
        }

        if (ZNet.instance == null)
        {
            return "refused (no world loaded)";
        }

        return "refused (this peer is not the host)";
    }

    private string Spawn()
    {
        if (!HasAuthority())
        {
            return "Refused: " + DescribeMissingAuthority() + ".";
        }

        if (_worker != null)
        {
            return "A worker already exists. This spike holds one at a time; despawn it first.";
        }

        Player player = Player.m_localPlayer;
        if (player == null)
        {
            return "Refused: no local player to spawn next to.";
        }

        string baseCreature = _settings.WorkerBaseCreature.Value;
        if (!ForemanWorkerPrefab.TryCreate(baseCreature, _log))
        {
            return "Refused: " + (ForemanWorkerPrefab.LastFailure ?? "the worker prefab could not be built") + ".";
        }

        Vector3 position = player.transform.position + (player.transform.forward * 3f);
        ForemanWorkerAI? worker = ForemanWorkerPrefab.Spawn(position, Quaternion.identity);
        if (worker == null)
        {
            return "Refused: that ground is not loaded.";
        }

        worker.AuthorityGate = HasAuthority;
        worker.UseSitePolicy(_sitePolicy);
        worker.OnDeferred = reason => _log($"Worker deferred: {Describe(reason)}");

        // A latched fault is an error, not verbose diagnostics. It is reported
        // unconditionally: with DebugLogging off (the default) a faulted worker
        // would otherwise go permanently inert with no trace anywhere except a
        // status command nobody has a reason to run.
        worker.ErrorLog = _log;
        if (_settings.DebugLogging.Value)
        {
            worker.DebugLog = _log;
        }

        _worker = worker;
        return $"Worker spawned at {Format(position)}. It has no order, so it should do nothing at all.";
    }

    private string GoTo(string[]? args)
    {
        if (_worker == null)
        {
            return "No worker. Spawn one first.";
        }

        if (args == null || args.Length < 3
            || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            || !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
        {
            return "Usage: cf_worker goto <x> <z>.";
        }

        // Probe from a fixed world ceiling, not from the worker's own height.
        // Sampling at workerY + 50 puts the origin *below* the terrain whenever
        // the target is more than fifty metres uphill, so the height resolves to
        // the wrong surface — and that wrong altitude is then what the hazard
        // check measures water depth against.
        const float ProbeCeiling = 5000f;
        float y = _worker.transform.position.y;
        if (ZoneSystem.instance != null
            && ZoneSystem.instance.GetSolidHeight(new Vector3(x, ProbeCeiling, z), out float ground))
        {
            y = ground;
        }

        _worker.SetGoal(new Vector3(x, y, z));
        return $"Worker ordered to {Format(new Vector3(x, y, z))}.";
    }

    private string Stop()
    {
        if (_worker == null)
        {
            return "No worker.";
        }

        _worker.ClearGoal();
        return "Order cleared. The worker should stop and stay stopped.";
    }

    private string Despawn()
    {
        if (_worker == null)
        {
            return "No worker.";
        }

        // Stop it before anything else. If the destroy below cannot happen we
        // are about to drop our only handle on this creature, and a worker left
        // walking with no handle is strictly worse than one standing still.
        _worker.ClearGoal();

        ZNetView view = _worker.GetComponent<ZNetView>();
        if (view == null || !view.IsValid() || !view.IsOwner())
        {
            // Keep the reference. Reporting success here and nulling it would
            // orphan a live creature AND let the next spawn create a second
            // one, breaking the one-worker-at-a-time invariant this spike
            // relies on.
            return "Could not despawn: this peer does not own that worker. " +
                "Its order has been cleared, so it will stand still.";
        }

        view.Destroy();
        _worker = null;
        return "Worker despawned.";
    }

    /// <summary>Called when a world unloads, so a second world in the same
    /// session does not inherit a dead reference or a prefab built against a
    /// scene that no longer exists.</summary>
    internal void OnWorldUnloaded()
    {
        _worker = null;
        ForemanWorkerPrefab.Reset();

        // The records are dropped too, so a second world in the same session
        // reads its own files instead of inheriting the first world's
        // settlement.
        _records.Forget();
    }

    private static string Format(Vector3 point)
    {
        return string.Format(
            CultureInfo.InvariantCulture, "({0:0.#}, {1:0.#}, {2:0.#})", point.x, point.y, point.z);
    }

    /// <summary>A deferral reason as a sentence. #273's gate 5 requires the
    /// worker to explain itself, and an enum name is not an explanation.</summary>
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
