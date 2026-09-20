using System;
using BepInEx.Logging;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.Workers;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>The call site of Gunnar's collection port (#381): the one place in
/// this product that orders a pick, and the one place its two lifecycle verbs
/// are reported from.
///
/// <b>Off by default, and that is now a statement about behaviour.</b> The port
/// used to have no caller at all, so "a player who has not opted in gets none of
/// it" was a statement about dead code. It is reachable now, behind
/// <c>Workers/GunnarCollectionEnabled</c> (off), Teamster's own master switch
/// (<c>General/Enabled</c>), the shared work-authority rule re-asked every frame
/// (opted in, a loaded world, the host, not dedicated, nobody else connected)
/// and the start-up capability probe. Every one of those refuses on its own, and
/// a refusal leaves every other Teamster feature running.
///
/// <b>One pick, explicitly ordered, within reach.</b> There is no survey, no
/// route, no autopilot and nothing that moves him: a player points at a loose
/// stone or a fallen branch he is already standing next to and orders it. The
/// automatic survey and the tour planner stay unwired; wiring them is their own
/// work with its own evidence.
///
/// <b>The two verbs are not spelled here.</b> This reports what happened - a
/// world went down, an order ended, the process is tearing down - to
/// <see cref="CollectionLifecycle"/>, which is game-free and decides which of
/// the port's two verbs that is. Routing a cancelled order to the world verb
/// re-opens the mint the port exists to close, so the decision lives somewhere a
/// test can drive rather than in a <c>MonoBehaviour</c> no test here can
/// load.</summary>
internal sealed class GunnarCollectionRuntime : MonoBehaviour
{
    private readonly GunnarCollectionPort _port = new GunnarCollectionPort();
    private readonly CollectionLimits _limits = CollectionLimits.Default.Validate();

    private CollectionLifecycle _lifecycle = null!;
    private TeamsterSettings _settings = null!;
    private ManualLogSource? _log;
    private Func<Humanoid?> _worker = null!;
    private Func<bool> _seamAvailable = null!;

    private bool _faulted;
    private bool _ordered;
    private string _orderedSource = string.Empty;

    /// <summary>Installs the runtime and its development console command. Always
    /// installed, like the hauling runtime: the switch decides what runs, not
    /// what exists, so turning the setting on does not need a different plugin
    /// state than turning it off.</summary>
    internal static GunnarCollectionRuntime Install(
        GameObject host,
        TeamsterSettings settings,
        Func<Humanoid?> worker,
        Func<bool> seamAvailable,
        ManualLogSource log)
    {
        GunnarCollectionRuntime runtime = host.AddComponent<GunnarCollectionRuntime>();
        runtime.Initialize(settings, worker, seamAvailable, log);
        var command = new CollectConsoleCommand(runtime);
        VanillaConsoleCommands.Register(command, log);
        log.LogInfo(VanillaConsoleCommands.Describe(new[] { command.Name }));
        return runtime;
    }

    /// <summary>The reverse of <see cref="Install"/>, for plugin teardown. The
    /// process is going away, so the world is: the world verb.</summary>
    internal static void Uninstall(GunnarCollectionRuntime? runtime)
    {
        if (runtime != null)
        {
            runtime.TearDown("the plugin is being removed");
            UnityEngine.Object.Destroy(runtime);
        }
    }

    private void Initialize(
        TeamsterSettings settings,
        Func<Humanoid?> worker,
        Func<bool> seamAvailable,
        ManualLogSource log)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        _seamAvailable = seamAvailable ?? throw new ArgumentNullException(nameof(seamAvailable));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _lifecycle = new CollectionLifecycle(_port);

        _log.LogInfo(
            "Gunnar's collection is " + (OptedIn() ? "ENABLED" : "off (the default)") +
            "; he picks up a loose stone or a fallen branch you point at and nothing else, " +
            "within " + _limits.PickupReachMetres.ToString("0.#") + " m, one at a time.");
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
            _log?.LogError("Gunnar's collection runtime faulted and is now off for this session: " + exception);
            try
            {
                // A faulted runtime is an order that has ended, not a world that
                // has gone: the sources it touched still exist.
                EndOrder("the collection runtime faulted");
            }
            catch
            {
                // The runtime is already off; nothing more may escape.
            }
        }
    }

    private void OnDestroy()
    {
        TearDown("the plugin is shutting down");
    }

    private void FrameTick()
    {
        bool worldUp = ZNetScene.instance != null && ZNet.instance != null && ZDOMan.instance != null;
        if (_lifecycle.ObserveWorld(worldUp) == PickForget.World)
        {
            _ordered = false;
            _orderedSource = string.Empty;
            _log?.LogInfo(
                "Gunnar's collection: the world went away, so the pick in flight and every " +
                "unconfirmed source went with it.");
        }

        if (!worldUp || !_ordered)
        {
            return;
        }

        if (Game.instance != null && Game.instance.IsShuttingDown())
        {
            TearDown("the game is shutting down");
            return;
        }

        // Authority is re-asked every frame, never once at the order: a peer
        // connecting mid-pick ends it.
        WorkAuthorityVerdict authority = Authority();
        if (authority != WorkAuthorityVerdict.Granted)
        {
            EndOrder(WorkAuthorityPolicy.Describe(authority));
            return;
        }

        PickProgress progress = _port.Poll(Time.time);
        switch (progress.Phase)
        {
            case PickPhase.Gathering:
                return;
            case PickPhase.Done:
                // Finished on its own: the port already released the pick and
                // handed its count back, so there is no verb to route here.
                // Calling either one would be reporting an event that did not
                // happen.
                _ordered = false;
                _log?.LogInfo(
                    "Gunnar's collection: he took " + progress.Taken + " from " + _orderedSource +
                    (progress.Detail.Length == 0 ? "." : " (" + progress.Detail + ")."));
                _orderedSource = string.Empty;
                return;
            default:
                _ordered = false;
                _log?.LogInfo(
                    "Gunnar's collection: nothing reached him from " + _orderedSource +
                    (progress.Detail.Length == 0 ? "." : " (" + progress.Detail + "). ") +
                    " Nothing is assumed about the source; the pick may well have happened.");
                _orderedSource = string.Empty;
                return;
        }
    }

    /// <summary>An order ended for a reason that is not the world going away.
    /// <b>The job verb</b>: the unconfirmed-source record stays, because those
    /// sources still exist and may still be mid-settle.</summary>
    private void EndOrder(string why)
    {
        bool had = _ordered;
        _ordered = false;
        _orderedSource = string.Empty;
        _lifecycle.OrderEnded();
        if (had)
        {
            _log?.LogInfo("Gunnar's collection: the order ended - " + why);
        }
    }

    /// <summary>This process is tearing down. <b>The world verb</b>: nothing can
    /// pick afterwards, so dropping the record cannot mint.</summary>
    private void TearDown(string why)
    {
        _ordered = false;
        _orderedSource = string.Empty;
        if (_lifecycle != null)
        {
            _lifecycle.Shutdown();
            _log?.LogInfo("Gunnar's collection: stood down because " + why + ".");
        }
    }

    private bool OptedIn()
    {
        try
        {
            return _settings.Enabled.Value && _settings.GunnarCollectionEnabled.Value;
        }
        catch
        {
            // An unreadable setting is not an opted-in one.
            return false;
        }
    }

    private WorkAuthorityVerdict Authority() =>
        WorkAuthorityPolicy.Evaluate(GunnarWorkAuthority.ReadWorldFacts(OptedIn()));

    // ------------------------------------------------------------------
    // ct_collect (a development aid, the way ct_haul is for #313)
    // ------------------------------------------------------------------

    internal string Execute(string[]? args)
    {
        string sub = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "status";
        switch (sub)
        {
            case "status":
                return Status();
            case "pick":
                return OrderOnePick();
            case "cancel":
                return Cancel();
            default:
                return "ct_collect: status, pick (the thing you are pointing at), cancel.";
        }
    }

    private string Status()
    {
        if (_faulted)
        {
            return "Gunnar's collection faulted earlier this session and is off; the log says why.";
        }

        WorkAuthorityVerdict authority = Authority();
        Humanoid? worker = _worker();
        return "Gunnar's collection: " + (OptedIn() ? "on" : "OFF (the default)") +
            "; " + WorkAuthorityPolicy.Describe(authority) +
            " Pickup seam " + (_seamAvailable() ? "available" : "UNAVAILABLE") +
            "; Gunnar " + (worker == null ? "is not here" : worker.IsDead() ? "is down" : "is here") +
            "; " + (_ordered ? "picking " + _orderedSource : "idle") +
            " (phase " + _port.Phase + ").";
    }

    private string Cancel()
    {
        if (!_ordered)
        {
            return "He is not picking anything up.";
        }

        string what = _orderedSource;
        EndOrder("you cancelled it");
        return "Cancelled. He keeps the record of " + what +
            " being picked until the world confirms it, so it cannot be picked twice.";
    }

    private string OrderOnePick()
    {
        Humanoid? worker = _worker();
        Player? player = Player.m_localPlayer;
        GameObject? hover = player != null ? player.GetHoverObject() : null;
        Pickable? source = hover != null ? hover.GetComponentInParent<Pickable>() : null;

        string sourceName = source != null ? source.gameObject.name : string.Empty;
        string yieldName = source != null && source.m_itemPrefab != null
            ? source.m_itemPrefab.name
            : string.Empty;
        int units = source != null ? source.m_amount : 0;

        var request = new CollectionOrderRequest(
            featureEnabled: OptedIn(),
            authority: Authority(),
            seamAvailable: _seamAvailable(),
            workerPresent: worker != null && !worker.IsDead(),
            pickInFlight: _ordered || _port.Phase == PickPhase.Gathering,
            pointedAtASource: source != null,
            sourceObjectName: sourceName,
            yieldItemPrefabName: yieldName,
            yieldUnits: units,
            reachMetres: _limits.PickupReachMetres,
            distanceMetres: FlatDistance(worker, source));

        CollectionOrderRefusal refusal = CollectionOrderGate.Evaluate(request);
        if (refusal != CollectionOrderRefusal.None)
        {
            return CollectionOrderGate.Describe(refusal, request.Authority);
        }

        // Everything above is a decision over values a test can drive. This is
        // the one line that reaches the game, and the port re-reads every
        // precondition from its own frame before it touches anything.
        PickRefusal started = _port.Begin(
            source,
            worker,
            featureEnabled: request.FeatureEnabled,
            seamAvailable: request.SeamAvailable,
            expectedItemPrefab: CollectionOrderGate.PrefabNameOf(yieldName),
            expectedUnits: units,
            nowSeconds: Time.time);
        if (started != PickRefusal.None)
        {
            return "He did not start: " + started + ".";
        }

        _ordered = true;
        _orderedSource = CollectionOrderGate.PrefabNameOf(sourceName);
        return "He is picking up " + _orderedSource + ".";
    }

    /// <summary>Horizontal distance between Gunnar and the source, or not a
    /// number when either is missing - which the gate refuses as unreadable
    /// rather than treating as zero.</summary>
    private static float FlatDistance(Humanoid? worker, Pickable? source)
    {
        if (worker == null || source == null)
        {
            return float.NaN;
        }

        Vector3 from = worker.transform.position;
        Vector3 to = source.transform.position;
        return new CollectionPoint(from.x, from.y, from.z)
            .FlatDistanceTo(new CollectionPoint(to.x, to.y, to.z));
    }
}
