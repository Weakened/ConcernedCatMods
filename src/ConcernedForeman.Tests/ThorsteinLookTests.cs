using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Domain.Presentation;

namespace ConcernedForeman.Tests;

/// <summary>#298, the Thorstein row: Mullet and Mustache, a deeper complexion,
/// and the rule that a missing preset is a fallback said out loud rather than
/// a match claimed.</summary>
public sealed class ThorsteinLookTests
{
    private static readonly IReadOnlyList<LookOption> Hair = new[]
    {
        new LookOption("Hair11", "Long Braid"),
        new LookOption("Hair23", "Short Curls"),
        new LookOption("Hair32", "Mullet"),
    };

    private static readonly IReadOnlyList<LookOption> Beards = new[]
    {
        new LookOption("Beard16", "Stonedweller"),
        new LookOption("Beard22", "Mustache"),
        new LookOption("Beard26", "Handlebar"),
    };

    [Fact]
    public void TheOwnersReferenceIsMulletAndMustacheByLabel()
    {
        Assert.Equal("Mullet", ThorsteinLookPlan.Hair.DisplayName);
        Assert.Equal("Mustache", ThorsteinLookPlan.Beard.DisplayName);

        LookChoice hair = ThorsteinLookPlan.Choose(Hair, ThorsteinLookPlan.Hair);
        LookChoice beard = ThorsteinLookPlan.Choose(Beards, ThorsteinLookPlan.Beard);

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
        var renumbered = new[] { new LookOption("Hair32", "Something Else"), new LookOption("Hair40", "Mullet") };

        LookChoice hair = ThorsteinLookPlan.Choose(renumbered, ThorsteinLookPlan.Hair);

        Assert.Equal("Hair40", hair.PrefabName);
        Assert.Equal(LookMatch.DisplayName, hair.Match);
    }

    [Fact]
    public void WithoutLabelsTheIdIsAFallbackAndNeverCalledAMatch()
    {
        var unlabelled = new[] { new LookOption("Hair11"), new LookOption("Hair32") };

        LookChoice hair = ThorsteinLookPlan.Choose(unlabelled, ThorsteinLookPlan.Hair);

        Assert.Equal("Hair32", hair.PrefabName);
        Assert.Equal(LookMatch.PrefabName, hair.Match);
        Assert.False(hair.MatchesReference);
    }

    [Fact]
    public void APlayersOverrideWinsAndAMissingOneFallsThrough()
    {
        Assert.Equal(
            LookMatch.Override, ThorsteinLookPlan.Choose(Hair, ThorsteinLookPlan.Hair, "Short Curls").Match);
        Assert.Equal("Hair23", ThorsteinLookPlan.Choose(Hair, ThorsteinLookPlan.Hair, "Hair23").PrefabName);

        LookChoice missing = ThorsteinLookPlan.Choose(Hair, ThorsteinLookPlan.Hair, "No Such Hair");
        Assert.Equal("Hair32", missing.PrefabName);
        Assert.True(missing.MatchesReference);
    }

    [Fact]
    public void NothingOfferedIsNothingChosen()
    {
        Assert.Null(ThorsteinLookPlan.Choose(null, ThorsteinLookPlan.Hair).PrefabName);
        Assert.Null(ThorsteinLookPlan.Choose(new LookOption[0], ThorsteinLookPlan.Beard).PrefabName);
    }

    [Fact]
    public void AnUnrelatedFamilyMemberIsAFamilyFallbackNotAMatch()
    {
        var odd = new[] { new LookOption("Hair99", "Topknot") };

        LookChoice hair = ThorsteinLookPlan.Choose(odd, ThorsteinLookPlan.Hair);

        Assert.Equal(LookMatch.Family, hair.Match);
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
        var palette = new LookPalette(
            skinLow: new LookRgb(1f, 0.9f, 0.8f),
            skinHigh: new LookRgb(0.4f, 0.3f, 0.2f),
            hairLow: new LookRgb(1f, 1f, 1f),
            hairHigh: new LookRgb(0.5f, 0.4f, 0.3f),
            minimumLevel: 0.1f,
            maximumLevel: 1f,
            observed: true);

        LookRgb skin = ThorsteinLookColour.ThorsteinSkin(palette)!.Value;
        LookRgb hair = ThorsteinLookColour.ThorsteinHair(palette);

        // skin = lerp(low, high, 0.68)
        Assert.Equal(1f + ((0.4f - 1f) * 0.68f), skin.R, 4);
        // hair = lerp(low, high, 0.94) * lerp(0.1, 1, 0.22)
        float level = 0.1f + (0.9f * 0.22f);
        Assert.Equal((1f + ((0.5f - 1f) * 0.94f)) * level, hair.R, 4);
    }

    [Fact]
    public void AnUnreadPaletteLeavesTheSkinAloneAndUsesTheStatedHairFallback()
    {
        Assert.Null(ThorsteinLookColour.ThorsteinSkin(LookPalette.Unobserved));
        LookRgb hair = ThorsteinLookColour.ThorsteinHair(LookPalette.Unobserved);
        Assert.Equal(ThorsteinLookColour.HairFallback.R, hair.R);
        Assert.Equal(ThorsteinLookColour.HairFallback.G, hair.G);
    }

    [Fact]
    public void ASkinnedPresetWithTheBodysBoneCountAdoptsTheBodysSkeleton()
    {
        // #305: matching by name keeps the piece's own order and drew Hulgi's
        // braid 0.99 m away. Same count means the body's array, verbatim.
        Assert.Equal(LookSkinBinding.AdoptBodySkeleton, LookSkinBindings.Decide(57, 57));
        Assert.Equal(LookSkinBinding.MatchByName, LookSkinBindings.Decide(12, 57));
        Assert.Equal(LookSkinBinding.Refuse, LookSkinBindings.Decide(0, 57));
        Assert.Equal(LookSkinBinding.Refuse, LookSkinBindings.Decide(57, 0));
    }

    [Fact]
    public void APieceThatMissedIsTakenOff()
    {
        Assert.True(LookFit.Fits(0.2f, LookFit.HeadToleranceMetres));
        Assert.False(LookFit.Fits(0.99f, LookFit.HeadToleranceMetres));
        Assert.False(LookFit.Fits(float.NaN, LookFit.HeadToleranceMetres));
        Assert.True(LookFit.Fits(0.8f, LookFit.GarmentToleranceMetres));
    }
}
