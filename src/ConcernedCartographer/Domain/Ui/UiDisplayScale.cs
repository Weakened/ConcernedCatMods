using System;

namespace TheConcernedCat.ConcernedCartographer.Ui;

/// <summary>Jötunn's CustomGUIFront canvas — the parent every Concerned
/// Cartographer side panel (Atlas Drawer, Pin Workbench, the CcSidePanel
/// family: System Markers/Settings/Routes/Share/Survey) is built on — never
/// sets CanvasScaler.uiScaleMode, so it stays at Unity's default
/// ConstantPixelSize. Those panels are therefore sized in literal screen
/// pixels with no relationship to the player's actual render resolution, and
/// shrink proportionally on any display larger than the 1920x1080 reference
/// they were designed against. Accessibility.UiScale's manual 0.8-1.6 range
/// exists for personal preference and cannot alone compensate for that gap.
///
/// This is NOT the whole story for every Cartographer panel: the Pin Palette
/// ("New Marker") is parented on Minimap.instance.m_largeRoot — Valheim's own
/// map canvas, which already scales correctly with resolution — so it must
/// NOT be run through this baseline (doing so would double-scale it). Only
/// panels parented on CustomGUIFront need it.</summary>
public static class UiDisplayScale
{
    public const float ReferenceWidth = 1920f;
    public const float ReferenceHeight = 1080f;

    /// <summary>Upper bound on the automatic baseline, kept well short of
    /// unbounded so an unusual canvas size cannot grow panels without
    /// limit.</summary>
    public const float MaxDisplayScale = 2.5f;

    /// <summary>Derives a baseline multiplier from the live canvas size,
    /// floored at 1 so a canvas at or below the 1920x1080 reference is never
    /// shrunk below the sizes every affected panel was designed at, and
    /// capped at <see cref="MaxDisplayScale"/>. Any non-positive or
    /// non-finite input reads as "unknown" and returns 1, so a bad
    /// width/height reading can never produce a pathological scale.</summary>
    public static float ResolveBaseline(float canvasWidth, float canvasHeight)
    {
        if (!IsUsableDimension(canvasWidth) || !IsUsableDimension(canvasHeight))
        {
            return 1f;
        }

        float ratio = Math.Min(canvasWidth / ReferenceWidth, canvasHeight / ReferenceHeight);
        return Math.Min(MaxDisplayScale, Math.Max(1f, ratio));
    }

    /// <summary>The full fix for a CustomGUIFront-parented panel: the
    /// automatic display baseline (above) with the player's own
    /// Accessibility.UiScale preference layered on top exactly as before.
    /// At the 1920x1080 reference resolution this is byte-identical to
    /// passing <paramref name="userPreference"/> straight through, so it
    /// changes nothing for anyone already at a normal resolution.</summary>
    public static float ResolveEffectiveScale(float canvasWidth, float canvasHeight, float userPreference)
    {
        return ResolveBaseline(canvasWidth, canvasHeight) * userPreference;
    }

    private static bool IsUsableDimension(float value)
    {
        return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
