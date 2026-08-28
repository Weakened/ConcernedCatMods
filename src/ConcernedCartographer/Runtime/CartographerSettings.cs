using BepInEx.Configuration;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

internal sealed class CartographerSettings
{
    private CartographerSettings(
        ConfigEntry<bool> enabled,
        ConfigEntry<bool> captureConstructionActions,
        ConfigEntry<bool> reconcileTerrainChanges,
        ConfigEntry<bool> recoverLoadedChunks,
        ConfigEntry<int> recoveryBudgetCellsPerFrame,
        ConfigEntry<float> sampleIntervalSeconds,
        ConfigEntry<float> minimumPointSpacingMeters,
        ConfigEntry<float> maximumStrokeGapMeters,
        ConfigEntry<float> duplicateSuppressionMeters,
        ConfigEntry<float> autosaveIntervalSeconds,
        ConfigEntry<float> paintThreshold,
        ConfigEntry<int> paintSampleRadius,
        ConfigEntry<int> lineWidthPixels,
        ConfigEntry<bool> debugLogging,
        ConfigEntry<bool> drawCalibrationMarkers,
        ConfigEntry<KeyCode> workbenchHotkey,
        ConfigEntry<KeyCode> drawerHotkey,
        ConfigEntry<bool> drawerShowDirt,
        ConfigEntry<bool> drawerShowPaved,
        ConfigEntry<bool> drawerShowPins,
        ConfigEntry<bool> drawerCluster,
        ConfigEntry<KeyCode> quickPinHotkey,
        ConfigEntry<float> quickPinDuplicateRadius,
        ConfigEntry<bool> surveyRulesEnabled,
        ConfigEntry<float> surveyScanIntervalSeconds,
        ConfigEntry<float> surveyScanRadius,
        ConfigEntry<float> surveyBaseExclusionRadius,
        ConfigEntry<int> surveyMaxObservations,
        ConfigEntry<KeyCode> routeDrawModifier,
        ConfigEntry<float> routeEraseRadius,
        ConfigEntry<float> routeSnapRadius,
        ConfigEntry<float> routeOnRoadTolerance,
        ConfigEntry<float> routeOffRoadSpeed,
        ConfigEntry<float> routeOnRoadSpeed,
        ConfigEntry<float> uiScale,
        ConfigEntry<bool> highContrast,
        ConfigEntry<string> workbenchGamepadButton,
        ConfigEntry<string> drawerGamepadButton,
        ConfigEntry<bool> enhancedPinPalette,
        ConfigEntry<bool> showVanillaPinPalette,
        ConfigEntry<CrashConsentState> crashReportingConsent,
        ConfigEntry<int> acceptedPrivacyPolicyVersion,
        ConfigEntry<string> sentryDsn)
    {
        Enabled = enabled;
        CaptureConstructionActions = captureConstructionActions;
        ReconcileTerrainChanges = reconcileTerrainChanges;
        RecoverLoadedChunks = recoverLoadedChunks;
        RecoveryBudgetCellsPerFrame = recoveryBudgetCellsPerFrame;
        SampleIntervalSeconds = sampleIntervalSeconds;
        MinimumPointSpacingMeters = minimumPointSpacingMeters;
        MaximumStrokeGapMeters = maximumStrokeGapMeters;
        DuplicateSuppressionMeters = duplicateSuppressionMeters;
        AutosaveIntervalSeconds = autosaveIntervalSeconds;
        PaintThreshold = paintThreshold;
        PaintSampleRadius = paintSampleRadius;
        LineWidthPixels = lineWidthPixels;
        DebugLogging = debugLogging;
        DrawCalibrationMarkers = drawCalibrationMarkers;
        WorkbenchHotkey = workbenchHotkey;
        DrawerHotkey = drawerHotkey;
        DrawerShowDirt = drawerShowDirt;
        DrawerShowPaved = drawerShowPaved;
        DrawerShowPins = drawerShowPins;
        DrawerCluster = drawerCluster;
        QuickPinHotkey = quickPinHotkey;
        QuickPinDuplicateRadius = quickPinDuplicateRadius;
        SurveyRulesEnabled = surveyRulesEnabled;
        SurveyScanIntervalSeconds = surveyScanIntervalSeconds;
        SurveyScanRadius = surveyScanRadius;
        SurveyBaseExclusionRadius = surveyBaseExclusionRadius;
        SurveyMaxObservations = surveyMaxObservations;
        RouteDrawModifier = routeDrawModifier;
        RouteEraseRadius = routeEraseRadius;
        RouteSnapRadius = routeSnapRadius;
        RouteOnRoadTolerance = routeOnRoadTolerance;
        RouteOffRoadSpeed = routeOffRoadSpeed;
        RouteOnRoadSpeed = routeOnRoadSpeed;
        UiScale = uiScale;
        HighContrast = highContrast;
        WorkbenchGamepadButton = workbenchGamepadButton;
        DrawerGamepadButton = drawerGamepadButton;
        EnhancedPinPalette = enhancedPinPalette;
        ShowVanillaPinPalette = showVanillaPinPalette;
        CrashReportingConsent = crashReportingConsent;
        AcceptedPrivacyPolicyVersion = acceptedPrivacyPolicyVersion;
        SentryDsn = sentryDsn;
    }

    public ConfigEntry<bool> Enabled { get; }
    public ConfigEntry<bool> CaptureConstructionActions { get; }
    public ConfigEntry<bool> ReconcileTerrainChanges { get; }
    public ConfigEntry<bool> RecoverLoadedChunks { get; }
    public ConfigEntry<int> RecoveryBudgetCellsPerFrame { get; }
    public ConfigEntry<float> SampleIntervalSeconds { get; }
    public ConfigEntry<float> MinimumPointSpacingMeters { get; }
    public ConfigEntry<float> MaximumStrokeGapMeters { get; }
    public ConfigEntry<float> DuplicateSuppressionMeters { get; }
    public ConfigEntry<float> AutosaveIntervalSeconds { get; }
    public ConfigEntry<float> PaintThreshold { get; }
    public ConfigEntry<int> PaintSampleRadius { get; }
    public ConfigEntry<int> LineWidthPixels { get; }
    public ConfigEntry<bool> DebugLogging { get; }
    public ConfigEntry<bool> DrawCalibrationMarkers { get; }
    public ConfigEntry<KeyCode> WorkbenchHotkey { get; }
    public ConfigEntry<KeyCode> DrawerHotkey { get; }
    public ConfigEntry<bool> DrawerShowDirt { get; }
    public ConfigEntry<bool> DrawerShowPaved { get; }
    public ConfigEntry<bool> DrawerShowPins { get; }
    public ConfigEntry<bool> DrawerCluster { get; }
    public ConfigEntry<KeyCode> QuickPinHotkey { get; }
    public ConfigEntry<float> QuickPinDuplicateRadius { get; }
    public ConfigEntry<bool> SurveyRulesEnabled { get; }
    public ConfigEntry<float> SurveyScanIntervalSeconds { get; }
    public ConfigEntry<float> SurveyScanRadius { get; }
    public ConfigEntry<float> SurveyBaseExclusionRadius { get; }
    public ConfigEntry<int> SurveyMaxObservations { get; }
    public ConfigEntry<KeyCode> RouteDrawModifier { get; }
    public ConfigEntry<float> RouteEraseRadius { get; }
    public ConfigEntry<float> RouteSnapRadius { get; }
    public ConfigEntry<float> RouteOnRoadTolerance { get; }
    public ConfigEntry<float> RouteOffRoadSpeed { get; }
    public ConfigEntry<float> RouteOnRoadSpeed { get; }
    public ConfigEntry<float> UiScale { get; }
    public ConfigEntry<bool> HighContrast { get; }
    public ConfigEntry<string> WorkbenchGamepadButton { get; }
    public ConfigEntry<string> DrawerGamepadButton { get; }
    public ConfigEntry<bool> EnhancedPinPalette { get; }
    public ConfigEntry<bool> ShowVanillaPinPalette { get; }
    public ConfigEntry<CrashConsentState> CrashReportingConsent { get; }
    public ConfigEntry<int> AcceptedPrivacyPolicyVersion { get; }
    public ConfigEntry<string> SentryDsn { get; }

    public static CartographerSettings Bind(ConfigFile config)
    {
        return new CartographerSettings(
            config.Bind("General", "Enabled", true, "Enable road surveying and map overlays."),
            config.Bind("Sources", "CaptureConstructionActions", true,
                "Record roads from your own successful hoe/cultivator/stonecutter paint actions immediately, without walking them."),
            config.Bind("Sources", "ReconcileTerrainChanges", true,
                "When you cultivate, reset, or repaint terrain, remove the covered road ink from the atlas so no ghost roads remain."),
            config.Bind("Sources", "RecoverLoadedChunks", true,
                "Recover narrow road paint from already-loaded terrain near you, limited to map areas you have explored."),
            config.Bind("Sources", "RecoveryBudgetCellsPerFrame", 256, new ConfigDescription(
                "Terrain paint cells examined per frame by chunk recovery. Higher recovers faster at more CPU cost.",
                new AcceptableValueRange<int>(32, 8192))),
            config.Bind("Survey", "SampleIntervalSeconds", 0.35f, new ConfigDescription(
                "Seconds between terrain samples.",
                new AcceptableValueRange<float>(0.10f, 5.0f))),
            config.Bind("Survey", "MinimumPointSpacingMeters", 1.5f, new ConfigDescription(
                "Minimum horizontal distance before a new road point is stored.",
                new AcceptableValueRange<float>(0.5f, 20.0f))),
            config.Bind("Survey", "MaximumStrokeGapMeters", 8.0f, new ConfigDescription(
                "A larger gap starts a new stroke instead of drawing a long connector.",
                new AcceptableValueRange<float>(2.0f, 100.0f))),
            config.Bind("Survey", "DuplicateSuppressionMeters", 2.0f, new ConfigDescription(
                "Skip samples within this distance of already-recorded road ink of the same kind, so re-walking a road never grows the atlas. 0 disables suppression; values above ~3 may also suppress tight hairpin switchbacks.",
                new AcceptableValueRange<float>(0.0f, 10.0f))),
            config.Bind("Persistence", "AutosaveIntervalSeconds", 15.0f, new ConfigDescription(
                "Seconds between dirty-atlas autosaves.",
                new AcceptableValueRange<float>(5.0f, 300.0f))),
            config.Bind("Detection", "PaintThreshold", 0.40f, new ConfigDescription(
                "Minimum averaged red/blue paint value used to identify roads.",
                new AcceptableValueRange<float>(0.10f, 0.95f))),
            config.Bind("Detection", "PaintSampleRadius", 1, new ConfigDescription(
                "Terrain paint pixels sampled around the player (0 is a single pixel).",
                new AcceptableValueRange<int>(0, 3))),
            config.Bind("Map", "LineWidthPixels", 1, new ConfigDescription(
                "Road line width on the map overlay, in map texels. One texel covers ~11.6 m of world, so widths above 1 make nearby roads merge into blobs.",
                new AcceptableValueRange<int>(1, 6))),
            config.Bind("Diagnostics", "DebugLogging", false, "Write diagnostic road classification messages."),
            config.Bind("Diagnostics", "DrawCalibrationMarkers", false,
                "Draw fixed calibration crosses into the dirt overlay at world origin (magenta), +128m east (yellow), and +128m north (cyan) to verify overlay/map alignment."),
            config.Bind("Workbench", "WorkbenchHotkey", KeyCode.P,
                "Key that opens the Pin Workbench for the pin under the cursor while the large map is open."),
            config.Bind("Drawer", "DrawerHotkey", KeyCode.L,
                "Key that toggles the Atlas Drawer (layers, search, saved views) while the large map is open."),
            config.Bind("Drawer", "ShowDirtRoads", true, "Show the dirt-road layer."),
            config.Bind("Drawer", "ShowPavedRoads", true, "Show the paved-road layer."),
            config.Bind("Drawer", "ShowPins", true, "Show managed pins on the map."),
            config.Bind("Drawer", "Clustering", true,
                "Fold crowded pins into cluster markers when zoomed out (display only; never changes stored pins)."),
            config.Bind("Workbench", "QuickPinHotkey", KeyCode.F7,
                "Key that pins the object you are looking at (set to None to disable). Never pins creatures."),
            config.Bind("Workbench", "QuickPinDuplicateRadius", 25f, new ConfigDescription(
                "Skip a quick pin when a same-named pin already exists within this many meters (0 disables the check).",
                new AcceptableValueRange<float>(0f, 200f))),
            config.Bind("Survey", "SurveyRulesEnabled", false,
                "Opt-in survey rules: nearby loaded objects matching survey-rules.tsv become reviewable observations (never pins directly)."),
            config.Bind("Survey", "SurveyScanIntervalSeconds", 10f, new ConfigDescription(
                "Seconds between survey scans.", new AcceptableValueRange<float>(2f, 60f))),
            config.Bind("Survey", "SurveyScanRadius", 40f, new ConfigDescription(
                "Survey scan radius around the player in meters.", new AcceptableValueRange<float>(10f, 100f))),
            config.Bind("Survey", "SurveyBaseExclusionRadius", 30f, new ConfigDescription(
                "No observations within this distance of a pin categorized/tagged Base (0 disables).",
                new AcceptableValueRange<float>(0f, 100f))),
            config.Bind("Survey", "SurveyMaxObservations", 200, new ConfigDescription(
                "Hard cap on pending survey observations.", new AcceptableValueRange<int>(10, 1000))),
            config.Bind("Routes", "RouteDrawModifier", KeyCode.LeftShift,
                "Modifier held with LeftClick on the large map for route draw/erase/waypoint modes (avoids vanilla map-drag conflicts)."),
            config.Bind("Routes", "RouteEraseRadius", 8f, new ConfigDescription(
                "Route erase brush radius in meters.", new AcceptableValueRange<float>(1f, 30f))),
            config.Bind("Routes", "RouteSnapRadius", 15f, new ConfigDescription(
                "Waypoints snap to roads within this many meters.", new AcceptableValueRange<float>(2f, 50f))),
            config.Bind("Routes", "RouteOnRoadTolerance", 6f, new ConfigDescription(
                "A route counts as on-road when within this distance of recorded road ink.",
                new AcceptableValueRange<float>(1f, 15f))),
            config.Bind("Routes", "RouteOffRoadSpeed", 2.5f, new ConfigDescription(
                "Off-road travel speed (m/s) for time estimates.", new AcceptableValueRange<float>(0.5f, 15f))),
            config.Bind("Routes", "RouteOnRoadSpeed", 5f, new ConfigDescription(
                "On-road travel speed (m/s) for time estimates.", new AcceptableValueRange<float>(0.5f, 15f))),
            config.Bind("Accessibility", "UiScale", 1f, new ConfigDescription(
                "Scale multiplier for Concerned Cartographer panels.", new AcceptableValueRange<float>(0.8f, 1.6f))),
            config.Bind("Accessibility", "HighContrast", false,
                "High-contrast map ink: near-black dirt, near-white paved, brighter route colors. Kinds stay distinguishable without color (dashed/dotted styles, icons, labels)."),
            config.Bind("Accessibility", "WorkbenchGamepadButton", "",
                "ZInput button name that opens the Pin Workbench (e.g. JoyLStick). Empty disables; conflicts are avoided by explicit opt-in."),
            config.Bind("Accessibility", "DrawerGamepadButton", "",
                "ZInput button name that toggles the Atlas Drawer. Empty disables."),
            config.Bind("Pins", "EnhancedPinPalette", true,
                "Show the Concerned Cartographer marker palette on the large map. Markers created through it are managed from birth (no upgrade step)."),
            config.Bind("Pins", "ShowVanillaPinPalette", false,
                "Keep Valheim's own five pin-icon buttons visible alongside (or instead of) the enhanced palette. Automatically treated as true when a known conflicting pin manager is installed."),
            config.Bind("Privacy", "SendCrashReports", CrashConsentState.Unknown,
                "Send anonymous crash reports when Concerned Cartographer hits an internal error. Unknown = not asked yet (a one-time dialog appears on the first large-map open); nothing is ever sent while Unknown or Disabled. What is and is not collected: PRIVACY.md. No gameplay analytics, ever."),
            config.Bind("Privacy", "AcceptedPrivacyPolicyVersion", 0,
                "Internal: the crash-reporting policy version the player answered. Re-prompts only if the collected data categories materially change in a future release."),
            config.Bind("Privacy", "SentryDsn", "",
                "Advanced: override the embedded crash-report ingestion DSN (a public event-submission key). Empty uses the built-in value; if both are empty, crash reporting is fully inert. NEVER put a Sentry auth token here."));
    }
}
