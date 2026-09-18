using BepInEx;
using BepInEx.Configuration;
using TheConcernedCat.ConcernedForeman.Domain.Settlement;
using TheConcernedCat.ConcernedForeman.Runtime;
using TheConcernedCat.ConcernedForeman.Runtime.Ladders;
using TheConcernedCat.ConcernedForeman.Runtime.Settlement;
using TheConcernedCat.Ladders;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman;

/// <summary>Concerned Foreman.
///
/// The product's promise is causal building diagnostics, and that half is
/// read-only and client-safe. This build carries only the first slice of the
/// <i>other</i> half — the opt-in settlement runtime from #273 — which is off
/// until a person turns it on.
///
/// Nothing here installs a patch, hooks a game event or touches world state at
/// load. Until the settlement runtime is enabled and a worker is deliberately
/// spawned, this plugin registers two console commands and does nothing else.</summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(Jotunn.Main.ModGuid)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.theconcernedcat.valheim.concernedforeman";
    public const string PluginName = "Concerned Foreman";
    public const string PluginVersion = "0.1.0";

    private SettlementRuntime? _settlement;
    private ClimbController? _ladders;
    private ConfigEntry<bool>? _laddersEnabled;
    private bool _worldWasUp;

    private void Awake()
    {
        ForemanSettlementSettings settings = ForemanSettlementSettings.Bind(Config);
        _settlement = new SettlementRuntime(settings, message => Logger.LogInfo(message));

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

    /// <summary>Ladder climbing (#326). This is the first thing Foreman patches
    /// at load, so it is deliberately explicit and it fails towards vanilla: if
    /// the game's own motor cannot be bound, nothing is patched, ladders keep
    /// teleporting, and the reason is logged once.
    ///
    /// The whole `Ladders` settings section belongs to #328; this one switch is
    /// here so the feature can be turned off before that lands.</summary>
    private void InstallLadders()
    {
        _laddersEnabled = Config.Bind(
            "Ladders",
            "Enabled",
            true,
            "Climb ladders instead of vanilla's teleport. Off restores the game's own behaviour exactly.");

        var options = new ClimbOptions { Enabled = _laddersEnabled.Value };
        _ladders = new ClimbController(options, message => Logger.LogInfo(message));
        if (!options.Enabled)
        {
            Logger.LogInfo("Ladder climbing is off in the config; ladders behave exactly as the game ships them.");
            return;
        }

        if (!_ladders.Install(PluginGuid + ".ladders"))
        {
            Logger.LogInfo("Ladder climbing is unavailable: " + ClimbMotor.Unavailable + ". Ladders are unchanged.");
        }
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

            // Before anything else drops the scene: a climber is holding a
            // ladder that is about to stop existing.
            _ladders?.OnWorldUnloaded();
        }

        _worldWasUp = worldIsUp;

        if (_ladders != null)
        {
            // Read live, so switching the setting off ends a climb on the next
            // frame rather than at the next restart.
            _ladders.Options.Enabled = _laddersEnabled == null || _laddersEnabled.Value;
            _ladders.Update(Time.deltaTime);
        }
    }

    /// <summary>Hands a climber back first, and only then removes the patches.
    /// One of the nine ways a climb ends.</summary>
    private void OnDestroy()
    {
        _ladders?.Stop();
    }
}
