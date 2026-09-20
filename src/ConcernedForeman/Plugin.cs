using System;
using System.Collections.Generic;
using BepInEx;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedForeman.Domain.Npc;
using TheConcernedCat.ConcernedForeman.Domain.Settlement;
using TheConcernedCat.ConcernedForeman.Runtime;
using TheConcernedCat.ConcernedForeman.Runtime.Collection;
using TheConcernedCat.ConcernedForeman.Runtime.Custody;
using TheConcernedCat.ConcernedForeman.Runtime.Cooperation;
using TheConcernedCat.ConcernedForeman.Runtime.Interop;
using TheConcernedCat.ConcernedForeman.Runtime.Ladders;
using TheConcernedCat.ConcernedForeman.Runtime.Settlement;
using TheConcernedCat.Ladders;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Cooperation;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Workers;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman;

/// <summary>Concerned Foreman.
///
/// The product's promise is causal building diagnostics, and that half is
/// read-only and client-safe. This build carries two things instead: the first
/// slice of the <i>other</i> half — the opt-in settlement runtime from #273,
/// which touches no world state until a person turns it on — and ladder
/// climbing (#326, #328).
///
/// Ladder climbing is the one thing here that patches the game at load, and
/// only ever the local player's own motor and the ladder's own Use: with
/// `Ladders/Enabled = false` nothing is patched at all. Nothing in this plugin
/// creates, moves, damages or writes to a piece, and no ladder is changed by
/// climbing it.</summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(Jotunn.Main.ModGuid)]
// Thorstein's actor mode comes from the Concerned NPC library, which ships as its
// own package. Hard rather than soft: without it there is no arbiter, so there is
// nothing to stop one identity having two bodies, and the honest failure is one
// line at load instead of a null reference in the middle of somebody's evening.
//
// Spelled out rather than referred to by constant on purpose: the validator reads
// this file as text (check_library_consumers), and a constant would leave the rule
// looking satisfied to a reader and unsatisfied to the gate.
[BepInDependency("com.theconcernedcat.valheim.concernednpc")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.theconcernedcat.valheim.concernedforeman";
    public const string PluginName = "Concerned Foreman";
    public const string PluginVersion = "0.1.0";

    private SettlementRuntime? _settlement;
    private ForemanNpcAdoption? _npc;
    private CollectionRuntime? _collection;
    private HaulProviderDiscovery? _haulProvider;
    private PresenceProviderDiscovery? _presenceProvider;
    private SurveyCompanions? _surveyCompanions;
    private ForemanCooperativeDelivery? _cooperativeDelivery;
    private ClimbController? _ladders;
    private LadderSettings? _ladderSettings;
    private ClimbPose? _climbPose;
    private ClimbSounds? _climbSounds;
    private Runtime.Construction.BuildOrderRuntime? _buildOrders;
    private Runtime.Construction.ShelterConstructionRuntime? _construction;
    private bool _worldWasUp;

    private void Awake()
    {
        ForemanSettlementSettings settings = ForemanSettlementSettings.Bind(Config);
        _settlement = new SettlementRuntime(settings, message => Logger.LogInfo(message));

        // Thorstein is offered to Concerned NPC before anything else, because
        // every question below about what he is doing is answered by the arbiter
        // this registers him with. Registration is per process, not per world, and
        // it never throws: four products register into one load and one of them
        // getting it wrong must not take the others down. A refusal is logged and
        // leaves this product unable to take a collection order - which is the
        // fail-closed direction: worker authority never grants on ambiguity.
        _npc = new ForemanNpcAdoption(
            NpcRoleRegistry.Shared,
            new ForemanNpcRole(SettlementRecords.DefaultRoot()),
            message => Logger.LogInfo(message));
        _npc.Register();
        ForemanNpcAdoption npc = _npc;

        // The worker prefab must be registered before any world's objects are
        // created, or a saved worker body is destroyed as an unknown prefab (D9).
        _settlement.Install();

        // #315/#316: Thorstein's collection runs on the custody runtime and shares its
        // world-load epoch, so every key made during one load agrees.
        ForemanCustodyRuntime custody = _settlement.Custody;
        CollectionSettings collectionSettings = CollectionSettings.Bind(Config);
        custody.CarryWeight = () => collectionSettings.WorkerCarryWeight.Value;

        // #317: discover Teamster by plugin GUID and consume only its BCL capability map.
        _haulProvider = new HaulProviderDiscovery(message => Logger.LogInfo(message));

        // #317: Cartographer says whether Hulgi is here and free, so a survey
        // can call itself joint when he is actually standing in it. Read-only:
        // that contract has no op that could fetch him.
        _presenceProvider = new PresenceProviderDiscovery(message => Logger.LogInfo(message));
        PresenceProviderDiscovery presence = _presenceProvider;
        _surveyCompanions = new SurveyCompanions(
            () =>
            {
                presence.EnsureProbed();
                return presence.Discovery;
            },
            PluginVersion,
            () => Time.time);
        _cooperativeDelivery = new ForemanCooperativeDelivery(
            _haulProvider, custody, () => Time.time, message => Logger.LogInfo(message), PluginVersion);

        _collection = new CollectionRuntime(
            settings,
            collectionSettings,
            message => Logger.LogInfo(message),
            npc.Modes,
            custody,
            cooperation: _cooperativeDelivery,
            sharedEpoch: () => custody.Epoch);

        CollectionRuntime collection = _collection;
        ForemanCooperativeDelivery delivery = _cooperativeDelivery;
        SurveyCompanions companions = _surveyCompanions;
        delivery.BindMotion(() => collection.Motion);
        _settlement.MayRetireBody = () => collection.Modes.MayRetireBody;

        // #380: Thorstein's work is building. The order is the player's - a
        // place, a facing, and a confirmation - and this runtime only ever
        // reads the world and hands it to the decisions in Domain/Construction.
        _buildOrders = new Runtime.Construction.BuildOrderRuntime(
            () => WorkAuthorityPolicy.Evaluate(
                CollectionWorldFacts.ReadAuthorityFacts(settings.SettlementRuntimeEnabled.Value)) ==
                WorkAuthorityVerdict.Granted,
            () => WorkAuthorityPolicy.Describe(WorkAuthorityPolicy.Evaluate(
                CollectionWorldFacts.ReadAuthorityFacts(settings.SettlementRuntimeEnabled.Value))),
            message => Logger.LogInfo(message));
        Runtime.Construction.BuildOrderRuntime buildOrders = _buildOrders;

        // #380: and this is what DRIVES the order. Everything above decides; until
        // this existed nothing executed, so WorldPiecePlacer - the gate followed by
        // the host player's own PlacePiece - had no production caller at all and a
        // confirmed shelter sat there priced and unbuilt. The runtime below holds
        // Thorstein's actor mode while it works, opens only the container the
        // player marked, and places only through that one gated call.
        SettlementRuntime settlementRuntime = _settlement;
        _construction = new Runtime.Construction.ShelterConstructionRuntime(
            buildOrders,
            npc.Modes,
            custody,
            collection.Motion,
            () => WorkAuthorityPolicy.Evaluate(
                CollectionWorldFacts.ReadAuthorityFacts(settings.SettlementRuntimeEnabled.Value)) ==
                WorkAuthorityVerdict.Granted,
            () => settlementRuntime.TryReadDesignations(
                    out System.Collections.Generic.IReadOnlyList<TheConcernedCat.Settlement.Designations.Designation> marked,
                    out bool stale)
                ? Runtime.Construction.SupplyChests.From(marked, stale)
                : default,
            () => Time.time,
            message => Logger.LogInfo(message));
        // The order's status line is wired by the runtime's own constructor, not
        // here: a hookup in this file is a hookup nothing can test.

        gameObject.AddComponent<Ui.BuildOrderPanel>().Initialize(
            () => settings.SettlementRuntimeEnabled.Value, buildOrders, Logger);

        gameObject.AddComponent<Ui.CollectionOrderPanel>().Initialize(
            () => settings.SettlementRuntimeEnabled.Value,
            arguments => collection.Execute(arguments),
            () => BuildOrderPanelFacts(settings, collection, custody, delivery, companions),
            delivery.PauseForPlayer,
            () =>
            {
                CollectionOrderDefinition? order = collection.Loop?.Order;
                if (order != null)
                {
                    delivery.Cancel(order, detachAndPark: true);
                }
            },
            Logger);

        // Which build this actually is, not just which version it claims: a test
        // profile keeps whatever DLL was last copied into it, and two builds of
        // one version read alike in the log without the commit.
        Logger.LogInfo(
            $"{PluginName} {PluginVersion} loaded. Release: ConcernedForeman@{ResolveInformationalVersion()}.");
        Logger.LogInfo(
            "Settlement runtime is " +
            (settings.SettlementRuntimeEnabled.Value ? "ENABLED" : "off (the default)") +
            ". Building diagnostics do not require it.");

        // Through the game's own command table, not Jötunn's manager: Jötunn 2.29.2 looks for a
        // Terminal.ConsoleCommand constructor Valheim 1.0.12 no longer has, so every command
        // silently did not exist (#307). What the console actually accepted is logged.
        Jotunn.Entities.ConsoleCommand[] commands =
        {
            new WorkerToolsCommand(_settlement),
            new SettlementToolsCommand(_settlement),
            new CollectCommand(_collection),
            new Runtime.Construction.BuildCommand(_buildOrders),
            // Read-only measurement of the game's own ladders (CF-LAD-001). It
            // places nothing and changes nothing; it exists so the ladder work
            // is built on measurements instead of guesses.
            new Runtime.Ladders.LadderAuditCommand(message => Logger.LogInfo(message)),
        };

        var names = new string[commands.Length];
        for (int index = 0; index < commands.Length; index++)
        {
            names[index] = commands[index].Name;
            VanillaConsoleCommands.Register(commands[index], Logger);
        }

        Logger.LogInfo(VanillaConsoleCommands.Describe(names));

        InstallLadders();
    }

    /// <summary>The release identity including the build commit (the SDK stamps
    /// InformationalVersion as "0.1.0+&lt;sha&gt;"), so the load line names the
    /// exact binary and nothing about the player.</summary>
    private static string ResolveInformationalVersion()
    {
        try
        {
            return System.Reflection.CustomAttributeExtensions
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(Plugin).Assembly)
                ?.InformationalVersion ?? PluginVersion;
        }
        catch
        {
            // The plain version is an acceptable release identity fallback.
            return PluginVersion;
        }
    }

    private static OrderPanelFacts BuildOrderPanelFacts(
        ForemanSettlementSettings settings,
        CollectionRuntime collection,
        ForemanCustodyRuntime custody,
        ForemanCooperativeDelivery delivery,
        SurveyCompanions? companions)
    {
        var loop = collection.Loop;
        return new OrderPanelFacts(
            settings.SettlementRuntimeEnabled.Value,
            WorkAuthorityPolicy.Evaluate(
                CollectionWorldFacts.ReadAuthorityFacts(settings.SettlementRuntimeEnabled.Value)),
            collection.Motion.IsPresent,
            loop?.Order,
            loop?.State ?? CollectionOrderState.Unspecified,
            loop?.Reason ?? CollectionAttentionReason.Unspecified,
            loop?.PausedByPlayer ?? false,
            ProgressFor(loop?.Order, custody.View),
            delivery.LastAvailability,
            delivery.ActiveRun?.Phase ?? CooperationPhase.Unspecified,
            delivery.ActiveRun?.Detail ?? delivery.LastAvailabilityDetail,
            delivery.ActiveRun?.DeliveryTrips ?? 0,
            hulgiSurveying: companions != null &&
                companions.IsHelping(loop?.State ?? CollectionOrderState.Unspecified));
    }

    private static IReadOnlyList<ResourceProgress> ProgressFor(
        CollectionOrderDefinition? order,
        IMaterialCustodyView view)
    {
        if (order == null)
        {
            return Array.Empty<ResourceProgress>();
        }

        var progress = new List<ResourceProgress>(order.Quotas.Count);
        foreach (ResourceQuota quota in order.Quotas)
        {
            progress.Add(view.ProgressFor(order, quota.Resource));
        }

        return progress;
    }

    /// <summary>Ladder climbing (#326, #328). This is the first thing Foreman
    /// patches at load, so it is deliberately explicit and it fails towards
    /// vanilla: if the game's own motor cannot be bound, nothing is patched,
    /// ladders keep teleporting, and the reason is logged once.
    ///
    /// With `Ladders/Enabled = false` at startup <b>no patch is installed at
    /// all</b> — not the motor's two, not the interaction's one — and the game
    /// behaves as if Concerned Foreman were not here.</summary>
    private void InstallLadders()
    {
        _ladderSettings = LadderSettings.Bind(Config);

        var options = new ClimbOptions();
        _ladderSettings.ApplyTo(options);
        _ladders = new ClimbController(options, message => Logger.LogInfo(message));
        if (!options.Enabled)
        {
            Logger.LogInfo("Ladder climbing is off in the config; ladders behave exactly as the game ships them.");
            return;
        }

        if (!_ladders.Install(PluginGuid + ".ladders"))
        {
            Logger.LogInfo("Ladder climbing is unavailable: " + ClimbMotor.Unavailable + ". Ladders are unchanged.");
            return;
        }

        // Which pieces count (#328). Data, not code: the rules are in
        // Domain/Ladders/LadderAdmission and the measurements come from the
        // loaded game.
        _ladders.Survey.Admits = new LadderPieces(message => Logger.LogInfo(message)).Admits;

        // Presentation is deliberately downstream of traversal. If either
        // visual/audio adapter fails, the safe climb still runs.
        _climbPose = new ClimbPose(message => Logger.LogInfo(message));
        if (_climbPose.Install())
        {
            _climbSounds = new ClimbSounds(message => Logger.LogInfo(message));
            _climbSounds.Install();
        }
        else
        {
            // ClimbSounds asks Valheim's FootStep system for its Climbing
            // effect. That classification depends on the pose's wall-running
            // flag; without the pose, silence is more truthful than a jog sound.
            Logger.LogInfo("Ladder rung audio is disabled because the climbing pose is unavailable.");
        }

        // And the Use key, which vanilla spends on a teleport. Its own Harmony
        // id, because the climb's patches and this one come out at different
        // times for different reasons.
        LadderInteraction.Install(
            PluginGuid + ".ladders.interaction", _ladders, _ladderSettings, message => Logger.LogInfo(message));
    }

    /// <summary>Notices a world going away.
    ///
    /// This watches <c>ZNetScene.instance</c> rather than subscribing to a
    /// scene-unload event, because the presence of the scene is the thing that
    /// actually matters here and it can be read directly. A worker reference and
    /// a prefab built against a scene that no longer exists must both be
    /// dropped, or a second world in the same session inherits them.</summary>
    private void Update()
    {
        bool worldIsUp = ZNetScene.instance != null;
        if (_worldWasUp && !worldIsUp)
        {
            _settlement?.OnWorldUnloaded();
            _collection?.OnWorldUnloaded();
            _cooperativeDelivery?.OnWorldUnloaded();
            _haulProvider?.Forget();
            _presenceProvider?.Forget();
            _surveyCompanions?.Forget();

            // A build-order marker names a place in a world that is going away,
            // and the loop that was building it has to give his body back first.
            _construction?.OnWorldUnloaded();
            _buildOrders?.Forget();

            // Before anything else drops the scene: a climber is holding a
            // ladder that is about to stop existing.
            _ladders?.OnWorldUnloaded();

            // Last, so the collection loop has already released Thorstein's mode
            // by name. This only has to catch what a fault or a crash left
            // behind: a job id nothing still holds a copy of, which no ordinary
            // release could ever give back.
            _npc?.NoteWorldUnloaded();
        }
        else if (!_worldWasUp && worldIsUp)
        {
            // Teamster's capability map is complete by the first world tick.
            _haulProvider?.EnsureProbed();
            _presenceProvider?.EnsureProbed();

            // The library mints this world load's epoch, or adopts the one
            // another product's runtime minted a frame earlier. A mode hold or a
            // body hold from the previous world ends here.
            _npc?.NoteWorldLoaded();

            // Before net time advances, custody reads the loaded world time.
            _settlement?.OnWorldLoaded();
        }

        _collection?.Update();

        // #380: one round of the shelter build, rate limited inside. It refuses
        // unless a player confirmed an order and this runtime may work at all, and
        // it is the only thing in this plugin that places a piece.
        if (worldIsUp)
        {
            _construction?.Tick();
        }

        _worldWasUp = worldIsUp;

        if (_ladders != null)
        {
            // Read live, so switching a setting off ends a climb on the next
            // frame rather than at the next restart.
            _ladderSettings?.ApplyTo(_ladders.Options);
            _ladders.Update(Time.deltaTime);
        }
    }

    /// <summary>Hands a climber back first, and only then removes the patches.
    /// One of the nine ways a climb ends.</summary>
    private void OnDestroy()
    {
        _cooperativeDelivery?.OnWorldUnloaded();
        _haulProvider?.Forget();
        _ladders?.Stop();
        _climbSounds?.Remove();
        _climbPose?.Remove();
        LadderInteraction.Remove();
    }
}
