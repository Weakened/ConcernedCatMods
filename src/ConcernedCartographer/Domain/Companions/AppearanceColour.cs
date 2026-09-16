using System;

namespace TheConcernedCat.ConcernedCartographer.Companions;

/// <summary>A linear RGB triple, in the same units the game stores skin and
/// hair colour in. Kept as three floats rather than a <c>Color</c> so the
/// conversion is unit-testable with no Unity present.</summary>
internal readonly struct ColourTriple
{
    public ColourTriple(float r, float g, float b)
    {
        R = r;
        G = g;
        B = b;
    }

    public float R { get; }
    public float G { get; }
    public float B { get; }

    public static ColourTriple White => new ColourTriple(1f, 1f, 1f);

    public override string ToString()
    {
        return $"({R:0.###}, {G:0.###}, {B:0.###})";
    }
}

/// <summary>The four endpoint colours and the two brightness limits the game's
/// character-creation screen lerps between.
///
/// These are Unity inspector values on the customization screen, not code
/// constants, so they can only be <i>read</i>, never derived. <see
/// cref="Observed"/> records which of the two happened, and the console tool
/// prints it: a tuning value taken from a live read is evidence, the built-in
/// fallback is not.</summary>
internal readonly struct CustomizationPalette
{
    public CustomizationPalette(
        ColourTriple skinLow,
        ColourTriple skinHigh,
        ColourTriple hairLow,
        ColourTriple hairHigh,
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

    /// <summary>Skin at slider 0 and slider 1 (<c>m_skinColor0/1</c>).</summary>
    public ColourTriple SkinLow { get; }
    public ColourTriple SkinHigh { get; }

    /// <summary>Hair at tone 0 and tone 1 (<c>m_hairColor0/1</c>).</summary>
    public ColourTriple HairLow { get; }
    public ColourTriple HairHigh { get; }

    /// <summary>Brightness multiplier at level 0 and level 1
    /// (<c>m_hairMinLevel/m_hairMaxLevel</c>).</summary>
    public float MinimumLevel { get; }
    public float MaximumLevel { get; }

    /// <summary>True when these numbers came from a live customization screen
    /// this session rather than from the built-in fallback.</summary>
    public bool Observed { get; }

    /// <summary>The fallback used when no live customization screen could be
    /// read.
    ///
    /// The two hair endpoints and the level limits are what the decompiled
    /// <c>PlayerCustomizaton</c> declares as its C# field initialisers
    /// (<c>Color.white</c>, <c>0.1f</c>, <c>1f</c>) — the inspector overrides
    /// them, so a white-to-white hair ramp is honestly "we do not know the
    /// ramp". <see cref="AppearanceColour.HairFallback"/> is what actually gets
    /// applied in that case, and the console tool says so rather than claiming
    /// the reference colour was matched.</summary>
    public static CustomizationPalette Unobserved => new CustomizationPalette(
        skinLow: ColourTriple.White,
        skinHigh: ColourTriple.White,
        hairLow: ColourTriple.White,
        hairHigh: ColourTriple.White,
        minimumLevel: 0.1f,
        maximumLevel: 1f,
        observed: false);
}

/// <summary>Whether an attached hair or beard actually ended up on the head.
///
/// Attaching a customization preset can succeed and still be wrong: Valheim's
/// hair and beards are skinned meshes, drawn by their bones rather than by
/// their transform, so one bound to the wrong skeleton renders wherever that
/// skeleton happens to be. Observed on 1.0.12: the braid hung at standing head
/// height while the companion sat on the ground a metre below it.
///
/// So the attachment is checked rather than assumed. A preset that cannot be
/// shown in the right place is taken off again — a companion with no hair is
/// incomplete, and a companion with a braid floating beside him is broken.</summary>
internal static class AppearanceFit
{
    /// <summary>How far a piece may sit from the head before it is not on the
    /// head. Generous enough for a tall hairstyle or a long beard, tight enough
    /// that a whole body-length of error cannot pass.</summary>
    public const float ToleranceMetres = 0.45f;

    /// <summary>How far a garment may sit from the body it is worn on. Looser
    /// than the head tolerance because a tunic and a pair of trousers are
    /// measured against the whole body's centre, and either one is legitimately
    /// half a torso away from it.</summary>
    public const float GarmentToleranceMetres = 0.85f;

    /// <summary>True when a piece centred <paramref name="distanceFromHead"/>
    /// away is close enough to be wearing.</summary>
    public static bool Fits(float distanceFromHead)
    {
        return Fits(distanceFromHead, ToleranceMetres);
    }

    /// <summary>The same question with the tolerance the slot deserves.</summary>
    public static bool Fits(float distance, float tolerance)
    {
        return !float.IsNaN(distance) && distance <= tolerance;
    }
}

/// <summary>Turns customization-screen slider positions into the colours the
/// game would store for them.
///
/// This is the game's own conversion, transcribed from
/// <c>PlayerCustomizaton.Update</c> on the installed build:
///
/// <code>
/// skin = Lerp(skinColor0, skinColor1, skinHue)
/// hair = Lerp(hairColor0, hairColor1, hairTone) * Lerp(hairMinLevel, hairMaxLevel, hairLevel)
/// </code>
///
/// CC-NPC-006 is explicit that the owner's slider readings must be mapped
/// through that conversion rather than stored as an RGB vector, which is why
/// Hulgi's appearance is three slider positions here and becomes a colour only
/// at the moment it is applied.</summary>
internal static class AppearanceColour
{
    /// <summary>Hulgi's skin slider, read from the owner's September 15
    /// customization screenshot. Approximate, and stated as such in #298.</summary>
    public const float HulgiSkinHue = 0.50f;

    /// <summary>Hulgi's hair tone slider (<c>m_hairTone</c>), the position
    /// along the dark-to-light hair ramp. The owner's reading labelled this
    /// one "hair tone".</summary>
    public const float HulgiHairTone = 0.94f;

    /// <summary>Hulgi's hair level slider (<c>m_hairLevel</c>), the brightness
    /// multiplier. The owner's reading labelled this one "blondness"; the game
    /// has exactly two hair sliders and <c>m_hairTone</c> is the other, so this
    /// is the only slider the second reading can be.</summary>
    public const float HulgiHairLevel = 0.74f;

    /// <summary>What to tint hair with when the live palette could not be read.
    ///
    /// This is the warm strawberry-blond tuning value the companion shipped
    /// with before #298, kept so an unreadable palette looks the same as it
    /// always did rather than worse. It is a starting point to be confirmed on
    /// screen, never a claim that the reference was matched — <see
    /// cref="CustomizationPalette.Observed"/> is what says which one
    /// happened.</summary>
    public static ColourTriple HairFallback => new ColourTriple(0.85f, 0.55f, 0.30f);

    /// <summary>Skin colour for a slider position.</summary>
    public static ColourTriple Skin(CustomizationPalette palette, float skinHue)
    {
        return Lerp(palette.SkinLow, palette.SkinHigh, skinHue);
    }

    /// <summary>Hair colour for a tone and a level, or the documented fallback
    /// when the palette was never read. Both hair and beard use this: the game
    /// tints them with the same colour.</summary>
    public static ColourTriple Hair(CustomizationPalette palette, float hairTone, float hairLevel)
    {
        if (!palette.Observed)
        {
            return HairFallback;
        }

        ColourTriple ramp = Lerp(palette.HairLow, palette.HairHigh, hairTone);
        float level = Mix(palette.MinimumLevel, palette.MaximumLevel, hairLevel);
        return new ColourTriple(ramp.R * level, ramp.G * level, ramp.B * level);
    }

    /// <summary>Hulgi's hair colour on this build.</summary>
    public static ColourTriple HulgiHair(CustomizationPalette palette)
    {
        return Hair(palette, HulgiHairTone, HulgiHairLevel);
    }

    /// <summary>Hulgi's skin colour on this build, or null when the palette was
    /// never read. Unlike hair there is no defensible fallback: tinting a body
    /// with a guessed colour is worse than leaving the model's own.</summary>
    public static ColourTriple? HulgiSkin(CustomizationPalette palette)
    {
        return palette.Observed ? Skin(palette, HulgiSkinHue) : (ColourTriple?)null;
    }

    /// <summary>Componentwise lerp with the clamp <c>Color.Lerp</c> applies, so
    /// a slider reading outside 0-1 behaves the way the game would.</summary>
    private static ColourTriple Lerp(ColourTriple low, ColourTriple high, float t)
    {
        float clamped = Clamp01(t);
        return new ColourTriple(
            low.R + ((high.R - low.R) * clamped),
            low.G + ((high.G - low.G) * clamped),
            low.B + ((high.B - low.B) * clamped));
    }

    private static float Mix(float low, float high, float t)
    {
        return low + ((high - low) * Clamp01(t));
    }

    private static float Clamp01(float value)
    {
        if (float.IsNaN(value))
        {
            return 0f;
        }

        return Math.Min(1f, Math.Max(0f, value));
    }
}
