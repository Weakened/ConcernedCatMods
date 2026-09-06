using BepInEx.Configuration;
using TheConcernedCat.ConcernedTeamster.Domain.Carts;
using TheConcernedCat.ConcernedTeamster.Domain.Profiles;
using TheConcernedCat.ConcernedTeamster.Domain.Ui;
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
        ConfigEntry<int> riskLookaheadPoints,
        ConfigEntry<bool> brakeEnabled,
        ConfigEntry<bool> tripsEnabled,
        ConfigEntry<float> tripRecordSpacingSeconds,
        ConfigEntry<int> tripMaxSamplesPerTrip,
        ConfigEntry<int> tripMaxTripsRetained,
        ConfigEntry<float> uiScale,
        ConfigEntry<bool> onboardingDismissed,
        ConfigEntry<ConfigProfile> activeProfile,
        ConfigEntry<ConfigProfile> lastAppliedProfile)
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
        BrakeEnabled = brakeEnabled;
        TripsEnabled = tripsEnabled;
        TripRecordSpacingSeconds = tripRecordSpacingSeconds;
        TripMaxSamplesPerTrip = tripMaxSamplesPerTrip;
        TripMaxTripsRetained = tripMaxTripsRetained;
        UiScale = uiScale;
        OnboardingDismissed = onboardingDismissed;
        ActiveProfile = activeProfile;
        LastAppliedProfile = lastAppliedProfile;
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
    public ConfigEntry<bool> BrakeEnabled { get; }
    public ConfigEntry<bool> TripsEnabled { get; }
    public ConfigEntry<float> TripRecordSpacingSeconds { get; }
    public ConfigEntry<int> TripMaxSamplesPerTrip { get; }
    public ConfigEntry<int> TripMaxTripsRetained { get; }

    /// <summary>Uniform scale applied to every panel's root transform
    /// (CT-033) — text, buttons, and the panel itself all grow or shrink
    /// together, so nothing can clip relative to its own container. Applies
    /// on the panel's next build (world enter or first open), not live to
    /// an already-open panel.</summary>
    public ConfigEntry<float> UiScale { get; }

    /// <summary>Set once, permanently, when the player taps the first-run
    /// onboarding hint (CT-034). Never reset by the mod itself.</summary>
    public ConfigEntry<bool> OnboardingDismissed { get; }

    /// <summary>The player's chosen settings preset (CT-034). Changing this
    /// value re-applies the preset's settings once, on the next plugin
    /// load; individual settings stay independently editable afterward.</summary>
    public ConfigEntry<ConfigProfile> ActiveProfile { get; }

    /// <summary>Internal bookkeeping — do not edit directly. Records which
    /// profile was last actually applied, so <see cref="ActiveProfile"/>
    /// re-applies only when it genuinely changes (CT-034's idempotence
    /// guarantee) instead of overwriting manually-tweaked settings on every
    /// restart.</summary>
    public ConfigEntry<ConfigProfile> LastAppliedProfile { get; }

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
                        Domain.Risk.LookaheadOptions.MaxPoints))),
            config.Bind("Brake", "Enabled", true,
                "The parking brake feature: a visible button that freezes a parked cart you control until you release it. Explicit per-use, always reversible, never saved into the world, and it releases itself on detach distance, world exit, shutdown, or any capability loss. Disable to remove the button entirely."),
            config.Bind("Trips", "Enabled", true,
                "Record hauling trips (position, grade, speed, load while you pull) into Teamster's own per-world sidecar file under BepInEx/config. Never touches Valheim saves; delete the sidecar folder to erase all history."),
            config.Bind("Trips", "RecordSpacingSeconds",
                Domain.Trips.TripRecorderOptions.DefaultRecordSpacingSeconds,
                new ConfigDescription(
                    "Seconds between recorded trip samples while pulling.",
                    new AcceptableValueRange<float>(
                        Domain.Trips.TripRecorderOptions.MinRecordSpacingSeconds,
                        Domain.Trips.TripRecorderOptions.MaxRecordSpacingSeconds))),
            config.Bind("Trips", "MaxSamplesPerTrip",
                Domain.Trips.TripRecorderOptions.DefaultMaxSamplesPerTrip,
                new ConfigDescription(
                    "Hard cap of samples in one trip; longer hauls split into segments.",
                    new AcceptableValueRange<int>(
                        Domain.Trips.TripRecorderOptions.MinMaxSamplesPerTrip,
                        Domain.Trips.TripRecorderOptions.MaxMaxSamplesPerTrip))),
            config.Bind("Trips", "MaxTripsRetained",
                Domain.Trips.TripRecorderOptions.DefaultMaxTripsRetained,
                new ConfigDescription(
                    "Retention: how many trips each world's sidecar keeps; the oldest are pruned first.",
                    new AcceptableValueRange<int>(
                        Domain.Trips.TripRecorderOptions.MinMaxTripsRetained,
                        Domain.Trips.TripRecorderOptions.MaxMaxTripsRetained))),
            config.Bind("Ui", "Scale", UiScaleOptions.DefaultScale,
                new ConfigDescription(
                    "Uniform size of every Teamster panel, button, and font. 1.0 is the default size; " +
                    "takes effect the next time a panel opens (or on world enter for the always-visible " +
                    "Cart button), not live on an already-open panel.",
                    new AcceptableValueRange<float>(UiScaleOptions.MinScale, UiScaleOptions.MaxScale))),
            config.Bind("Onboarding", "Dismissed", false,
                "Set automatically once you dismiss the first-run hint pointing at the Cart button. " +
                "Set back to false to see the hint again."),
            config.Bind("General", "Profile", ConfigProfile.Standard,
                "A documented settings preset: Minimal (telemetry only, warnings/HUD hints/trips off), " +
                "Standard (Teamster's normal defaults), or EverythingObservational (every read-only " +
                "feature on). The parking brake stays off in every profile — enable Brake.Enabled " +
                "yourself if you want it. Changing this re-applies the preset's values once, on the " +
                "next load; your own edits to individual settings are never overwritten again until " +
                "you change this value to something else."),
            config.Bind("General", "LastAppliedProfile", ConfigProfile.Standard,
                "Internal bookkeeping — do not edit. Records which profile was last applied so a " +
                "restart with no Profile change never re-applies it."));
    }
}
