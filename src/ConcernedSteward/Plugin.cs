using BepInEx;
using TheConcernedCat.ConcernedSteward.Runtime;

namespace TheConcernedCat.ConcernedSteward;

/// <summary>Concerned Steward.
///
/// A settlement worker with one job: keep the fires you marked burning, with
/// wood from the chest you marked.
///
/// <b>This plugin patches nothing.</b> There is no Harmony patch anywhere in
/// it, and with <c>Steward/StewardRuntimeEnabled</c> false — the default — it
/// registers two console commands and a prefab and does nothing else at all: it
/// marks nothing, spawns nothing, reads no chest and writes no world state.
/// Turning the runtime on lets it act on a host you control, and turning
/// <c>Steward/TendFiresEnabled</c> on is a second, separate decision about
/// whether it may reach into your chest.
///
/// <b>The prefab is registered at startup on purpose.</b> The Steward's body is
/// persistent — his identity and his pack live in his own network object and
/// are saved with the world — and the host destroys any saved object whose
/// prefab is not registered when a world's objects are created. A prefab built
/// lazily, when the player first asks for him, would let the next load delete
/// him and everything he was carrying. So it is built as soon as vanilla
/// prefabs exist, at the main menu, whether or not the runtime is on.</summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(Jotunn.Main.ModGuid)]
// CNPC-R3 (#382): the Steward's identity, his body claim and his job planning
// come from the Concerned NPC library, which ships as its own package. Hard
// rather than soft: without it there is no arbiter, so there is nothing to stop
// one identity having two bodies, and the honest failure is one line at load
// instead of a null reference in the middle of somebody's evening.
//
// Spelled out rather than referred to by constant on purpose: the validator
// reads this file as text (check_library_consumers), and a constant would leave
// the rule looking satisfied to a reader and unsatisfied to the gate.
[BepInDependency("com.theconcernedcat.valheim.concernednpc")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.theconcernedcat.valheim.concernedsteward";
    public const string PluginName = "Concerned Steward";
    public const string PluginVersion = "0.1.0";

    private StewardRuntime? _steward;
    private bool _worldWasUp;

    private void Awake()
    {
        StewardSettings settings = StewardSettings.Bind(Config);
        _steward = new StewardRuntime(settings, message => Logger.LogInfo(message));
        _steward.Install();

        // Which build this actually is, not just which version it claims: a test
        // profile keeps whatever DLL was last copied into it, and two builds of
        // one version read alike in the log without the commit.
        Logger.LogInfo(
            $"{PluginName} {PluginVersion} loaded. Release: ConcernedSteward@{ResolveInformationalVersion()}.");
        Logger.LogInfo(
            "The Steward is " + (settings.RuntimeEnabled.Value ? "ENABLED" : "off (the default)") +
            "; fire tending is " + (settings.TendFiresEnabled.Value ? "ENABLED" : "off (the default)") +
            ". With both off this mod changes nothing about your game.");

        // Through the game's own command table rather than Jötunn's manager:
        // Jötunn 2.29.2 looks for a Terminal.ConsoleCommand constructor this
        // game build no longer has, so every command registered its way silently
        // does not exist (#307). What the console actually accepted is logged.
        Jotunn.Entities.ConsoleCommand[] commands =
        {
            new StewardCommand(_steward),
            new StewardFiresCommand(_steward),
        };

        var names = new string[commands.Length];
        for (int index = 0; index < commands.Length; index++)
        {
            names[index] = commands[index].Name;
            VanillaConsoleCommands.Register(commands[index], Logger);
        }

        Logger.LogInfo(VanillaConsoleCommands.Describe(names));
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

    /// <summary>Notices a world arriving and going away, and drives the Steward.
    ///
    /// <c>ZNetScene.instance</c> is watched rather than a scene-unload event
    /// subscribed to, because the presence of the scene is the thing that
    /// actually matters and it can be read directly. A body reference and an
    /// object-identity epoch belonging to a world that no longer exists must
    /// both be dropped, or a second world in the same session inherits them —
    /// and inheriting an identity epoch is exactly how a remembered chest key
    /// would come to name somebody else's chest.</summary>
    private void Update()
    {
        bool worldIsUp = ZNetScene.instance != null;
        if (_worldWasUp && !worldIsUp)
        {
            _steward?.OnWorldUnloaded();
        }
        else if (!_worldWasUp && worldIsUp)
        {
            _steward?.OnWorldLoaded();
        }

        _worldWasUp = worldIsUp;

        if (worldIsUp)
        {
            _steward?.Update();
        }
    }

    private void OnDestroy()
    {
        _steward?.OnWorldUnloaded();
    }
}
