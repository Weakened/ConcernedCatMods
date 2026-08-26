using BepInEx.Configuration;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

internal sealed class CartographerSettings
{
    private CartographerSettings(
        ConfigEntry<bool> enabled,
        ConfigEntry<bool> captureConstructionActions,
        ConfigEntry<float> sampleIntervalSeconds,
        ConfigEntry<float> minimumPointSpacingMeters,
        ConfigEntry<float> maximumStrokeGapMeters,
        ConfigEntry<float> duplicateSuppressionMeters,
        ConfigEntry<float> autosaveIntervalSeconds,
        ConfigEntry<float> paintThreshold,
        ConfigEntry<int> paintSampleRadius,
        ConfigEntry<int> lineWidthPixels,
        ConfigEntry<bool> debugLogging,
        ConfigEntry<bool> drawCalibrationMarkers)
    {
        Enabled = enabled;
        CaptureConstructionActions = captureConstructionActions;
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
    }

    public ConfigEntry<bool> Enabled { get; }
    public ConfigEntry<bool> CaptureConstructionActions { get; }
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

    public static CartographerSettings Bind(ConfigFile config)
    {
        return new CartographerSettings(
            config.Bind("General", "Enabled", true, "Enable road surveying and map overlays."),
            config.Bind("Sources", "CaptureConstructionActions", true,
                "Record roads from your own successful hoe/cultivator/stonecutter paint actions immediately, without walking them."),
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
                "Draw fixed calibration crosses into the dirt overlay at world origin (magenta), +128m east (yellow), and +128m north (cyan) to verify overlay/map alignment."));
    }
}
