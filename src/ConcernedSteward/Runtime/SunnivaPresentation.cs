using System;
using TheConcernedCat.ConcernedNPC.Body;
using TheConcernedCat.ConcernedNPC.Body.Dressing;
using TheConcernedCat.ConcernedSteward.Domain.Appearance;
using TheConcernedCat.ConcernedSteward.Domain.Npc;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedSteward.Runtime;

/// <summary>What the runtime knows that the figure needs, read each pass.
/// </summary>
internal readonly struct SunnivaFigureInputs
{
    internal SunnivaFigureInputs(int savedWorkerBodies, bool settled, bool resting, Vector3? home)
    {
        SavedWorkerBodies = savedWorkerBodies;
        Settled = settled;
        Resting = resting;
        Home = home;
    }

    /// <summary>Saved bodies with her identity; negative when unknown.</summary>
    public int SavedWorkerBodies { get; }

    public bool Settled { get; }

    public bool Resting { get; }

    /// <summary>Her home post: the supply chest, else the settlement's centre.
    /// </summary>
    public Vector3? Home { get; }
}

/// <summary>Sunniva at home: a player-style figure while she rests with no
/// worker body in the world (#382; owner decision 2026-10-27), on Concerned
/// NPC's <c>ForWorkerWithPresentation</c> seam.
///
/// <b>One body at a time, enforced three ways.</b>
/// <list type="number">
/// <item><see cref="SunnivaBodyPlan"/> never asks for the figure while a saved
/// <c>CS_Steward</c> body with her identity exists anywhere in the world, while
/// a round or its record is open, or while anything it needs is unknown.</item>
/// <item>Concerned NPC's arbiter refuses the figure's lease while her worker
/// lease is held, and the worker lease while the figure's is held.</item>
/// <item>Here: the figure is built only after its lease is granted, and taken
/// down before that lease is given up - every pass, the hand-over to her worker
/// body (<see cref="YieldToWorker"/>), a world unload and plugin teardown.</item>
/// </list>
///
/// <b>What the figure is.</b> The game's own character prefab, extracted dark
/// by Concerned NPC (<see cref="NpcPresentationBody"/>): no network view, no
/// character, no AI, no collider, nothing saved. Never a live <c>Player</c>
/// stripped afterwards. Dressed and stood beside her home post by Concerned
/// NPC's <see cref="NpcFigureDresser"/> from <see cref="SunnivaLookPlan.Spec"/>.
/// It changes nothing in the world and nobody else sees it.
///
/// <b>Where it runs.</b> On the host only, because only there is the count of
/// saved bodies the whole world's.</summary>
internal sealed class SunnivaPresentation
{
    internal const string FigureName = "CS_SunnivaFigure";
    internal const string HolderName = "CS_SunnivaHarvest";
    internal const string WorkerHolderName = "CS_SunnivaWorkerHarvest";

    private const float PassSeconds = 0.5f;
    private const float ShowWithinMetres = 48f;
    private const float KeepWithinMetres = 64f;
    private const float HomeMovedMetres = 2f;
    private const float StandOffMetres = 1.6f;
    private const float StepTolerance = 0.5f;

    private readonly StewardSettings _settings;
    private readonly StewardNpcAdoption _npc;
    private readonly Func<SunnivaFigureInputs> _inputs;
    private readonly Action<string> _log;
    private readonly SunnivaFigureLease _lease;

    private GameObject? _figure;
    private NpcFigureDresser? _dresser;
    private NpcWorkerDresser? _workerLook;
    private string _workerLookRefusal = string.Empty;
    private Humanoid? _workerLookOn;
    private Vector3 _builtFor;
    private string _figureRefusal = string.Empty;
    private bool _buildFailedThisWorld;
    private float _sincePass = PassSeconds;
    private bool _faulted;
    private int _blockMask;

    private SunnivaBodyDecision _last = new SunnivaBodyDecision(false, "not asked yet");

    internal SunnivaPresentation(
        StewardSettings settings,
        StewardNpcAdoption npc,
        Func<SunnivaFigureInputs> inputs,
        Action<string> log)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _npc = npc ?? throw new ArgumentNullException(nameof(npc));
        _inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _lease = new SunnivaFigureLease(npc.Registry, npc.Identity);
    }

    /// <summary>A figure stands now.</summary>
    internal bool IsShown => _figure != null;

    /// <summary>One pass every half second while a world is up.</summary>
    internal void Tick(float deltaTime)
    {
        if (_faulted)
        {
            return;
        }

        _sincePass += deltaTime;
        _dresser?.VerifyOnce();
        _workerLook?.VerifyOnce();
        if (_sincePass < PassSeconds)
        {
            return;
        }

        _sincePass = 0f;
        try
        {
            Pass();
        }
        catch (Exception exception)
        {
            // Latched: the figure goes and its lease is given up. Showing
            // nothing is always safe.
            _faulted = true;
            YieldToWorker();
            _log("Sunniva's figure at home stopped after a fault and will not be shown again in this world: " +
                SafeFailure.Brief(exception));
        }
    }

    /// <summary>Her worker body exists, or is about to: the figure is taken
    /// down first, then its lease is given up, so the worker lease can be held.
    /// Called before every worker claim and before every spawn. Idempotent.
    /// </summary>
    internal void YieldToWorker()
    {
        TakeDown();
        _lease.Release();
    }

    internal void OnWorldLoaded()
    {
        _buildFailedThisWorld = false;
        _figureRefusal = string.Empty;
        _faulted = false;
        _sincePass = PassSeconds;
    }

    /// <summary>Called before Concerned NPC is told the world has gone, so the
    /// figure is down before its lease ends. Also the plugin's teardown.</summary>
    internal void OnWorldUnloaded()
    {
        OnWorkerBodyGone();
        TakeDown();
        _lease.Release();
        _lease.Forget();
    }

    /// <summary>Owner decision 2026-10-29: her worker body has just bound.
    /// Unless the player turned it off, her own hair is put on it, locally, by
    /// Concerned NPC's <see cref="NpcWorkerDresser"/>, which refuses anything
    /// that is not her saved worker body and falls back to the plain look when
    /// a piece will not attach or does not fit. No prefab, saved data or
    /// network object changes. Idempotent for the same body.</summary>
    internal void OnWorkerBodyLoaded(Humanoid? body)
    {
        if (body != null && ReferenceEquals(body, _workerLookOn))
        {
            return;
        }

        OnWorkerBodyGone();
        if (body == null || !_settings.WorkerLook.Value || !_npc.IsRegistered)
        {
            return;
        }

        _workerLookOn = body;
        try
        {
            _workerLook = NpcWorkerDresser.TryDress(
                _npc.Identity,
                body,
                SunnivaLookPlan.WorkerSpec(_settings.HairStyle.Value),
                WorkerHolderName,
                _log,
                out string refusal);
            _workerLookRefusal = refusal;
            _log(_workerLook != null
                ? "Sunniva's worker body wears her own hair: " + _workerLook.Describe() + "."
                : "Sunniva's worker body keeps its plain look: " + refusal + ".");
        }
        catch (Exception exception)
        {
            _workerLook = null;
            _workerLookRefusal = "dressing it failed on this build";
            _log("Sunniva's worker body keeps its plain look: " + SafeFailure.Brief(exception));
        }
    }

    /// <summary>Her worker body is gone or unbound: whatever was put on it is
    /// taken off. Idempotent and cheap when nothing is on.</summary>
    internal void OnWorkerBodyGone()
    {
        if (_workerLook == null && _workerLookOn == null)
        {
            return;
        }

        _workerLook?.TakeOff();
        _workerLook = null;
        _workerLookOn = null;
        _workerLookRefusal = string.Empty;
    }

    /// <summary>For <c>cs_steward status</c>.</summary>
    internal string Describe()
    {
        string text = "Figure at home: " + (_figure != null ? "shown" : "not shown") + ". Wanted: " + _last + ".";
        if (_lease.LastRefusal.Length != 0)
        {
            text += " Concerned NPC refused: " + _lease.LastRefusal + ".";
        }

        if (_figureRefusal.Length != 0)
        {
            text += " The figure could not be built: " + _figureRefusal + ".";
        }

        if (_dresser != null)
        {
            text += " Look: " + _dresser.Describe() + ".";
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
        ZNet net = ZNet.instance;
        bool host = net != null && net.IsServer() && !net.IsDedicated();
        bool runtimeOn = _settings.RuntimeEnabled.Value;
        bool show = _settings.ShowAtHome.Value;

        // Nothing is read unless the figure could be shown at all.
        SunnivaFigureInputs inputs = host && runtimeOn && show && _npc.IsRegistered
            ? _inputs()
            : new SunnivaFigureInputs(-1, false, false, null);

        Vector3 home = inputs.Home ?? default;
        Player? player = Player.m_localPlayer;
        bool near = inputs.Home != null && player != null && IsNear(player.transform.position, home);

        var facts = new SunnivaBodyFacts(
            registered: _npc.IsRegistered,
            worldLoaded: ZNetScene.instance != null && !_npc.World.IsUnknown,
            isHost: host,
            runtimeEnabled: runtimeOn,
            showAtHome: show,
            settled: inputs.Settled,
            savedWorkerBodies: inputs.SavedWorkerBodies,
            resting: inputs.Resting,
            homeKnown: inputs.Home != null,
            playerNearHome: near);

        _last = SunnivaBodyPlan.Decide(facts);
        if (!_last.ShowFigure)
        {
            // Down first, then the lease.
            YieldToWorker();
            return;
        }

        if (!_lease.TryClaim())
        {
            TakeDown();
            return;
        }

        if (_figure != null && Vector3.Distance(_builtFor, home) > HomeMovedMetres)
        {
            // Her home post moved: she moves with it, as a new figure.
            TakeDown();
        }

        if (_figure == null && !_buildFailedThisWorld && _lease.Holds && player != null)
        {
            Build(home, player.transform.position);
        }
    }

    private void Build(Vector3 home, Vector3 viewer)
    {
        GameObject? source = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Player") : null;
        NpcPresentationFigure figure = NpcPresentationBody.TryExtract(
            source, "Player", FigureName, HolderName, string.Empty, _log);
        if (!figure.IsBuilt || figure.Root == null)
        {
            _buildFailedThisWorld = true;
            _figureRefusal = figure.Refusal;
            _log("Sunniva could not be shown at home on this build: " + figure.Refusal);
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
            SunnivaLookPlan.Spec(_settings.HairStyle.Value, _settings.Garment.Value, _settings.GarmentLegs.Value),
            HolderName,
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
        // worker body created in the same frame.
        _dresser?.TakeDown();
        _dresser = null;
    }

    /// <summary>A spot beside her home post on the same floor, with nothing
    /// solid between the two; if no side passes, the post itself.</summary>
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
        return (player - home).sqrMagnitude <= limit * limit;
    }
}
