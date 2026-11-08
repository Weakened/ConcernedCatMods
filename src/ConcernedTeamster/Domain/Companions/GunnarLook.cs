using TheConcernedCat.ConcernedNPC.Appearance;
namespace TheConcernedCat.ConcernedTeamster.Domain.Companions;

/// <summary>Gunnar's look (#458), from the owner's September 15 character
/// reference (<c>docs/companions/APPEARANCE_REFERENCE.md</c>) and #381's
/// appearance line, resolved by Concerned NPC's shared rule against whatever
/// this game build actually has.
///
/// <b>Hair and beard are the owner's:</b> "Short Curls" and "Stonedweller", by
/// the customization screen's label first, then by the label's localization
/// token, then by prefab id. <b>The token and the id were read from the
/// installed game</b> (2026-11-08, read-only): its localization table in
/// <c>valheim_Data/resources.assets</c> has <c>"customization_hair23","Short
/// Curls"</c> and <c>"customization_beard16","Stonedweller"</c>, and its prefab
/// manifest (<c>StreamingAssets/SoftRef/manifest_extended</c>) lists
/// <c>customizations/hairs/Hair23.prefab</c> and
/// <c>customizations/beards/Beard16.prefab</c>. They agree with the Jötunn
/// 1.0.7 list the reference cites. A later build that renumbers them is caught
/// by the label, which outranks both.
///
/// <b>Colours</b> go through the character screen's own palette at the slider
/// readings the reference gives (skin ~0.52, hair tone ~0.05, blondness ~0.49).
/// Where the palette cannot be read, the stated fallback is a grey-brown hair
/// colour and the source model's own skin.
///
/// <b>Clothes are #381's line, marked as this build's proposal</b> for the
/// creator screenshots lock none: leather trousers (installed label "Leather
/// Trousers", token <c>$item_legs_leather</c>, prefab <c>ArmorLeatherLegs</c>,
/// all read from the same files) and a bare torso, so no chest piece is asked
/// for. Megingjord is not drawn: the shared dresser dresses hair, beard, chest
/// and legs only, and adding a belt slot to Concerned NPC is not this issue's.
/// None of this changes his carrying capacity, which is a rule of its own.
/// </summary>
internal static class GunnarLook
{
    public const string HairLabel = "Short Curls";
    public const string BeardLabel = "Stonedweller";
    public const string LegsLabel = "Leather Trousers";

    public static readonly NpcLookRequest Hair = new(
        displayName: HairLabel,
        prefabCandidates: new[] { "Hair23" },
        shapeFallbacks: new[] { "curl", "short" },
        familyPrefix: "Hair",
        token: "$customization_hair23");

    public static readonly NpcLookRequest Beard = new(
        displayName: BeardLabel,
        prefabCandidates: new[] { "Beard16" },
        shapeFallbacks: new[] { "full", "long" },
        familyPrefix: "Beard",
        token: "$customization_beard16");

    /// <summary>Proposed, not owner-locked: see the class remarks.</summary>
    public static readonly NpcLookRequest Legs = new(
        displayName: LegsLabel,
        prefabCandidates: new[] { "ArmorLeatherLegs" },
        shapeFallbacks: new[] { "leather", "trousers", "pants" },
        familyPrefix: "Armor",
        token: "$item_legs_leather");

    public const float SkinHue = 0.52f;
    public const float HairTone = 0.05f;
    public const float HairLevel = 0.49f;
    public static readonly NpcLookRgb HairFallback = new(0.35f, 0.32f, 0.28f);

    /// <summary>What his figure is asked to look like. Overrides are a player's
    /// own choice for a slot and are never replaced by the stock default.</summary>
    public static NpcLookSpec Spec(string? hairOverride = null, string? beardOverride = null,
        string? legsOverride = null) =>
        new("Gunnar", NpcBodyModel.Male,
            Hair, Beard, chest: null, Legs,
            SkinHue, HairTone, HairLevel, HairFallback,
            hairOverride, beardOverride, chestOverride: null, legsOverride);
}
