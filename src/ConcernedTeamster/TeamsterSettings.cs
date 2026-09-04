using BepInEx.Configuration;

namespace TheConcernedCat.ConcernedTeamster;

/// <summary>Configuration binding for Concerned Teamster (CT-001). Only
/// settings whose behavior already exists are bound; feature settings arrive
/// with their features so the config file never advertises unimplemented
/// behavior.</summary>
internal sealed class TeamsterSettings
{
    private TeamsterSettings(
        ConfigEntry<bool> enabled,
        ConfigEntry<bool> debugLogging)
    {
        Enabled = enabled;
        DebugLogging = debugLogging;
    }

    public ConfigEntry<bool> Enabled { get; }
    public ConfigEntry<bool> DebugLogging { get; }

    public static TeamsterSettings Bind(ConfigFile config)
    {
        return new TeamsterSettings(
            config.Bind("General", "Enabled", true,
                "Master switch for Concerned Teamster cart telemetry features. Disabling leaves carts fully vanilla; the mod never changes cart physics by default either way."),
            config.Bind("Diagnostics", "DebugLogging", false,
                "Write diagnostic messages for Teamster feature development. Never enables per-frame logging."));
    }
}
