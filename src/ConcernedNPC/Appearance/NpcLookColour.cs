using System;

namespace TheConcernedCat.ConcernedNPC.Appearance;

/// <summary>A linear RGB triple in the units the game stores skin and hair
/// colour in. Three floats rather than an engine colour, so the conversion is
/// tested with no game present.</summary>
public readonly struct NpcLookRgb
{
    public NpcLookRgb(float r, float g, float b)
    {
        R = r;
        G = g;
        B = b;
    }

    public float R { get; }

    public float G { get; }

    public float B { get; }

    public static NpcLookRgb White => new NpcLookRgb(1f, 1f, 1f);

    public override string ToString() =>
        "(" + R.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ", "
        + G.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ", "
        + B.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ")";
}

/// <summary>The character-creation screen's own colour endpoints: the four
/// colours and two brightness limits it lerps between. They are inspector
/// values on that screen, so they can only be read off the live game, never
/// derived. <see cref="Observed"/> records which happened.</summary>
public readonly struct NpcLookPalette
{
    public NpcLookPalette(
        NpcLookRgb skinLow,
        NpcLookRgb skinHigh,
        NpcLookRgb hairLow,
        NpcLookRgb hairHigh,
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

    public NpcLookRgb SkinLow { get; }

    public NpcLookRgb SkinHigh { get; }

    public NpcLookRgb HairLow { get; }

    public NpcLookRgb HairHigh { get; }

    public float MinimumLevel { get; }

    public float MaximumLevel { get; }

    /// <summary>True only when read from a live customization screen this
    /// session.</summary>
    public bool Observed { get; }

    /// <summary>Nothing could be read: the field initialisers of the game's
    /// customization component, which the inspector overrides. An honest "we do
    /// not know the ramp".</summary>
    public static NpcLookPalette Unobserved => new NpcLookPalette(
        NpcLookRgb.White, NpcLookRgb.White, NpcLookRgb.White, NpcLookRgb.White, 0.1f, 1f, observed: false);
}

/// <summary>Slider positions turned into colours through the game's own
/// conversion, the one the character screen performs:
/// <code>
/// skin = Lerp(skinColor0, skinColor1, skinHue)
/// hair = Lerp(hairColor0, hairColor1, hairTone) * Lerp(hairMinLevel, hairMaxLevel, hairLevel)
/// </code>
/// A role supplies its own slider readings and its own stated fallback; this
/// type owns none.</summary>
public static class NpcLookColour
{
    public static NpcLookRgb Skin(in NpcLookPalette palette, float skinHue) =>
        Lerp(palette.SkinLow, palette.SkinHigh, skinHue);

    /// <summary>Hair and beard share one colour, as in the game. An unread
    /// palette yields <paramref name="fallback"/>, which the role states.</summary>
    public static NpcLookRgb Hair(in NpcLookPalette palette, float hairTone, float hairLevel, NpcLookRgb fallback)
    {
        if (!palette.Observed)
        {
            return fallback;
        }

        NpcLookRgb ramp = Lerp(palette.HairLow, palette.HairHigh, hairTone);
        float level = Mix(palette.MinimumLevel, palette.MaximumLevel, hairLevel);
        return new NpcLookRgb(ramp.R * level, ramp.G * level, ramp.B * level);
    }

    /// <summary>A skin colour, or null when the palette was never read or no
    /// hue was asked for. There is no defensible guess for a body colour, so
    /// either leaves the model's own.</summary>
    public static NpcLookRgb? SkinOrNone(in NpcLookPalette palette, float? skinHue) =>
        palette.Observed && skinHue.HasValue ? Skin(palette, skinHue.Value) : (NpcLookRgb?)null;

    private static NpcLookRgb Lerp(NpcLookRgb low, NpcLookRgb high, float t)
    {
        float c = Clamp01(t);
        return new NpcLookRgb(
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
internal static class NpcLookFit
{
    internal const float HeadToleranceMetres = 0.45f;

    internal const float GarmentToleranceMetres = 0.85f;

    internal static bool Fits(float distance, float tolerance) =>
        !float.IsNaN(distance) && distance <= tolerance;
}

/// <summary>How to bind a skinned preset mesh to a body. The engine skins by
/// bone index, and a vanilla preset is authored against the player skeleton's
/// array, so a mesh with the body's bone count takes the body's array verbatim.
/// Matching by name keeps the mesh's own order and was #305's floating braid.
/// </summary>
internal enum NpcLookSkinBinding
{
    AdoptBodySkeleton = 0,
    MatchByName = 1,
    Refuse = 2,
}

internal static class NpcLookSkinBindings
{
    internal static NpcLookSkinBinding Decide(int bindposeCount, int bodyBoneCount)
    {
        if (bindposeCount <= 0 || bodyBoneCount <= 0)
        {
            return NpcLookSkinBinding.Refuse;
        }

        return bindposeCount == bodyBoneCount
            ? NpcLookSkinBinding.AdoptBodySkeleton
            : NpcLookSkinBinding.MatchByName;
    }
}
