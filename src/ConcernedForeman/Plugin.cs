using BepInEx;
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
/// read-only and client-safe. This build carries two things instead: the first
/// slice of the <i>other</i> half â€” the opt-in settlement runtime from #273,
/// which touches no world state until a person turns it on â€” and ladder
/// climbing (#326, #328).
///
/// Ladder climbing is the one thing here that patches the game at load, and
/// only ever the local player's own motor and the ladder's own Use: with
/// `Ladders/Enabled = false` nothing is patched at all. Nothing in this plugin
/// creates, moves, damages or writes to a piece, and no ladder is changed by
/// climbing it.</summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(Jotunn.Main.ModGuid)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.theconcernedcat.valheim.concernedforeman";
    public const string PluginName = "Concerned Foreman";
    public const string PluginVersion = "0.1.0";

    private SettlementRuntime? _settlement;
    private ClimbController? _ladders;
    private LadderSettings? _ladderSettings;
    private ClimbPose? _climbPose;
    private ClimbSounds? _climbSounds;
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

        // Through the game's own command table, not JÃ¶tunn's manager: JÃ¶tunn 2.29.2 looks for a
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

    /// <summary>Ladder climbing (#326, #328). This is the first thing Foreman
    /// patches at load, so it is deliberately explicit and it fails towards
    /// vanilla: if the game's own motor cannot be bound, nothing is patched,
    /// ladders keep teleporting, and the reason is logged once.
    ///
    /// With `Ladders/Enabled = false` at startup <b>no patch is installed at
    /// all</b> â€” not the motor's two, not the interaction's one â€” and the game
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

            // Before anything else drops the scene: a climber is holding a
            // ladder that is about to stop existing.
            _ladders?.OnWorldUnloaded();
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
        _ladders?.Stop();
        _climbSounds?.Remove();
        _climbPose?.Remove();
        LadderInteraction.Remove();
    }
}
