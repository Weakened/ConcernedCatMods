using System;

namespace TheConcernedCat.ConcernedNPC.Appearance;

/// <summary>Which of the game's two player bodies a figure wears. The numbers
/// are the game's own model indices: the character screen's female toggle
/// selects index 1, and resets the beard when it does.</summary>
public enum NpcBodyModel
{
    /// <summary>The body the player prefab is authored with. Nothing is
    /// changed, so a role that asks for this gets exactly the extracted
    /// figure's own body.</summary>
    Male = 0,

    /// <summary>The second player body, taken from the source character's own
    /// model list. If that list cannot be read the figure keeps the first body,
    /// and <c>NpcFigureDresser.ModelApplied</c> says so.</summary>
    Female = 1,
}

/// <summary>Everything a role asks a figure to look like. All of it is the
/// role's data: this library names no character, no preset and no colour.
///
/// A slot whose request is null is left alone: nothing is attached there, and
/// nothing is reported as missing. That is how a role asks for no beard.
///
/// <b>Overrides</b> are a player's own configuration value for a slot, a label
/// or prefab name. One that names something this build does not have falls
/// through to the request, so a typo never leaves a slot bare.</summary>
public sealed class NpcLookSpec
{
    public NpcLookSpec(
        string who,
        NpcBodyModel model,
        NpcLookRequest? hair,
        NpcLookRequest? beard,
        NpcLookRequest? chest,
        NpcLookRequest? legs,
        float? skinHue,
        float hairTone,
        float hairLevel,
        NpcLookRgb hairFallback,
        string? hairOverride = null,
        string? beardOverride = null,
        string? chestOverride = null,
        string? legsOverride = null)
    {
        if (string.IsNullOrWhiteSpace(who))
        {
            throw new ArgumentException("A figure is dressed for somebody; say who, for the log.", nameof(who));
        }

        if (!Enum.IsDefined(typeof(NpcBodyModel), model))
        {
            throw new ArgumentOutOfRangeException(nameof(model), model, "Not one of the game's two bodies.");
        }

        Who = who.Trim();
        Model = model;
        Hair = hair;
        Beard = beard;
        Chest = chest;
        Legs = legs;
        SkinHue = skinHue;
        HairTone = hairTone;
        HairLevel = hairLevel;
        HairFallback = hairFallback;
        HairOverride = hairOverride;
        BeardOverride = beardOverride;
        ChestOverride = chestOverride;
        LegsOverride = legsOverride;
    }

    /// <summary>The name the log lines use, as the player would recognise it.</summary>
    public string Who { get; }

    public NpcBodyModel Model { get; }

    public NpcLookRequest? Hair { get; }

    public NpcLookRequest? Beard { get; }

    public NpcLookRequest? Chest { get; }

    public NpcLookRequest? Legs { get; }

    /// <summary>The skin slider, or null to keep the model's own skin.</summary>
    public float? SkinHue { get; }

    public float HairTone { get; }

    public float HairLevel { get; }

    /// <summary>The role's stated hair colour for when the palette was never
    /// read. Never a claim that the reference was matched.</summary>
    public NpcLookRgb HairFallback { get; }

    public string? HairOverride { get; }

    public string? BeardOverride { get; }

    public string? ChestOverride { get; }

    public string? LegsOverride { get; }

    /// <summary>The hair colour this spec resolves to on <paramref name="palette"/>.</summary>
    public NpcLookRgb HairColour(in NpcLookPalette palette) =>
        NpcLookColour.Hair(palette, HairTone, HairLevel, HairFallback);

    /// <summary>The skin colour, or null to keep the model's own.</summary>
    public NpcLookRgb? SkinColour(in NpcLookPalette palette) => NpcLookColour.SkinOrNone(palette, SkinHue);
}
