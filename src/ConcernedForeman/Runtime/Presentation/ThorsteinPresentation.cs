using System;
using TheConcernedCat.ConcernedForeman.Domain.Npc;
using TheConcernedCat.ConcernedForeman.Domain.Settlement;
using TheConcernedCat.ConcernedForeman.Runtime.Collection;
using TheConcernedCat.ConcernedForeman.Runtime.Custody;
using TheConcernedCat.ConcernedForeman.Runtime.Settlement;
using TheConcernedCat.ConcernedForeman.Domain.Presentation;
using TheConcernedCat.ConcernedNPC.Body;
using TheConcernedCat.ConcernedNPC.Body.Dressing;
using TheConcernedCat.ConcernedNPC.Body.Keepsakes;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Presentation;

/// <summary>Thorstein at home: his presentation body while he rests, and the
/// hand-over to his worker body when an order needs one (#295 step 1, #298).
///
/// <b>One body at a time, enforced three ways.</b>
/// <list type="number">
/// <item><see cref="ThorsteinBodyPlan"/> never asks for the figure while a
/// saved worker body with his identity exists anywhere in the world, while a job
/// holds him, or while anything it needs is unknown.</item>
/// <item><see cref="ThorsteinBodies"/> holds Concerned NPC's leases, releasing
/// one before claiming the other, and the arbiter refuses a figure while the
/// worker lease is held, from any runtime in the process.</item>
/// <item>Here: the figure is built only after its lease is granted, and taken
/// down before anything that could release that lease. That covers every pass,
/// the hand-over (<see cref="TakeWorkerBody"/>), a world unload and the
/// plugin's own teardown.</item>
/// </list>
///
/// <b>What the figure is.</b> The game's own character prefab, extracted dark
/// by Concerned NPC (<see cref="NpcPresentationBody"/>): no network view, no
/// character, no AI, no collider, nothing saved, and every game script removed
/// before it was ever enabled. Never a live <c>Player</c> stripped afterwards.
/// It is dressed and stood near the respawn anchor by Concerned NPC's
/// <see cref="NpcFigureDresser"/>, from <see cref="ThorsteinLookPlan.Spec"/>.
/// It changes nothing in the world: no bed is claimed, nothing is
/// placed, and nobody else sees it.
///
/// <b>Where it runs.</b> On the host only (single player, or the host of a
/// game), because only there is the census of saved bodies the whole world.
/// Anywhere else it shows nothing.</summary>
internal sealed class ThorsteinPresentation
{
    private const float PassSeconds = 0.5f;
    private const float CensusSeconds = 30f;
    private const float FollowUpCountSeconds = 3f;
    private const float ShowWithinMetres = 48f;
    private const float KeepWithinMetres = 64f;
    private const float HomeMovedMetres = 2f;
    private const float StandOffMetres = 1.1f;
    private const float StepTolerance = 0.5f;

    private readonly ForemanSettlementSettings _settlementSettings;
    private readonly ThorsteinSettings _settings;
    private readonly ForemanNpcAdoption _npc;
    private readonly SettlementRuntime _settlement;
    private readonly Action<string> _log;
    private readonly ThorsteinBodies _bodies;

    private GameObject? _figure;
    private NpcFigureDresser? _dresser;
    private NpcWorkerDresser? _workerLook;
    private string _workerLookRefusal = string.Empty;
    private NpcKeepsake? _keepsake;
    private Vector3 _keepsakeFor;
    private string _keepsakeRefusal = string.Empty;
    private bool _keepsakeFailedThisWorld;
    private Vector3 _builtFor;
    private string _figureRefusal = string.Empty;
    private bool _buildFailedThisWorld;

    private int _census = -1;
    private bool _censusDue = true;
    private float _sinceCensus;
    private float _followUpIn;
    private float _sincePass;
    private bool _faulted;
    private bool _gateWasOpen;
    private int _blockMask;

    private ThorsteinBodyDecision _last = new ThorsteinBodyDecision(ThorsteinBodyKind.None, "not asked yet");

    internal ThorsteinPresentation(
        ForemanSettlementSettings settlementSettings,
        ThorsteinSettings settings,
        ForemanNpcAdoption npc,
        SettlementRuntime settlement,
        Action<string> log)
    {
        _settlementSettings = settlementSettings ?? throw new ArgumentNullException(nameof(settlementSettings));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _npc = npc ?? throw new ArgumentNullException(nameof(npc));
        _settlement = settlement ?? throw new ArgumentNullException(nameof(settlement));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _bodies = new ThorsteinBodies(npc.Registry, npc.Identity);
    }

    /// <summary>One pass every half second while a world is up. A pass that
    /// changes nothing builds nothing; the census of saved bodies runs on an
    /// event (spawn, despawn, death, world load) or every 30 seconds.</summary>
    internal void Tick(float deltaTime)
    {
        if (_faulted)
        {
            return;
        }

        _sincePass += deltaTime;
        _sinceCensus += deltaTime;
        if (_followUpIn > 0f)
        {
            _followUpIn -= deltaTime;
            if (_followUpIn <= 0f)
            {
                _censusDue = true;
            }
        }

        _dresser?.VerifyOnce();
        _workerLook?.VerifyOnce();
        if (_sincePass < PassSeconds)
        {
            return;
        }

        _sincePass = 0f;
        try
        {
            KeepsakePass();
            Pass();
        }
        catch (Exception exception)
        {
            // Latched, so a fault is one line and not one a second. The figure
            // and the carpenter's square go, and both leases are given up:
            // showing nothing is always safe.
            _faulted = true;
            TakeDown();
            RemoveKeepsake();
            _bodies.Reconcile(ThorsteinBodyKind.None);
            _log("Thorstein's presentation stopped after a fault and will not be shown again in this world: " +
                SafeFailure.Brief(exception));
        }
    }

    /// <summary>The hand-over an order makes before his worker body is created
    /// (<c>cf_worker spawn</c>). The figure is taken down first, then its lease
    /// is given up, then the worker lease is asked for. <paramref name="at"/> is
    /// where the figure stood, so the worker body can appear where he was.</summary>
    internal bool TakeWorkerBody(out Vector3? at, out string refusal)
    {
        at = _figure != null ? _figure.transform.position : (Vector3?)null;
        TakeDown();
        bool granted = _bodies.TryTakeWorker(out refusal);
        _censusDue = true;
        if (!granted && _npc.IsRegistered && !_npc.Modes.MayRetireBody)
        {
            // A job holds him, so only that job may claim a body - and for as
            // long as it holds him the arbiter refuses a figure to everybody.
            // There is no figure to hand over from and none can appear, so the
            // body may be created without this runtime's lease. The next pass
            // finds the body and takes the lease once he is resting again.
            refusal = string.Empty;
            return true;
        }

        return granted;
    }

    /// <summary>The worker body was not created, or has gone: give up its lease.
    /// The next pass decides whether he is shown at home again.</summary>
    internal void ReleaseWorkerBody()
    {
        _bodies.ReleaseWorker();
        NoteBodiesChanged();
    }

    /// <summary>A saved body may have appeared or gone (spawn, despawn, death).
    /// The next pass counts again before it decides anything.</summary>
    internal void NoteBodiesChanged()
    {
        _censusDue = true;
        _sincePass = PassSeconds;

        // And once more a few seconds later: a body that has just died or been
        // despawned can still be counted for a moment, and the safe answer to
        // that moment is "worker", which would otherwise last until the next
        // 30-second count.
        _followUpIn = FollowUpCountSeconds;
    }

    internal void OnWorldLoaded()
    {
        _census = -1;
        _censusDue = true;
        _buildFailedThisWorld = false;
        _figureRefusal = string.Empty;
        _faulted = false;
    }

    /// <summary>Called before Concerned NPC is told the world has gone, so the
    /// figure is down before its lease ends.</summary>
    internal void OnWorldUnloaded()
    {
        TakeDown();
        RemoveKeepsake();
        _keepsakeFailedThisWorld = false;
        ForgetWorkerLook();
        _bodies.Reconcile(ThorsteinBodyKind.None);
        _bodies.Forget();
        _census = -1;
        _censusDue = true;
        _followUpIn = 0f;
        _gateWasOpen = false;
    }

    /// <summary>#295 S6 (owner decision 2026-10-29, option (b)): a worker body
    /// with Thorstein's identity has just loaded or been created. Unless the
    /// player turned it off, his own hair and beard pieces are put on it,
    /// locally, by Concerned NPC's <see cref="NpcWorkerDresser"/>, which refuses
    /// anything that is not his saved worker body and falls back to the plain
    /// look when a piece will not attach or does not fit. No prefab, saved data
    /// or network object changes.</summary>
    internal void OnWorkerBodyLoaded(Humanoid? body)
    {
        ForgetWorkerLook();
        if (body == null || !_settings.WorkerLook.Value || !_npc.IsRegistered)
        {
            return;
        }

        try
        {
            _workerLook = NpcWorkerDresser.TryDress(
                _npc.Identity,
                body,
                ThorsteinLookPlan.Spec(_settings.Hair.Value, _settings.Beard.Value, null, null),
                "CF_ThorsteinWorkerHarvest",
                _log,
                out string refusal);
            _workerLookRefusal = refusal;
            _log(_workerLook != null
                ? "Thorstein's worker body wears his own hair and beard: " + _workerLook.Describe() + "."
                : "Thorstein's worker body keeps its plain look: " + refusal + ".");
        }
        catch (Exception exception)
        {
            _workerLook = null;
            _workerLookRefusal = "dressing it failed on this build";
            _log("Thorstein's worker body keeps its plain look: " + SafeFailure.Brief(exception));
        }
    }

    private void ForgetWorkerLook()
    {
        _workerLook?.TakeOff();
        _workerLook = null;
        _workerLookRefusal = string.Empty;
    }

    /// <summary>Plugin teardown.</summary>
    internal void Dispose()
    {
        RemoveKeepsake();
        ForgetWorkerLook();
        TakeDown();
        _bodies.Reconcile(ThorsteinBodyKind.None);
    }

    /// <summary>For <c>cf_worker status</c>.</summary>
    internal string Describe()
    {
        string held = _bodies.Held switch
        {
            ThorsteinBodyKind.Worker => "the worker body's",
            ThorsteinBodyKind.Presentation => _figure != null ? "the figure at home's" : "the figure's, not built",
            _ => "none",
        };

        string text = "Thorstein's body lease held: " + held + ". Wanted: " + _last + ".";
        if (_bodies.LastRefusal.Length != 0)
        {
            text += " Concerned NPC refused: " + _bodies.LastRefusal + ".";
        }

        if (_figureRefusal.Length != 0)
        {
            text += " The figure could not be built: " + _figureRefusal + ".";
        }

        if (_dresser != null)
        {
            text += " Look: " + _dresser.Describe() + ".";
        }

        if (_keepsake != null && _keepsake.IsPlaced)
        {
            text += " The carpenter's square lies near your bed (looks like \"" + _keepsake.Source + "\"" +
                (_keepsake.HoverObserved ? ", hovered" : string.Empty) + ").";
        }
        else if (_keepsakeRefusal.Length != 0)
        {
            text += " The carpenter's square could not be placed: " + _keepsakeRefusal + ".";
        }

        if (_workerLook != null && _workerLook.IsOnBody)
        {
            text += " Worker body look: " + _workerLook.Describe() + ".";
        }
        else if (_workerLookRefusal.Length != 0)
        {
            text += " Worker body look: plain (" + _workerLookRefusal + ").";
        }

        return text;
    }

    // ---------------------------------------------------------------------------------------------------

    private void Pass()
    {
        // Nothing is counted, read or held unless the figure could be shown at
        // all: a player who has not opted in pays for no world scan. Turning the
        // gate on forces a fresh count, so a stale zero can never show the
        // figure beside a body.
        bool gate = IsHost(ZNet.instance) && _settlementSettings.SettlementRuntimeEnabled.Value
            && _settings.ShowAtHome.Value && _npc.IsRegistered;
        if (!gate)
        {
            if (_gateWasOpen)
            {
                _gateWasOpen = false;
                _census = -1;
                _censusDue = true;
            }

            TakeDown();
            _bodies.Reconcile(ThorsteinBodyKind.None);
            _last = new ThorsteinBodyDecision(
                ThorsteinBodyKind.None,
                "not shown here: needs the host, Settlement/SettlementRuntimeEnabled, Thorstein/ShowAtHome " +
                "and his Concerned NPC registration");
            return;
        }

        if (!_gateWasOpen)
        {
            _gateWasOpen = true;
            _census = -1;
            _censusDue = true;
        }

        if (_censusDue || _sinceCensus >= CensusSeconds)
        {
            Count();
        }

        Vector3 home = default;
        bool homeKnown = TryGetHome(out home);
        Player? player = Player.m_localPlayer;
        bool near = homeKnown && player != null && IsNear(player.transform.position, home);

        ZNet net = ZNet.instance;
        bool host = IsHost(net);
        bool settlementOn = _settlementSettings.SettlementRuntimeEnabled.Value;

        // The roster is read only when it could change the answer, so a world
        // where the settlement is off never opens its records from here.
        bool recruited = host && settlementOn && _census == 0 && _settings.ShowAtHome.Value
            && _settlement.IsThorsteinRecruited();

        var facts = new ThorsteinBodyFacts(
            registered: _npc.IsRegistered,
            worldLoaded: ZNetScene.instance != null && !_npc.World.IsUnknown,
            isHost: host,
            settlementEnabled: settlementOn,
            presentationEnabled: _settings.ShowAtHome.Value,
            recruited: recruited,
            savedWorkerBodies: _census,
            resting: _npc.Modes.MayRetireBody,
            homeKnown: homeKnown,
            playerNearHome: near);

        _last = ThorsteinBodyPlan.Decide(facts);
        if (_last.Kind != ThorsteinBodyKind.Presentation)
        {
            // Down first, then the lease.
            TakeDown();
            _bodies.Reconcile(_last.Kind);
            return;
        }

        if (_bodies.Reconcile(ThorsteinBodyKind.Presentation) != ThorsteinBodyKind.Presentation)
        {
            TakeDown();
            return;
        }

        if (_figure != null && Vector3.Distance(_builtFor, home) > HomeMovedMetres)
        {
            // A new bed. He moves with it, as a new figure.
            TakeDown();
        }

        if (_figure == null && !_buildFailedThisWorld && _bodies.HoldsPresentation && player != null)
        {
            Build(home, player.transform.position);
        }
    }

    /// <summary>#295 S2: the worn carpenter's square that opens Tools of the
    /// Trade. Concerned NPC's shared keepsake: local, never saved or networked,
    /// examined with the game's own use key. It lies near the respawn anchor
    /// while this character's introduction has not begun and the settlement
    /// runtime is on, and is taken away once it has. Examining it does exactly
    /// what <c>cf_settle quest examine</c> does.</summary>
    private void KeepsakePass()
    {
        Player? player = Player.m_localPlayer;
        Vector3 home = default;
        bool homeKnown = player != null && TryGetHome(out home);
        bool near = homeKnown && IsWithin(player!.transform.position, home,
            _keepsake != null ? KeepWithinMetres : ShowWithinMetres);
        bool host = IsHost(ZNet.instance);
        bool settlementOn = _settlementSettings.SettlementRuntimeEnabled.Value;

        // The story is read only when everything else already says yes, so a
        // player who has not opted in, or is far from home, pays nothing.
        ThorsteinQuestStage? stage = host && settlementOn && near ? _settlement.ThorsteinQuestStage() : null;
        if (!ToolsOfTheTrade.ShowKeepsake(stage, host, settlementOn, homeKnown, near))
        {
            RemoveKeepsake();
            return;
        }

        if (_keepsake != null && Vector3.Distance(_keepsakeFor, home) > HomeMovedMetres)
        {
            RemoveKeepsake();
        }

        if (_keepsake != null || _keepsakeFailedThisWorld || player == null)
        {
            return;
        }

        Vector3 spot = ChooseSpot(home, player.transform.position, out Vector3 outward);
        spot += outward.normalized * 0.6f;
        _keepsake = NpcKeepsake.TryPlace(
            ToolsOfTheTrade.Keepsake,
            spot,
            Quaternion.Euler(0f, 35f, 0f),
            "CF_ThorsteinSquare",
            "CF_ThorsteinSquareHarvest",
            ExamineKeepsake,
            _log,
            out string refusal);
        _keepsakeFor = home;
        _keepsakeRefusal = refusal;
        if (_keepsake == null)
        {
            _keepsakeFailedThisWorld = true;
            _log("The carpenter's square could not be placed on this build: " + refusal +
                ". cf_settle quest examine still opens Thorstein's introduction.");
        }
    }

    private void ExamineKeepsake()
    {
        string result = _settlement.ExamineThorsteinKeepsake();
        _log(result.Replace("\n", " "));
        try
        {
            ShowOnScreen(result);
        }
        catch (Exception exception)
        {
            // A game build whose message screen changed shape loses the lines,
            // never the examine: the story is already recorded.
            _log("The introduction could not be shown on screen on this build: " + SafeFailure.Brief(exception));
        }
    }

    /// <summary>Kept out of line, so a message-screen signature that changed
    /// in a game update fails here, where the caller catches it, and not while
    /// the caller itself is compiled.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ShowOnScreen(string text)
    {
        MessageHud hud = MessageHud.instance;
        if (hud == null)
        {
            return;
        }

        foreach (string line in text.Split('\n'))
        {
            if (line.Trim().Length != 0)
            {
                hud.ShowMessage(MessageHud.MessageType.TopLeft, line.Trim());
            }
        }
    }

    private void RemoveKeepsake()
    {
        _keepsake?.Remove();
        _keepsake = null;
        _keepsakeRefusal = string.Empty;
    }

    private void Count()
    {
        _censusDue = false;
        _sinceCensus = 0f;
        ZNet net = ZNet.instance;
        if (!IsHost(net) || ZDOMan.instance == null)
        {
            _census = -1;
            return;
        }

        try
        {
            WorkerBodyCensus census = WorldCustodyObjects.Census(ForemanRole.Worker.Value);

            // A worker body with no identity stamped (an older build, or a stamp
            // that failed) might be his. Unknown, so nothing is shown.
            _census = census.Bodies.Count > 0 ? census.Bodies.Count : census.Unidentified > 0 ? -1 : 0;
        }
        catch (Exception)
        {
            _census = -1;
        }
    }

    private void Build(Vector3 home, Vector3 viewer)
    {
        GameObject? source = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Player") : null;
        NpcPresentationFigure figure = NpcPresentationBody.TryExtract(
            source, "Player", "CF_ThorsteinFigure", "CF_ThorsteinHarvest", string.Empty, _log);
        if (!figure.IsBuilt || figure.Root == null)
        {
            _buildFailedThisWorld = true;
            _figureRefusal = figure.Refusal;
            _log("Thorstein could not be shown at home on this build: " + figure.Refusal);
            return;
        }

        _figureRefusal = string.Empty;
        Vector3 spot = ChooseSpot(home, viewer, out Vector3 outward);
        Vector3 look = viewer - spot;
        look.y = 0f;
        if (look.sqrMagnitude < 0.01f)
        {
            look = outward;
        }

        _dresser = new NpcFigureDresser(
            figure,
            ThorsteinLookPlan.Spec(
                _settings.Hair.Value, _settings.Beard.Value, _settings.ChestGarment.Value, _settings.LegsGarment.Value),
            "CF_ThorsteinHarvest",
            _log);
        _dresser.StandAt(spot, Quaternion.LookRotation(look.normalized, Vector3.up));
        _figure = figure.Root;
        _builtFor = home;

        _dresser.Apply();
    }

    private void TakeDown()
    {
        _figure = null;

        // Dark at once, gone at the end of the frame: never drawn beside a
        // worker body created in the same frame. The dresser owns the figure it
        // was built for, and is the only thing that takes it down.
        _dresser?.TakeDown();
        _dresser = null;
    }

    /// <summary>A spot beside the respawn point: on the same floor, with nothing
    /// solid between the two, so he does not stand inside a wall. If no side
    /// passes, the respawn point itself, which the game already uses to stand a
    /// player up.</summary>
    private Vector3 ChooseSpot(Vector3 home, Vector3 viewer, out Vector3 outward)
    {
        if (_blockMask == 0)
        {
            _blockMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
        }

        Vector3 toward = viewer - home;
        toward.y = 0f;
        float start = toward.sqrMagnitude > 0.01f ? Mathf.Atan2(toward.x, toward.z) : 0f;
        ZoneSystem zones = ZoneSystem.instance;

        for (int step = 0; step < 8; step++)
        {
            float angle = start + (step * Mathf.PI / 4f);
            var direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            Vector3 candidate = home + (direction * StandOffMetres);
            if (zones == null || !zones.GetSolidHeight(candidate, out float floor, 1))
            {
                continue;
            }

            if (Mathf.Abs(floor - home.y) > StepTolerance)
            {
                continue;
            }

            candidate.y = floor;
            if (Physics.Linecast(home + Vector3.up, candidate + Vector3.up, _blockMask, QueryTriggerInteraction.Ignore))
            {
                continue;
            }

            outward = direction;
            return candidate;
        }

        outward = toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.forward;
        return home;
    }

    private bool IsNear(Vector3 player, Vector3 home)
    {
        ZoneSystem zones = ZoneSystem.instance;
        if (zones == null || !zones.IsZoneLoaded(home))
        {
            return false;
        }

        float limit = _figure != null ? KeepWithinMetres : ShowWithinMetres;
        return IsWithin(player, home, limit);
    }

    /// <summary>Within <paramref name="limit"/> of home, with home's zone
    /// loaded. The square uses its own limit, wider once it is placed, so a
    /// player standing at the edge does not see it built and removed every
    /// half second.</summary>
    private static bool IsWithin(Vector3 player, Vector3 home, float limit)
    {
        ZoneSystem zones = ZoneSystem.instance;
        if (zones == null || !zones.IsZoneLoaded(home))
        {
            return false;
        }

        return (player - home).sqrMagnitude <= limit * limit;
    }

    private static bool TryGetHome(out Vector3 home)
    {
        home = default;
        if (!RespawnAnchorSource.TryGetLatest(out RespawnAnchor anchor, out _))
        {
            return false;
        }

        home = SitePoints.ToVector3(anchor.Point);
        return true;
    }

    private static bool IsHost(ZNet net) => net != null && net.IsServer() && !net.IsDedicated();
}
