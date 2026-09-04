using BepInEx;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Reporting;
using TheConcernedCat.ConcernedCartographer.Runtime;

namespace TheConcernedCat.ConcernedCartographer;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(Jotunn.Main.ModGuid)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.theconcernedcat.valheim.concernedcartographer";
    public const string PluginName = "Concerned Cartographer";
    public const string PluginVersion = "0.10.1";

    private CartographerRuntime? _runtime;
    private CrashReportingHub? _crashHub;

    private void Awake()
    {
        CartographerSettings settings = CartographerSettings.Bind(Config);
        Persistence.LocalizationPersistence.Initialize(Logger);

        // Crash reporting (#97) attaches before the runtime exists so even
        // construction-time failures are captured; it is fully inert until
        // the player consents AND a DSN is configured.
        try
        {
            _crashHub = new CrashReportingHub(settings, BuildCrashContext());
            _crashHub.Attach(Logger);
        }
        catch (System.Exception exception)
        {
            _crashHub = null;
            Logger.LogWarning($"Crash reporting unavailable this session: {SafeLogText.Brief(exception)}");
        }

        _runtime = new CartographerRuntime(settings, Logger);

        MinimapManager.OnVanillaMapAvailable += HandleMapAvailable;
        // RC15: vanilla loads the character's saved map AFTER Minimap.Start
        // (LoadMapData → SetMapData → ClearPins + re-AddPin), destroying
        // every pin rendering the map-available pass created. This second
        // hook fires right after that reconstruction so managed markers
        // re-claim their saved renderings and rebind their cc:* sprites.
        MinimapManager.OnVanillaMapDataLoaded += HandleMapDataLoaded;
        CommandManager.Instance.AddConsoleCommand(new RoadToolsCommand(_runtime));
        CommandManager.Instance.AddConsoleCommand(new PinToolsCommand(_runtime));
        CommandManager.Instance.AddConsoleCommand(new AtlasToolsCommand(_runtime));
        CommandManager.Instance.AddConsoleCommand(new SurveyToolsCommand(_runtime));
        CommandManager.Instance.AddConsoleCommand(new RouteToolsCommand(_runtime));
        CommandManager.Instance.AddConsoleCommand(new SyncToolsCommand(_runtime));
        Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
        LogEnvironment(settings);
    }

    /// <summary>The release identity including the build commit (SDK stamps
    /// InformationalVersion as "0.10.1+&lt;sha&gt;"). Shared by the crash
    /// context and the RC15 lifecycle log line, and safe to log: it names
    /// this build of the mod and nothing about the player.</summary>
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

    private Reporting.CrashReportContext BuildCrashContext()
    {
        string informational = ResolveInformationalVersion();

        string bepInExVersion = "unknown";
        string jotunnVersion = "unknown";
        try
        {
            bepInExVersion = typeof(BaseUnityPlugin).Assembly.GetName().Version?.ToString() ?? "unknown";
            jotunnVersion = typeof(Jotunn.Main).Assembly.GetName().Version?.ToString() ?? "unknown";
        }
        catch
        {
            // Version labels are metadata only.
        }

        return new Reporting.CrashReportContext
        {
            Release = "ConcernedCartographer@" + informational,
            ModVersion = PluginVersion,
            ValheimVersion = ResolveGameVersion(),
            UnityVersion = UnityEngine.Application.unityVersion,
            BepInExVersion = bepInExVersion,
            JotunnVersion = jotunnVersion,
            RuntimeState = SampleRuntimeState,
        };
    }

    private static Reporting.CrashReportRuntimeState SampleRuntimeState()
    {
        bool multiplayer = false;
        bool noMap = false;
        bool mapOpen = false;
        try
        {
            multiplayer = ZNet.instance != null && ZNet.instance.GetPeers().Count > 0;
        }
        catch
        {
        }

        try
        {
            noMap = ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey("nomap");
        }
        catch
        {
        }

        try
        {
            mapOpen = Minimap.IsOpen();
        }
        catch
        {
        }

        return new Reporting.CrashReportRuntimeState(multiplayer, noMap, mapOpen);
    }

    private void LogEnvironment(CartographerSettings settings)
    {
        try
        {
            string bepInExVersion = typeof(BaseUnityPlugin).Assembly.GetName().Version?.ToString() ?? "unknown";
            string jotunnVersion = typeof(Jotunn.Main).Assembly.GetName().Version?.ToString() ?? "unknown";
            string gameVersion = ResolveGameVersion();

            Logger.LogInfo(
                $"Environment: Valheim {gameVersion}, Unity {UnityEngine.Application.unityVersion}, " +
                $"BepInEx {bepInExVersion}, Jotunn {jotunnVersion}.");
            // RC15 lifecycle diagnostics: the exact build (version+commit)
            // at the top of every LogOutput, so support bundles identify
            // the binary without any player data.
            Logger.LogInfo($"Release: ConcernedCartographer@{ResolveInformationalVersion()}.");
            Logger.LogInfo(
                "Effective config (out-of-range values are clamped to documented ranges): " +
                $"Enabled={settings.Enabled.Value}, " +
                $"CaptureConstructionActions={settings.CaptureConstructionActions.Value}, " +
                $"ReconcileTerrainChanges={settings.ReconcileTerrainChanges.Value}, " +
                $"SampleIntervalSeconds={settings.SampleIntervalSeconds.Value}, " +
                $"MinimumPointSpacingMeters={settings.MinimumPointSpacingMeters.Value}, " +
                $"MaximumStrokeGapMeters={settings.MaximumStrokeGapMeters.Value}, " +
                $"DuplicateSuppressionMeters={settings.DuplicateSuppressionMeters.Value}, " +
                $"AutosaveIntervalSeconds={settings.AutosaveIntervalSeconds.Value}, " +
                $"PaintThreshold={settings.PaintThreshold.Value}, " +
                $"PaintSampleRadius={settings.PaintSampleRadius.Value}, " +
                $"LineWidthPixels={settings.LineWidthPixels.Value}, " +
                $"DebugLogging={settings.DebugLogging.Value}, " +
                $"DrawCalibrationMarkers={settings.DrawCalibrationMarkers.Value}.");
        }
        catch (System.Exception exception)
        {
            Logger.LogWarning($"Could not record environment versions: {SafeLogText.Brief(exception)}");
        }
    }

    private static string ResolveGameVersion()
    {
        // Calling Version.GetVersionString() directly threw MissingMethodException
        // at runtime (the publicized reference assembly and the live game assembly
        // disagree on the signature), so the game version is resolved reflectively.
        try
        {
            System.Type versionType = typeof(global::Version);
            const System.Reflection.BindingFlags publicStatic =
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;

            object? currentVersion =
                versionType.GetField("CurrentVersion", publicStatic)?.GetValue(null) ??
                versionType.GetProperty("CurrentVersion", publicStatic)?.GetValue(null);
            if (currentVersion is not null)
            {
                return currentVersion.ToString();
            }

            foreach (System.Reflection.MethodInfo method in versionType.GetMethods(publicStatic))
            {
                if (method.Name != "GetVersionString" || method.ReturnType != typeof(string))
                {
                    continue;
                }

                System.Reflection.ParameterInfo[] parameters = method.GetParameters();
                object?[] arguments = new object?[parameters.Length];
                for (int index = 0; index < parameters.Length; index++)
                {
                    arguments[index] = parameters[index].HasDefaultValue
                        ? parameters[index].DefaultValue
                        : parameters[index].ParameterType.IsValueType
                            ? System.Activator.CreateInstance(parameters[index].ParameterType)
                            : null;
                }

                return method.Invoke(null, arguments) as string ?? "unknown";
            }
        }
        catch
        {
            // The version banner is cosmetic; never fail startup over it.
        }

        return "unknown";
    }

    private void HandleMapAvailable()
    {
        _runtime?.OnMapAvailable();
    }

    private void HandleMapDataLoaded()
    {
        _runtime?.OnMapDataReconstructed();
    }

    private void Update()
    {
        _runtime?.Tick(UnityEngine.Time.unscaledDeltaTime);
    }

    private void OnApplicationQuit()
    {
        _runtime?.SaveAll();
    }

    private void OnDestroy()
    {
        MinimapManager.OnVanillaMapAvailable -= HandleMapAvailable;
        MinimapManager.OnVanillaMapDataLoaded -= HandleMapDataLoaded;
        _runtime?.Dispose();
        _runtime = null;

        // Last, so runtime teardown failures are still captured; its own
        // flush is bounded and its sender is a background thread.
        _crashHub?.Dispose();
        _crashHub = null;
    }
}
