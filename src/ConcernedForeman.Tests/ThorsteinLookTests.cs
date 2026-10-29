using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Domain.Presentation;
using TheConcernedCat.ConcernedNPC.Appearance;

namespace ConcernedForeman.Tests;

/// <summary>#298, the Thorstein row: Mullet and Mustache, a deeper complexion,
/// and the rule that a missing preset is a fallback said out loud rather than
/// a match claimed.</summary>
public sealed class ThorsteinLookTests
{
    private static readonly IReadOnlyList<NpcLookOption> Hair = new[]
    {
        new NpcLookOption("Hair11", "Long Braid"),
        new NpcLookOption("Hair23", "Short Curls"),
        new NpcLookOption("Hair32", "Mullet"),
    };

    private static readonly IReadOnlyList<NpcLookOption> Beards = new[]
    {
        new NpcLookOption("Beard16", "Stonedweller"),
        new NpcLookOption("Beard22", "Mustache"),
        new NpcLookOption("Beard26", "Handlebar"),
    };

    [Fact]
    public void TheOwnersReferenceIsMulletAndMustacheByLabel()
    {
        Assert.Equal("Mullet", ThorsteinLookPlan.Hair.DisplayName);
        Assert.Equal("Mustache", ThorsteinLookPlan.Beard.DisplayName);

        NpcLookChoice hair = NpcLookPlan.Choose(Hair, ThorsteinLookPlan.Hair);
        NpcLookChoice beard = NpcLookPlan.Choose(Beards, ThorsteinLookPlan.Beard);

        Assert.Equal("Hair32", hair.PrefabName);
        Assert.True(hair.MatchesReference);
        Assert.Equal("Beard22", beard.PrefabName);
        Assert.True(beard.MatchesReference);
    }

    [Fact]
    public void TheLabelOutranksTheDocumentedIdWhenABuildRenumbers()
    {
        // The 1.0.7 id is a lookup candidate, not proof. If a later build moved
        // "Mullet" to another id, the label is what the owner chose.
        var renumbered = new[] { new NpcLookOption("Hair32", "Something Else"), new NpcLookOption("Hair40", "Mullet") };

        NpcLookChoice hair = NpcLookPlan.Choose(renumbered, ThorsteinLookPlan.Hair);

        Assert.Equal("Hair40", hair.PrefabName);
        Assert.Equal(NpcLookMatch.DisplayName, hair.Match);
    }

    [Fact]
    public void WithoutLabelsTheIdIsAFallbackAndNeverCalledAMatch()
    {
        var unlabelled = new[] { new NpcLookOption("Hair11"), new NpcLookOption("Hair32") };

        NpcLookChoice hair = NpcLookPlan.Choose(unlabelled, ThorsteinLookPlan.Hair);

        Assert.Equal("Hair32", hair.PrefabName);
        Assert.Equal(NpcLookMatch.PrefabName, hair.Match);
        Assert.False(hair.MatchesReference);
    }

    [Fact]
    public void APlayersOverrideWinsAndAMissingOneFallsThrough()
    {
        Assert.Equal(
            NpcLookMatch.Override, NpcLookPlan.Choose(Hair, ThorsteinLookPlan.Hair, "Short Curls").Match);
        Assert.Equal("Hair23", NpcLookPlan.Choose(Hair, ThorsteinLookPlan.Hair, "Hair23").PrefabName);

        NpcLookChoice missing = NpcLookPlan.Choose(Hair, ThorsteinLookPlan.Hair, "No Such Hair");
        Assert.Equal("Hair32", missing.PrefabName);
        Assert.True(missing.MatchesReference);
    }

    [Fact]
    public void NothingOfferedIsNothingChosen()
    {
        Assert.Null(NpcLookPlan.Choose(null, ThorsteinLookPlan.Hair).PrefabName);
        Assert.Null(NpcLookPlan.Choose(new NpcLookOption[0], ThorsteinLookPlan.Beard).PrefabName);
    }

    [Fact]
    public void AnUnrelatedFamilyMemberIsAFamilyFallbackNotAMatch()
    {
        var odd = new[] { new NpcLookOption("Hair99", "Topknot") };

        NpcLookChoice hair = NpcLookPlan.Choose(odd, ThorsteinLookPlan.Hair);

        Assert.Equal(NpcLookMatch.Family, hair.Match);
        Assert.False(hair.MatchesReference);
    }

    [Fact]
    public void HeIsDarkerAndLessBlondThanHulgi()
    {
        // The owner's readings: Thorstein skin ~0.68 and blondness ~0.22, against
        // Hulgi's 0.50 and 0.74. Same tone slider.
        Assert.True(ThorsteinLookColour.SkinHue > 0.5f);
        Assert.True(ThorsteinLookColour.HairLevel < 0.74f);
        Assert.Equal(0.68f, ThorsteinLookColour.SkinHue);
        Assert.Equal(0.94f, ThorsteinLookColour.HairTone);
        Assert.Equal(0.22f, ThorsteinLookColour.HairLevel);
    }

    [Fact]
    public void ColourGoesThroughTheGamesOwnConversion()
    {
        var palette = new NpcLookPalette(
            skinLow: new NpcLookRgb(1f, 0.9f, 0.8f),
            skinHigh: new NpcLookRgb(0.4f, 0.3f, 0.2f),
            hairLow: new NpcLookRgb(1f, 1f, 1f),
            hairHigh: new NpcLookRgb(0.5f, 0.4f, 0.3f),
            minimumLevel: 0.1f,
            maximumLevel: 1f,
            observed: true);

        NpcLookRgb skin = ThorsteinLookColour.ThorsteinSkin(palette)!.Value;
        NpcLookRgb hair = ThorsteinLookColour.ThorsteinHair(palette);

        // skin = lerp(low, high, 0.68)
        Assert.Equal(1f + ((0.4f - 1f) * 0.68f), skin.R, 4);
        // hair = lerp(low, high, 0.94) * lerp(0.1, 1, 0.22)
        float level = 0.1f + (0.9f * 0.22f);
        Assert.Equal((1f + ((0.5f - 1f) * 0.94f)) * level, hair.R, 4);
    }

    [Fact]
    public void AnUnreadPaletteLeavesTheSkinAloneAndUsesTheStatedHairFallback()
    {
        Assert.Null(ThorsteinLookColour.ThorsteinSkin(NpcLookPalette.Unobserved));
        NpcLookRgb hair = ThorsteinLookColour.ThorsteinHair(NpcLookPalette.Unobserved);
        Assert.Equal(ThorsteinLookColour.HairFallback.R, hair.R);
        Assert.Equal(ThorsteinLookColour.HairFallback.G, hair.G);
    }

    [Fact]
    public void HisSpecIsTheReferenceOnTheFirstBodyWithThePlayersOverridesPassedThrough()
    {
        // Since the lift Concerned NPC dresses him; what Foreman still owns is
        // the data, and this is the whole of it.
        NpcLookSpec spec = ThorsteinLookPlan.Spec("Short Curls", null, string.Empty, "ArmorRagsLegs");

        Assert.Equal("Thorstein", spec.Who);
        Assert.Equal(NpcBodyModel.Male, spec.Model);
        Assert.Same(ThorsteinLookPlan.Hair, spec.Hair);
        Assert.Same(ThorsteinLookPlan.Beard, spec.Beard);
        Assert.Same(ThorsteinLookPlan.Chest, spec.Chest);
        Assert.Same(ThorsteinLookPlan.Legs, spec.Legs);
        Assert.Equal(ThorsteinLookColour.SkinHue, spec.SkinHue);
        Assert.Equal(ThorsteinLookColour.HairTone, spec.HairTone);
        Assert.Equal(ThorsteinLookColour.HairLevel, spec.HairLevel);
        Assert.Equal("Short Curls", spec.HairOverride);
        Assert.Null(spec.BeardOverride);
        Assert.Equal(string.Empty, spec.ChestOverride);
        Assert.Equal("ArmorRagsLegs", spec.LegsOverride);

        // The spec's colours are exactly his own wrappers', so the lift moved
        // the arithmetic without changing an answer.
        Assert.Equal(
            ThorsteinLookColour.ThorsteinHair(NpcLookPalette.Unobserved).R,
            spec.HairColour(NpcLookPalette.Unobserved).R);
        Assert.Null(spec.SkinColour(NpcLookPalette.Unobserved));
    }
}
