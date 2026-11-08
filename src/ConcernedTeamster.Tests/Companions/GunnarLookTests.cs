using TheConcernedCat.ConcernedNPC.Appearance;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;
namespace TheConcernedCat.ConcernedTeamster.Tests.Companions;

/// <summary>#458, the Gunnar row, modelled on Foreman's ThorsteinLookTests:
/// Short Curls and Stonedweller by the owner's label, the ids and tokens read
/// from the installed game as secondary keys, the palette readings, and the
/// rule that a missing preset is a fallback said out loud rather than a match
/// claimed.</summary>
public sealed class GunnarLookTests
{
    private static readonly IReadOnlyList<NpcLookOption> Hair = new[]
    {
        new NpcLookOption("Hair11", "Long Braid", "$customization_hair11"),
        new NpcLookOption("Hair23", "Short Curls", "$customization_hair23"),
        new NpcLookOption("Hair32", "Mullet", "$customization_hair32"),
    };

    private static readonly IReadOnlyList<NpcLookOption> Beards = new[]
    {
        new NpcLookOption("Beard16", "Stonedweller", "$customization_beard16"),
        new NpcLookOption("Beard22", "Mustache", "$customization_beard22"),
    };

    [Fact]
    public void TheOwnersReferenceIsShortCurlsAndStonedwellerByLabel()
    {
        Assert.Equal("Short Curls", GunnarLook.Hair.DisplayName);
        Assert.Equal("Stonedweller", GunnarLook.Beard.DisplayName);
        NpcLookChoice hair = NpcLookPlan.Choose(Hair, GunnarLook.Hair);
        NpcLookChoice beard = NpcLookPlan.Choose(Beards, GunnarLook.Beard);
        Assert.Equal("Hair23", hair.PrefabName);
        Assert.Equal(NpcLookMatch.DisplayName, hair.Match);
        Assert.Equal("Beard16", beard.PrefabName);
        Assert.True(beard.MatchesReference);
    }

    [Fact]
    public void TheSecondaryKeysAreTheOnesReadFromTheInstalledGame()
    {
        // Read 2026-11-08 from the installed game's localization table and
        // prefab manifest (see GunnarLook's remarks), not from a generated list.
        Assert.Equal(new[] { "Hair23" }, GunnarLook.Hair.PrefabCandidates);
        Assert.Equal("$customization_hair23", GunnarLook.Hair.Token);
        Assert.Equal(new[] { "Beard16" }, GunnarLook.Beard.PrefabCandidates);
        Assert.Equal("$customization_beard16", GunnarLook.Beard.Token);
    }

    [Fact]
    public void TheLabelOutranksTheIdWhenABuildRenumbers()
    {
        var renumbered = new[] { new NpcLookOption("Hair23", "Something Else"), new NpcLookOption("Hair40", "Short Curls") };
        NpcLookChoice hair = NpcLookPlan.Choose(renumbered, GunnarLook.Hair);
        Assert.Equal("Hair40", hair.PrefabName);
        Assert.Equal(NpcLookMatch.DisplayName, hair.Match);
    }

    [Fact]
    public void InAnotherLanguageTheTokenStillMatches()
    {
        var swedish = new[] { new NpcLookOption("Hair23", "Korta lockar", "$customization_hair23") };
        NpcLookChoice hair = NpcLookPlan.Choose(swedish, GunnarLook.Hair);
        Assert.Equal("Hair23", hair.PrefabName);
        Assert.Equal(NpcLookMatch.Token, hair.Match);
        Assert.True(hair.MatchesReference);
    }

    [Fact]
    public void WithoutLabelsOrTokensTheIdIsAFallbackAndNeverCalledAMatch()
    {
        var unlabelled = new[] { new NpcLookOption("Hair11"), new NpcLookOption("Hair23") };
        NpcLookChoice hair = NpcLookPlan.Choose(unlabelled, GunnarLook.Hair);
        Assert.Equal("Hair23", hair.PrefabName);
        Assert.Equal(NpcLookMatch.PrefabName, hair.Match);
        Assert.False(hair.MatchesReference);
    }

    [Fact]
    public void AMissingPresetIsAnHonestFallback()
    {
        var other = new[] { new NpcLookOption("Hair11", "Long Braid") };
        NpcLookChoice hair = NpcLookPlan.Choose(other, GunnarLook.Hair);
        Assert.False(hair.MatchesReference);
        Assert.NotEqual(NpcLookMatch.DisplayName, hair.Match);
        Assert.Equal(NpcLookMatch.None, NpcLookPlan.Choose(Array.Empty<NpcLookOption>(), GunnarLook.Hair).Match);
    }

    [Fact]
    public void ThePaletteReadingsAreTheReferencesAndTheFallbackIsStated()
    {
        NpcLookSpec spec = GunnarLook.Spec();
        Assert.Equal("Gunnar", spec.Who);
        Assert.Equal(NpcBodyModel.Male, spec.Model);
        Assert.Equal(0.52f, spec.SkinHue);
        Assert.Equal(0.05f, spec.HairTone);
        Assert.Equal(0.49f, spec.HairLevel);
        Assert.Equal(GunnarLook.HairFallback, spec.HairFallback);
    }

    [Fact]
    public void LeatherTrousersAndABareTorsoAreTheProposal()
    {
        NpcLookSpec spec = GunnarLook.Spec();
        Assert.Null(spec.Chest);
        Assert.NotNull(spec.Legs);
        var legs = new[] { new NpcLookOption("ArmorRagsLegs", "Rag pants"), new NpcLookOption("ArmorLeatherLegs", "Leather Trousers", "$item_legs_leather") };
        NpcLookChoice chosen = NpcLookPlan.Choose(legs, spec.Legs!);
        Assert.Equal("ArmorLeatherLegs", chosen.PrefabName);
        Assert.Equal(NpcLookMatch.DisplayName, chosen.Match);
    }

    [Fact]
    public void APlayersOwnChoiceIsNeverReplacedByTheStockDefault()
    {
        NpcLookSpec spec = GunnarLook.Spec("Hair11", "Beard22", "ArmorRagsLegs");
        Assert.Equal("Hair11", spec.HairOverride);
        Assert.Equal("Beard22", spec.BeardOverride);
        Assert.Equal("ArmorRagsLegs", spec.LegsOverride);
        NpcLookChoice hair = NpcLookPlan.Choose(Hair, spec.Hair!, spec.HairOverride);
        Assert.Equal("Hair11", hair.PrefabName);
        Assert.Equal(NpcLookMatch.Override, hair.Match);
    }
}
