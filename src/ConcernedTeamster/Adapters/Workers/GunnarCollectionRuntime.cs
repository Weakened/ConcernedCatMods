using System;
using System.Collections.Generic;
using BepInEx.Logging;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
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
internal sealed class GunnarCollectionRuntime : MonoBehaviour
{
    private readonly GunnarCollectionPort _port = new GunnarCollectionPort();
    private readonly GunnarDepositPort _deposit = new GunnarDepositPort();
    private readonly CollectionLimits _limits = CollectionLimits.Default.Validate();

    /// <summary>What he has taken and where it has gone, this session. The
    /// ledger never subtracts, so "was anything invented" is one comparison and
    /// a deposit that measured more than is there leaves the discrepancy
    /// visible instead of balancing itself.</summary>
    private readonly CollectionAccount _account = new CollectionAccount();

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
        ContainerPermissionRuntime? containers,
        ManualLogSource log)
    {
        GunnarCollectionRuntime runtime = host.AddComponent<GunnarCollectionRuntime>();
        runtime.Initialize(settings, worker, identity, recordUnwritable, recordUnreadable, seamAvailable, log);
        runtime._containers = containers;
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
            _lease.Release();
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
        _ordered = false;
        _orderedSource = string.Empty;
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
            case "chest":
                // #374: the containers a player has opened to him. A diagnostic;
                // the key while looking at a chest is the player-facing surface.
                return _containers == null
                    ? "Container permissions are unavailable this session."
                    : _containers.Console(args != null && args.Length > 1 ? args[1] : "");
            default:
                return "ct_collect: status, pick (the thing you are pointing at), cancel, "
                    + "destination (the chest you are pointing at), deposit, "
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
            "; " + (_ordered ? "picking " + _orderedSource : "idle") +
            " (phase " + _port.Phase + ")" +
            "; chest " + (_destination == null ? "NOT CHOSEN - he never looks for the nearest one"
                : "chosen") +
            "; the ledger records " + _account.Ledger.Acquired.ToString(
                System.Globalization.CultureInfo.InvariantCulture) + " unit(s) taken this session" +
            (_account.Ledger.IsConserved ? "" : "; THE LEDGER DOES NOT BALANCE - see the log") + ".";
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
            worldIsUp: _lifecycle.WorldIsUp,
            authority: Authority(),
            seamAvailable: _seamAvailable(),
            workerPresent: worker != null && !worker.IsDead(),
            workerRecordUnwritable: _recordUnwritable(),
            workerRecordUnreadable: _recordUnreadable(),
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
            _deposit.Carrying(worker, GunnarCollectionAllowlist.IsCollectedYield);

        var request = new DepositRequest(
            featureEnabled: OptedIn(),
            worldIsUp: _lifecycle.WorldIsUp,
            authority: Authority(),
            seamAvailable: _seamAvailable(),
            workerPresent: worker != null && !worker.IsDead(),
            workerRecordUnwritable: _recordUnwritable(),
            workerRecordUnreadable: _recordUnreadable(),
            transferInFlight: _ordered || _port.Phase == PickPhase.Gathering,
            carryingSomething: carried.Count > 0,
            destinationDesignated: _destination != null && _destinationKey.Length != 0);

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
        int legs = 0;
        try
        {
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

                said.Append(legs == 1 ? string.Empty : " ")
                    .Append(Describe(material.Key, result, why));

                if (result.Outcome == DepositOutcome.Uncertain)
                {
                    said.Append(" Nothing after this was attempted, and nothing is put right " +
                        "automatically - a person decides.");
                    break;
                }
            }
        }
        finally
        {
            _lease.Release();
        }

        return said.Length == 0 ? "He is not carrying anything." : said.ToString();
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
