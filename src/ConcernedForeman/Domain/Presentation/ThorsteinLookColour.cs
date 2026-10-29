using TheConcernedCat.ConcernedNPC.Appearance;

namespace TheConcernedCat.ConcernedForeman.Domain.Presentation;

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
    internal static NpcLookRgb HairFallback => new NpcLookRgb(0.28f, 0.18f, 0.10f);

    /// <summary>Hair and beard share one colour, as in the game.</summary>
    internal static NpcLookRgb ThorsteinHair(in NpcLookPalette palette) =>
        NpcLookColour.Hair(palette, HairTone, HairLevel, HairFallback);

    /// <summary>His skin, or null when the palette was never read. There is no
    /// defensible guess for a body colour, so an unread palette leaves the
    /// model's own.</summary>
    internal static NpcLookRgb? ThorsteinSkin(in NpcLookPalette palette) =>
        NpcLookColour.SkinOrNone(palette, SkinHue);
}
