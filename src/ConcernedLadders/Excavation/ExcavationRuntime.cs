using System;
using System.Globalization;
using TheConcernedCat.ConcernedLadders.Domain.Excavation;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Excavation;

/// <summary>Plans and commits one measured excavation (#487 slice 2), behind the off-by-default
/// <c>[Excavation] Enabled</c> switch. This is the only place in the product that changes terrain, and it does so in
/// exactly one way: one <c>Attack.SpawnOnHitTerrain</c> call with this mod's own registered terrain operation, the
/// path the pickaxe uses, so persistence, reload and synchronisation are the game's own (EXCAVATION.md section 3).
/// It writes no height, delta, ZDO or RPC of its own, removes nothing, and charges nothing (there is no shovel yet).
///
/// The order of a commit: switch, start-up probe, registration, the game's terrain-op kill switch, one-in-flight,
/// a fresh terrain read, the planner, then - only for a Ready plan - the ward at the wider radius, no-build locations
/// and the object scan, then the single spawn. After it, the floor is read back every frame through
/// <see cref="ExcavationPlanner.Check"/> until it is Complete, Overshot, Unreadable or timed out. Nothing is ever
/// retried; the next dig is a fresh plan the player asks for.
///
/// <b>Not observed in game.</b> Every in-game row of #487 is OWNER GO-AROUND PENDING.</summary>
internal sealed class ExcavationRuntime : IDisposable
{
    /// <summary>How far from the player the console harness may aim. A harness bound only; the shovel's reach and
    /// targeting rule belong to the preview slice.</summary>
    internal const float HarnessReach = 10f;

    private readonly Action<string> _log;
    private readonly ExcavationTerrainOps _ops;
    private readonly ExcavationTerrainReader _reader = new ExcavationTerrainReader();
    private readonly ExcavationWorldChecks _world = new ExcavationWorldChecks();
    private readonly ExcavationSamples _smallSamples = new ExcavationSamples(ExcavationMode.Small);
    private readonly ExcavationSamples _largeSamples = new ExcavationSamples(ExcavationMode.Large);
    private readonly ExcavationSamples _smallCheck = new ExcavationSamples(ExcavationMode.Small);
    private readonly ExcavationSamples _largeCheck = new ExcavationSamples(ExcavationMode.Large);
    private readonly ExcavationInFlight _inFlight = new ExcavationInFlight();
    private bool _faulted;

    internal ExcavationRuntime(Action<string> log, bool enabled)
    {
        _log = log;
        Enabled = enabled;
        _ops = new ExcavationTerrainOps(log);
    }

    /// <summary>The opt-in switch, read once at start-up.</summary>
    internal bool Enabled { get; }

    /// <summary>Both start-up probes found every game member they read.</summary>
    internal bool Available => _reader.Available && _world.Available && !_faulted;

    /// <summary>Called for every printed result, so the console harness can show it; the log always gets it too.</summary>
    internal Action<string>? Report { get; set; }

    /// <summary>Registers the terrain ops on every peer (see <see cref="ExcavationTerrainOps"/>) and logs the probe.</summary>
    internal void Install()
    {
        _ops.Install();
        string probe = _reader.Missing ?? _world.Missing ?? "ok";
        _log("Excavation (#487, experimental) is " + (Enabled ? "ENABLED" : "off (the default)") + "; game probe " + probe + ".");
    }

    internal string Status()
    {
        string state = "Excavation is " + (Enabled ? "enabled" : "off") + "; probe " + (_reader.Missing ?? _world.Missing ?? (_faulted ? "faulted" : "ok")) +
                       "; terrain ops " + (_ops.IsRegistered(ExcavationMode.Small) && _ops.IsRegistered(ExcavationMode.Large) ? "registered" : "NOT registered") + ".";
        if (_inFlight.IsActive) state += " Waiting on " + ExcavationReport.Outcome(ExcavationOutcome.Waiting, _inFlight.Plan);
        return state;
    }

    /// <summary>Read-only: plans at the aimed point and describes it. Never commits, flashes a ward or scans objects.</summary>
    internal string PlanAtAim(ExcavationMode mode)
    {
        if (!Enabled) return ExcavationReport.Commit(ExcavationCommitRefusal.Disabled, ExcavationReadFault.None, default, null);
        if (!Available) return ExcavationReport.Commit(ExcavationCommitRefusal.Unavailable, ExcavationReadFault.None, default, null);
        if (!Aim(out int centreX, out int centreZ, out string? why)) return why!;
        ExcavationSamples samples = Samples(mode);
        ExcavationReadFault fault = _reader.Read(samples, centreX, centreZ);
        if (fault != ExcavationReadFault.None) return ExcavationReport.ReadFault(fault);
        ExcavationPlan plan = ExcavationPlanner.Plan(samples, centreX, centreZ, WaterLevel());
        return ExcavationReport.Plan(plan);
    }

    /// <summary>The commit gate. Returns what happened; the result of a submitted dig arrives later through
    /// <see cref="Update"/>.</summary>
    internal string DigAtAim(ExcavationMode mode)
    {
        try
        {
            if (!Enabled) return ExcavationReport.Commit(ExcavationCommitRefusal.Disabled, ExcavationReadFault.None, default, null);
            if (!Aim(out int centreX, out int centreZ, out string? why)) return why!;
            ExcavationSamples samples = Samples(mode);
            ExcavationReadFault fault = ExcavationReadFault.None;
            var plan = default(ExcavationPlan);
            bool registered = _ops.IsRegistered(mode);
            bool readable = Available && registered && !TerrainOp.m_forceDisableTerrainOps && !_inFlight.IsActive;
            if (readable)
            {
                fault = _reader.Read(samples, centreX, centreZ);
                if (fault == ExcavationReadFault.None) plan = ExcavationPlanner.Plan(samples, centreX, centreZ, WaterLevel());
            }
            ExcavationCommitRefusal refusal = ExcavationGate.BeforeWorldChecks(Enabled, Available, registered,
                TerrainOp.m_forceDisableTerrainOps, _inFlight.IsActive, fault, plan);
            if (refusal != ExcavationCommitRefusal.None) return ExcavationReport.Commit(refusal, fault, plan, null);

            bool ward = ExcavationWorldChecks.WardAllows(plan);
            bool noBuild = ward && _world.InNoBuildArea(plan);
            int pieces = 0;
            bool overflow = false;
            string? obstruction = null;
            int obstructions = ward && !noBuild ? _world.Obstructions(plan, HighestGround(samples), out pieces, out overflow, out obstruction) : 0;
            refusal = ExcavationGate.WorldChecks(ward, noBuild, obstructions, overflow);
            if (refusal != ExcavationCommitRefusal.None) return ExcavationReport.Commit(refusal, fault, plan, obstruction);

            return Commit(plan, pieces);
        }
        catch (Exception exception)
        {
            return Fault(exception);
        }
    }

    /// <summary>The single terrain change. Takes the in-flight slot before spawning, so no second commit can start
    /// while this one is unresolved, and frees it only through a read-back.</summary>
    private string Commit(in ExcavationPlan plan, int pieces)
    {
        TerrainOp? op = _ops.For(plan.Mode);
        if (op == null || !_inFlight.TryStart(plan, Time.time))
        {
            return ExcavationReport.Commit(op == null ? ExcavationCommitRefusal.NotRegistered : ExcavationCommitRefusal.Busy,
                ExcavationReadFault.None, plan, null);
        }
        var point = new Vector3(plan.CentreX * ExcavationGeometry.VertexSpacing, plan.OperationY, plan.CentreZ * ExcavationGeometry.VertexSpacing);
        // SpawnOnHitTerrain returns the instantiated op, whose Awake has already applied it and asked Unity to destroy
        // it (deferred to the end of the frame); it returns a plain null only when its own ward or no-build check
        // refused, before instantiating anything. So only a reference null is a refusal.
        GameObject? placed = Attack.SpawnOnHitTerrain(point, op.gameObject, null, 0f, null, null);
        if (ReferenceEquals(placed, null))
        {
            // The game's own terrain-tool path refused (its ward or no-build re-check). Nothing was instantiated, so
            // there is nothing to wait for.
            _inFlight.Abandon();
            return ExcavationReport.Commit(ExcavationCommitRefusal.SpawnRefused, ExcavationReadFault.None, plan, null);
        }
        string text = ExcavationReport.Commit(ExcavationCommitRefusal.None, ExcavationReadFault.None, plan, null) + " " + ExcavationReport.Plan(plan);
        if (pieces > 0) text += " " + pieces.ToString(CultureInfo.InvariantCulture) + " building-piece collider(s) stand on the changed ground; their support is the game's own.";
        _log("Excavation committed: " + text);
        return text;
    }

    /// <summary>Reads back an operation in flight. Called every frame; does nothing when none is.</summary>
    internal void Update()
    {
        if (!_inFlight.IsActive) return;
        try
        {
            ExcavationPlan plan = _inFlight.Plan;
            float now = Time.time;
            if (now <= _inFlight.StartedAt) return; // The operation's rebuild runs in this frame's LateUpdate.
            if (Player.m_localPlayer == null || ZNetScene.instance == null)
            {
                if (_inFlight.Abandon()) Publish(ExcavationReport.Outcome(ExcavationOutcome.Abandoned, plan));
                return;
            }
            ExcavationSamples after = Check(plan.Mode);
            ExcavationReadFault fault = _reader.Read(after, plan.CentreX, plan.CentreZ);
            ExcavationProgress progress = fault == ExcavationReadFault.None ? ExcavationPlanner.Check(plan, after) : ExcavationProgress.Unreadable;
            ExcavationOutcome outcome = _inFlight.Observe(fault, progress, now);
            if (outcome != ExcavationOutcome.Waiting) Publish(ExcavationReport.Outcome(outcome, plan));
        }
        catch (Exception exception)
        {
            _inFlight.Abandon();
            Publish(Fault(exception));
        }
    }

    private void Publish(string text)
    {
        _log("Excavation: " + text);
        try { Report?.Invoke(text); } catch { /* The console is optional. */ }
    }

    private string Fault(Exception exception)
    {
        _faulted = true;
        string text = "Excavation stopped after an unexpected fault (" + SafeFailure.Brief(exception) + "); it stays off until restart.";
        _log(text);
        return text;
    }

    /// <summary>The console harness's target: the terrain the camera looks at, within <see cref="HarnessReach"/> of the
    /// player, snapped to the vertex the engine op will centre on.</summary>
    private static bool Aim(out int centreX, out int centreZ, out string? why)
    {
        centreX = 0;
        centreZ = 0;
        why = null;
        Player player = Player.m_localPlayer;
        GameCamera camera = GameCamera.instance;
        if (player == null || camera == null) { why = "No local player in a world."; return false; }
        Transform eye = camera.transform;
        if (!Physics.Raycast(eye.position, eye.forward, out RaycastHit hit, 50f, LayerMask.GetMask("terrain"), QueryTriggerInteraction.Ignore))
        {
            why = "Look at the ground to aim.";
            return false;
        }
        if (Vector3.Distance(player.transform.position, hit.point) > HarnessReach)
        {
            why = "Aim within " + HarnessReach.ToString("0", CultureInfo.InvariantCulture) + " m of yourself.";
            return false;
        }
        centreX = ExcavationGeometry.SnapToVertex(hit.point.x);
        centreZ = ExcavationGeometry.SnapToVertex(hit.point.z);
        return true;
    }

    private static float? WaterLevel()
    {
        ZoneSystem zones = ZoneSystem.instance;
        return zones == null ? (float?)null : zones.m_waterLevel;
    }

    private static float HighestGround(ExcavationSamples samples)
    {
        float highest = float.NegativeInfinity;
        int reach = samples.Reach;
        for (int dz = -reach; dz <= reach; dz++)
        {
            for (int dx = -reach; dx <= reach; dx++)
            {
                if (samples.IsLoaded(dx, dz) && samples.Height(dx, dz) > highest) highest = samples.Height(dx, dz);
            }
        }
        return highest;
    }

    private ExcavationSamples Samples(ExcavationMode mode) => mode == ExcavationMode.Small ? _smallSamples : _largeSamples;

    private ExcavationSamples Check(ExcavationMode mode) => mode == ExcavationMode.Small ? _smallCheck : _largeCheck;

    public void Dispose()
    {
        if (_inFlight.Abandon()) _log("Excavation: " + ExcavationReport.Outcome(ExcavationOutcome.Abandoned, _inFlight.Plan));
        _ops.Dispose();
    }
}
