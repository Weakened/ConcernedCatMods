using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedLadders.Domain.Excavation;
using TheConcernedCat.ConcernedLadders.Runtime;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Excavation;

/// <summary>Plans, commits and undoes player-sized digs (#487; reworked by #551), behind the off-by-default
/// <c>[Excavation] Enabled</c> switch. This is the only place in the product that changes terrain, and it does so in exactly
/// one way: <c>Attack.SpawnOnHitTerrain</c> with this mod's own registered single-vertex terrain operations, the path the
/// pickaxe uses, so persistence, reload and synchronisation are the game's own (EXCAVATION.md sections 3 and 10). It writes
/// no height, delta, ZDO or RPC of its own and removes nothing.
///
/// <para>The only triggers are the equipped Iron Shovel's own Attack and Secondary Attack presses
/// (<see cref="ExcavationShovelInput"/>) and the wheel in the depth stage (<see cref="ExcavationPreview"/>), driving the
/// game-free <see cref="DigSession"/>: corner, corner, depth, dig; Secondary steps back, and with nothing to step back to
/// arms and then confirms an undo of the last dig. A dig charges the shovel's durability and the player's stamina once, here
/// and nowhere else, scaled by the ground it removes (<see cref="DigCost"/>); an undo charges and refunds nothing.</para>
///
/// <para>The order of a dig: switch, ladder, the preview confirmation, then (in <see cref="Evaluate"/>) start-up probe,
/// registration, the game's terrain-op kill switch, one-in-flight, a fresh terrain read, the planner, then - only for a
/// Ready plan - whether the shovel and the player can pay, then the ward at the rectangle's corner radius, no-build
/// locations and the object scan; then the fresh plan must be the one shown; then the batch (<see cref="Place"/>, the single
/// spawn site), then the charge. An undo asks the same questions of the last dig's record, refusing if anything now stands
/// in the hole. After either, the ground is read back every frame until it is Complete, Overshot, Unreadable or timed out.
/// Nothing is ever retried; the next dig or undo is a fresh decision the player makes.</para>
///
/// <b>Not observed in game.</b> Every in-game row of #487 and #551 is OWNER GO-AROUND PENDING.</summary>
internal sealed class ExcavationRuntime : IDisposable
{
    private readonly Action<string> _log;
    private readonly ExcavationTerrainOps _ops;
    private readonly ExcavationTerrainReader _reader = new ExcavationTerrainReader();
    private readonly ExcavationWorldChecks _world = new ExcavationWorldChecks();
    private readonly DigSamples _samples = new DigSamples();
    private readonly DigSamples _check = new DigSamples();
    private readonly DigSamples _previewSamples = new DigSamples();
    private readonly DigInFlight _inFlight = new DigInFlight();
    private readonly DigSession _session = new DigSession();
    private readonly DigHistory _history = new DigHistory();
    private readonly DigPreviewState _shown = new DigPreviewState();
    private readonly DigPreviewShape _shape = new DigPreviewShape();
    private DigStage _lastStage;
    private DigFootprint _lastFootprint;
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

    /// <summary>Called for every later result (the read-back of a dig or an undo), so the HUD can show it; the log always
    /// gets it too.</summary>
    internal Action<string>? Report { get; set; }

    /// <summary>Called once, when a fault stops excavation for the rest of the session, so the plugin can disable the
    /// shovel's recipe (no one should craft a tool that can no longer dig).</summary>
    internal Action? Faulted { get; set; }

    /// <summary>The dig flow the presses drive.</summary>
    internal DigSession Session => _session;

    /// <summary>What the preview shows now, and the rule a dig press must pass to use it.</summary>
    internal DigPreviewState Shown => _shown;

    /// <summary>The outlines for <see cref="Shown"/>.</summary>
    internal DigPreviewShape Shape => _shape;

    /// <summary>Registers the terrain ops on every peer (see <see cref="ExcavationTerrainOps"/>) and logs the probe.</summary>
    internal void Install()
    {
        _ops.Install();
        string probe = _reader.Missing ?? _world.Missing ?? "ok";
        _log("Excavation (#487/#551, experimental) is " + (Enabled ? "ENABLED" : "off (the default)") + "; game probe " + probe + ".");
    }

    internal string Status()
    {
        string probe = _reader.Missing ?? _world.Missing ?? (_disposed ? "stopped" : _faulted ? "faulted" : "ok");
        string state = "Excavation is " + (Enabled ? "enabled" : "off") + "; probe " + probe + "; terrain ops " +
                       (_ops.IsRegistered() ? "registered" : "NOT registered") + "; stage " + _session.Stage + ", depth " +
                       N(_session.Depth) + " m; " + N(_history.Count) + " dig(s) can be undone; preview terrain reads " +
                       _previewReads.ToString(CultureInfo.InvariantCulture) + ".";
        if (_inFlight.IsActive) state += " Waiting on a " + (_inFlight.Kind == DigFlightKind.Dig ? "dig" : "fill") + ".";
        return state;
    }

    /// <summary>Read-only: plans the rectangle the shovel shows now and describes it in full, for the console. Never commits
    /// or flashes a ward.</summary>
    internal string PlanAtAim()
    {
        if (!Enabled) return DigText.Refusal(ExcavationCommitRefusal.Disabled, ExcavationReadFault.None, default, null);
        if (!Available) return DigText.Refusal(ExcavationCommitRefusal.Unavailable, ExcavationReadFault.None, default, null);
        if (!_shown.Visible || !_shown.HasTarget) return "Hold the Iron Shovel and aim at the ground first; the plan is the outline it shows.";
        DigFootprint footprint = _shown.Footprint;
        ExcavationReadFault fault = _reader.Read(_samples, footprint);
        if (fault != ExcavationReadFault.None) return DigText.ReadFault(fault);
        DigPlan plan = DigPlanner.Plan(_samples, footprint, WaterLevel());
        return DigText.Detail(plan);
    }

    /// <summary>The preview stopped showing (the shovel was put away, a menu opened, the player died): the session starts
    /// over.</summary>
    internal void HidePreview()
    {
        _shown.Hide();
        _session.Reset();
    }

    /// <summary>The wheel in the depth stage: deeper (positive) or shallower. Changes nothing but the depth shown.</summary>
    internal void ChangeDepth(int steps)
    {
        if (_session.ChangeDepth(steps)) _lastPreviewRead = float.NaN;
    }

    /// <summary>Refreshes what the preview shows. <b>Read-only</b>: it aims, reads the terrain into the preview's own grid,
    /// plans, and asks every gate question a dig would ask (the shovel, the stamina, the ward without flashing it, no-build
    /// areas, the object scan) without committing, charging, flashing or spawning anything. The terrain is re-read at most
    /// every <see cref="ExcavationPreviewRules.MinimumRefreshSeconds"/> while the rectangle moves, and every
    /// <see cref="ExcavationPreviewRules.SteadyRefreshSeconds"/> while it stays put. Returns whether it re-read.</summary>
    internal bool RefreshPreview(Player player, ItemDrop.ItemData shovel, int frame, float now)
    {
        bool available = Available;
        ExcavationAim aim = ExcavationAim.Ok;
        Collider? blocker = null;
        float groundY = float.NaN;
        if (available && _session.FollowsAim)
        {
            aim = Aim(player, out int x, out int z, out groundY, out blocker);
            if (aim == ExcavationAim.Ok) _session.Aim(x, z);
            else _session.LoseAim();
        }

        DigFootprint footprint = _session.Footprint;
        bool changed = !_shown.Visible || _session.Stage != _lastStage || !footprint.Equals(_lastFootprint) ||
                       (_session.FollowsAim && _shown.HasTarget != _session.HasAim);
        if (!ExcavationPreviewRules.ShouldRefresh(changed, now - _lastPreviewRead))
        {
            _shown.Touch(frame);
            return false;
        }

        _lastPreviewRead = now;
        _lastStage = _session.Stage;
        _lastFootprint = footprint;
        _previewReads++;
        _shape.Clear();
        if (!available)
        {
            _shown.ShowNoTarget(_session.Stage, ExcavationAim.NoPlayer, DigText.Refusal(ExcavationCommitRefusal.Unavailable, ExcavationReadFault.None, default, null), frame, now);
            return true;
        }

        if (OnLadder())
        {
            _shown.ShowNoTarget(_session.Stage, ExcavationAim.Ok, DigText.Refusal(ExcavationCommitRefusal.OnLadder, ExcavationReadFault.None, default, null), frame, now);
            return true;
        }

        if (_session.FollowsAim && !_session.HasAim)
        {
            _shown.ShowNoTarget(_session.Stage, aim, DigText.Aim(aim, NameOf(blocker)), frame, now);
            return true;
        }

        ExcavationCommitRefusal refusal = Evaluate(footprint, player, shovel, committing: false, _previewSamples, out DigPlan plan,
            out ExcavationReadFault fault, out _, out string? obstruction, out _, out _, out bool planned);
        if (planned) _shape.Build(plan, WaterLevel());
        else _shape.BuildFootprint(footprint, groundY);
        _shown.Show(_session.Stage, footprint, planned, plan, refusal, Line(refusal, fault, plan, obstruction), frame, now);
        return true;
    }

    /// <summary>The line the HUD shows for a stage: the refusal if there is one, otherwise the plan's warning or the
    /// prompt.</summary>
    private string Line(ExcavationCommitRefusal refusal, ExcavationReadFault fault, in DigPlan plan, string? obstruction)
    {
        // Before the last stage a refusal is only a hint about this spot: the corner can still be set and moved.
        if (refusal != ExcavationCommitRefusal.None && refusal != ExcavationCommitRefusal.NothingToDig &&
            !(refusal == ExcavationCommitRefusal.NotEnoughStamina && _session.Stage != DigStage.Depth))
        {
            return DigText.Refusal(refusal, fault, plan, obstruction);
        }

        // Nothing to dig, or a ready floor below the water, says so; otherwise the stage's prompt.
        return DigText.Plan(plan) ?? DigText.Prompt(_session.Stage, _session.Footprint);
    }

    /// <summary>The Attack press: sets a corner, or digs what the preview shows. Runs inside the game's own attack-input
    /// step, which the game gives no press while chat, the console, a menu, the inventory or the map has the input.</summary>
    internal string Primary(Player player, ItemDrop.ItemData shovel)
    {
        try
        {
            if (!Enabled) return DigText.Refusal(ExcavationCommitRefusal.Disabled, ExcavationReadFault.None, default, null);
            if (!Available) return DigText.Refusal(ExcavationCommitRefusal.Unavailable, ExcavationReadFault.None, default, null);
            DigAction action = _session.Primary();
            _lastPreviewRead = float.NaN;
            return action switch
            {
                DigAction.Dig => DigWithShovel(player, shovel),
                DigAction.Advanced => string.Empty,
                _ => DigText.Aim(_shown.Aim, null),
            };
        }
        catch (Exception exception)
        {
            return Fault(exception);
        }
    }

    /// <summary>The Secondary Attack press: one stage back; with nothing to step back to, arms an undo, and a second press
    /// within <see cref="DigSession.UndoConfirmSeconds"/> fills the last hole back in.</summary>
    internal string Secondary(Player player, ItemDrop.ItemData shovel)
    {
        try
        {
            if (!Enabled) return DigText.Refusal(ExcavationCommitRefusal.Disabled, ExcavationReadFault.None, default, null);
            if (!Available) return DigText.Refusal(ExcavationCommitRefusal.Unavailable, ExcavationReadFault.None, default, null);
            DigAction action = _session.Secondary(Time.realtimeSinceStartup);
            _lastPreviewRead = float.NaN;
            switch (action)
            {
                case DigAction.Undo: return UndoWithShovel(player, shovel);
                case DigAction.UndoArmed:
                    return _history.Last == null ? DigText.Undo(UndoRefusal.Nothing) : DigText.UndoArmed;
                default: return string.Empty;
            }
        }
        catch (Exception exception)
        {
            return Fault(exception);
        }
    }

    /// <summary>The commit gate for a dig. It digs what the preview shows and nothing else: the rectangle and depth set,
    /// only once the outline has been visible for the dwell and was drawn in the latest frames
    /// (<see cref="DigPreviewState.Confirm"/>), and only when a fresh read at the press still gives the plan that was shown
    /// as ready (<see cref="DigPreviewState.Matches"/>).</summary>
    internal string DigWithShovel(Player player, ItemDrop.ItemData shovel)
    {
        if (!Available) return DigText.Refusal(ExcavationCommitRefusal.Unavailable, ExcavationReadFault.None, default, null);
        if (OnLadder()) return DigText.Refusal(ExcavationCommitRefusal.OnLadder, ExcavationReadFault.None, default, null);
        ExcavationCommitRefusal refusal = _shown.Confirm(Time.frameCount, Time.realtimeSinceStartup);
        if (refusal != ExcavationCommitRefusal.None) return DigText.Refusal(refusal, ExcavationReadFault.None, default, null);
        refusal = Evaluate(_shown.Footprint, player, shovel, committing: true, _samples, out DigPlan plan, out ExcavationReadFault fault,
            out int pieces, out string? obstruction, out float staminaCost, out float durabilityCost, out _);
        if (refusal != ExcavationCommitRefusal.None) return DigText.Refusal(refusal, fault, plan, obstruction);
        if (!_shown.Matches(plan))
        {
            // A Ready plan the player has not seen (the ground or the floor moved since the outline was drawn) is a new
            // decision: nothing is committed, and the next frame draws the plan as it is now.
            _shown.Invalidate();
            _lastPreviewRead = float.NaN;
            return DigText.Refusal(ExcavationCommitRefusal.PreviewChanged, ExcavationReadFault.None, plan, null);
        }

        return Commit(plan, pieces, player, shovel, staminaCost, durabilityCost);
    }

    /// <summary>Every gate question for one rectangle, in the commit order (probe, registration, the game's terrain-op kill
    /// switch, one in flight, a fresh terrain read, the planner, whether the shovel and the player can pay, the ward, no-build
    /// areas, the object scan). It never commits, charges or spawns. Only when <paramref name="committing"/> (the press) does a
    /// refusal give the player the game's feedback: the ward flashes, as the pickaxe's does, and the stamina bar flashes. The
    /// preview asks the same questions quietly, so what it shows is what the press would decide.</summary>
    private ExcavationCommitRefusal Evaluate(DigFootprint footprint, Player player, ItemDrop.ItemData shovel, bool committing,
        DigSamples samples, out DigPlan plan, out ExcavationReadFault fault, out int pieces, out string? obstruction,
        out float staminaCost, out float durabilityCost, out bool planned)
    {
        fault = ExcavationReadFault.None;
        plan = default;
        pieces = 0;
        obstruction = null;
        planned = false;
        bool registered = _ops.IsRegistered();
        bool readable = Available && registered && !TerrainOp.m_forceDisableTerrainOps && !_inFlight.IsActive;
        if (readable)
        {
            fault = _reader.Read(samples, footprint);
            if (fault == ExcavationReadFault.None)
            {
                plan = DigPlanner.Plan(samples, footprint, WaterLevel());
                planned = true;
            }
        }

        durabilityCost = DigCost.DurabilityCost(plan.LoweredVolume, Game.m_durabilityRate);
        staminaCost = DigCost.Stamina(plan.LoweredVolume);
        ExcavationCommitRefusal refusal = DigGate.BeforeWorldChecks(Enabled, Available, registered,
            TerrainOp.m_forceDisableTerrainOps, _inFlight.IsActive, fault, plan);
        if (refusal != ExcavationCommitRefusal.None) return refusal;

        // Whether the shovel and the player can pay, before any world check (a tired player flashes no ward).
        refusal = IronShovel.CanPay(Equipped(player, shovel), Ready(player), shovel.m_shared.m_useDurability, shovel.m_durability,
            durabilityCost, player.HaveStamina(DigCost.StaminaNeeded(plan.LoweredVolume, Game.m_staminaRate)));
        if (refusal != ExcavationCommitRefusal.None)
        {
            if (committing && refusal == ExcavationCommitRefusal.NotEnoughStamina) FlashStamina();
            return refusal;
        }

        bool ward = committing ? ExcavationWorldChecks.WardAllows(footprint, plan.Datum) : ExcavationWorldChecks.WardAllowsQuietly(footprint, plan.Datum);
        bool noBuild = ward && _world.InNoBuildArea(footprint, plan.Datum);
        bool overflow = false;
        int obstructions = ward && !noBuild
            ? _world.Obstructions(footprint, plan.Bottom, Math.Max(HighestGround(samples), plan.Datum), out pieces, out overflow, out obstruction, out _)
            : 0;
        return DigGate.WorldChecks(ward, noBuild, obstructions, overflow);
    }

    /// <summary>The batch: takes the in-flight slot, then hands each vertex's single-vertex operation to the game through
    /// the pickaxe's own path, the one place the product changes terrain. Returns what was placed (null, with the slot
    /// freed, when nothing was), and narrows the slot to it when the game's own per-operation re-check refused part.</summary>
    private DigRecord? Place(DigFlightKind kind, DigRecord record, out ExcavationCommitRefusal refused)
    {
        refused = ExcavationCommitRefusal.None;
        TerrainOp? op = _ops.For(kind);
        if (op == null || !_inFlight.TryStart(kind, record, Time.time))
        {
            refused = op == null ? ExcavationCommitRefusal.NotRegistered : ExcavationCommitRefusal.Busy;
            return null;
        }

        var placed = new List<DigVertex>(record.Vertices.Count);
        foreach (DigVertex vertex in record.Vertices)
        {
            float target = kind == DigFlightKind.Dig ? vertex.After : vertex.Before;
            var point = new Vector3(vertex.X * ExcavationGeometry.VertexSpacing, ExcavationTerrainOps.OperationY(kind, target),
                vertex.Z * ExcavationGeometry.VertexSpacing);
            // SpawnOnHitTerrain returns the instantiated op, whose Awake has already applied it and asked Unity to destroy
            // it; it returns a plain null only when its own ward or no-build check refused, before instantiating anything.
            GameObject? spawned = Attack.SpawnOnHitTerrain(point, op.gameObject, null, 0f, null, null);
            if (ReferenceEquals(spawned, null)) break;
            placed.Add(vertex);
        }

        if (placed.Count == 0)
        {
            // The game's own terrain-tool path refused (its ward or no-build re-check). Nothing was instantiated, so there
            // is nothing to wait for, and nothing is charged.
            _inFlight.Abandon();
            refused = ExcavationCommitRefusal.SpawnRefused;
            return null;
        }

        if (placed.Count == record.Vertices.Count) return record;
        var narrowed = new DigRecord(record.Footprint, placed);
        _inFlight.Narrow(narrowed);
        _log("Excavation: the game refused " + N(record.Vertices.Count - placed.Count) + " of " + N(record.Vertices.Count) +
             " operation(s) (its own ward or no-build re-check); only the rest were placed.");
        return narrowed;
    }

    /// <summary>A Ready dig: its record (every vertex it lowers, with its height before) goes to the batch; the charge is
    /// taken once, after the game has placed it, for what was placed.</summary>
    private string Commit(in DigPlan plan, int pieces, Player player, ItemDrop.ItemData shovel, float staminaCost, float durabilityCost)
    {
        var record = new DigRecord(plan.Footprint, DigPlanner.Lowered(plan, _samples));
        DigRecord? placed = Place(DigFlightKind.Dig, record, out ExcavationCommitRefusal refused);
        if (placed == null) return DigText.Refusal(refused, ExcavationReadFault.None, plan, null);
        if (placed.Vertices.Count < record.Vertices.Count)
        {
            // The game's own re-check refused part of the batch: only what it placed is charged, pro rata.
            float share = placed.Vertices.Count / (float)record.Vertices.Count;
            staminaCost *= share;
            durabilityCost *= share;
        }

        // The charge, once (EXCAVATION.md 7.3). The operations have been handed to every tile owner, so whatever the
        // read-back later reports, this dig has been paid for and is never paid for again: the in-flight slot refuses a
        // second commit until it resolves, and a repeat of a completed plan is NothingToDig, which is free.
        Charge(player, shovel, staminaCost, durabilityCost);
        _session.Dug();
        _shown.Invalidate();
        _lastPreviewRead = float.NaN;
        string text = "Digging " + N(placed.Vertices.Count) + " of " + N(record.Vertices.Count) + " point(s) to " + F(plan.Bottom) + " m.";
        if (pieces > 0) text += " " + N(pieces) + " building-piece collider(s) stand on the changed ground; their support is the game's own.";
        _log("Excavation committed: " + text);
        return string.Empty;
    }

    /// <summary>The undo gate (owner decision 2026-11-10): the last dig, newest first, restored exactly, with no charge and
    /// no refund. Refused, changing nothing, when there is no dig to undo, something is in flight, the ground there is not
    /// loaded or has changed since, a ward or a no-build area covers it, or anything now stands in the hole.</summary>
    internal string UndoWithShovel(Player player, ItemDrop.ItemData shovel)
    {
        if (!Available) return DigText.Refusal(ExcavationCommitRefusal.Unavailable, ExcavationReadFault.None, default, null);
        if (OnLadder()) return DigText.Refusal(ExcavationCommitRefusal.OnLadder, ExcavationReadFault.None, default, null);
        DigRecord? record = _history.Last;
        if (record == null) return DigText.Undo(UndoRefusal.Nothing);
        if (_inFlight.IsActive) return DigText.Undo(UndoRefusal.Busy);
        if (!_ops.IsRegistered() || TerrainOp.m_forceDisableTerrainOps)
            return DigText.Refusal(_ops.IsRegistered() ? ExcavationCommitRefusal.TerrainOpsDisabled : ExcavationCommitRefusal.NotRegistered, ExcavationReadFault.None, default, null);
        if (!Equipped(player, shovel) || !Ready(player)) return DigText.Refusal(ExcavationCommitRefusal.NotReady, ExcavationReadFault.None, default, null);
        ExcavationReadFault fault = _reader.Read(_check, record.Footprint);
        if (fault == ExcavationReadFault.Rebuilding || fault == ExcavationReadFault.TilesDisagree) return DigText.ReadFault(fault);
        UndoRefusal undo = fault == ExcavationReadFault.None ? DigUndo.Check(record, Read(_check)) : UndoRefusal.Unloaded;
        if (undo != UndoRefusal.None) return DigText.Undo(undo);
        float floor = float.PositiveInfinity, top = float.NegativeInfinity;
        foreach (DigVertex vertex in record.Vertices)
        {
            floor = Math.Min(floor, vertex.After);
            top = Math.Max(top, vertex.Before);
        }

        if (!ExcavationWorldChecks.WardAllows(record.Footprint, top)) return DigText.Refusal(ExcavationCommitRefusal.Protected, ExcavationReadFault.None, default, null);
        if (_world.InNoBuildArea(record.Footprint, top)) return DigText.Refusal(ExcavationCommitRefusal.NoBuildArea, ExcavationReadFault.None, default, null);
        int obstructions = _world.Obstructions(record.Footprint, floor, top, out int pieces, out bool overflow, out string? obstruction, out _);
        // Raising the ground back would bury whatever stands in the hole now, a building piece as much as anything else.
        if (overflow || obstructions > 0 || pieces > 0) return DigText.Undo(UndoRefusal.PieceInside);
        DigRecord? placed = Place(DigFlightKind.Undo, record, out ExcavationCommitRefusal refused);
        if (placed == null) return DigText.Refusal(refused, ExcavationReadFault.None, default, null);
        _shown.Invalidate();
        _lastPreviewRead = float.NaN;
        _log("Excavation undo committed: raising " + N(placed.Vertices.Count) + " of " + N(record.Vertices.Count) + " point(s) back.");
        return string.Empty;
    }

    /// <summary>Takes the dig's cost from the shovel and the player. Stamina first, through the game's own UseStamina (which
    /// applies the world's stamina modifier), then durability (a plain field of the player's own item, which cannot throw),
    /// so a fault charges nothing rather than half.
    ///
    /// <b>No animation is played.</b> The donor's swing clip raises the game's attack-trigger event, and
    /// <c>Humanoid.OnAttackTrigger</c> runs <c>m_currentAttack.OnAttackTrigger()</c> on whatever attack is still stored, which
    /// nothing clears when an attack finishes: a punch thrown before equipping the shovel would land a real melee hit outside
    /// this gate (independent review of #487 slice 3).</summary>
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
    /// attached (LadderRuntime.Free), so the shovel neither previews nor digs from one.</summary>
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

    /// <summary>Reads back a batch in flight. Called every frame; does nothing when none is. Leaving the world forgets the
    /// undo history: its records are this world's coordinates.</summary>
    internal void Update()
    {
        if (Player.m_localPlayer == null || ZNetScene.instance == null) _history.Clear();
        if (!_inFlight.IsActive) return;
        DigRecord? record = _inFlight.Record;
        DigFlightKind kind = _inFlight.Kind;
        try
        {
            float now = Time.time;
            // The slot is taken in the press's FixedUpdate, where Time.time is the fixed clock, usually a little behind the
            // same frame's Update clock; a read before the rebuild sees a queued rebuild or the old heights, and both wait.
            if (now <= _inFlight.StartedAt || record == null) return;
            if (Player.m_localPlayer == null || ZNetScene.instance == null)
            {
                if (_inFlight.Abandon()) _log("Excavation: the world went away before the " + (kind == DigFlightKind.Dig ? "dig" : "fill") + " could be checked.");
                return;
            }

            // TilesDisagree here means one tile under the hole has rebuilt and its neighbour not yet: Observe waits.
            ExcavationReadFault fault = _reader.Read(_check, record.Footprint);
            ExcavationProgress progress = fault != ExcavationReadFault.None ? ExcavationProgress.Unreadable
                : kind == DigFlightKind.Dig ? DigPlanner.Progress(record, Read(_check)) : DigUndo.Progress(record, Read(_check));
            ExcavationOutcome outcome = _inFlight.Observe(fault, progress, now);
            if (outcome == ExcavationOutcome.Waiting) return;
            if (outcome == ExcavationOutcome.Complete)
            {
                if (kind == DigFlightKind.Dig) _history.Push(record);
                else _history.Remove(record);
            }

            _log("Excavation " + (kind == DigFlightKind.Dig ? "dig" : "fill") + " of " + N(record.Footprint.Width) + "x" +
                 N(record.Footprint.Length) + " m at (" + N(record.Footprint.MinX) + ", " + N(record.Footprint.MinZ) + "): " + outcome +
                 (fault != ExcavationReadFault.None ? " (" + fault + ")" : "") + ".");
            Publish(DigText.Outcome(outcome, kind, record.Footprint));
        }
        catch (Exception exception)
        {
            _inFlight.Abandon();
            Publish(Fault(exception));
        }
    }

    private void Publish(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Report?.Invoke(text); } catch { /* The HUD is optional. */ }
    }

    /// <summary>Stops excavation for the rest of the session after a fault anywhere in it (a press, the read-back, or the
    /// preview, which a dig needs). The shovel's recipe is disabled through <see cref="Faulted"/>.</summary>
    internal string StopAfterFault(Exception exception) => Fault(exception);

    private string Fault(Exception exception)
    {
        bool first = !_faulted;
        _faulted = true;
        _shown.Hide();
        _log("Excavation stopped after an unexpected fault (" + SafeFailure.Brief(exception) + "); it stays off until restart.");
        if (first)
        {
            try { Faulted?.Invoke(); } catch { /* Recipe feedback only. */ }
        }

        return "Digging stopped after a fault: see the log.";
    }

    /// <summary>The target of the shovel, by the game's own build-placement rule (Player.PieceRayTest, read from the
    /// installed game): the camera's ray, 50 m long, against the placement layers (pieces, solid static objects and terrain),
    /// triggers ignored. The first thing it meets must be terrain - so the shovel never digs through a wall, a floor or a rock
    /// it cannot reach - and that point must be within <see cref="ExcavationLimits.Reach"/> of the player's eyes. The point
    /// is snapped to the nearest lattice vertex; rectangles follow the world axes.</summary>
    private ExcavationAim Aim(Player? player, out int x, out int z, out float groundY, out Collider? blocker)
    {
        x = 0;
        z = 0;
        groundY = float.NaN;
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
        x = ExcavationGeometry.SnapToVertex(hit.point.x);
        z = ExcavationGeometry.SnapToVertex(hit.point.z);
        groundY = hit.point.y;
        return ExcavationAim.Ok;
    }

    /// <summary>The prefab name of what blocked the aim, for the HUD. Read only when the preview refreshes, because reading a
    /// Unity object's name allocates.</summary>
    private static string? NameOf(Collider? collider)
    {
        if (collider == null) return null;
        Transform root = collider.transform.root;
        string name = (root != null ? root : collider.transform).name;
        int clone = name.IndexOf('(');
        return clone > 0 ? name.Substring(0, clone).Trim() : name;
    }

    /// <summary>A vertex's height from a filled grid, or null where it was not read.</summary>
    private static Func<int, int, float?> Read(DigSamples samples) =>
        (x, z) => samples.Covers(x, z) && samples.IsLoaded(x, z) ? samples.Height(x, z) : (float?)null;

    private static float? WaterLevel()
    {
        ZoneSystem zones = ZoneSystem.instance;
        return zones == null ? (float?)null : zones.m_waterLevel;
    }

    private static float HighestGround(DigSamples samples)
    {
        float highest = float.NegativeInfinity;
        for (int z = samples.OriginZ; z < samples.OriginZ + samples.SideZ; z++)
        {
            for (int x = samples.OriginX; x < samples.OriginX + samples.SideX; x++)
            {
                if (samples.IsLoaded(x, z) && samples.Height(x, z) > highest) highest = samples.Height(x, z);
            }
        }

        return highest;
    }

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string F(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        _disposed = true;
        _shown.Hide();
        if (_inFlight.Abandon()) _log("Excavation: a batch was still unconfirmed at shutdown.");
        _history.Clear();
        _ops.Dispose();
    }
}
