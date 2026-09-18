using BepInEx;
using TheConcernedCat.ConcernedForeman.Domain.Settlement;
using TheConcernedCat.ConcernedForeman.Runtime;
using TheConcernedCat.ConcernedForeman.Runtime.Collection;
using TheConcernedCat.ConcernedForeman.Runtime.Custody;
using TheConcernedCat.ConcernedForeman.Runtime.Settlement;

namespace TheConcernedCat.ConcernedForeman;

/// <summary>Concerned Foreman.
///
/// The product's promise is causal building diagnostics, and that half is
/// read-only and client-safe. This build carries only the first slice of the
/// <i>other</i> half — the opt-in settlement runtime from #273 — which is off
/// until a person turns it on.
///
/// Nothing here installs a patch or touches world state at load. It registers the
/// worker prefab (so a saved worker body survives a world load), subscribes the
/// world-save hook (which writes only to Foreman's own settlement record, and
/// only when custody has unsaved rows), and registers two console commands.
/// Until the settlement runtime is enabled and a worker is deliberately spawned,
/// it does nothing else.</summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(Jotunn.Main.ModGuid)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.theconcernedcat.valheim.concernedforeman";
    public const string PluginName = "Concerned Foreman";
    public const string PluginVersion = "0.1.0";

    private SettlementRuntime? _settlement;
    private CollectionRuntime? _collection;
    private bool _worldWasUp;

    private void Awake()
    {
        ForemanSettlementSettings settings = ForemanSettlementSettings.Bind(Config);
        _settlement = new SettlementRuntime(settings, message => Logger.LogInfo(message));

        // The worker prefab must be registered before any world's objects are
        // created, or a saved worker body is destroyed as an unknown prefab (D9);
        // the world-save hook writes custody markers and nothing else.
        _settlement.Install();

        // #315 over #316: Thorstein's collection runs on the custody runtime and shares its
        // world-load epoch, so every key made during one load agrees (CONTRACTS C2).
        ForemanCustodyRuntime custody = _settlement.Custody;
        CollectionSettings collectionSettings = CollectionSettings.Bind(Config);

        // One carry budget: the worker's inventory port refuses what the loop would
        // never plan, so the two can't disagree about a full load.
        custody.CarryWeight = () => collectionSettings.WorkerCarryWeight.Value;
        _collection = new CollectionRuntime(
            settings,
            collectionSettings,
            message => Logger.LogInfo(message),
            custody,
            cooperation: null,
            sharedEpoch: () => custody.Epoch);

        // A body a collection job holds is never despawned out from under it (ARCH-02).
        CollectionRuntime collection = _collection;
        _settlement.MayRetireBody = () => collection.Modes.MayRetireBody;

        Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
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
        }
        else if (!_worldWasUp && worldIsUp)
        {
            // As soon as the world is up, before its net time can advance:
            // custody reads the loaded world time here (CONTRACTS.md §5.5).
            _settlement?.OnWorldLoaded();
        }

        _collection?.Update();

        _worldWasUp = worldIsUp;
    }
}
