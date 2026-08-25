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
    public const string PluginVersion = "0.1.0";

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
            string gameVersion = "unknown";
            try
            {
                gameVersion = global::Version.GetVersionString();
            }
            catch
            {
                // The version API is cosmetic; never fail startup over it.
            }

            Logger.LogInfo(
                $"Environment: Valheim {gameVersion}, Unity {UnityEngine.Application.unityVersion}, " +
                $"BepInEx {bepInExVersion}, Jotunn {jotunnVersion}.");
            Logger.LogInfo(
                "Effective config (out-of-range values are clamped to documented ranges): " +
                $"Enabled={settings.Enabled.Value}, " +
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
