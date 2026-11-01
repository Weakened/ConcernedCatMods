using System;
using System.Collections.Generic;
using BepInEx.Logging;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;
using TheConcernedCat.ConcernedTeamster.Domain.Workers;
using UnityEngine;
using TheConcernedCat.Diagnostics;

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
internal sealed partial class GunnarCollectionRuntime : MonoBehaviour, IGunnarDeathDropHost
{
    private readonly GunnarCollectionPort _port = new GunnarCollectionPort();
    private readonly GunnarDepositPort _deposit = new GunnarDepositPort();
    private readonly CollectionLimits _limits = CollectionLimits.Default.Validate();

    /// <summary>What he has taken and where it has gone, this session. The
    /// ledger never subtracts, so "was anything invented" is one comparison and
    /// a deposit that measured more than is there leaves the discrepancy
    /// visible instead of balancing itself.</summary>
    private readonly CollectionAccount _account = new CollectionAccount();

    /// <summary>The durable record of what he carried when he died (D16).
    /// </summary>
    private readonly GunnarDeathRecordStore _deaths = new GunnarDeathRecordStore();

    /// <summary>The tour's write-ahead plan (#379): one per Gunnar per world,
    /// in <c>teamster_gunnar_tour_&lt;world&gt;.tsv</c> beside the death record.
    /// Opened at the world-up edge, closed at the world-down edge, and
    /// revalidated only once his body, his record, the switch and the authority
    /// can all answer.</summary>
    private readonly GunnarTourPlan _tourPlan = new GunnarTourPlan();

    /// <summary>Whether this world's plan file has been opened, or found to have
    /// no world id to be named by.</summary>
    private bool _tourPlanOpened;
    private bool _tourPlanUnnamed;
    private Func<WorkerBodyStatus> _bodyStatus = () => WorkerBodyStatus.Unspecified;

    /// <summary>The chest the player chose, and the world load they chose it in.
    /// <b>There is no fallback.</b> With none designated the deposit refuses and
    /// says so; nothing here looks for the nearest one (`DECISIONS.md` D15).
    /// </summary>
    private Container? _destination;
    private string _destinationKey = string.Empty;
    private NpcWorldEpoch _destinationEpoch;
    private int _step;

    private CollectionLifecycle _lifecycle = null!;
    private TeamsterSettings _settings = null!;
    private ManualLogSource? _log;
    private Func<Humanoid?> _worker = null!;
    private Func<WorkerIdentityHold?> _identity = null!;
    private CollectionIdentityLease _lease = new CollectionIdentityLease();
    private Func<bool> _recordUnwritable = null!;
    private Func<bool> _recordUnreadable = null!;
    private Func<bool> _seamAvailable = null!;
    private Func<Humanoid?, bool> _releaseCartFromDyingBody = _ => false;

    private ContainerPermissionRuntime? _containers;

    private bool _faulted;
    private bool _ordered;
    private string _orderedSource = string.Empty;
    private string _orderedYield = string.Empty;

    /// <summary>Installs the runtime and its development console command. Always
    /// installed, like the hauling runtime: the switch decides what runs, not
    /// what exists, so turning the setting on does not need a different plugin
    /// state than turning it off.</summary>
    internal static GunnarCollectionRuntime Install(
        GameObject host,
        TeamsterSettings settings,
        Func<Humanoid?> worker,
        Func<WorkerIdentityHold?> identity,
        Func<bool> recordUnwritable,
        Func<bool> recordUnreadable,
        Func<bool> seamAvailable,
        Func<Humanoid?, bool> releaseCartFromDyingBody,
        Func<bool> cartHeld,
        Func<WorkerBodyStatus> bodyStatus,
        ContainerPermissionRuntime? containers,
        ManualLogSource log)
    {
        GunnarCollectionRuntime runtime = host.AddComponent<GunnarCollectionRuntime>();
        runtime.Initialize(settings, worker, identity, recordUnwritable, recordUnreadable, seamAvailable, log);
        runtime._containers = containers;
        runtime._bodyStatus = bodyStatus ?? throw new ArgumentNullException(nameof(bodyStatus));
        runtime._releaseCartFromDyingBody = releaseCartFromDyingBody
            ?? throw new ArgumentNullException(nameof(releaseCartFromDyingBody));
        runtime._cartHeld = cartHeld ?? throw new ArgumentNullException(nameof(cartHeld));

        // D16: the runtime that can have put anything in him is the one a death
        // reports to. Installed with it, removed with it.
        GunnarDeathDropPort.ErrorLog = message => log.LogError(message);
        GunnarDeathDropPort.MayListen = seamAvailable;
        GunnarDeathDropPort.Host = runtime;
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
        Func<WorkerIdentityHold?> identity,
        Func<bool> recordUnwritable,
        Func<bool> recordUnreadable,
        Func<bool> seamAvailable,
        ManualLogSource log)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _recordUnwritable = recordUnwritable ?? throw new ArgumentNullException(nameof(recordUnwritable));
        _recordUnreadable = recordUnreadable ?? throw new ArgumentNullException(nameof(recordUnreadable));
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
            _log?.LogError("Gunnar's collection runtime faulted and is now off for this session: " + SafeFailure.Describe(exception));
            try
            {
                // A faulted runtime is an order that has ended, not a world that
                // has gone: the sources it touched still exist.
                CancelTour("the collection runtime faulted");
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
        if (worldUp && Game.instance != null && Game.instance.IsShuttingDown())
        {
            // The game is going away while its singletons are still up for a
            // frame or two. Reported as the world going, so the down-edge fires
            // exactly once - and, because the lifecycle then says no world is
            // up, no order may start in that window either. Ending the order
            // without closing the window was a way to reach the mint: forget the
            // record, then order the same source again before the process died.
            worldUp = false;
        }

        if (_lifecycle.ObserveWorld(worldUp) == PickForget.World)
        {
            DropTourForWorld();
            _lease.Release();
            _ordered = false;
            _orderedSource = string.Empty;
            _log?.LogInfo(
                "Gunnar's collection: the world went away, so the pick in flight and every " +
                "unconfirmed source went with it.");
        }

        if (worldUp)
        {
            WatchTourPlan();
        }

        if (worldUp && _tour != null)
        {
            WatchTourFromFrame();
        }

        if (worldUp && _tour != null)
        {
            WorkAuthorityVerdict tourAuthority = Authority();
            if (tourAuthority != WorkAuthorityVerdict.Granted)
            {
                CancelTour(WorkAuthorityPolicy.Describe(tourAuthority));
            }
            else if (CartHeld())
            {
                CancelTour("a cart was assigned to him while he was on the tour");
            }
        }

        if (!worldUp || !_ordered)
        {
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

        if (!_lease.IsHeld)
        {
            EndOrder("Gunnar no longer holds this collection job");
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
                _lease.Release();
                _ordered = false;
                if (progress.Taken > 0 && _orderedYield.Length != 0)
                {
                    // Recorded under the name of the step that caused it, so a
                    // retry re-states what it already did and is told so.
                    _account.Record(
                        NextStep("pick"), _orderedYield, progress.Taken, StopResult.Took());
                }

                _log?.LogInfo(
                    "Gunnar's collection: he took " + progress.Taken + " from " + _orderedSource +
                    (progress.Detail.Length == 0 ? "." : " (" + progress.Detail + ")."));
                _orderedSource = string.Empty;
                _orderedYield = string.Empty;
                return;
            default:
                _lease.Release();
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
        try { _lifecycle.OrderEnded(); }
        finally { _lease.Release(); }
        if (had)
        {
            _log?.LogInfo("Gunnar's collection: the order ended - " + why);
        }
    }

    /// <summary>This process is tearing down. <b>The world verb</b>: nothing can
    /// pick afterwards, so dropping the record cannot mint.</summary>
    private void TearDown(string why)
    {
        if (ReferenceEquals(GunnarDeathDropPort.Host, this))
        {
            GunnarDeathDropPort.Host = null;
        }

        _ordered = false;
        _orderedSource = string.Empty;
        DropTourForWorld();
        if (_lifecycle != null)
        {
            try { _lifecycle.Shutdown(); }
            finally { _lease.Release(); }
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
    // #379: the tour's plan, opened at the world-up edge and revalidated
    // only once everything it is checked against can answer
    // ------------------------------------------------------------------

    /// <summary>Opens this world's plan once its id can be read, and revalidates
    /// a recovered plan once its body, its record, the switch and the authority
    /// can all answer. Nothing here moves material or builds anything.</summary>
    private void WatchTourPlan()
    {
        if (!_tourPlanOpened)
        {
            long? uid = CurrentWorldUid();
            if (uid == null)
            {
                if (!_tourPlanUnnamed)
                {
                    _tourPlanUnnamed = true;
                    _tourPlan.Open(null, "this world has no id to name a plan file by yet");
                }

                return;
            }

            _tourPlanOpened = true;
            string? path = GunnarTourPlanFiles.PathFor(
                Adapters.TripRecordingService.SidecarDirectory, uid, out string why);
            TourPlanStanding standing = _tourPlan.Open(path, why);
            if (standing != TourPlanStanding.Idle && standing != TourPlanStanding.Closed)
            {
                _log?.LogInfo("Gunnar's collection: " + _tourPlan.Describe() + ".");
            }
        }

        if (_tour != null)
        {
            return;
        }

        TourPlanStanding open = _tourPlan.Standing;
        TourPlanVerdict verdict;
        if (open == TourPlanStanding.Recovered)
        {
            if (TourPlanWaitingFor().Length != 0)
            {
                return;
            }

            verdict = _tourPlan.Revalidate(TourPlanFactsNow());
        }
        else if ((open == TourPlanStanding.Suspended || open == TourPlanStanding.Replanned)
            && BodyStatus() == WorkerBodyStatus.NotFound
            && TourPlanWaitingFor().Length == 0)
        {
            // An open plan with a load, and the census found no body: retire
            // force, or a body lost. Stopped for a person now, with that reason,
            // rather than at a start the tour gate refuses for another one.
            verdict = _tourPlan.RevalidateIfBodyGone(TourPlanFactsNow());
            if (verdict.Response == InterruptionResponse.Unspecified)
            {
                return;
            }
        }
        else
        {
            return;
        }

        if (verdict.Response == InterruptionResponse.NeedsAttention)
        {
            _log?.LogWarning("Gunnar's collection: " + verdict.Sentence);
        }
        else
        {
            _log?.LogInfo("Gunnar's collection: " + verdict.Sentence);
        }
    }

    /// <summary>Why a recovered plan is not revalidated yet, or empty.</summary>
    private string TourPlanWaitingFor()
    {
        if (CurrentEpoch().IsUnknown)
        {
            return "no world load is current yet";
        }

        return GunnarTourPlan.WaitingFor(
            BodyStatus(),
            !_recordUnwritable() && !_recordUnreadable(),
            OptedIn(),
            Authority() == WorkAuthorityVerdict.Granted);
    }

    /// <summary>What the plan is checked against, read now: the world load, the
    /// census's count of bodies, what he carries (null when it could not be
    /// read, or when there is no body to read), and the authority.</summary>
    private TourPlanFacts TourPlanFactsNow()
    {
        int bodies = GunnarTourPlan.BodiesFor(BodyStatus());
        return new TourPlanFacts(
            CurrentEpoch(),
            bodies < 0 ? 0 : bodies,
            CarriedForPlan(),
            Authority() == WorkAuthorityVerdict.Granted,
            Time.time);
    }

    /// <summary>What he carries, per collected material, from his own inventory;
    /// null when there is no trusted body or it could not be read. Never an
    /// empty list standing in for "unknown".</summary>
    private IReadOnlyList<KeyValuePair<string, int>>? CarriedForPlan()
    {
        try
        {
            Humanoid? worker = _worker();
            if (worker == null)
            {
                return null;
            }

            IReadOnlyList<KeyValuePair<string, int>> carried =
                _deposit.Carrying(worker, GunnarCollectionAllowlist.IsCollectedYield, out bool readable);
            return readable ? carried : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private WorkerBodyStatus BodyStatus()
    {
        try
        {
            return _bodyStatus();
        }
        catch (Exception)
        {
            return WorkerBodyStatus.Unspecified;
        }
    }

    /// <summary><c>ct_collect resolve</c>: the person's exit from a tour stopped
    /// for them. Records what his body holds now - nothing else, and nothing
    /// recreated.</summary>
    private string ResolveTourPlan()
    {
        bool busy = TourRunning || _ordered || _port.Phase == PickPhase.Gathering;
        bool resolved = _tourPlan.Resolve(TourPlanFactsNow(), busy, out string sentence);
        if (resolved)
        {
            _log?.LogInfo("Gunnar's collection: " + sentence);
        }

        return sentence;
    }

    // ------------------------------------------------------------------
    // D16: he died. Called from vanilla's own death path through the port.
    // ------------------------------------------------------------------

    /// <summary>The cart first, through the hauling runtime's D4 teardown, then
    /// the switches and the work-authority rule, read now rather than cached.
    /// A release is attempted for every genuine death whatever the switches say:
    /// letting a dead body's cart go is the hauling runtime's own behaviour, and
    /// running it now only moves it ahead of the drop.</summary>
    DeathDropFacts IGunnarDeathDropHost.Prepare(Humanoid body, bool genuineDeath, bool inventoryLoaded)
    {
        bool released = false;
        if (genuineDeath)
        {
            try
            {
                released = _releaseCartFromDyingBody(body);
            }
            catch (Exception exception)
            {
                _log?.LogError("Gunnar died and whether a cart still holds him could not be established: " +
                    SafeFailure.Describe(exception));
                released = false;
            }

            // #379: an intent before the drop, so its outcome can be concluded.
            // Its result is deliberately ignored - a plan that could not be
            // written must never stand between a dead body and vanilla's drop;
            // it stops for a person afterwards instead.
            try
            {
                _tourPlan.PrepareForDeath();
            }
            catch (Exception exception)
            {
                _log?.LogError("Gunnar died and his tour plan could not note it: " + SafeFailure.Describe(exception));
            }
        }

        return new DeathDropFacts(genuineDeath, released, OptedIn(), Authority(), _seamAvailable(), inventoryLoaded);
    }

    /// <summary>The ledger, the durable record and the log, in that order, each
    /// on its own so one failing cannot cost the others. An order or a tour in
    /// flight ends with the job verb: the source he was picking still exists.</summary>
    void IGunnarDeathDropHost.Died(DeathDropReport report, Vector3 where)
    {
        try
        {
            _account.RecordDeath(NextStep("death"), report);
        }
        catch (Exception exception)
        {
            _log?.LogError("Gunnar died and the session ledger could not record it: " + SafeFailure.Describe(exception));
        }

        try
        {
            if (_ordered)
            {
                EndOrder("Gunnar died");
            }
        }
        catch (Exception exception)
        {
            _log?.LogError("Gunnar died and his order could not be ended cleanly: " + SafeFailure.Describe(exception));
        }

        // The durable record first, so the plan below can say where it is.
        string rows = WorkerDeathRows.Serialize(DateTime.UtcNow, WorkerKey.Gunnar.Value, where.x, where.y, where.z, report);
        string kept;
        try
        {
            _deaths.TryAppend(CurrentWorldUid(), rows, out kept);
        }
        catch (Exception exception)
        {
            kept = "the death record could not be written, so this one is in the log only: " + SafeFailure.Brief(exception);
        }

        // #379: the plan, before the tour is cancelled. A full, measured drop of
        // exactly what the plan says he carried closes it as refunded; anything
        // else stops it for a person. Before CancelTour, because cancelling ends
        // the plan as a stopped tour - which would say his load is still on him.
        try
        {
            _tourPlan.Died(report.Held, report.Dropped, report.Unresolved, kept);
        }
        catch (Exception exception)
        {
            _log?.LogError("Gunnar died and his tour plan could not record it: " + SafeFailure.Describe(exception));
        }

        // Without this a tour would end only when the frame watcher noticed the
        // worker ticks had stopped, seconds later, for a vaguer reason: the body
        // is destroyed straight after vanilla's death callback, so no tick sees
        // it dead. Same job verb as a cancel: the sources still exist.
        try
        {
            if (_tour != null)
            {
                CancelTour("Gunnar died");
            }
        }
        catch (Exception exception)
        {
            _log?.LogError("Gunnar died and his tour could not be ended cleanly: " + SafeFailure.Describe(exception));
        }

        _log?.LogWarning(
            "Gunnar died: " + WorkerDeathRows.Summary(report) + ". He held " + report.Held + ", " + report.Dropped +
            " went on the ground and " + report.Unresolved + " are unresolved; " + kept + "." + Environment.NewLine + rows.TrimEnd('\n'));
    }

    private static long? CurrentWorldUid()
    {
        try
        {
            if (ZNet.instance == null)
            {
                return null;
            }

            long uid = ZNet.instance.GetWorldUID();
            return uid == 0L ? (long?)null : uid;
        }
        catch (Exception)
        {
            return null;
        }
    }

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
            case "destination":
                return Designate();
            case "deposit":
                return Deposit();
            case "area":
                return SetArea(args);
            case "start":
                return StartTour();
            case "stop":
                return StopTour();
            case "resolve":
                return ResolveTourPlan();
            case "chest":
                // #374: the containers a player has opened to him. A diagnostic;
                // the key while looking at a chest is the player-facing surface.
                return _containers == null
                    ? "Container permissions are unavailable this session."
                    : _containers.Console(args != null && args.Length > 1 ? args[1] : "");
            default:
                return "ct_collect: status, pick (the thing you are pointing at), cancel, "
                    + "destination (the chest you are pointing at), deposit, "
                    + "area [radius] (around where you stand), start (a tour of the area), stop, "
                    + "resolve (after you have looked at a tour stopped for you), "
                    + "chest [status|list|clear].";
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
            "; Gunnar " + (_recordUnwritable()
                ? "is here but could not write down what he is carrying, so he is handed nothing "
                    + "more; a reload brings him back with what was last saved"
                : _recordUnreadable()
                ? "is here but has not been able to read what he is carrying, so he is inert; the "
                    + "log says what could not be read"
                : worker == null ? "is not here" : worker.IsDead() ? "is down" : "is here") +
            "; " + (_ordered ? "picking " + _orderedSource : TourStatus()) +
            " (phase " + _port.Phase + ")" +
            "; chest " + (_destination == null ? "NOT CHOSEN - he never looks for the nearest one"
                : "chosen") +
            "; tour plan: " + _tourPlan.Describe() +
            (_tourPlan.Standing == TourPlanStanding.Recovered && TourPlanWaitingFor().Length != 0
                ? " - it is checked once nothing stands in the way, and now " + TourPlanWaitingFor()
                : "") +
            "; the ledger records " + _account.Ledger.Acquired.ToString(
                System.Globalization.CultureInfo.InvariantCulture) + " unit(s) taken this session" +
            (_account.Ledger.IsConserved ? "" : "; THE LEDGER DOES NOT BALANCE - see the log") + ".";
    }

    private string Cancel()
    {
        if (_tour != null)
        {
            return StopTour();
        }

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
            worldIsUp: _lifecycle.WorldIsUp,
            authority: Authority(),
            seamAvailable: _seamAvailable(),
            workerPresent: worker != null && !worker.IsDead(),
            workerRecordUnwritable: _recordUnwritable(),
            workerRecordUnreadable: _recordUnreadable(),
            pickInFlight: _ordered || TourRunning || _port.Phase == PickPhase.Gathering,
            pointedAtASource: source != null,
            sourceObjectName: sourceName,
            yieldItemPrefabName: yieldName,
            yieldUnits: units,
            reachMetres: _limits.PickupReachMetres,
            distanceMetres: FlatDistance(worker, source),
            interruptedTourOpen: _tourPlan.BlocksOrders);

        CollectionOrderRefusal refusal = CollectionOrderGate.Evaluate(request);
        if (refusal != CollectionOrderRefusal.None)
        {
            return CollectionOrderGate.Describe(refusal, request.Authority);
        }

        // Everything above is a decision over values a test can drive. This is
        // the one line that reaches the game, and the port re-reads every
        // precondition from its own frame before it touches anything.
        if (!_lease.TryAcquire(_identity()))
        {
            return "Gunnar is busy with another job, or his job authority is unavailable. Finish or cancel that job first.";
        }

        PickRefusal started;
        try
        {
            started = _port.Begin(
            source,
            worker,
            featureEnabled: request.FeatureEnabled,
            seamAvailable: request.SeamAvailable,
            expectedItemPrefab: CollectionOrderGate.PrefabNameOf(yieldName),
            expectedUnits: units,
            nowSeconds: Time.time);
        }
        catch
        {
            EndOrder("the pickup could not start");
            throw;
        }
        if (started != PickRefusal.None)
        {
            _lease.Release();
            return "He did not start: " + started + ".";
        }

        _ordered = true;
        _orderedSource = CollectionOrderGate.PrefabNameOf(sourceName);
        _orderedYield = CollectionOrderGate.PrefabNameOf(yieldName);
        return "He is picking up " + _orderedSource + ".";
    }

    /// <summary>Chooses the chest the player is looking at as the one Gunnar
    /// deposits into (#374/#381, `DECISIONS.md` D15).
    ///
    /// <b>The player points at it.</b> That is the whole designation, and it is
    /// deliberately the same convention as ordering a pick and marking a
    /// container: the explicit act is looking at the thing. Nothing in this
    /// product searches for a chest, ranks chests by distance, or falls back to
    /// one when none is chosen.
    ///
    /// The mark is checked here so a player finds out now rather than at the end
    /// of a tour - but it is checked <b>again</b> at the moment of every move,
    /// because between now and then they can change it.</summary>
    private string Designate()
    {
        if (TourRunning)
        {
            return "He is on a tour to the chest you chose. Stop it first (ct_collect stop) - the chest is " +
                "never changed under a tour that is walking to it.";
        }

        Player? player = Player.m_localPlayer;
        GameObject? hover = player != null ? player.GetHoverObject() : null;
        Container? chest = hover != null ? hover.GetComponentInParent<Container>() : null;
        if (chest == null)
        {
            return "Look at the chest he should use, then run this again. He never looks for the " +
                "nearest one.";
        }

        string key = GunnarDepositPort.KeyOf(chest);
        if (key.Length == 0)
        {
            return "That chest has no network record this client can name, so it cannot be chosen.";
        }

        NpcContainerUse allowed = Allowance(chest);
        if ((allowed & NpcContainerUse.Deposit) != NpcContainerUse.Deposit)
        {
            return DepositSentences.Describe(
                allowed == NpcContainerUse.Off
                    ? NpcContainerRefusal.NotEnabled
                    : NpcContainerRefusal.UseNotAllowed);
        }

        _destination = chest;
        _destinationKey = key;
        _destinationEpoch = CurrentEpoch();
        return "He will put things in that chest. The mark on it is re-checked every time he " +
            "actually moves something, so changing it takes effect at once.";
    }

    /// <summary>Moves everything he is carrying into the designated chest, one
    /// material at a time.
    ///
    /// <b>One material, one permit, one measured leg.</b> A deposit of stone and
    /// wood is two authorizations, because the player's permission is re-asked
    /// for each and a chest can be opened, warded or destroyed between them. A
    /// leg that comes back <see cref="DepositOutcome.Uncertain"/> stops the
    /// whole deposit where it stands: the legs that verifiably completed keep
    /// their measured credit, and nothing after the uncertain one is
    /// attempted.</summary>
    private string Deposit()
    {
        Humanoid? worker = _worker();
        // The cargo filter for an automatic collection deposit is what
        // collecting yields, read off the same allowlist that decides what he may
        // pick up. So widening one widens the other, in one edit, and a tool he
        // is ever issued is not swept into the chest with the stone.
        IReadOnlyList<KeyValuePair<string, int>> carried =
            _deposit.Carrying(worker, GunnarCollectionAllowlist.IsCollectedYield, out bool readable);

        var request = new DepositRequest(
            featureEnabled: OptedIn(),
            worldIsUp: _lifecycle.WorldIsUp,
            authority: Authority(),
            seamAvailable: _seamAvailable(),
            workerPresent: worker != null && !worker.IsDead(),
            workerRecordUnwritable: _recordUnwritable(),
            workerRecordUnreadable: _recordUnreadable(),
            transferInFlight: _ordered || TourRunning || _port.Phase == PickPhase.Gathering,
            carryingSomething: carried.Count > 0,
            destinationDesignated: _destination != null && _destinationKey.Length != 0,
            carriedIsReadable: readable,
            interruptedTourOpen: _tourPlan.BlocksOrders);

        DepositRefusal refusal = DepositOrderGate.Evaluate(request);
        if (refusal != DepositRefusal.None)
        {
            return DepositSentences.Describe(refusal, request.Authority);
        }

        NpcWorldEpoch world = CurrentEpoch();
        if (world.IsUnknown || !world.Matches(_destinationEpoch))
        {
            // The chest was chosen in a world load that has ended, so the key
            // names whatever inherited that id this time. Refused and forgotten,
            // never resolved.
            Forget("the world has been loaded again since you chose that chest");
            return "That chest was chosen in a different world load, so the name no longer means " +
                "the same object. Choose it again.";
        }

        if (!_lease.TryAcquire(_identity()))
        {
            return "Gunnar is busy with another job, or his job authority is unavailable. " +
                "Finish or cancel that job first.";
        }

        var said = new System.Text.StringBuilder();
        try
        {
            DepositLegs(worker, carried, said);
        }
        finally
        {
            _lease.Release();
        }

        return said.Length == 0 ? "He is not carrying anything." : said.ToString();
    }

    /// <summary>What a run of deposit legs came to, for a caller that has to
    /// decide what happens next.</summary>
    private readonly struct DepositTally
    {
        public DepositTally(int legs, int moved, bool uncertain, string refused, bool anyPartial = false, bool anyRefused = false)
        {
            Legs = legs;
            Moved = moved;
            Uncertain = uncertain;
            Refused = refused ?? string.Empty;
            AnyPartial = anyPartial;
            AnyRefused = anyRefused;
        }

        /// <summary>A leg moved some and the chest would take no more.</summary>
        public bool AnyPartial { get; }

        /// <summary>A leg moved nothing because the chest refused.</summary>
        public bool AnyRefused { get; }

        /// <summary>How many legs were attempted.</summary>
        public int Legs { get; }

        /// <summary>How many units were measured arriving, across every leg.
        /// </summary>
        public int Moved { get; }

        /// <summary>A leg came back uncertain, so the run stopped there.</summary>
        public bool Uncertain { get; }

        /// <summary>Why nothing could be attempted at all, or empty.</summary>
        public string Refused { get; }
    }

    /// <summary>One material, one permit, one measured leg, through the pinned
    /// deposit port, with every D15 gate re-asked by the port in the frame of the
    /// move. An uncertain leg stops the run where it stands.</summary>
    private DepositTally DepositLegs(
        Humanoid? worker, IReadOnlyList<KeyValuePair<string, int>> carried, System.Text.StringBuilder said)
    {
        NpcWorldEpoch world = CurrentEpoch();
        if (world.IsUnknown || !world.Matches(_destinationEpoch))
        {
            Forget("the world has been loaded again since you chose that chest");
            string why = "That chest was chosen in a different world load, so the name no longer means " +
                "the same object. Choose it again.";
            said.Append(why);
            return new DepositTally(0, 0, false, why);
        }

        int legs = 0;
        int moved = 0;
        bool anyPartial = false;
        bool anyRefused = false;
        for (int index = 0; index < carried.Count; index++)
        {
            KeyValuePair<string, int> material = carried[index];
            DepositResult result = _deposit.DepositOne(
                worker,
                _destination,
                _destinationKey,
                material.Key,
                material.Value,
                _destinationEpoch,
                world,
                Allowance,
                WorkerPosition,
                _limits.PickupReachMetres,
                out NpcContainerRefusal why);

            // Refused and uncertain both move nothing in the ledger: he
            // still has it, or nobody knows, and writing either down as
            // fact is how material is lost or invented.
            _account.RecordDeposit(NextStep("deposit"), result);
            legs++;
            anyPartial |= result.Outcome == DepositOutcome.PartlyDeposited;
            anyRefused |= result.Outcome == DepositOutcome.Refused;
            if (result.Outcome == DepositOutcome.Deposited || result.Outcome == DepositOutcome.PartlyDeposited)
            {
                for (int part = 0; part < result.Moved.Count; part++)
                {
                    moved += result.Moved[part].Value > 0 ? result.Moved[part].Value : 0;
                }
            }

            said.Append(legs == 1 ? string.Empty : " ")
                .Append(Describe(material.Key, result, why));

            if (result.Outcome == DepositOutcome.Uncertain)
            {
                said.Append(" Nothing after this was attempted, and nothing is put right " +
                    "automatically - a person decides.");
                return new DepositTally(legs, moved, true, string.Empty, anyPartial, anyRefused);
            }
        }

        return new DepositTally(legs, moved, false, string.Empty, anyPartial, anyRefused);
    }

    private static string Describe(string material, DepositResult result, NpcContainerRefusal why)
    {
        switch (result.Outcome)
        {
            case DepositOutcome.Deposited:
                return "All the " + material + " went in.";
            case DepositOutcome.PartlyDeposited:
                return Moved(result) + " " + material + " went in; he keeps the rest.";
            case DepositOutcome.Uncertain:
                return "Whether the " + material + " moved could not be established: " +
                    result.Detail;
            default:
                return why == NpcContainerRefusal.None
                    ? "No " + material + " moved: " + result.Detail
                    : "No " + material + " moved. " + DepositSentences.Describe(why);
        }
    }

    private static string Moved(DepositResult result)
    {
        int total = 0;
        for (int index = 0; index < result.Moved.Count; index++)
        {
            total += result.Moved[index].Value;
        }

        return total.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Forgets the designated chest. Never called to recover from a
    /// refusal - a chest that refused is still the chest the player chose - only
    /// when the name itself has stopped meaning anything.</summary>
    private void Forget(string why)
    {
        _destination = null;
        _destinationKey = string.Empty;
        _destinationEpoch = NpcWorldEpoch.Unknown;
        _log?.LogInfo("Gunnar's collection: the chosen chest was forgotten - " + why + ".");
    }

    /// <summary>What the player allowed for this container, through the same
    /// derivation that wrote the mark. A caller that assembled its own identity
    /// could read OFF for a chest the player had enabled, which looks exactly
    /// like the permission not working.</summary>
    private NpcContainerUse Allowance(Container? container) =>
        _containers == null ? NpcContainerUse.Off : _containers.Allowance(container);

    private Vector3? WorkerPosition()
    {
        Humanoid? worker = _worker();
        return worker == null ? (Vector3?)null : worker.transform.position;
    }

    private static NpcWorldEpoch CurrentEpoch()
    {
        try
        {
            return NpcRoleRegistry.Shared.CurrentWorld;
        }
        catch (Exception)
        {
            // An epoch nobody could read is unknown, which matches nothing.
            return NpcWorldEpoch.Unknown;
        }
    }

    /// <summary>A name for one movement, unique within this session. The ledger
    /// refuses a name reused for something else, which is what makes a retry
    /// answerable rather than applied twice.</summary>
    private string NextStep(string what) =>
        what + "-" + (++_step).ToString(System.Globalization.CultureInfo.InvariantCulture);

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
