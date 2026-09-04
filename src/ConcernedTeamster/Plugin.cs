using BepInEx;
using TheConcernedCat.ConcernedTeamster.Adapters;
using TheConcernedCat.ConcernedTeamster.Domain;
using TheConcernedCat.ConcernedTeamster.Domain.Capabilities;

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

        LogCartCapability(settings);

        // CT-002 delivers the read-only cart adapter and its startup
        // capability probe only. Telemetry sampling and panels arrive with
        // CT-003..CT-005; nothing observes or mutates carts yet.
    }

    /// <summary>Runs the cart capability probe once and reports the outcome
    /// as one line: INFO when every verified member is present, one
    /// actionable WARN naming each missing member otherwise. The probe reads
    /// type metadata only — no world or cart state is touched.</summary>
    private void LogCartCapability(TeamsterSettings settings)
    {
        GameCapabilityReport report = CartAdapter.ProbeCapability();
        if (report.Enabled)
        {
            Logger.LogInfo(
                $"Cart telemetry capability ENABLED: {report.VerifiedMembers.Count} game members verified.");
            if (settings.DebugLogging.Value)
            {
                Logger.LogDebug("Verified cart members: " + string.Join(", ", report.VerifiedMembers));
            }
        }
        else
        {
            Logger.LogWarning(
                "Cart telemetry capability DISABLED: missing " +
                string.Join(", ", report.MissingMembers) +
                ". A Valheim update likely changed cart internals; cart features stay off until a " +
                "Concerned Teamster update, and everything else keeps working.");
        }
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
            GameVersionResolver.Resolve(),
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
}
