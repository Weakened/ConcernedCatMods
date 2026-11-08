using System;
using System.Collections.Generic;
using BepInEx.Logging;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.Companions.Quest;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Diagnostics;
using TheConcernedCat.Workers;
using UnityEngine;
namespace TheConcernedCat.ConcernedTeamster.Adapters.Companions;

/// <summary>Gunnar at home (#458): his presentation figure while he rests, and
/// the hand-over to his worker body when <c>ct_haul spawn</c> asks for one.
///
/// <b>One body at a time, enforced three ways.</b>
/// <list type="number">
/// <item><see cref="GunnarBodyPlan"/> never asks for the figure while a saved
/// worker body with his identity exists anywhere in the world, while a job
/// holds him, while a hand-over is still waiting for the census, or while
/// anything it needs is unknown.</item>
/// <item><see cref="GunnarBodyLeases"/> holds Concerned NPC's leases, releasing
/// one before claiming the other; the arbiter refuses a figure while any worker
/// lease is held, from any runtime in the process.</item>
/// <item><see cref="GunnarFigurePresenter"/> builds the figure only after its
/// lease is granted and takes it down before anything that could release it.</item>
/// </list>
///
/// <b>What it reads, and when.</b> Nothing is read for a player who has not met
/// him: the gate is the host (single player, or the host of a game, never a
/// dedicated server or a client), <c>Companions/Gunnar</c>, his registration
/// and a world; then this character's story must be at the visitor stage
/// (<c>IntroPending</c>) or recruited. Only then is the respawn anchor looked up
/// and a spot probed, with the keepsake runtime's bounded read-only queries,
/// twice a second. The census is the hauling runtime's existing one; no second
/// scan of the world is added here.
///
/// It changes nothing in the world: no bed is claimed, nothing is placed, and
/// nobody else sees him.</summary>
internal sealed class GunnarPresentation
{
    private const float PassSeconds = 0.5f;
    private const float ShowWithinMetres = 48f;
    private const float KeepWithinMetres = 64f;


    // Built once: the pass runs twice a second and the look never changes.
    private static readonly TheConcernedCat.ConcernedNPC.Appearance.NpcLookSpec Look = GunnarLook.Spec();

    private readonly GunnarCompanionAdapter _companion;
    private readonly Func<WorkerBodyStatus> _census;
    private readonly Func<bool> _liveBody;
    private readonly Func<bool> _resting;
    private readonly Action<string> _log;
    private readonly GunnarBodyLeases _leases;
    private readonly GunnarFigurePresenter _figure;
    private readonly GunnarKeepsakeEnvironment _environment = new();

    private float _sincePass = PassSeconds;
    private bool _faulted;
    private bool _arrivalPending;
    private float _arrivalSince;
    private bool _planned;
    private CompanionAnchor _plannedFor;
    private PlacementResult _placement;
    private string _last = "not asked yet";
    private int _solidMask;

    private GunnarPresentation(GunnarCompanionAdapter companion, Func<WorkerBodyStatus> census,
        Func<bool> liveBody, Func<bool> resting, NpcIdentity identity, Action<string> log)
    {
        _companion = companion;
        _census = census;
        _liveBody = liveBody;
        _resting = resting;
        _log = log;
        _leases = new GunnarBodyLeases(NpcRoleRegistry.Shared, identity);
        _figure = new GunnarFigurePresenter(_leases, log);
    }

    /// <summary>Null, with one warning, when the companion layer is missing or
    /// his identity cannot be named; every Teamster tool keeps working.</summary>
    public static GunnarPresentation? Install(GunnarCompanionAdapter? companion, Func<WorkerBodyStatus> census,
        Func<bool> liveBody, Func<bool> resting, ManualLogSource log)
    {
        if (companion == null) return null;
        try
        {
            if (!NpcIdentity.TryParse(WorkerKey.Gunnar.Value, out NpcIdentity identity))
            {
                log.LogWarning("Gunnar's figure is unavailable: his identity could not be named. Existing tools remain available.");
                return null;
            }

            return new GunnarPresentation(companion, census, liveBody, resting, identity, text => log.LogInfo(text));
        }
        catch (Exception exception)
        {
            log.LogWarning("Gunnar's figure is unavailable (" + exception.GetType().Name + "). Existing tools remain available.");
            return null;
        }
    }

    /// <summary>Every frame while the plugin runs. Cheap when nothing is due.</summary>
    public void Tick(float deltaTime)
    {
        // The arrival guard is settled even after a fault: a guard nothing
        // released would refuse every haul in this world.
        try { SettleArrival(); }
        catch (Exception) { _leases.ReleaseWorker(); _arrivalPending = false; }
        if (_faulted) return;
        try
        {
            _figure.VerifyOnce();
            _sincePass += deltaTime;
            if (_sincePass < PassSeconds) return;
            _sincePass = 0f;
            Pass();
        }
        catch (Exception exception)
        {
            // Latched: one line, not one a second. Showing nothing is always safe.
            _faulted = true;
            try { _figure.Hide(); } catch (Exception) { /* the figure dies with the scene */ }
            _leases.ReleaseWorker();
            _arrivalPending = false;
            _log("Gunnar's figure stopped after a fault and will not be shown again in this world: " +
                SafeFailure.Brief(exception) + ". Every Teamster tool is unchanged.");
        }
    }

    /// <summary><c>ct_haul spawn</c>'s hand-over, called before his worker body
    /// is created: figure down, presentation lease released, worker lease
    /// claimed. <paramref name="standing"/> is where the figure stood, so the
    /// body appears where he was. False with a reason means no body may be
    /// placed.</summary>
    public bool TakeWorkerBody(out Vector3? standing, out string refusal)
    {
        standing = null;
        if (_arrivalPending)
        {
            // A body was just placed and not yet counted (the arrival guard may
            // not be held when a job already holds him): a second one now would
            // be the duplicate the census exists to catch.
            refusal = "a worker body was just placed and has not been counted yet; try again in a moment";
            return false;
        }

        bool granted;
        try
        {
            granted = _figure.HandOverToWorker(out standing, out refusal);
        }
        catch (Exception exception)
        {
            refusal = "his figure could not be taken down (" + exception.GetType().Name + ")";
            return false;
        }

        _planned = false;
        if (!granted) return false;
        _arrivalPending = true;
        _arrivalSince = Time.unscaledTime;
        try
        {
            // The spot was probed for a figure, not a character's body: a spot
            // with no headroom (a beam, a piece built since) is not used, and
            // the body falls back to the spot in front of the player.
            if (standing.HasValue && !HasHeadroom(standing.Value)) standing = null;
        }
        catch (Exception)
        {
            standing = null;
        }

        return true;
    }

    /// <summary>The body was not placed after all: give the arrival guard back.
    /// The next pass decides whether he is shown at home again.</summary>
    public void ReleaseWorkerBody()
    {
        _leases.ReleaseWorker();
        _arrivalPending = false;
        _sincePass = PassSeconds;
    }

    /// <summary>A body may have appeared or gone (spawn, retire). The next frame
    /// decides again.</summary>
    public void NoteBodiesChanged() => _sincePass = PassSeconds;

    /// <summary>Called by the hauling runtime before Concerned NPC is told the
    /// world has gone, so the figure is down before its lease ends.</summary>
    public void OnWorldUnloading()
    {
        try { _figure.Hide(); } catch (Exception) { /* the figure dies with the scene */ }
        _leases.ReleaseWorker();
        _leases.Forget();
        _environment.Forget();
        _figure.ResetForWorld();
        _arrivalPending = false;
        _planned = false;
        _faulted = false;
        _sincePass = PassSeconds;
        _last = "the world unloaded";
    }

    /// <summary>Plugin teardown.</summary>
    public void Dispose()
    {
        try { _figure.Hide(); } catch (Exception) { /* nothing more may escape a teardown */ }
        _leases.ReleaseWorker();
    }

    /// <summary>For <c>ct_haul status</c>.</summary>
    public string Describe()
    {
        string held = _leases.HoldsWorker ? "the arrival guard (a worker body was just placed)"
            : _leases.HoldsPresentation ? (_figure.IsShown ? "the figure at home" : "the figure's, not built")
            : "none";
        string text = "Gunnar at home: lease held " + held + "; " + _last + ".";
        if (_leases.LastRefusal.Length != 0) text += " Concerned NPC refused: " + _leases.LastRefusal + ".";
        if (_figure.LastRefusal.Length != 0) text += " The figure could not be built: " + _figure.LastRefusal + ".";
        string look = _figure.Describe();
        if (look.Length != 0) text += " Look: " + look + ".";
        return text;
    }

    // ------------------------------------------------------------------

    private void SettleArrival()
    {
        if (!_arrivalPending && !_leases.HoldsWorker) return;
        bool? present = GunnarWorkerPresence.From(_census(), _liveBody());
        if (GunnarArrival.MayRelease(present, Time.unscaledTime - _arrivalSince))
        {
            _leases.ReleaseWorker();
            _arrivalPending = false;
        }
    }

    private void Pass()
    {
        ZNet net = ZNet.instance;
        bool host = net != null && net.IsServer() && !net.IsDedicated();
        bool enabled = _companion.Enabled.Value;
        bool registered = _leases.Registered;
        bool worldLoaded = net != null && !NpcRoleRegistry.Shared.CurrentWorld.IsUnknown && Player.m_localPlayer != null;
        if (!GunnarBodyPlan.MaySurvey(registered, worldLoaded, host, enabled))
        {
            Down("not shown here: needs the host, Companions/Gunnar, his Concerned NPC registration and a world");
            return;
        }

        // The story is read before anything physical: a character who has not
        // met him pays for no anchor lookup and no probe.
        GunnarCompanionRecord? record = _companion.Current();
        QuestState? stage = record != null && record.ShouldPresentFigure(true) ? record.State : (QuestState?)null;
        if (stage == null)
        {
            Down("this character has not met him yet, or skipped the story");
            return;
        }

        int saved = GunnarArrival.SavedWorkers(_arrivalPending || _leases.HoldsWorker,
            GunnarWorkerPresence.From(_census(), _liveBody()));
        if (saved != 0)
        {
            // Settled before any physics: a body, or not knowing, means no figure.
            Down(saved > 0 ? "his worker body is in this world" : "his saved bodies are not accounted for yet");
            return;
        }

        bool resting = _resting();
        bool anchorKnown = _environment.TryAnchor(out CompanionAnchor anchor);
        Player? player = Player.m_localPlayer;
        float limit = _figure.IsShown ? KeepWithinMetres : ShowWithinMetres;
        bool near = anchorKnown && player != null &&
            (player.transform.position - ToVector(anchor.Position)).sqrMagnitude <= limit * limit;

        var facts = new GunnarBodyFacts(registered, worldLoaded, host, enabled, stage, saved, resting, anchorKnown, near);
        GunnarBodyKind kind = GunnarBodyPlan.Decide(facts);
        if (kind != GunnarBodyKind.Presentation)
        {
            Down(kind == GunnarBodyKind.Worker ? "his worker body is in this world"
                : saved < 0 ? "his saved bodies are not accounted for yet"
                : !resting ? "a job holds him"
                : !anchorKnown ? "neither a claimed bed nor the world start could be found"
                : "you are not near home");
            return;
        }

        if (!_planned || anchor.DiffersFrom(_plannedFor, PlacementRules.DefaultAnchorMoveTolerance))
        {
            IReadOnlyList<WorldPoint>? others = _environment.OtherMementos(anchor);
            _placement = GunnarFigurePlacement.Plan(anchor, _environment, others);
            _plannedFor = anchor;
            // A deferral is retried on the next pass rather than remembered.
            _planned = _placement.Found;
        }

        if (!_placement.Found)
        {
            Down("no clear spot beside home yet");
            return;
        }

        Vector3 spot = ToVector(_placement.Position);
        Vector3 look = player!.transform.position - spot;
        look.y = 0f;
        Quaternion facing = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look.normalized, Vector3.up) : Quaternion.identity;
        _figure.Show(Game.instance != null ? Game.instance.m_playerPrefab : null, spot, facing,
            Look, _companion.DisplayName.Value, talk: null);
        _last = _figure.IsShown ? "at home, resting" : "wanted at home, not shown";
    }

    private bool HasHeadroom(Vector3 spot)
    {
        if (_solidMask == 0) _solidMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece");
        return _solidMask != 0 && !Physics.CheckCapsule(spot + Vector3.up * 0.6f, spot + Vector3.up * 1.7f, 0.3f,
            _solidMask, QueryTriggerInteraction.Ignore);
    }

    private void Down(string why)
    {
        _figure.Hide();
        _last = why;
    }

    private static Vector3 ToVector(WorldPoint p) => new(p.X, p.Y, p.Z);
}
