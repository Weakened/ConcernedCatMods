using BepInEx;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Runtime;

namespace TheConcernedCat.ConcernedCartographer;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(Jotunn.Main.ModGuid)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.theconcernedcat.valheim.concernedcartographer";
    public const string PluginName = "Concerned Cartographer";
    public const string PluginVersion = "0.2.0";

    private CartographerRuntime? _runtime;

    private void Awake()
    {
        CartographerSettings settings = CartographerSettings.Bind(Config);
        _runtime = new CartographerRuntime(settings, Logger);

        MinimapManager.OnVanillaMapAvailable += HandleMapAvailable;
        Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
        LogEnvironment(settings);
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
            Logger.LogInfo(
                "Effective config (out-of-range values are clamped to documented ranges): " +
                $"Enabled={settings.Enabled.Value}, " +
                $"CaptureConstructionActions={settings.CaptureConstructionActions.Value}, " +
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
            Logger.LogWarning($"Could not record environment versions: {exception.Message}");
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

    private void Update()
    {
        _runtime?.Tick(UnityEngine.Time.unscaledDeltaTime);
    }

    private void OnApplicationQuit()
    {
        _runtime?.SaveIfDirty();
    }

    private void OnDestroy()
    {
        MinimapManager.OnVanillaMapAvailable -= HandleMapAvailable;
        _runtime?.Dispose();
        _runtime = null;
    }
}
