using TheConcernedCat.ConcernedNPC.Appearance;

namespace TheConcernedCat.ConcernedSteward.Domain.Appearance;

/// <summary>Sunniva's look as a player-style figure at rest (#382; the owner's
/// lock 2026-10-27: female, Matronly hair, fair, light blonde). Dressed by
/// Concerned NPC's <c>NpcFigureDresser</c>, which owns the resolving rule
/// (<see cref="NpcLookPlan"/>); this type owns only her data.
///
/// <b>Nothing is assumed to exist.</b> Preset names are asset data, not API.
/// The hair is asked for by the label the character screen shows ("Matronly"),
/// then by the game's own token (<c>$customization_hair12</c>, which
/// <see cref="StewardLooks.DefaultHairStyle"/> already names), then by id and
/// shape. A missing preset changes how she looks and nothing else.
///
/// <b>No beard.</b> An empty request leaves the slot empty.
///
/// <b>Clothes</b> are the leather tunic and trousers #382 names as the
/// first-playable fallback; <c>Appearance/Garment</c> and <c>GarmentLegs</c>
/// override them, as they do on her worker body.</summary>
internal static class SunnivaLookPlan
{
    internal static readonly NpcLookRequest Hair = new NpcLookRequest(
        displayName: "Matronly",
        prefabCandidates: new[] { "Hair12" },
        shapeFallbacks: new[] { "matronly", "bun", "braid" },
        familyPrefix: "Hair",
        token: StewardLooks.DefaultHairStyle);

    internal static readonly NpcLookRequest Chest = new NpcLookRequest(
        displayName: "Leather tunic",
        prefabCandidates: new[] { StewardLooks.LeatherChest },
        shapeFallbacks: new[] { "leather", "tunic" },
        familyPrefix: "Armor");

    internal static readonly NpcLookRequest Legs = new NpcLookRequest(
        displayName: "Leather pants",
        prefabCandidates: new[] { StewardLooks.LeatherLegs },
        shapeFallbacks: new[] { "leather", "pants", "trousers" },
        familyPrefix: "Armor");

    /// <summary>What her worker body is asked to wear while she works (owner
    /// decision 2026-10-29): her own hair only, put on the existing Dverger
    /// body by Concerned NPC's <c>NpcWorkerDresser</c>. No beard and no
    /// garments - the worker body keeps its own clothes, which
    /// <c>StewardAppearance</c> already sets.</summary>
    internal static NpcLookSpec WorkerSpec(string? hair) =>
        new NpcLookSpec(
            "Sunniva",
            NpcBodyModel.Female,
            Hair,
            null,
            null,
            null,
            SunnivaLookColour.SkinHue,
            SunnivaLookColour.HairTone,
            SunnivaLookColour.HairLevel,
            SunnivaLookColour.HairFallback,
            hair,
            null,
            null,
            null);

    /// <summary>What her figure is asked to look like. The overrides are the
    /// player's own settings; empty means the reference.</summary>
    internal static NpcLookSpec Spec(string? hair, string? chest, string? legs) =>
        new NpcLookSpec(
            "Sunniva",
            NpcBodyModel.Female,
            Hair,
            null,
            Chest,
            Legs,
            SunnivaLookColour.SkinHue,
            SunnivaLookColour.HairTone,
            SunnivaLookColour.HairLevel,
            SunnivaLookColour.HairFallback,
            hair,
            null,
            chest,
            legs);
}

/// <summary>Sunniva's colouring as slider positions, turned into colours by the
/// game's own conversion inside Concerned NPC (the same rule Hulgi's and
/// Thorstein's use). These are this build's proposal for "fair, light blonde"
/// and are to be checked on screen in the owner's go-around; they are not a
/// claim that a reference was matched.</summary>
internal static class SunnivaLookColour
{
    /// <summary>Skin slider: fair, lighter than Hulgi's 0.50.</summary>
    internal const float SkinHue = 0.25f;

    /// <summary>Hair tone slider.</summary>
    internal const float HairTone = 0.40f;

    /// <summary>Hair level ("blondness") slider: light blonde, more than Hulgi's
    /// 0.74.</summary>
    internal const float HairLevel = 0.90f;

    /// <summary>Hair colour when the live palette could not be read: the same
    /// light blonde her worker body is given.</summary>
    internal static NpcLookRgb HairFallback => new NpcLookRgb(0.93f, 0.86f, 0.62f);
}
