using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.Warnings;

/// <summary>Clamped warning configuration (CT-009). Only the steep-grade
/// caution threshold is a knob; the hysteresis band and the fall hold are
/// constants so config edits cannot create a flickering warning.</summary>
public sealed class WarningOptions
{
    public const float DefaultSteepGradeCautionPercent = 18f;
    public const float MinSteepGradeCautionPercent = 5f;
    public const float MaxSteepGradeCautionPercent = 60f;

    /// <summary>How far the smoothed grade must fall below the enter
    /// threshold before the steep-grade cause releases.</summary>
    public const float ExitBandPercent = 3f;

    /// <summary>A level only falls after this long of continuously lower
    /// evaluations (clock = telemetry sample times, so tests are exact).</summary>
    public const double FallHoldSeconds = 4.0;

    private WarningOptions(float steepGradeCautionPercent, bool panelWarningsEnabled, bool hudHintsEnabled)
    {
        SteepGradeCautionPercent = steepGradeCautionPercent;
        PanelWarningsEnabled = panelWarningsEnabled;
        HudHintsEnabled = hudHintsEnabled;
    }

    public float SteepGradeCautionPercent { get; }

    public bool PanelWarningsEnabled { get; }

    public bool HudHintsEnabled { get; }

    public static WarningOptions CreateClamped(
        float steepGradeCautionPercent, bool panelWarningsEnabled, bool hudHintsEnabled)
    {
        float clamped = float.IsNaN(steepGradeCautionPercent) || float.IsInfinity(steepGradeCautionPercent)
            ? DefaultSteepGradeCautionPercent
            : Math.Min(MaxSteepGradeCautionPercent, Math.Max(MinSteepGradeCautionPercent, steepGradeCautionPercent));
        return new WarningOptions(clamped, panelWarningsEnabled, hudHintsEnabled);
    }
}
