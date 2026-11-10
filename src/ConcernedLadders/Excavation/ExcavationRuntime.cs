using System;
using System.Globalization;
using TheConcernedCat.ConcernedLadders.Domain.Excavation;
using TheConcernedCat.ConcernedLadders.Runtime;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Excavation;

/// <summary>Plans and commits one measured excavation (#487 slices 2 to 4), behind the off-by-default
/// <c>[Excavation] Enabled</c> switch. This is the only place in the product that changes terrain, and it does so in
/// exactly one way: one <c>Attack.SpawnOnHitTerrain</c> call with this mod's own registered terrain operation, the
/// path the pickaxe uses, so persistence, reload and synchronisation are the game's own (EXCAVATION.md section 3).
/// It writes no height, delta, ZDO or RPC of its own and removes nothing. The only trigger is a press of the equipped
/// Iron Shovel's Attack (<see cref="ExcavationShovelInput"/>), and a dig that goes ahead charges the shovel's
/// durability and the player's stamina once, here and nowhere else.
///
/// Since slice 4 a press confirms what the preview (<see cref="RefreshPreview"/>, read-only) shows: the selected size at
/// the square the outline was drawn on. The order of a commit: switch, ladder, the preview confirmation, then (in
/// <see cref="Evaluate"/>) start-up probe, registration, the game's terrain-op kill switch, one-in-flight, a fresh
/// terrain read, the planner, then - only for a Ready plan - whether the shovel and the player can pay (equipped, free to
/// act, durability, stamina), then the ward at the wider radius, no-build locations and the object scan; then the fresh
/// plan must be the one shown; then the single spawn, then the charge. After it, the floor is read back every frame
/// through <see cref="ExcavationPlanner.Check"/> until it is Complete, Overshot, Unreadable or timed out. Nothing is ever
/// retried; the next dig is a fresh plan the player asks for.
///
/// <b>Not observed in game.</b> Every in-game row of #487 is OWNER GO-AROUND PENDING.</summary>
internal sealed class ExcavationRuntime : IDisposable
{
    private readonly Action<string> _log;
    private readonly ExcavationTerrainOps _ops;
    private readonly ExcavationTerrainReader _reader = new ExcavationTerrainReader();
    private readonly ExcavationWorldChecks _world = new ExcavationWorldChecks();
    private readonly ExcavationSamples _smallSamples = new ExcavationSamples(ExcavationMode.Small);
    private readonly ExcavationSamples _largeSamples = new ExcavationSamples(ExcavationMode.Large);
    private readonly ExcavationSamples _smallCheck = new ExcavationSamples(ExcavationMode.Small);
    private readonly ExcavationSamples _largeCheck = new ExcavationSamples(ExcavationMode.Large);
    private readonly ExcavationSamples _smallPreview = new ExcavationSamples(ExcavationMode.Small);
    private readonly ExcavationSamples _largePreview = new ExcavationSamples(ExcavationMode.Large);
    private readonly ExcavationInFlight _inFlight = new ExcavationInFlight();
    private readonly ExcavationSelection _selection = new ExcavationSelection();
    private readonly ExcavationPreviewState _shown = new ExcavationPreviewState();
    private readonly ExcavationPreviewShape _shape = new ExcavationPreviewShape();
    private float _lastPreviewRead = float.NaN;
    private long _previewReads;
    private int _aimMask;
    private bool _faulted;
    private bool _disposed;

    internal ExcavationRuntime(Action<string> log, bool enabled)
    {
        _log = log;
        Enabled = enabled;
        _ops = new ExcavationTerrainOps(log);
    }

    /// <summary>The opt-in switch, read once at start-up.</summary>
    internal bool Enabled { get; }

    /// <summary>Both start-up probes found every game member they read, nothing has faulted since, and the runtime has not
    /// been disposed.</summary>
    internal bool Available => _reader.Available && _world.Available && !_faulted && !_disposed;

    /// <summary>Called for every later result (the read-back of a dig), so the console and the HUD can show it; the log
    /// always gets it too.</summary>
    internal Action<string>? Report { get; set; }

    /// <summary>Called once, when a fault stops excavation for the rest of the session, so the plugin can disable the
    /// shovel's recipe (no one should craft a tool that can no longer dig).</summary>
    internal Action? Faulted { get; set; }

    /// <summary>Registers the terrain ops on every peer (see <see cref="ExcavationTerrainOps"/>) and logs the probe.</summary>
    internal void Install()
    {
        _ops.Install();
        string probe = _reader.Missing ?? _world.Missing ?? "ok";
        _log("Excavation (#487, experimental) is " + (Enabled ? "ENABLED" : "off (the default)") + "; game probe " + probe + ".");
    }

    internal string Status()
    {
        string probe = _reader.Missing ?? _world.Missing ?? (_disposed ? "stopped" : _faulted ? "faulted" : "ok");
        string state = "Excavation is " + (Enabled ? "enabled" : "off") + "; probe " + probe +
                       "; terrain ops " + (_ops.IsRegistered(ExcavationMode.Small) && _ops.IsRegistered(ExcavationMode.Large) ? "registered" : "NOT registered") +
                       "; shovel size " + ExcavationReport.ModeName(_selection.Selected) + "; preview terrain reads " +
                       _previewReads.ToString(CultureInfo.InvariantCulture) + ".";
        if (_inFlight.IsActive) state += " Waiting on " + ExcavationReport.Outcome(ExcavationOutcome.Waiting, _inFlight.Plan);
        return state;
    }

    /// <summary>Read-only: plans at the aimed point and describes it, with the object scan for a Ready plan (what would
    /// refuse, and how many building pieces stand on ground that would change). Never commits or flashes a ward.</summary>
    internal string PlanAtAim(ExcavationMode mode)
    {
        if (!Enabled) return ExcavationReport.Commit(ExcavationCommitRefusal.Disabled, ExcavationReadFault.None, default, null);
        if (!Available) return ExcavationReport.Commit(ExcavationCommitRefusal.Unavailable, ExcavationReadFault.None, default, null);
        ExcavationAim aim = Aim(Player.m_localPlayer, out int centreX, out int centreZ, out Collider? blocker);
        if (aim != ExcavationAim.Ok) return ExcavationReport.Aim(aim, NameOf(blocker));
        ExcavationSamples samples = Samples(mode);
        ExcavationReadFault fault = _reader.Read(samples, centreX, centreZ);
        if (fault != ExcavationReadFault.None) return ExcavationReport.ReadFault(fault);
        ExcavationPlan plan = ExcavationPlanner.Plan(samples, centreX, centreZ, WaterLevel());
        string text = ExcavationReport.Plan(plan);
        if (plan.Status == ExcavationStatus.Ready)
        {
            int obstructions = _world.Obstructions(plan, HighestGround(samples), out int pieces, out bool overflow, out string? first);
            text += " " + ExcavationReport.Ground(obstructions, first, pieces, overflow) + " " + ExcavationReport.Cost(plan.Mode);
        }
        return text;
    }

    /// <summary>The size the held shovel previews and digs (#487 slice 4); local and in memory only.</summary>
    internal ExcavationSelection Selection => _selection;

    /// <summary>What the preview shows now, and the rule a press must pass to use it.</summary>
    internal ExcavationPreviewState Shown => _shown;

    /// <summary>The outlines for <see cref="Shown"/>.</summary>
    internal ExcavationPreviewShape Shape => _shape;

    /// <summary>The secondary-attack press: switches the previewed size. Never digs, reads or changes anything else.</summary>
    internal void SwitchMode() => _selection.Switch();

    /// <summary>The preview stopped showing (the shovel was put away, a menu opened, the player died).</summary>
    internal void HidePreview() => _shown.Hide();

    /// <summary>Refreshes what the preview shows (#487 slice 4). <b>Read-only</b>: it aims, reads the terrain into the
    /// preview's own sample grids, plans, and asks every gate question the press would ask (the shovel, the stamina, the
    /// ward without flashing it, no-build areas, the object scan) without committing, charging, flashing or spawning
    /// anything. The terrain is re-read at most every <see cref="ExcavationPreviewRules.MinimumRefreshSeconds"/> while the
    /// aim moves, and every <see cref="ExcavationPreviewRules.SteadyRefreshSeconds"/> while it stays put; in between, the
    /// last result is kept. Returns whether it re-read.</summary>
    internal bool RefreshPreview(Player player, ItemDrop.ItemData shovel, int frame, float now)
    {
        ExcavationMode mode = _selection.Selected;
        int centreX = 0;
        int centreZ = 0;
        Collider? blocker = null;
        bool available = Available;
        ExcavationAim aim = available ? Aim(player, out centreX, out centreZ, out blocker) : ExcavationAim.Ok;
        bool changed = !_shown.Visible || _shown.Mode != mode || _shown.Aim != aim ||
                       (aim == ExcavationAim.Ok && (_shown.CentreX != centreX || _shown.CentreZ != centreZ));
        if (!ExcavationPreviewRules.ShouldRefresh(changed, now - _lastPreviewRead))
        {
            _shown.Touch(frame);
            return false;
        }
        _lastPreviewRead = now;
        _previewReads++;
        _shape.Clear();
        if (!available)
        {
            _shown.ShowTarget(mode, 0, 0, ExcavationCommitRefusal.Unavailable, ExcavationReadFault.None, default, null, 0, frame, now);
            return true;
        }
        if (OnLadder())
        {
            _shown.ShowTarget(mode, centreX, centreZ, ExcavationCommitRefusal.OnLadder, ExcavationReadFault.None, default, null, 0, frame, now);
            return true;
        }
        if (aim != ExcavationAim.Ok)
        {
            _shown.ShowNoTarget(mode, aim, NameOf(blocker), frame, now);
            return true;
        }
        ExcavationSamples samples = PreviewSamples(mode);
        ExcavationCommitRefusal refusal = Evaluate(mode, centreX, centreZ, player, shovel, committing: false, samples, out ExcavationPlan plan,
            out ExcavationReadFault fault, out int pieces, out string? obstruction, out _, out _, out bool planned);
        if (planned) _shape.Build(plan, samples, WaterLevel());
        _shown.ShowTarget(mode, centreX, centreZ, refusal, fault, plan, obstruction, pieces, frame, now);
        return true;
    }

    /// <summary>The commit gate for one press of the Iron Shovel's Attack. Returns what happened; the result of a submitted
    /// dig arrives later through <see cref="Update"/>. Runs inside the game's own attack-input step, which the game gives
    /// no press while chat, the console, a menu, the inventory or the map has the input.
    ///
    /// <para>Since slice 4 the press confirms what the preview shows and nothing else: the selected size, at the square
    /// the outline was drawn on, only once the outline has been visible for the dwell and was drawn in the latest frames
    /// (<see cref="ExcavationPreviewState.Confirm"/>), and only when a fresh read at the press still gives the plan that
    /// was shown as ready (<see cref="ExcavationPreviewState.Matches"/>).</para></summary>
    internal string DigWithShovel(Player player, ItemDrop.ItemData shovel)
    {
        try
        {
            if (!Enabled) return ExcavationReport.Commit(ExcavationCommitRefusal.Disabled, ExcavationReadFault.None, default, null);
            if (!Available) return ExcavationReport.Commit(ExcavationCommitRefusal.Unavailable, ExcavationReadFault.None, default, null);
            ExcavationMode mode = _selection.Selected;
            if (OnLadder()) return ExcavationReport.Commit(ExcavationCommitRefusal.OnLadder, ExcavationReadFault.None, default, null);
            ExcavationCommitRefusal refusal = _shown.Confirm(mode, Time.frameCount, Time.realtimeSinceStartup);
            if (refusal != ExcavationCommitRefusal.None) return ExcavationReport.Commit(refusal, ExcavationReadFault.None, default, null);
            if (!_shown.HasTarget) return ExcavationReport.Aim(_shown.Aim, _shown.Blocker);
            refusal = Evaluate(mode, _shown.CentreX, _shown.CentreZ, player, shovel, committing: true, Samples(mode), out ExcavationPlan plan,
                out ExcavationReadFault fault, out int pieces, out string? obstruction, out float staminaCost, out float durabilityCost, out _);
            if (refusal != ExcavationCommitRefusal.None) return ExcavationReport.Commit(refusal, fault, plan, obstruction);
            if (!_shown.Matches(plan))
            {
                // A Ready plan the player has not seen (the ground or the floor moved since the outline was drawn) is a new
                // decision: nothing is committed, and the next frame draws the plan as it is now.
                _shown.Invalidate();
                _lastPreviewRead = float.NaN;
                return ExcavationReport.Commit(ExcavationCommitRefusal.PreviewChanged, ExcavationReadFault.None, plan, null);
            }
            return Commit(plan, pieces, player, shovel, staminaCost, durabilityCost);
        }
        catch (Exception exception)
        {
            return Fault(exception);
        }
    }

    /// <summary>Every gate question for one square, in the commit order (probe, registration, the game's terrain-op kill
    /// switch, one in flight, a fresh terrain read, the planner, whether the shovel and the player can pay, the ward,
    /// no-build areas, the object scan). It never commits, charges or spawns. Only when <paramref name="committing"/> (the
    /// press) does a refusal give the player the game's feedback: the ward flashes, as the pickaxe's does, and the stamina
    /// bar flashes. The preview asks the same questions quietly, so what it shows is what the press would decide.</summary>
    private ExcavationCommitRefusal Evaluate(ExcavationMode mode, int centreX, int centreZ, Player player, ItemDrop.ItemData shovel,
        bool committing, ExcavationSamples samples, out ExcavationPlan plan, out ExcavationReadFault fault, out int pieces,
        out string? obstruction, out float staminaCost, out float durabilityCost, out bool planned)
    {
        fault = ExcavationReadFault.None;
        plan = default;
        pieces = 0;
        obstruction = null;
        planned = false;
        bool registered = _ops.IsRegistered(mode);
        bool readable = Available && registered && !TerrainOp.m_forceDisableTerrainOps && !_inFlight.IsActive;
        if (readable)
        {
            fault = _reader.Read(samples, centreX, centreZ);
            if (fault == ExcavationReadFault.None)
            {
                plan = ExcavationPlanner.Plan(samples, centreX, centreZ, WaterLevel());
                planned = true;
            }
        }
        durabilityCost = IronShovel.DurabilityCost(mode, Game.m_durabilityRate);
        staminaCost = ExcavationCost.For(mode).Stamina;
        ExcavationCommitRefusal refusal = ExcavationGate.BeforeWorldChecks(Enabled, Available, registered,
            TerrainOp.m_forceDisableTerrainOps, _inFlight.IsActive, fault, plan);
        if (refusal != ExcavationCommitRefusal.None) return refusal;

        // Whether the shovel and the player can pay, before any world check (a tired player flashes no ward).
        refusal = IronShovel.CanPay(Equipped(player, shovel), Ready(player), shovel.m_shared.m_useDurability, shovel.m_durability,
            durabilityCost, player.HaveStamina(IronShovel.StaminaNeeded(mode, Game.m_staminaRate)));
        if (refusal != ExcavationCommitRefusal.None)
        {
            if (committing && refusal == ExcavationCommitRefusal.NotEnoughStamina) FlashStamina();
            return refusal;
        }

        bool ward = committing ? ExcavationWorldChecks.WardAllows(plan) : ExcavationWorldChecks.WardAllowsQuietly(plan);
        bool noBuild = ward && _world.InNoBuildArea(plan);
        bool overflow = false;
        int obstructions = ward && !noBuild ? _world.Obstructions(plan, HighestGround(samples), out pieces, out overflow, out obstruction) : 0;
        return ExcavationGate.WorldChecks(ward, noBuild, obstructions, overflow);
    }

    /// <summary>The single terrain change. Takes the in-flight slot before spawning, so no second commit can start
    /// while this one is unresolved, and frees it only through a read-back. Charges once, only after the game has
    /// placed the operation.</summary>
    private string Commit(in ExcavationPlan plan, int pieces, Player player, ItemDrop.ItemData shovel, float staminaCost, float durabilityCost)
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
            // there is nothing to wait for, and nothing is charged.
            _inFlight.Abandon();
            return ExcavationReport.Commit(ExcavationCommitRefusal.SpawnRefused, ExcavationReadFault.None, plan, null);
        }
        // The charge, once (EXCAVATION.md 7.3). The operation has been handed to every tile owner, so whatever the
        // read-back later reports (Complete, Overshot, TimedOut, Unreadable, Abandoned), this dig has been paid for and
        // is never paid for again: the in-flight slot refuses a second commit until it resolves, and a repeat of a
        // completed plan is NothingToDig, which is free.
        Charge(player, shovel, staminaCost, durabilityCost);
        // What was shown is spent: the next frame re-reads and shows the dig in flight.
        _shown.Invalidate();
        _lastPreviewRead = float.NaN;
        string text = ExcavationReport.Commit(ExcavationCommitRefusal.None, ExcavationReadFault.None, plan, null) + " " + ExcavationReport.Plan(plan);
        if (pieces > 0) text += " " + pieces.ToString(CultureInfo.InvariantCulture) + " building-piece collider(s) stand on the changed ground; their support is the game's own.";
        _log("Excavation committed: " + text);
        return text;
    }

    /// <summary>Takes the dig's cost from the shovel and the player. Stamina first, through the game's own UseStamina
    /// (which applies the world's stamina modifier), then durability (a plain field of the player's own item, which
    /// cannot throw), so a fault charges nothing rather than half.
    ///
    /// <b>No animation is played.</b> The donor's swing clip raises the game's attack-trigger event, and
    /// <c>Humanoid.OnAttackTrigger</c> runs <c>m_currentAttack.OnAttackTrigger()</c> on whatever attack is still stored,
    /// which nothing clears when an attack finishes (only StartAttack and unequipping its weapon do): a punch thrown
    /// before equipping the shovel would land a real melee hit outside this gate (independent review of slice 3). A
    /// coherent tool animation that cannot reach a stored attack stays an owner question (EXCAVATION.md 8.6).</summary>
    private static void Charge(Player player, ItemDrop.ItemData shovel, float staminaCost, float durabilityCost)
    {
        player.UseStamina(staminaCost);
        if (shovel.m_shared.m_useDurability) shovel.m_durability = IronShovel.DurabilityAfter(shovel.m_durability, durabilityCost);
    }

    /// <summary>The shovel is this player's equipped weapon right now: the item the press came from is still the one in
    /// hand.</summary>
    private static bool Equipped(Player player, ItemDrop.ItemData shovel) =>
        ReferenceEquals(player, Player.m_localPlayer) && ReferenceEquals(player.GetCurrentWeapon(), shovel) &&
        IronShovel.IsShovelName(shovel.m_shared.m_name);

    /// <summary>The states in which the game itself refuses to start an attack (Humanoid.StartAttack), and teleporting.</summary>
    private static bool Ready(Player player) =>
        !player.InAttack() && !player.InDodge() && player.CanMove() && !player.IsStaggering() && !player.IsKnockedBack() &&
        !player.InMinorAction() && !player.IsTeleporting();

    /// <summary>The local player is on a Concerned Ladders ladder. A ladder lets only the building hammer be used while
    /// attached (LadderRuntime.Free), so the shovel neither previews a dig nor digs from one (a lead default, EXCAVATION.md
    /// 8.6).</summary>
    private static bool OnLadder() => LadderRuntime.Current?.IsClimbing == true;

    private static void FlashStamina()
    {
        try
        {
            Hud hud = Hud.instance;
            if (hud != null) hud.StaminaBarEmptyFlash();
        }
        catch { /* Feedback only. */ }
    }

    /// <summary>Reads back an operation in flight. Called every frame; does nothing when none is.</summary>
    internal void Update()
    {
        if (!_inFlight.IsActive) return;
        try
        {
            ExcavationPlan plan = _inFlight.Plan;
            float now = Time.time;
            // The slot is taken in the press's FixedUpdate, where Time.time is the fixed clock, usually a little behind
            // the same frame's Update clock; so this guard does not always skip the commit frame. That is harmless: a
            // read before the rebuild sees a queued rebuild (Rebuilding) or the old heights (Pending), and both wait.
            if (now <= _inFlight.StartedAt) return;
            if (Player.m_localPlayer == null || ZNetScene.instance == null)
            {
                if (_inFlight.Abandon()) Publish(ExcavationReport.Outcome(ExcavationOutcome.Abandoned, plan));
                return;
            }
            ExcavationSamples after = Check(plan.Mode);
            // TilesDisagree here means one tile under the hole has rebuilt and its neighbour not yet: Observe waits.
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

    /// <summary>Stops excavation for the rest of the session after a fault anywhere in it (the press, the read-back, or the
    /// preview, which a press needs). The shovel's recipe is disabled through <see cref="Faulted"/>.</summary>
    internal string StopAfterFault(Exception exception) => Fault(exception);

    private string Fault(Exception exception)
    {
        bool first = !_faulted;
        _faulted = true;
        _shown.Hide();
        string text = "Excavation stopped after an unexpected fault (" + SafeFailure.Brief(exception) + "); it stays off until restart.";
        _log(text);
        if (first)
        {
            try { Faulted?.Invoke(); } catch { /* Recipe feedback only. */ }
        }
        return text;
    }

    /// <summary>The target of a shovel preview or a console plan (#487 slice 4), by the game's own build-placement rule
    /// (Player.PieceRayTest, read from the installed game): the camera's ray, 50 m long, against the placement layers
    /// (pieces, solid static objects and terrain), triggers ignored. The first thing it meets must be terrain - so the
    /// shovel never digs through a wall, a floor or a rock it cannot reach, while ground seen under a raised floor or
    /// through an opening in one is fair - and that point must be within <see cref="ExcavationLimits.Reach"/> of the
    /// player's eyes. The point is snapped to the vertex the engine op will centre on. The square follows the world axes
    /// only (EXCAVATION.md claim 4), so there is no rotation to choose.</summary>
    private ExcavationAim Aim(Player? player, out int centreX, out int centreZ, out Collider? blocker)
    {
        centreX = 0;
        centreZ = 0;
        blocker = null;
        GameCamera camera = GameCamera.instance;
        if (player == null || camera == null || player.m_eye == null) return ExcavationAim.NoPlayer;
        if (_aimMask == 0)
        {
            // Player.m_placeRayMask, as the game builds it in Player.Awake (the field itself is private).
            _aimMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "vehicle");
        }
        Transform eye = camera.transform;
        if (!Physics.Raycast(eye.position, eye.forward, out RaycastHit hit, ExcavationLimits.AimRayLength, _aimMask, QueryTriggerInteraction.Ignore))
        {
            return ExcavationAim.NoGround;
        }
        Collider collider = hit.collider;
        if (collider == null || collider.GetComponent<Heightmap>() == null)
        {
            blocker = collider;
            return ExcavationAim.Blocked;
        }
        if (Vector3.Distance(player.m_eye.position, hit.point) > ExcavationLimits.Reach) return ExcavationAim.TooFar;
        centreX = ExcavationGeometry.SnapToVertex(hit.point.x);
        centreZ = ExcavationGeometry.SnapToVertex(hit.point.z);
        return ExcavationAim.Ok;
    }

    /// <summary>The prefab name of what blocked the aim, for the HUD. Read only when the preview refreshes (at most 20 times
    /// a second), because reading a Unity object's name allocates (independent review of slice 4, N1).</summary>
    private static string? NameOf(Collider? collider)
    {
        if (collider == null) return null;
        Transform root = collider.transform.root;
        string name = (root != null ? root : collider.transform).name;
        int clone = name.IndexOf('(');
        return clone > 0 ? name.Substring(0, clone).Trim() : name;
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

    private ExcavationSamples PreviewSamples(ExcavationMode mode) => mode == ExcavationMode.Small ? _smallPreview : _largePreview;

    public void Dispose()
    {
        _disposed = true;
        _shown.Hide();
        if (_inFlight.Abandon()) _log("Excavation: " + ExcavationReport.Outcome(ExcavationOutcome.Abandoned, _inFlight.Plan));
        _ops.Dispose();
    }
}
