using TheConcernedCat.ConcernedNPC.Appearance;

namespace TheConcernedCat.ConcernedForeman.Domain.Presentation;

/// <summary>Thorstein's look, from the owner's September 15 character
/// reference (#298, <c>docs/companions/APPEARANCE_REFERENCE.md</c>), and the
/// rule that resolves it against whatever this game build actually has.
///
/// <b>The reference.</b> Hair "Mullet", beard "Mustache". This replaces the
/// earlier short-hair and short-beard placeholder. The complexion is deeper and
/// tanned, substantially less blond than Hulgi's
/// (<see cref="ThorsteinLookColour"/>).
///
/// <b>Nothing is assumed to exist.</b> Preset names are asset data, not API.
/// The options are enumerated from the game's own item table at run time, the
/// same query its customization screen makes, and the owner's label is the
/// primary key. The Jötunn 1.0.7 id (<c>Hair32</c>, <c>Beard22</c>) is the
/// secondary one, then shape, then family, then nothing. A missing preset
/// changes how he looks and nothing else.
///
/// <b>Clothes are this build's proposal, not the owner's.</b> The reference
/// screenshots lock hair, beard and colouring only, and #298 says clothes are
/// not locked by them but must be safe and modest. A carpenter's working
/// leathers (the vanilla leather tunic and trousers) are proposed here.
/// <c>Thorstein/ChestGarment</c> and <c>LegsGarment</c> override them, and the
/// owner may replace the proposal.
///
/// The algorithm is the one Hulgi's <c>AppearancePlan</c> uses in Concerned
/// Cartographer. Since the lift it lives once, in Concerned NPC's
/// <see cref="NpcLookPlan"/>, which every companion product shares as a
/// library; this type keeps Thorstein's requests and nothing else.</summary>
internal static class ThorsteinLookPlan
{
    internal static readonly NpcLookRequest Hair = new NpcLookRequest(
        displayName: "Mullet",
        prefabCandidates: new[] { "Hair32" },
        shapeFallbacks: new[] { "mullet", "short", "swept" },
        familyPrefix: "Hair");

    internal static readonly NpcLookRequest Beard = new NpcLookRequest(
        displayName: "Mustache",
        prefabCandidates: new[] { "Beard22" },
        shapeFallbacks: new[] { "mustache", "moustache", "short" },
        familyPrefix: "Beard");

    /// <summary>Proposed, not owner-locked: see the class remarks.</summary>
    internal static readonly NpcLookRequest Chest = new NpcLookRequest(
        displayName: "Leather tunic",
        prefabCandidates: new[] { "ArmorLeatherChest" },
        shapeFallbacks: new[] { "leather", "tunic" },
        familyPrefix: "Armor");

    /// <summary>Proposed, not owner-locked: see the class remarks.</summary>
    internal static readonly NpcLookRequest Legs = new NpcLookRequest(
        displayName: "Leather pants",
        prefabCandidates: new[] { "ArmorLeatherLegs" },
        shapeFallbacks: new[] { "leather", "pants", "trousers" },
        familyPrefix: "Armor");

    /// <summary>What Thorstein's figure is asked to look like, with the
    /// player's four overrides (empty means the reference). Dressed by Concerned
    /// NPC's <c>NpcFigureDresser</c>, which owns the resolving rule
    /// (<see cref="NpcLookPlan"/>) since the lift; this type owns only his data.
    /// </summary>
    internal static NpcLookSpec Spec(string? hair, string? beard, string? chest, string? legs) =>
        new NpcLookSpec(
            "Thorstein",
            NpcBodyModel.Male,
            Hair,
            Beard,
            Chest,
            Legs,
            ThorsteinLookColour.SkinHue,
            ThorsteinLookColour.HairTone,
            ThorsteinLookColour.HairLevel,
            ThorsteinLookColour.HairFallback,
            hair,
            beard,
            chest,
            legs);
}
