using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Appearance;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>The game-free half of the figure dresser, lifted from Concerned
/// Foreman (#295): how a requested look is resolved against what a build
/// offers, and how slider readings become colours. Thorstein's own requests are
/// tested in Foreman; these are the rules every role shares, including the two
/// Thorstein never needed - a localization token and a second body - which is
/// what Sunniva (female, Matronly hair, no beard) asks for.</summary>
public sealed class AppearanceTests
{
    private static readonly NpcLookRequest Matronly = new NpcLookRequest(
        displayName: "Matronly",
        prefabCandidates: new[] { "Hair12" },
        shapeFallbacks: new[] { "bun" },
        familyPrefix: "Hair",
        token: "$customization_hair12");

    private static readonly IReadOnlyList<NpcLookOption> Offered = new[]
    {
        new NpcLookOption("Hair5", "Ponytail", "$customization_hair5"),
        new NpcLookOption("Hair12", "Matronly", "$customization_hair12"),
        new NpcLookOption("Hair20", "Bun", "$customization_hair20"),
    };

    [Fact]
    public void The_label_is_the_first_key_and_counts_as_a_match()
    {
        NpcLookChoice hair = NpcLookPlan.Choose(Offered, Matronly);

        Assert.Equal("Hair12", hair.PrefabName);
        Assert.Equal(NpcLookMatch.DisplayName, hair.Match);
        Assert.True(hair.MatchesReference);
    }

    [Fact]
    public void In_another_language_the_token_still_finds_the_same_choice_and_counts_as_a_match()
    {
        var german = new[]
        {
            new NpcLookOption("Hair5", "Pferdeschwanz", "$customization_hair5"),
            new NpcLookOption("Hair40", "Matrone", "$customization_hair12"),
        };

        NpcLookChoice hair = NpcLookPlan.Choose(german, Matronly);

        // The token outranks the documented id: the build renumbered the
        // prefab, and the token is what the owner's label was looked up by.
        Assert.Equal("Hair40", hair.PrefabName);
        Assert.Equal(NpcLookMatch.Token, hair.Match);
        Assert.True(hair.MatchesReference);
    }

    [Fact]
    public void Without_label_or_token_the_id_is_a_fallback_and_never_called_a_match()
    {
        var bare = new[] { new NpcLookOption("Hair5"), new NpcLookOption("Hair12") };

        NpcLookChoice hair = NpcLookPlan.Choose(bare, Matronly);

        Assert.Equal("Hair12", hair.PrefabName);
        Assert.Equal(NpcLookMatch.PrefabName, hair.Match);
        Assert.False(hair.MatchesReference);
    }

    [Fact]
    public void A_players_override_outranks_label_and_token_and_a_missing_one_falls_through()
    {
        Assert.Equal("Hair20", NpcLookPlan.Choose(Offered, Matronly, "Bun").PrefabName);
        Assert.Equal(NpcLookMatch.Override, NpcLookPlan.Choose(Offered, Matronly, "Hair5").Match);

        NpcLookChoice missing = NpcLookPlan.Choose(Offered, Matronly, "No Such Hair");
        Assert.Equal("Hair12", missing.PrefabName);
        Assert.Equal(NpcLookMatch.DisplayName, missing.Match);
    }

    [Fact]
    public void A_request_without_a_token_behaves_exactly_as_before_the_lift()
    {
        var noToken = new NpcLookRequest("Matronly", new[] { "Hair12" }, new[] { "bun" }, "Hair");
        Assert.Null(noToken.Token);

        var renumbered = new[] { new NpcLookOption("Hair40", null, "$customization_hair12") };
        NpcLookChoice hair = NpcLookPlan.Choose(renumbered, noToken);

        // No label, no token asked for, no id: the family fallback, as Foreman's
        // copy would have chosen.
        Assert.Equal(NpcLookMatch.Family, hair.Match);
        Assert.Equal("Hair40", hair.PrefabName);
    }

    [Fact]
    public void Nothing_offered_is_nothing_chosen()
    {
        Assert.Null(NpcLookPlan.Choose(null, Matronly).PrefabName);
        Assert.Null(NpcLookPlan.Choose(Array.Empty<NpcLookOption>(), Matronly).PrefabName);
        Assert.Throws<ArgumentNullException>(() => NpcLookPlan.Choose(Offered, null!));
    }

    [Fact]
    public void A_role_asks_for_no_beard_by_leaving_the_slot_empty_and_for_the_second_body_by_name()
    {
        var spec = new NpcLookSpec(
            "Sunniva",
            NpcBodyModel.Female,
            Matronly,
            beard: null,
            chest: new NpcLookRequest("Leather tunic", new[] { "ArmorLeatherChest" }, new[] { "leather" }, "Armor"),
            legs: new NpcLookRequest("Leather pants", new[] { "ArmorLeatherLegs" }, new[] { "leather" }, "Armor"),
            skinHue: 0.1f,
            hairTone: 0.9f,
            hairLevel: 0.9f,
            hairFallback: new NpcLookRgb(0.8f, 0.7f, 0.5f));

        Assert.Equal(NpcBodyModel.Female, spec.Model);
        Assert.Equal(1, (int)spec.Model);
        Assert.Null(spec.Beard);
        Assert.Same(Matronly, spec.Hair);
    }

    [Fact]
    public void The_body_numbers_are_the_games_own_indices()
    {
        // The character screen's female toggle selects model 1 and resets the
        // beard (PlayerCustomizaton.SetPlayerModel, read from the installed
        // 1.0 assembly). A renumbering here would dress every role wrongly.
        Assert.Equal(0, (int)NpcBodyModel.Male);
        Assert.Equal(1, (int)NpcBodyModel.Female);
        Assert.Equal(2, Enum.GetValues(typeof(NpcBodyModel)).Length);
    }

    [Fact]
    public void A_spec_names_somebody_and_one_of_the_two_bodies()
    {
        Assert.Throws<ArgumentException>(() => new NpcLookSpec(
            " ", NpcBodyModel.Male, null, null, null, null, null, 0f, 0f, NpcLookRgb.White));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NpcLookSpec(
            "Someone", (NpcBodyModel)7, null, null, null, null, null, 0f, 0f, NpcLookRgb.White));
    }

    [Fact]
    public void Colour_goes_through_the_games_own_conversion_and_an_unread_palette_uses_the_roles_fallback()
    {
        var palette = new NpcLookPalette(
            skinLow: new NpcLookRgb(1f, 0.9f, 0.8f),
            skinHigh: new NpcLookRgb(0.4f, 0.3f, 0.2f),
            hairLow: new NpcLookRgb(1f, 1f, 1f),
            hairHigh: new NpcLookRgb(0.5f, 0.4f, 0.3f),
            minimumLevel: 0.1f,
            maximumLevel: 1f,
            observed: true);

        NpcLookRgb skin = NpcLookColour.Skin(palette, 0.5f);
        Assert.Equal(0.7f, skin.R, 3);

        // ramp at tone 0.5 is (0.75, 0.7, 0.65); level at 0.5 is 0.55.
        NpcLookRgb hair = NpcLookColour.Hair(palette, 0.5f, 0.5f, NpcLookRgb.White);
        Assert.Equal(0.75f * 0.55f, hair.R, 3);

        var fallback = new NpcLookRgb(0.2f, 0.1f, 0.05f);
        Assert.Equal(0.2f, NpcLookColour.Hair(NpcLookPalette.Unobserved, 0.5f, 0.5f, fallback).R);
        Assert.Null(NpcLookColour.SkinOrNone(NpcLookPalette.Unobserved, 0.5f));
        Assert.Null(NpcLookColour.SkinOrNone(palette, null));
        Assert.NotNull(NpcLookColour.SkinOrNone(palette, 0.5f));
    }

    [Fact]
    public void A_skinned_preset_with_the_bodys_bone_count_adopts_the_bodys_skeleton()
    {
        // #305: matching by name keeps the piece's own order and drew Hulgi's
        // braid 0.99 m away. Same count means the body's array, verbatim.
        Assert.Equal(NpcLookSkinBinding.AdoptBodySkeleton, NpcLookSkinBindings.Decide(57, 57));
        Assert.Equal(NpcLookSkinBinding.MatchByName, NpcLookSkinBindings.Decide(12, 57));
        Assert.Equal(NpcLookSkinBinding.Refuse, NpcLookSkinBindings.Decide(0, 57));
        Assert.Equal(NpcLookSkinBinding.Refuse, NpcLookSkinBindings.Decide(57, 0));
    }

    [Fact]
    public void A_piece_that_missed_is_taken_off()
    {
        Assert.True(NpcLookFit.Fits(0.2f, NpcLookFit.HeadToleranceMetres));
        Assert.False(NpcLookFit.Fits(0.99f, NpcLookFit.HeadToleranceMetres));
        Assert.False(NpcLookFit.Fits(float.NaN, NpcLookFit.HeadToleranceMetres));
        Assert.True(NpcLookFit.Fits(0.8f, NpcLookFit.GarmentToleranceMetres));
        Assert.False(NpcLookFit.Fits(0.9f, NpcLookFit.GarmentToleranceMetres));
    }
}
