using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.Ui;

/// <summary>Clamped UI scale factor (CT-033), applied uniformly to a whole
/// panel via its root transform rather than to individual dimensions inside
/// it. Uniform scaling of a parent transform carries every child's relative
/// position and size along with it, so a panel that does not clip its own
/// content at one scale cannot newly clip it at another — the property is
/// structural, not something each panel has to re-prove.</summary>
public static class UiScaleOptions
{
    public const float DefaultScale = 1.0f;
    public const float MinScale = 0.8f;

    /// <summary>Chosen so the tallest panel (Trip History, 760 units) stays
    /// under a conservative 1080-unit reference canvas height even at
    /// maximum scale (988 &lt; 1080) — see
    /// <c>AccessibilityTests.MaxScale_TallestPanelStaysUnderConservativeReferenceCanvasHeight</c>
    /// and ACCESSIBILITY.md. The 1080 figure is a commonly-cited Valheim UI
    /// reference height, not independently verified against Jötunn's actual
    /// canvas setup on this machine.</summary>
    public const float MaxScale = 1.3f;

    /// <summary>NaN/Infinity fall back to the default (matches
    /// <c>WarningOptions.CreateClamped</c>'s pattern); everything else
    /// clamps into [MinScale, MaxScale].</summary>
    public static float Clamp(float scale)
    {
        if (float.IsNaN(scale) || float.IsInfinity(scale))
        {
            return DefaultScale;
        }

        return Math.Min(MaxScale, Math.Max(MinScale, scale));
    }

    /// <summary>Jötunn's <c>CustomGUIFront</c> canvas (where every Teamster
    /// panel is parented) never sets <c>CanvasScaler.uiScaleMode</c>, so it
    /// stays at Unity's default <c>ConstantPixelSize</c> — panels are sized
    /// in literal screen pixels with no relationship to the player's actual
    /// render resolution. On a high-resolution or high-DPI display that
    /// makes every panel far smaller than intended, and <see cref="MaxScale"/>
    /// alone (1.3x) cannot compensate. This reference width/height pair is
    /// the 1920x1080 canvas the panel sizes were designed against.</summary>
    public const float ReferenceWidth = 1920f;

    public const float ReferenceHeight = 1080f;

    /// <summary>Upper bound on the automatic display baseline (below), kept
    /// well short of unbounded so an unusual canvas size cannot blow panels
    /// up without limit.</summary>
    public const float MaxDisplayScale = 2.5f;

    /// <summary>The automatic part of the fix: derives a baseline multiplier
    /// from the live canvas size, floored at 1 so a canvas at or below the
    /// 1920x1080 reference is never shrunk below the sizes every panel was
    /// designed at (that case is already covered by <see cref="MaxScale"/>'s
    /// own margin), and capped at <see cref="MaxDisplayScale"/>. Any
    /// non-positive or non-finite input reads as "unknown" and returns 1, so
    /// a bad Screen.width/height reading (Unity Editor test host, or a
    /// resolution query before the first frame) can never crash or return a
    /// pathological scale.</summary>
    public static float ResolveDisplayBaseline(float canvasWidth, float canvasHeight)
    {
        if (!IsUsableDimension(canvasWidth) || !IsUsableDimension(canvasHeight))
        {
            return DefaultScale;
        }

        float ratio = Math.Min(canvasWidth / ReferenceWidth, canvasHeight / ReferenceHeight);
        return Math.Min(MaxDisplayScale, Math.Max(DefaultScale, ratio));
    }

    /// <summary>The full fix: the automatic display baseline (above), with
    /// the player's own accessibility preference (already clamped by the
    /// caller via <see cref="Clamp"/>) layered on top exactly as before —
    /// <see cref="MaxScale"/>'s panel-height margin is computed against the
    /// baseline case (ratio clamped to 1) and still holds unchanged for it;
    /// the baseline only grows on a canvas taller than the 1080 reference,
    /// which has the matching extra headroom.</summary>
    public static float ResolveEffectiveScale(float canvasWidth, float canvasHeight, float userPreference)
    {
        return ResolveDisplayBaseline(canvasWidth, canvasHeight) * Clamp(userPreference);
    }

    private static bool IsUsableDimension(float value)
    {
        return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
