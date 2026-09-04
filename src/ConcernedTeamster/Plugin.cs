using BepInEx;
using TheConcernedCat.ConcernedTeamster.Domain;

namespace TheConcernedCat.ConcernedTeamster;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(Jotunn.Main.ModGuid)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.theconcernedcat.valheim.concernedteamster";
    public const string PluginName = "Concerned Teamster";
    public const string PluginVersion = "0.1.0";

    private void Awake()
    {
        TeamsterSettings settings = TeamsterSettings.Bind(Config);

        Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
        LogEnvironmentBanner();
        Logger.LogInfo(
            "Effective config: " +
            $"Enabled={settings.Enabled.Value}, " +
            $"DebugLogging={settings.DebugLogging.Value}.");

        // CT-001 is the product bootstrap: identity, config, and lifecycle
        // only. Cart adapters and telemetry arrive with CT-002/CT-003 and no
        // gameplay observation or mutation happens before them.
    }

    private void LogEnvironmentBanner()
    {
        string bepInExVersion = EnvironmentBanner.Unknown;
        string jotunnVersion = EnvironmentBanner.Unknown;
        string unityVersion = EnvironmentBanner.Unknown;
        try
        {
            bepInExVersion = typeof(BaseUnityPlugin).Assembly.GetName().Version?.ToString() ?? EnvironmentBanner.Unknown;
            jotunnVersion = typeof(Jotunn.Main).Assembly.GetName().Version?.ToString() ?? EnvironmentBanner.Unknown;
            unityVersion = UnityEngine.Application.unityVersion;
        }
        catch
        {
            // Version labels are metadata only; the banner still prints.
        }

        Logger.LogInfo(EnvironmentBanner.Compose(
            "ConcernedTeamster@" + ResolveInformationalVersion(),
            ResolveGameVersion(),
            unityVersion,
            bepInExVersion,
            jotunnVersion));
    }

    /// <summary>The release identity including the build commit (the SDK
    /// stamps InformationalVersion as "0.1.0+&lt;sha&gt;"), so a log excerpt
    /// identifies the exact binary and nothing about the player.</summary>
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

    private static string ResolveGameVersion()
    {
        // Version.GetVersionString() bound at compile time can throw
        // MissingMethodException when the publicized reference assembly and
        // the live game assembly disagree (observed on Concerned
        // Cartographer), so the game version is resolved reflectively.
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

                return method.Invoke(null, arguments) as string ?? EnvironmentBanner.Unknown;
            }
        }
        catch
        {
            // The version banner is cosmetic; never fail startup over it.
        }

        return EnvironmentBanner.Unknown;
    }
}
