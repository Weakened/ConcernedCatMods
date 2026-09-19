using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.ConcernedSteward.Domain.Appearance;

/// <summary>A colour, as three numbers between zero and one.
///
/// <b>Not <c>UnityEngine.Color</c>, and not because of purity for its own
/// sake.</b> Everything in this namespace is decided without the game running,
/// which is what lets a badly typed setting be proved to fail closed rather than
/// to produce a Steward who is bright green. The adapter converts at the
/// boundary, in one line.</summary>
internal readonly struct LookColour : IEquatable<LookColour>
{
    internal LookColour(float red, float green, float blue)
    {
        Red = Clamp(red);
        Green = Clamp(green);
        Blue = Clamp(blue);
    }

    public float Red { get; }

    public float Green { get; }

    public float Blue { get; }

    /// <summary>Reads <c>r,g,b</c>. Returns false for anything else — a blank
    /// setting, a missing component, a value that is not a number — because a
    /// colour that could not be read must leave the base creature's own alone
    /// rather than become black.</summary>
    internal static bool TryParse(string? text, out LookColour colour)
    {
        colour = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text!.Split(',');
        if (parts.Length != 3)
        {
            return false;
        }

        var values = new float[3];
        for (int index = 0; index < 3; index++)
        {
            if (!float.TryParse(
                    parts[index].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                    out values[index])
                || float.IsNaN(values[index]) || float.IsInfinity(values[index]))
            {
                return false;
            }
        }

        colour = new LookColour(values[0], values[1], values[2]);
        return true;
    }

    public bool Equals(LookColour other) =>
        Red.Equals(other.Red) && Green.Equals(other.Green) && Blue.Equals(other.Blue);

    public override bool Equals(object? obj) => obj is LookColour other && Equals(other);

    public override int GetHashCode() =>
        unchecked((Red.GetHashCode() * 397) ^ (Green.GetHashCode() * 31) ^ Blue.GetHashCode());

    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture, "{0:0.##},{1:0.##},{2:0.##}", Red, Green, Blue);

    private static float Clamp(float value) =>
        float.IsNaN(value) ? 0f : value < 0f ? 0f : value > 1f ? 1f : value;
}

/// <summary>One thing she might be wearing: a chest piece and, where the piece
/// is not a full-length garment, legs to go with it.</summary>
internal readonly struct Outfit
{
    internal Outfit(string? chest, string? legs, string? describe)
    {
        Chest = chest ?? string.Empty;
        Legs = legs ?? string.Empty;
        Describe = string.IsNullOrEmpty(describe) ? Chest : describe!;
    }

    /// <summary>The chest item's prefab name.</summary>
    public string Chest { get; }

    /// <summary>The leg item's prefab name, or empty for a garment that covers
    /// the legs by itself.</summary>
    public string Legs { get; }

    /// <summary>What to call it in a log line.</summary>
    public string Describe { get; }

    public bool IsEmpty => Chest.Length == 0 && Legs.Length == 0;
}

/// <summary>What Sunniva looks like, decided before anything is applied.
///
/// <b>Why the clothing is an ordered list rather than one answer.</b> #382 sets
/// a priority — a suitable vanilla dress or robe if one exists, and a leather
/// tunic and leather pants as the first-playable fallback — and the first half
/// of that cannot be settled from the installed assembly at all. Armour prefab
/// names live in the game's asset bundles, exactly as creature prefab names do
/// (<c>StewardSettings.BaseCreature</c> carries the same warning for the same
/// reason), so this product cannot prove that any given robe exists; it can only
/// ask the game at run time and take the first answer.
///
/// So the priority is expressed as candidates in order and the adapter takes the
/// first that resolves. A name this build does not have costs one log line and
/// falls through to the next; the last candidate is the leather the issue names,
/// which is the one that makes her testable with no new dependency of any kind.
///
/// <b>Third-party clothing is deliberately not here.</b> Not a candidate, not a
/// default, not a soft dependency. #382 allows it only as a future option and
/// only with the owner's approval, and nothing in this repository may add, copy
/// or redistribute somebody else's asset. A player who has such a mod installed
/// can already name its prefab in the setting, which is the whole of the support
/// this product will offer until the owner says otherwise.</summary>
internal readonly struct StewardLook
{
    private readonly Outfit[]? _clothing;

    internal StewardLook(
        int modelIndex,
        string? hairItem,
        LookColour? hairColour,
        LookColour? skinColour,
        IReadOnlyList<Outfit>? clothing)
    {
        ModelIndex = modelIndex < 0 ? 0 : modelIndex;
        HairItem = hairItem ?? string.Empty;
        HairColour = hairColour;
        SkinColour = skinColour;

        if (clothing == null || clothing.Count == 0)
        {
            _clothing = null;
            return;
        }

        var kept = new List<Outfit>();
        foreach (Outfit outfit in clothing)
        {
            if (!outfit.IsEmpty)
            {
                kept.Add(outfit);
            }
        }

        _clothing = kept.Count == 0 ? null : kept.ToArray();
    }

    /// <summary>Which of the base creature's models to wear.
    ///
    /// <b>One is the female model where a creature has two</b>, which is the
    /// convention vanilla's own character uses. A base creature with only one
    /// model has nothing to choose, and asking for an index it does not have is
    /// refused by the game itself — <c>VisEquipment.SetModel</c> ignores an
    /// index outside its own array — so a base creature that cannot be female
    /// produces the creature it can be rather than a null reference.</summary>
    public int ModelIndex { get; }

    /// <summary>A hair prefab name, or empty to leave the base creature's hair
    /// alone.
    ///
    /// Empty by default, and that is the careful answer rather than the lazy
    /// one: hair prefab names are asset data like everything else here, a wrong
    /// one produces a bald Steward, and the colour below is what actually
    /// carries "fair, light or blonde" whatever the base creature's hair mesh
    /// happens to be.</summary>
    public string HairItem { get; }

    /// <summary>Her hair colour, or null to leave the base creature's alone.
    /// </summary>
    public LookColour? HairColour { get; }

    /// <summary>Her skin colour, or null to leave the base creature's alone.
    /// </summary>
    public LookColour? SkinColour { get; }

    /// <summary>What she might wear, in the order #382 sets. The adapter takes
    /// the first that this game build actually has.</summary>
    public IReadOnlyList<Outfit> Clothing => _clothing ?? Array.Empty<Outfit>();

    /// <summary>Whether there is anything to apply at all. A look that says
    /// nothing leaves the base creature exactly as it was, which is what the
    /// Steward has always looked like and is never a failure.</summary>
    public bool SaysAnything =>
        ModelIndex > 0 || HairItem.Length != 0 || HairColour != null || SkinColour != null
        || Clothing.Count > 0;
}

/// <summary>Turning the settings into a look.</summary>
internal static class StewardLooks
{
    /// <summary>The leather the issue names as the first-playable fallback, and
    /// the last candidate in every list.
    ///
    /// Vanilla's own starting armour: craftable at a workbench from materials a
    /// player has before they have a settlement worth a Steward, present in
    /// every game build since release, and requiring no dependency of any kind.
    /// </summary>
    internal const string LeatherChest = "ArmorLeatherChest";

    internal const string LeatherLegs = "ArmorLeatherLegs";

    /// <summary>Builds the look.</summary>
    /// <param name="modelIndex">Which model of the base creature.</param>
    /// <param name="preferredChest">A dress or robe the owner or the player has
    /// named, or empty. Tried first, and dropped with a log line if this build
    /// does not have it.</param>
    /// <param name="preferredLegs">Legs to go with it, or empty for a garment
    /// that covers them.</param>
    /// <param name="hairItem">A hair prefab, or empty to leave the base
    /// creature's hair.</param>
    /// <param name="hairColour">Text of the form <c>r,g,b</c>, or empty.</param>
    /// <param name="skinColour">Text of the form <c>r,g,b</c>, or empty.</param>
    internal static StewardLook From(
        int modelIndex,
        string? preferredChest,
        string? preferredLegs,
        string? hairItem,
        string? hairColour,
        string? skinColour)
    {
        var clothing = new List<Outfit>();
        if (!string.IsNullOrWhiteSpace(preferredChest))
        {
            clothing.Add(new Outfit(
                preferredChest!.Trim(),
                string.IsNullOrWhiteSpace(preferredLegs) ? string.Empty : preferredLegs!.Trim(),
                "the garment named in the settings"));
        }

        clothing.Add(new Outfit(LeatherChest, LeatherLegs, "a leather tunic and leather pants"));

        return new StewardLook(
            modelIndex,
            string.IsNullOrWhiteSpace(hairItem) ? string.Empty : hairItem!.Trim(),
            LookColour.TryParse(hairColour, out LookColour hair) ? hair : (LookColour?)null,
            LookColour.TryParse(skinColour, out LookColour skin) ? skin : (LookColour?)null,
            clothing);
    }

    /// <summary>The shipped light-blonde hair, as the setting's default text.
    /// Warm rather than white, so she reads as fair in firelight, which is the
    /// only light anybody will see her in.</summary>
    internal const string DefaultHairColour = "0.93,0.86,0.62";
}
