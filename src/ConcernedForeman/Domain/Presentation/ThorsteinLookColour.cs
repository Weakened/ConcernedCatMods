using System;

namespace TheConcernedCat.ConcernedForeman.Domain.Presentation;

/// <summary>A linear RGB triple in the units the game stores skin and hair
/// colour in. Three floats rather than a Unity colour, so the conversion is
/// tested with no game present.</summary>
internal readonly struct LookRgb
{
    internal LookRgb(float r, float g, float b)
    {
        R = r;
        G = g;
        B = b;
    }

    internal float R { get; }

    internal float G { get; }

    internal float B { get; }

    internal static LookRgb White => new LookRgb(1f, 1f, 1f);

    public override string ToString() =>
        "(" + R.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ", "
        + G.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ", "
        + B.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ")";
}

/// <summary>The character-creation screen's own colour endpoints: the four
/// colours and two brightness limits it lerps between. They are Unity
/// inspector values on that screen, so they can only be read off the live
/// game, never derived. <see cref="Observed"/> records which happened.</summary>
internal readonly struct LookPalette
{
    internal LookPalette(
        LookRgb skinLow,
        LookRgb skinHigh,
        LookRgb hairLow,
        LookRgb hairHigh,
        float minimumLevel,
        float maximumLevel,
        bool observed)
    {
        SkinLow = skinLow;
        SkinHigh = skinHigh;
        HairLow = hairLow;
        HairHigh = hairHigh;
        MinimumLevel = minimumLevel;
        MaximumLevel = maximumLevel;
        Observed = observed;
    }

    internal LookRgb SkinLow { get; }

    internal LookRgb SkinHigh { get; }

    internal LookRgb HairLow { get; }

    internal LookRgb HairHigh { get; }

    internal float MinimumLevel { get; }

    internal float MaximumLevel { get; }

    /// <summary>True only when read from a live customization screen this
    /// session.</summary>
    internal bool Observed { get; }

    /// <summary>Nothing could be read: the C# field initialisers of the game's
    /// customization component, which the inspector overrides. Honest "we do
    /// not know the ramp".</summary>
    internal static LookPalette Unobserved => new LookPalette(
        LookRgb.White, LookRgb.White, LookRgb.White, LookRgb.White, 0.1f, 1f, observed: false);
}

/// <summary>Thorstein's colouring: the owner's slider positions, turned into
/// colours through the game's own conversion (#298).
///
/// #298 forbids storing the slider readings as an RGB vector. This is the
/// conversion the character screen performs, transcribed the same way Hulgi's
/// is in Concerned Cartographer:
/// <code>
/// skin = Lerp(skinColor0, skinColor1, skinHue)
/// hair = Lerp(hairColor0, hairColor1, hairTone) * Lerp(hairMinLevel, hairMaxLevel, hairLevel)
/// </code>
/// The readings are approximate, read off a screenshot under campfire light,
/// and #298 says so. They are starting points to be checked on screen.</summary>
internal static class ThorsteinLookColour
{
    /// <summary>Skin slider, about 0.68: deeper and tanned, against Hulgi's 0.50.</summary>
    internal const float SkinHue = 0.68f;

    /// <summary>Hair tone slider, about 0.94.</summary>
    internal const float HairTone = 0.94f;

    /// <summary>Hair level ("blondness") slider, about 0.22: substantially less
    /// blond than Hulgi's 0.74.</summary>
    internal const float HairLevel = 0.22f;

    /// <summary>Hair colour when the live palette could not be read. A dark
    /// brown tuning value, chosen to read as "much less blond than Hulgi". It
    /// is never a claim that the reference was matched; the status line says
    /// which one was used.</summary>
    internal static LookRgb HairFallback => new LookRgb(0.28f, 0.18f, 0.10f);

    internal static LookRgb Skin(in LookPalette palette, float skinHue) =>
        Lerp(palette.SkinLow, palette.SkinHigh, skinHue);

    /// <summary>Hair and beard share one colour, as in the game.</summary>
    internal static LookRgb Hair(in LookPalette palette, float hairTone, float hairLevel)
    {
        if (!palette.Observed)
        {
            return HairFallback;
        }

        LookRgb ramp = Lerp(palette.HairLow, palette.HairHigh, hairTone);
        float level = Mix(palette.MinimumLevel, palette.MaximumLevel, hairLevel);
        return new LookRgb(ramp.R * level, ramp.G * level, ramp.B * level);
    }

    internal static LookRgb ThorsteinHair(in LookPalette palette) => Hair(palette, HairTone, HairLevel);

    /// <summary>His skin, or null when the palette was never read. There is no
    /// defensible guess for a body colour, so an unread palette leaves the
    /// model's own.</summary>
    internal static LookRgb? ThorsteinSkin(in LookPalette palette) =>
        palette.Observed ? Skin(palette, SkinHue) : (LookRgb?)null;

    private static LookRgb Lerp(LookRgb low, LookRgb high, float t)
    {
        float c = Clamp01(t);
        return new LookRgb(
            low.R + ((high.R - low.R) * c),
            low.G + ((high.G - low.G) * c),
            low.B + ((high.B - low.B) * c));
    }

    private static float Mix(float low, float high, float t) => low + ((high - low) * Clamp01(t));

    private static float Clamp01(float value) =>
        float.IsNaN(value) ? 0f : Math.Min(1f, Math.Max(0f, value));
}

/// <summary>Whether an attached piece is drawn where it was put. A skinned
/// preset bound to the wrong skeleton reports success and renders a metre away
/// (#305), so the fit is measured, and a piece that misses is taken off.
/// Hulgi's tolerances.</summary>
internal static class LookFit
{
    internal const float HeadToleranceMetres = 0.45f;

    internal const float GarmentToleranceMetres = 0.85f;

    internal static bool Fits(float distance, float tolerance) =>
        !float.IsNaN(distance) && distance <= tolerance;
}

/// <summary>How to bind a skinned preset mesh to a body. Unity skins by bone
/// index, and a vanilla preset is authored against the player skeleton's
/// array, so a mesh with the body's bone count takes the body's array verbatim.
/// Matching by name keeps the mesh's own order and was #305's floating braid.
/// </summary>
internal enum LookSkinBinding
{
    AdoptBodySkeleton = 0,
    MatchByName = 1,
    Refuse = 2,
}

internal static class LookSkinBindings
{
    internal static LookSkinBinding Decide(int bindposeCount, int bodyBoneCount)
    {
        if (bindposeCount <= 0 || bodyBoneCount <= 0)
        {
            return LookSkinBinding.Refuse;
        }

        return bindposeCount == bodyBoneCount ? LookSkinBinding.AdoptBodySkeleton : LookSkinBinding.MatchByName;
    }
}
