using BepInEx.Configuration;
using TheConcernedCat.ConcernedTeamster.Domain.Carts;
using TheConcernedCat.ConcernedTeamster.Domain.Warnings;

namespace TheConcernedCat.ConcernedTeamster;

/// <summary>Configuration binding for Concerned Teamster (CT-001). Only
/// settings whose behavior already exists are bound; feature settings arrive
/// with their features so the config file never advertises unimplemented
/// behavior. Telemetry bounds (CT-003) mirror the domain's hard limits: the
/// config UI clamps via AcceptableValueRange and the sampler re-clamps in
/// code, so no config edit can remove the budget.</summary>
internal sealed class TeamsterSettings
{
    private TeamsterSettings(
        ConfigEntry<bool> enabled,
        ConfigEntry<bool> debugLogging,
        ConfigEntry<float> sampleIntervalSeconds,
        ConfigEntry<float> searchRadiusMeters,
        ConfigEntry<int> maxCartsPerTick,
        ConfigEntry<int> maxTrackedCarts,
        ConfigEntry<KeyboardShortcut> panelShortcut,
        ConfigEntry<bool> panelWarningsEnabled,
        ConfigEntry<bool> hudWarningHintsEnabled,
        ConfigEntry<float> steepGradeCautionPercent,
        ConfigEntry<int> riskLookaheadPoints)
    {
        Enabled = enabled;
        DebugLogging = debugLogging;
        SampleIntervalSeconds = sampleIntervalSeconds;
        SearchRadiusMeters = searchRadiusMeters;
        MaxCartsPerTick = maxCartsPerTick;
        MaxTrackedCarts = maxTrackedCarts;
        PanelShortcut = panelShortcut;
        PanelWarningsEnabled = panelWarningsEnabled;
        HudWarningHintsEnabled = hudWarningHintsEnabled;
        SteepGradeCautionPercent = steepGradeCautionPercent;
        RiskLookaheadPoints = riskLookaheadPoints;
    }

    public ConfigEntry<bool> Enabled { get; }
    public ConfigEntry<bool> DebugLogging { get; }
    public ConfigEntry<float> SampleIntervalSeconds { get; }
    public ConfigEntry<float> SearchRadiusMeters { get; }
    public ConfigEntry<int> MaxCartsPerTick { get; }
    public ConfigEntry<int> MaxTrackedCarts { get; }
    public ConfigEntry<KeyboardShortcut> PanelShortcut { get; }
    public ConfigEntry<bool> PanelWarningsEnabled { get; }
    public ConfigEntry<bool> HudWarningHintsEnabled { get; }
    public ConfigEntry<float> SteepGradeCautionPercent { get; }
    public ConfigEntry<int> RiskLookaheadPoints { get; }

    public static TeamsterSettings Bind(ConfigFile config)
    {
        return new TeamsterSettings(
            config.Bind("General", "Enabled", true,
                "Master switch for Concerned Teamster cart telemetry features. Disabling leaves carts fully vanilla; the mod never changes cart physics by default either way."),
            config.Bind("Diagnostics", "DebugLogging", false,
                "Write diagnostic messages for Teamster feature development. Never enables per-frame logging."),
            config.Bind("Telemetry", "SampleIntervalSeconds",
                TelemetrySamplerOptions.DefaultSampleIntervalSeconds,
                new ConfigDescription(
                    "Seconds between cart telemetry samples. Read-only observation of nearby carts; between samples the cost is one comparison per frame.",
                    new AcceptableValueRange<float>(
                        TelemetrySamplerOptions.MinSampleIntervalSeconds,
                        TelemetrySamplerOptions.MaxSampleIntervalSeconds))),
            config.Bind("Telemetry", "SearchRadiusMeters",
                TelemetrySamplerOptions.DefaultSearchRadiusMeters,
                new ConfigDescription(
                    "Carts farther than this from you are not observed.",
                    new AcceptableValueRange<float>(
                        TelemetrySamplerOptions.MinSearchRadiusMeters,
                        TelemetrySamplerOptions.MaxSearchRadiusMeters))),
            config.Bind("Telemetry", "MaxCartsPerTick",
                TelemetrySamplerOptions.DefaultMaxCartsPerTick,
                new ConfigDescription(
                    "Hard budget of carts examined per telemetry tick.",
                    new AcceptableValueRange<int>(
                        TelemetrySamplerOptions.MinMaxCartsPerTick,
                        TelemetrySamplerOptions.MaxMaxCartsPerTick))),
            config.Bind("Telemetry", "MaxTrackedCarts",
                TelemetrySamplerOptions.DefaultMaxTrackedCarts,
                new ConfigDescription(
                    "Hard cap of carts kept in the telemetry table at once.",
                    new AcceptableValueRange<int>(
                        TelemetrySamplerOptions.MinMaxTrackedCarts,
                        TelemetrySamplerOptions.MaxMaxTrackedCarts))),
            config.Bind("Ui", "PanelShortcut", KeyboardShortcut.Empty,
                "Optional keyboard accelerator that toggles the Cart Status panel. The visible Cart button is always the primary path; leave empty for no shortcut."),
            config.Bind("Warnings", "PanelWarningsEnabled", true,
                "Show load/grade warnings in the Cart Status panel."),
            config.Bind("Warnings", "HudWarningHintsEnabled", false,
                "Also show the current warning as a small HUD hint under the Cart button while pulling. Off by default: the panel is the primary surface and HUD space is precious; enable if you haul with the panel closed."),
            config.Bind("Warnings", "SteepGradeCautionPercent",
                WarningOptions.DefaultSteepGradeCautionPercent,
                new ConfigDescription(
                    "Smoothed climbing grade (percent) that raises the steep-climb caution. Exit hysteresis and anti-flicker hold are fixed so no configuration can make warnings spam.",
                    new AcceptableValueRange<float>(
                        WarningOptions.MinSteepGradeCautionPercent,
                        WarningOptions.MaxSteepGradeCautionPercent))),
            config.Bind("Risk", "LookaheadPoints",
                Domain.Risk.LookaheadOptions.DefaultPoints,
                new ConfigDescription(
                    "Terrain samples taken ahead of the pulled cart (4 m apart) to rate the upcoming descent. 0 disables lookahead. Each sample is one bounded ground-height read per telemetry tick.",
                    new AcceptableValueRange<int>(
                        Domain.Risk.LookaheadOptions.MinPoints,
                        Domain.Risk.LookaheadOptions.MaxPoints))));
    }
}
