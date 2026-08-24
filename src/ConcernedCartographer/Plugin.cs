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
