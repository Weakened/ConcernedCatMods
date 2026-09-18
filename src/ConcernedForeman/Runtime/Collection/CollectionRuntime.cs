using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TheConcernedCat.ConcernedForeman.Domain.Settlement;
using TheConcernedCat.ConcernedForeman.Runtime.Settlement;
using TheConcernedCat.ConcernedForeman.Runtime.Work;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Collection;

/// <summary>Drives Thorstein's collection (#315) inside the running game.
///
/// <b>Where things run.</b> Everything that mutates the world (a pick, a take,
/// a deposit) runs inside the worker actor's own tick, through
/// <see cref="ForemanWorkerAI.WorkTick"/>, so it is part of the worker's
/// <c>FixedUpdate</c> step and never interleaves with a player's auto-pickup
/// (PICKUP_SEAM_AUDIT.md §7.3). The plugin's <c>Update</c> only notices worlds
/// coming and going, finds the body, and supervises: when the worker's tick
/// stops arriving, the order stops rather than claiming to work.
///
/// <b>Faults.</b> Any exception from the loop or its adapters is caught here,
/// latched, logged once, and the worker is stopped. A latched runtime does no
/// more work this session; the order keeps its last recorded state.
///
/// <b>What it owns.</b> One <see cref="ActorModeOwner"/> for Thorstein's
/// identity, for the life of the plugin, and per world load: the load's epoch,
/// the source reservation book, the source directory, request ids and the loop.
/// A reload drops all of those, as the contract says (CONTRACTS.md §8).</summary>
internal sealed class CollectionRuntime
{
    private const float BodyLookupSeconds = 0.5f;
    private const float BodyRecheckSeconds = 5f;
    private const float PreviewValidSeconds = 600f;
    private const int PreviewSurveySteps = 2000;
    private const string WorkerName = "thorstein";

    private readonly ForemanSettlementSettings _settlement;
    private readonly CollectionSettings _settings;
    private readonly Action<string> _log;
    private readonly ICustodyRuntime? _custody;
    private readonly ICooperativeDelivery? _cooperation;
    private readonly Func<Guid>? _sharedEpoch;
    private readonly SettlementDiskReader _reader = new SettlementDiskReader();
    private readonly WorkScopeBuilder _scopes;
    private readonly IDesignationSite _site = new WorldDesignationSite();
    private readonly Action<ForemanWorkerAI, float> _workTick;

    private ZNetScene? _scene;
    private Guid _ownEpoch;
    private WorldState? _world;
    private ForemanWorkerAI? _body;
    private BodyLookupOutcome _bodyOutcome = BodyLookupOutcome.Missing;
    private float _bodyLookedAt = float.NegativeInfinity;
    private PreviewToken? _preview;
    private float _adoptionAskedAt = float.NegativeInfinity;
    private bool _faulted;
    private string _fault = string.Empty;

    /// <param name="custody">Agent D's custody runtime. Without one, every order
    /// is refused: there is no honest way to move material without a record.
    /// </param>
    /// <param name="cooperation">Agent E's cooperative delivery, if present.
    /// </param>
    /// <param name="sharedEpoch">The world-load epoch custody resolves
    /// containers against, when custody owns it. Collection must use the same
    /// one; without it this runtime mints its own per world open.</param>
    internal CollectionRuntime(
        ForemanSettlementSettings settlement,
        CollectionSettings settings,
        Action<string> log,
        ICustodyRuntime? custody = null,
        ICooperativeDelivery? cooperation = null,
        Func<Guid>? sharedEpoch = null)
    {
        _settlement = settlement ?? throw new ArgumentNullException(nameof(settlement));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _log = log ?? (_ => { });
        _custody = custody;
        _cooperation = cooperation;
        _sharedEpoch = sharedEpoch;
        _scopes = new WorkScopeBuilder(_reader);
        _workTick = OnWorkerTick;
        Motion = new ForemanWorkerMotion(Modes, () => _body);
    }

    /// <summary>Thorstein's single actor-mode owner (ARCH-02).</summary>
    internal ActorModeOwner Modes { get; } = new ActorModeOwner(WorkerKey.Thorstein);

    /// <summary>Walking Thorstein, for agent E's cooperative delivery.</summary>
    internal IWorkerMotion Motion { get; }

    internal Guid WorldLoadEpoch => _sharedEpoch != null ? _sharedEpoch() : _ownEpoch;

    internal SoloCollectionLoop? Loop => _world?.Loop;

    // --- Lifecycle ---------------------------------------------------------------------------------------

    /// <summary>Called from the plugin's <c>Update</c>.</summary>
    internal void Update()
    {
        if (_faulted)
        {
            return;
        }

        try
        {
            EnsureWorld();
            if (_world == null)
            {
                return;
            }

            RefreshBody();
            AdoptRecoveredOrder();
            _world.Loop?.Supervise(Time.time);
        }
        catch (Exception exception)
        {
            Latch("update", exception);
        }
    }

    /// <summary>Called when the world goes away.</summary>
    internal void OnWorldUnloaded()
    {
        try
        {
            DropWorld();
        }
        catch (Exception exception)
        {
            _log("Collection: could not tidy up after the world closed: " + exception.GetType().Name);
        }
    }

    private void EnsureWorld()
    {
        ZNetScene scene = ZNetScene.instance;
        if (scene == null)
        {
            if (_world != null)
            {
                DropWorld();
            }

            return;
        }

        if (_world != null && ReferenceEquals(scene, _scene) && _world.Epoch == WorldLoadEpoch)
        {
            return;
        }

        if (_world != null)
        {
            DropWorld();
        }

        _scene = scene;
        _ownEpoch = Guid.NewGuid();
        Guid epoch = WorldLoadEpoch;
        if (epoch != Guid.Empty)
        {
            _world = new WorldState(this, epoch);
        }
    }

    private void DropWorld()
    {
        _world?.Loop?.Abandon();
        Detach(_body);
        _body = null;
        _bodyOutcome = BodyLookupOutcome.Missing;
        _bodyLookedAt = float.NegativeInfinity;
        _world = null;
        _scene = null;
        _preview = null;
        _reader.Forget();
    }

    private void RefreshBody()
    {
        if (_body != null && !_body)
        {
            // Destroyed: Unity's null.
            _body = null;
        }

        float now = Time.time;
        float wait = _body != null ? BodyRecheckSeconds : BodyLookupSeconds;
        if (now - _bodyLookedAt < wait && now >= _bodyLookedAt)
        {
            return;
        }

        _bodyLookedAt = now;
        _bodyOutcome = ForemanWorkerBodies.Find(WorkerKey.Thorstein, out ForemanWorkerAI? found);
        ForemanWorkerAI? chosen = _bodyOutcome == BodyLookupOutcome.Found ? found : null;
        if (!ReferenceEquals(chosen, _body))
        {
            Detach(_body);
            _body = chosen;
            Attach(_body);
        }
    }

    /// <summary>C2 (B1): after a reload the record still holds the worker's
    /// order; the loop is new and holds none. Ask custody for it until it
    /// answers, so the player can see it, rebind it or cancel it — without
    /// which neither the material in his body nor the body itself can be
    /// released. Asked at most once a second, and only while no order is held.
    /// </summary>
    private void AdoptRecoveredOrder()
    {
        SoloCollectionLoop? loop = _world?.Loop;
        if (loop == null || loop.HasActiveOrder)
        {
            return;
        }

        float now = Time.time;
        if (now - _adoptionAskedAt < 1f && now >= _adoptionAskedAt)
        {
            return;
        }

        _adoptionAskedAt = now;
        if (loop.AdoptRecovered(now))
        {
            _log(
                "Collection: took up order " + loop.Order!.Order.Value + " again from the settlement record, stopped" +
                (loop.NeedsRebind ? " and waiting for its work area and chest to be chosen again (cf_collect rebind)." : "."));
        }
    }

    private void Attach(ForemanWorkerAI? body)
    {
        if (body == null)
        {
            return;
        }

        body.WorkTick = _workTick;
        body.IsHeldByJob = () => Modes.JobId != null;
    }

    private void Detach(ForemanWorkerAI? body)
    {
        if (body == null)
        {
            return;
        }

        if (body.WorkTick == _workTick)
        {
            body.WorkTick = null;
        }

        body.IsHeldByJob = null;
    }

    private void OnWorkerTick(ForemanWorkerAI body, float dt)
    {
        WorldState? world = _world;
        if (_faulted || world?.Loop == null || !ReferenceEquals(body, _body))
        {
            return;
        }

        try
        {
            world.Loop.Tick(Time.time);
        }
        catch (Exception exception)
        {
            Latch("worker tick", exception);
        }
        finally
        {
            try
            {
                world.Pickup?.ReleaseUntakenDrops();
            }
            catch (Exception exception)
            {
                Latch("drop release", exception);
            }
        }
    }

    private void Latch(string where, Exception exception)
    {
        if (_faulted)
        {
            return;
        }

        _faulted = true;
        _fault = where + ": " + exception.GetType().Name + ": " + exception.Message;
        _log("Collection FAULTED in " + where + " and does no more work this session. " + exception);
        try
        {
            string? job = Modes.JobId;
            if (job != null)
            {
                Motion.Stop(job);
            }
        }
        catch
        {
            // Already faulting; a second failure must not escape either.
        }
    }

    // --- Commands ----------------------------------------------------------------------------------------

    internal string Execute(string[]? args)
    {
        string subcommand = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "status";
        try
        {
            EnsureWorld();
            switch (subcommand)
            {
                case "status":
                    return Status();
                case "preview":
                    return Preview(args);
                case "start":
                    return Start(args);
                case "pause":
                    return Control(loop => loop.Pause(Time.time));
                case "resume":
                    return Control(loop => loop.Resume(Time.time));
                case "rebind":
                    return Rebind();
                case "cancel":
                    return Control(loop => loop.Cancel(Time.time));
                default:
                    return "Unknown subcommand. Try: status, preview [radius], start <stone> <wood> [hold], " +
                        "rebind (look at the chest), pause, resume, cancel.";
            }
        }
        catch (Exception exception)
        {
            return "cf_collect failed: " + exception.GetType().Name + ": " + exception.Message;
        }
    }

    private string Status()
    {
        var text = new StringBuilder();
        WorkAuthorityVerdict authority = WorkAuthorityPolicy.Evaluate(
            CollectionWorldFacts.ReadAuthorityFacts(_settlement.SettlementRuntimeEnabled.Value));
        text.Append("Collection: ").Append(WorkAuthorityPolicy.Describe(authority));
        if (_faulted)
        {
            text.AppendLine().Append("  COLLECTION FAULTED (").Append(_fault).Append("); see the log.");
        }

        if (_custody == null)
        {
            text.AppendLine().Append("  ").Append(CollectionIntake.Describe(CollectionIntakeRefusal.CustodyUnavailable));
        }

        if (_world == null)
        {
            text.AppendLine().Append("  No world is loaded.");
            return text.ToString();
        }

        RefreshBody();
        text.AppendLine().Append("  Thorstein: ");
        switch (_bodyOutcome)
        {
            case BodyLookupOutcome.Found:
                Vector3 at = _body!.transform.position;
                text.Append("here at ").Append(Format(at)).Append(Motion.IsPresent ? "." : ", but not under this world's control.");
                break;
            case BodyLookupOutcome.Duplicated:
                text.Append("two bodies claim to be him; neither is used until that is sorted out.");
                break;
            default:
                text.Append("not found. His body has to be in loaded ground (cf_worker spawn in this build).");
                break;
        }

        text.AppendLine().Append("  Actor mode: ").Append(Modes.Mode).Append(Modes.JobId != null ? " for " + Modes.JobId : string.Empty)
            .Append(". World load ").Append(_world.Epoch.ToString("N").Substring(0, 8)).Append('.');
        text.AppendLine().Append(_world.Loop != null ? _world.Loop.Describe() : "No collection order.");
        return text.ToString();
    }

    private string Preview(string[]? args)
    {
        if (_world == null)
        {
            return "No world is loaded.";
        }

        float radius = WorkScope.DefaultCampRadiusMetres;
        if (args != null && args.Length > 1 &&
            (!float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out radius) ||
                !(radius >= WorkScope.MinRadiusMetres) || radius > WorkScope.MaxRadiusMetres))
        {
            return "Usage: cf_collect preview [radius], with a radius of " + WorkScope.MinRadiusMetres.ToString("0", CultureInfo.InvariantCulture) +
                "-" + WorkScope.MaxRadiusMetres.ToString("0", CultureInfo.InvariantCulture) + " m (30 by default).";
        }

        if (!TryBuildScope(radius, out WorkScope? scope, out string label, out string failure))
        {
            return "No work area: " + failure;
        }

        if (scope!.Source == WorkScopeSource.DefaultCampCircle)
        {
            _preview = new PreviewToken(_world.Epoch, scope.SourceRevision, radius, Time.time);
        }

        // A one-off bounded survey with its own directory, so it never
        // disturbs the sources an order is working from.
        var survey = new SurveyScheduler(
            _world.Parameters, scope, new WorldSourceSurvey(new SourceDirectory(), _site), 0, SurveyProvenance.SoloForeman, Time.time);
        for (int step = 0; step < PreviewSurveySteps && !survey.IsComplete; step++)
        {
            survey.Step(Time.time);
        }

        var text = new StringBuilder(label);
        SurveyAccounting accounting = survey.Accounting;
        text.AppendLine().Append("  Solo survey by Thorstein: ").Append(accounting.LoadedCells).Append(" of ")
            .Append(accounting.TotalCells).Append(" cells loaded");
        if (!survey.IsComplete || accounting.TruncatedByBudget)
        {
            text.Append(", CUT SHORT (the numbers below are not everything)");
        }

        foreach (CollectedResource resource in new[] { CollectedResource.Stone, CollectedResource.Wood })
        {
            text.AppendLine().Append("  ").Append(resource == CollectedResource.Stone ? "Loose stones" : "Branches")
                .Append(": available ").Append(accounting.Available(resource))
                .Append(", exhausted ").Append(accounting.Exhausted(resource))
                .Append(", inaccessible ").Append(accounting.Inaccessible(resource))
                .Append(", unknown ").Append(accounting.Unknown(resource));
        }

        text.AppendLine().Append("  Look-alikes and modified objects rejected: ").Append(accounting.RejectedNotNatural).Append('.');
        return text.ToString();
    }

    private string Start(string[]? args)
    {
        WorldState? world = _world;
        if (world == null)
        {
            return "No world is loaded.";
        }

        if (_custody == null || world.Loop == null)
        {
            return "Refused: " + CollectionIntake.Describe(CollectionIntakeRefusal.CustodyUnavailable);
        }

        if (args == null || args.Length < 3 ||
            !int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int stone) ||
            !int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int wood) ||
            stone < 0 || wood < 0 || stone > CollectedResources.MaxQuota || wood > CollectedResources.MaxQuota)
        {
            return "Usage: cf_collect start <stone> <wood> [hold], each 0-" + CollectedResources.MaxQuota +
                ". Look at the chest he should deliver to, or add \"hold\" for him to keep the materials for you.";
        }

        if (stone == 0 && wood == 0)
        {
            return "Refused: " + CollectionIntake.Describe(CollectionIntakeRefusal.NoQuotas);
        }

        bool hold = args.Length > 3 && string.Equals(args[3], "hold", StringComparison.OrdinalIgnoreCase);
        DeliveryTarget delivery;
        if (hold)
        {
            delivery = DeliveryTarget.HoldForPlayer();
        }
        else if (SettlementTargets.TryResolveHoveredContainer(out Container? container, out string? key, out string failure))
        {
            delivery = DeliveryTarget.ToContainer(
                key!, world.Epoch, NaturalSourceClassifier.ToSitePoint(container!.transform.position));
        }
        else
        {
            return "Refused: look at the chest he should deliver to, or add \"hold\": " + failure;
        }

        float radius = _preview != null && _preview.Epoch == world.Epoch ? _preview.Radius : WorkScope.DefaultCampRadiusMetres;
        if (!TryBuildScope(radius, out WorkScope? scope, out _, out string scopeFailure))
        {
            return "Refused: " + CollectionIntake.Describe(CollectionIntakeRefusal.ScopeInvalid) + " (" + scopeFailure + ")";
        }

        bool previewed = scope!.Source != WorkScopeSource.DefaultCampCircle ||
            (_preview != null && _preview.Epoch == world.Epoch && _preview.Revision == scope.SourceRevision &&
                Time.time - _preview.At <= PreviewValidSeconds && Time.time >= _preview.At);

        var quotas = new List<ResourceQuota>();
        if (stone > 0)
        {
            quotas.Add(new ResourceQuota(CollectedResource.Stone, stone));
        }

        if (wood > 0)
        {
            quotas.Add(new ResourceQuota(CollectedResource.Wood, wood));
        }

        var yields = new Dictionary<CollectedResource, int>
        {
            [CollectedResource.Stone] = NaturalSourceClassifier.ExpectedYieldOfPrefab(NaturalSourceAllowlist.LooseStonePrefab),
            [CollectedResource.Wood] = NaturalSourceClassifier.ExpectedYieldOfPrefab(NaturalSourceAllowlist.BranchPrefab),
        };

        string issuedBy = string.Empty;
        try
        {
            issuedBy = Game.instance != null ? Game.instance.GetPlayerProfile()?.GetName() ?? string.Empty : string.Empty;
        }
        catch (Exception)
        {
            issuedBy = string.Empty;
        }

        var order = new CollectionOrderDefinition(
            new OrderId("collect-" + Guid.NewGuid().ToString("N").Substring(0, 8)),
            new WorkerId(WorkerName),
            quotas,
            scope,
            delivery,
            ParticipationMode.Solo,
            issuedBy);

        CollectionIntakeRefusal refusal = world.Loop.Accept(order, previewed, yields, Time.time);
        if (refusal != CollectionIntakeRefusal.Unspecified)
        {
            return "Refused: " + CollectionIntake.Describe(refusal);
        }

        return "Accepted " + order.Order.Value + ": " + (stone > 0 ? stone + " Stone " : string.Empty) +
            (wood > 0 ? wood + " Wood " : string.Empty) + (hold ? "held for you" : "to that chest") + ", in " +
            scope.AnchorDescription + " (radius " + scope.RadiusMetres.ToString("0.#", CultureInfo.InvariantCulture) +
            " m). He looks over the area on his own first. Only newly delivered units count.";
    }

    /// <summary>C2 (B1): the player confirms where an order recovered from the
    /// record works and where it delivers, both chosen in this world load.
    /// </summary>
    private string Rebind()
    {
        WorldState? world = _world;
        SoloCollectionLoop? loop = world?.Loop;
        if (world == null || loop == null || loop.Order == null)
        {
            return "There is no collection order.";
        }

        CollectionOrderDefinition order = loop.Order;
        if (!TryBuildScopeOfKind(order.Scope.Source, order.Scope.RadiusMetres, world.Epoch, out WorkScope? scope, out string failure))
        {
            return "Refused: " + failure;
        }

        DeliveryTarget delivery;
        if (order.Delivery.Kind == DeliveryKind.HoldForPlayer)
        {
            delivery = DeliveryTarget.HoldForPlayer();
        }
        else if (SettlementTargets.TryResolveHoveredContainer(out Container? container, out string? key, out string chestFailure))
        {
            delivery = DeliveryTarget.ToContainer(
                key!, world.Epoch, NaturalSourceClassifier.ToSitePoint(container!.transform.position));
        }
        else
        {
            return "Refused: look at the chest this order should deliver to: " + chestFailure;
        }

        return loop.Rebind(scope!, delivery, Time.time).Message;
    }

    private string Control(Func<SoloCollectionLoop, ControlResult> act)
    {
        SoloCollectionLoop? loop = _world?.Loop;
        if (loop == null)
        {
            return "There is no collection order.";
        }

        return act(loop).Message;
    }

    /// <summary>The scope a new order would get: the harvest designation when
    /// one is marked, else the default circle on the latest valid respawn
    /// anchor. An unreadable record is a refusal, never a fallback.</summary>
    private bool TryBuildScope(float radius, out WorkScope? scope, out string label, out string failure)
    {
        scope = null;
        label = string.Empty;
        Guid epoch = _world!.Epoch;

        if (!_scopes.TryGetHarvestArea(out Designation? harvest, out failure))
        {
            failure = "the settlement record could not be read (" + failure + "), so the assigned area is unknown";
            return false;
        }

        if (harvest != null)
        {
            scope = WorkScopeBuilder.FromHarvest(harvest, epoch);
            label = "Assigned work area: the harvest area at " + harvest.Centre + ", radius " +
                harvest.Radius.ToString("0.#", CultureInfo.InvariantCulture) + " m. Orders use it as it stands now; " +
                "a radius typed here does not apply.";
            return true;
        }

        if (!RespawnAnchorSource.TryGetLatest(out RespawnAnchor anchor, out failure))
        {
            return false;
        }

        scope = WorkScopeBuilder.FromAnchor(anchor, radius, epoch);
        label = "No harvest area is marked. Default work area: a " + radius.ToString("0.#", CultureInfo.InvariantCulture) +
            " m circle around " + anchor.Describe() + " at " + anchor.Point + ". It stays put when you walk away. " +
            "Run cf_collect start within ten minutes to use exactly this area.";
        return true;
    }

    /// <summary>A work area of exactly the kind an order already has,
    /// snapshotted in this world load. A rebind never changes the kind, and an
    /// assigned area that cannot be read never falls back to the circle.
    /// </summary>
    private bool TryBuildScopeOfKind(
        WorkScopeSource source, float radius, Guid epoch, out WorkScope? scope, out string failure)
    {
        scope = null;
        switch (source)
        {
            case WorkScopeSource.HarvestDesignation:
                if (!_scopes.TryGetHarvestArea(out Designation? harvest, out failure))
                {
                    failure = "the settlement record could not be read (" + failure + "), so the harvest area is unknown";
                    return false;
                }

                if (harvest == null)
                {
                    failure = "this order works in the harvest area, and none is marked now. Mark it again, or cancel the order.";
                    return false;
                }

                scope = WorkScopeBuilder.FromHarvest(harvest, epoch);
                return true;

            case WorkScopeSource.DefaultCampCircle:
                if (!RespawnAnchorSource.TryGetLatest(out RespawnAnchor anchor, out failure))
                {
                    return false;
                }

                scope = WorkScopeBuilder.FromAnchor(anchor, radius, epoch);
                return true;

            default:
                failure = "this order's kind of work area has no provider in this build";
                return false;
        }
    }

    private static string Format(Vector3 point) =>
        string.Format(CultureInfo.InvariantCulture, "({0:0.#}, {1:0.#}, {2:0.#})", point.x, point.y, point.z);

    // --- Per world load ----------------------------------------------------------------------------------

    private sealed class PreviewToken
    {
        public PreviewToken(Guid epoch, int revision, float radius, float at)
        {
            Epoch = epoch;
            Revision = revision;
            Radius = radius;
            At = at;
        }

        public Guid Epoch { get; }

        public int Revision { get; }

        public float Radius { get; }

        public float At { get; }
    }

    private sealed class WorldState
    {
        public WorldState(CollectionRuntime runtime, Guid epoch)
        {
            Epoch = epoch;
            float carry = Mathf.Clamp(
                runtime._settings.WorkerCarryWeight.Value,
                CollectionParameters.MinWorkerCarryWeight,
                CollectionParameters.MaxWorkerCarryWeight);
            Parameters = new CollectionParameters(workerCarryWeight: carry);
            Reservations = new SourceReservationBook(epoch);
            Directory = new SourceDirectory();
            Directory.Reset(epoch);
            RequestIds = new CollectionRequestIds(epoch);
            Facts = new CollectionWorldFacts(
                () => runtime._settlement.SettlementRuntimeEnabled.Value, runtime._reader, runtime._scopes,
                () => runtime._body, () => runtime.WorldLoadEpoch);

            if (runtime._custody == null)
            {
                return;
            }

            SoloCollectionLoop? loop = null;
            Pickup = new WorldSourcePickupPort(
                Parameters,
                runtime._custody,
                Directory,
                Reservations,
                id => loop != null && loop.HasActiveOrder && loop.Order!.Order.Equals(id) ? loop.Order : null,
                () => runtime._body,
                runtime._site,
                RequestIds,
                Facts.EvaluateAuthority,
                Facts.UnitWeight,
                runtime._log);

            loop = new SoloCollectionLoop(
                Parameters,
                WorkerKey.Thorstein,
                new WorkerId(WorkerName),
                runtime.Modes,
                Reservations,
                new CollectionMotionBridge(runtime.Motion),
                new CustodyBridge(runtime._custody, WorkerKey.Thorstein),
                Pickup,
                new WorldSourceSurvey(Directory, runtime._site),
                Facts,
                runtime._cooperation != null ? new CooperationBridge(runtime._cooperation) : null,
                RequestIds,
                runtime._log);
            Loop = loop;
        }

        public Guid Epoch { get; }

        public CollectionParameters Parameters { get; }

        public SourceReservationBook Reservations { get; }

        public SourceDirectory Directory { get; }

        public CollectionRequestIds RequestIds { get; }

        public CollectionWorldFacts Facts { get; }

        public WorldSourcePickupPort? Pickup { get; }

        public SoloCollectionLoop? Loop { get; }
    }
}
