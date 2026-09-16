using TheConcernedCat.Companions.Placement;
using TheConcernedCat.ConcernedCartographer.Companions;

namespace ConcernedCartographer.Tests.Companions;

/// <summary>CC-NPC-004: where the companion lives, and what he looks like.
///
/// Almost every test here is about <i>not</i> acting. A bed whose chunk is not
/// loaded must not move anybody; a hidden companion must not lose his home; a
/// home point that jitters must not cause a rebuild. Those are the cases that
/// make a companion feel settled rather than twitchy, and they are also the
/// ones a live playtest would take an hour to reach.</summary>
public sealed class CompanionResidencyTests
{
    private static CompanionAnchor Bed(float x = 100f, float z = 100f)
    {
        return new CompanionAnchor(AnchorKind.ClaimedBed, new WorldPoint(x, 30f, z));
    }

    private static CompanionAnchor Start(float x = 0f, float z = 0f)
    {
        return new CompanionAnchor(AnchorKind.DefaultSpawn, new WorldPoint(x, 30f, z));
    }

    private static ResidencyInputs Inputs(
        bool recruited = true,
        bool visible = true,
        bool actorPresent = false,
        bool supported = true,
        CompanionAnchor current = default,
        CompanionAnchor placed = default,
        AnchorValidity validity = AnchorValidity.Valid,
        SeatStatus seat = SeatStatus.NotSeated)
    {
        return new ResidencyInputs(
            recruited, visible, actorPresent, supported, current, placed, validity, seat);
    }

    // ------------------------------------------------------------------
    // Giving up a seat
    // ------------------------------------------------------------------

    [Fact]
    public void Seat_ASeatThatWasTakenOrTakenAwayIsGivenUpAtOnce()
    {
        // The home point has NOT moved in either case, so without this rule
        // nothing below would notice and he would stay folded into a chair
        // that is gone, or sitting inside the player who just sat down.
        Assert.Equal(
            ResidencyAction.Rehome,
            ResidencyPlanner.Decide(Inputs(
                actorPresent: true, current: Bed(), placed: Bed(), seat: SeatStatus.Lost)));
    }

    [Fact]
    public void Seat_HoldingASeatChangesNothing()
    {
        Assert.Equal(
            ResidencyAction.None,
            ResidencyPlanner.Decide(Inputs(
                actorPresent: true, current: Bed(), placed: Bed(), seat: SeatStatus.Held)));

        Assert.Equal(
            ResidencyAction.None,
            ResidencyPlanner.Decide(Inputs(
                actorPresent: true, current: Bed(), placed: Bed(), seat: SeatStatus.NotSeated)));
    }

    [Fact]
    public void Seat_ALostSeatNeverResurrectsAHiddenOrUnrecruitedCompanion()
    {
        // Presentation is downstream of everything: losing a seat must not
        // become a reason to build an actor the player asked not to see.
        Assert.Equal(
            ResidencyAction.Remove,
            ResidencyPlanner.Decide(Inputs(
                visible: false, actorPresent: true, current: Bed(), placed: Bed(),
                seat: SeatStatus.Lost)));

        Assert.Equal(
            ResidencyAction.Remove,
            ResidencyPlanner.Decide(Inputs(
                recruited: false, actorPresent: true, current: Bed(), placed: Bed(),
                seat: SeatStatus.Lost)));

        Assert.Equal(
            ResidencyAction.Remove,
            ResidencyPlanner.Decide(Inputs(
                supported: false, actorPresent: true, current: Bed(), placed: Bed(),
                seat: SeatStatus.Lost)));
    }

    [Fact]
    public void Seat_ALostSeatWithNoActorIsStillJustAPlacement()
    {
        Assert.Equal(
            ResidencyAction.Place,
            ResidencyPlanner.Decide(Inputs(
                actorPresent: false, current: Bed(), placed: default, seat: SeatStatus.Lost)));
    }

    // ------------------------------------------------------------------
    // Which home point to believe
    // ------------------------------------------------------------------

    [Fact]
    public void Resolve_AValidBedWins()
    {
        Assert.Equal(
            AnchorKind.ClaimedBed,
            ResidencyPlanner.Resolve(Bed(), AnchorValidity.Valid, Start()).Kind);
    }

    [Fact]
    public void Resolve_AnUnloadedBedIsStillTheBed()
    {
        // The single most important line in this file. "I cannot see it" is not
        // "it is gone" — otherwise every trip away from home relocates the
        // companion to the world's starting point.
        Assert.Equal(
            AnchorKind.ClaimedBed,
            ResidencyPlanner.Resolve(Bed(), AnchorValidity.Unknown, Start()).Kind);
    }

    [Fact]
    public void Resolve_ABedConfirmedGoneFallsBackToTheWorldStart()
    {
        Assert.Equal(
            AnchorKind.DefaultSpawn,
            ResidencyPlanner.Resolve(Bed(), AnchorValidity.Gone, Start()).Kind);
    }

    [Fact]
    public void Resolve_NoBedAtAllUsesTheWorldStart()
    {
        Assert.Equal(
            AnchorKind.DefaultSpawn,
            ResidencyPlanner.Resolve(CompanionAnchor.None, AnchorValidity.Gone, Start()).Kind);
    }

    // ------------------------------------------------------------------
    // Whether to build, move or remove the actor
    // ------------------------------------------------------------------

    [Fact]
    public void Decide_ARecruitedVisibleCompanionWithNoActorIsPlaced()
    {
        Assert.Equal(
            ResidencyAction.Place,
            ResidencyPlanner.Decide(Inputs(current: Bed())));
    }

    [Fact]
    public void Decide_SomebodyWhoChoseToolsOnlyGetsNoActor()
    {
        Assert.Equal(
            ResidencyAction.None,
            ResidencyPlanner.Decide(Inputs(recruited: false, current: Bed())));
    }

    [Fact]
    public void Decide_HidingTheCompanionRemovesTheActor()
    {
        Assert.Equal(
            ResidencyAction.Remove,
            ResidencyPlanner.Decide(Inputs(
                visible: false, actorPresent: true, current: Bed(), placed: Bed())));
    }

    [Fact]
    public void Decide_AnUnsupportedPresentationRemovesTheActorAndNothingElse()
    {
        Assert.Equal(
            ResidencyAction.Remove,
            ResidencyPlanner.Decide(Inputs(
                supported: false, actorPresent: true, current: Bed(), placed: Bed())));
    }

    [Fact]
    public void Decide_NoHomePointYetLeavesAnExistingActorWhereItStands()
    {
        // A momentary resolution failure is not a reason to make somebody
        // vanish from the world.
        Assert.Equal(
            ResidencyAction.None,
            ResidencyPlanner.Decide(Inputs(
                actorPresent: true, current: CompanionAnchor.None, placed: Bed())));
    }

    [Fact]
    public void Decide_ASettledActorAtAnUnchangedHomeStaysPut()
    {
        Assert.Equal(
            ResidencyAction.None,
            ResidencyPlanner.Decide(Inputs(actorPresent: true, current: Bed(), placed: Bed())));
    }

    [Fact]
    public void Decide_ASmallJitterInTheHomePointDoesNotRebuild()
    {
        CompanionAnchor placed = Bed(100f, 100f);
        CompanionAnchor jittered = Bed(100f + (ResidencyPlanner.MoveTolerance * 0.5f), 100f);

        Assert.Equal(
            ResidencyAction.None,
            ResidencyPlanner.Decide(Inputs(actorPresent: true, current: jittered, placed: placed)));
    }

    [Fact]
    public void Decide_ABedClaimedSomewhereElseMovesHim()
    {
        Assert.Equal(
            ResidencyAction.Rehome,
            ResidencyPlanner.Decide(Inputs(
                actorPresent: true, current: Bed(400f, 400f), placed: Bed(100f, 100f))));
    }

    [Fact]
    public void Decide_ADestroyedBedMovesHimToTheWorldStart()
    {
        Assert.Equal(
            ResidencyAction.Rehome,
            ResidencyPlanner.Decide(Inputs(
                actorPresent: true,
                current: Start(),
                placed: Bed(),
                validity: AnchorValidity.Gone)));
    }

    [Fact]
    public void Decide_AnUnloadedBedNeverMovesHim()
    {
        // The resolution step already kept the bed, so current == placed; this
        // pins that the decision step cannot undo that on its own.
        Assert.Equal(
            ResidencyAction.None,
            ResidencyPlanner.Decide(Inputs(
                actorPresent: true,
                current: Bed(),
                placed: Bed(),
                validity: AnchorValidity.Unknown)));
    }

    [Fact]
    public void Decide_RemovalIsNotRepeatedWhenThereIsNothingToRemove()
    {
        Assert.Equal(
            ResidencyAction.None,
            ResidencyPlanner.Decide(Inputs(visible: false, actorPresent: false, current: Bed())));
    }

    // ------------------------------------------------------------------
    // Appearance
    // ------------------------------------------------------------------

    [Fact]
    public void Appearance_MatchesTheOwnersCustomizationLabelFirst()
    {
        // The label is what the owner actually chose on the character screen,
        // so it outranks the prefab id even when both are present and point
        // somewhere else.
        var available = new[]
        {
            new AppearanceOption("Hair11", "Braided Ponytail"),
            new AppearanceOption("Hair31", "Long Braid"),
        };

        AppearanceChoice choice = AppearancePlan.Choose(available, AppearancePlan.HulgiHair);

        Assert.Equal("Hair31", choice.PrefabName);
        Assert.Equal(AppearanceMatch.DisplayName, choice.Match);
        Assert.True(choice.MatchesReference);
    }

    [Fact]
    public void Appearance_FallsBackToTheDocumentedPrefabIdWhenLabelsAreUnavailable()
    {
        // A build with no localization still has the prefab ids, and the
        // Jotunn-derived mapping is exactly what they are for.
        var available = new[]
        {
            new AppearanceOption("Hair10"),
            new AppearanceOption("Hair11"),
        };

        AppearanceChoice choice = AppearancePlan.Choose(available, AppearancePlan.HulgiHair);

        Assert.Equal("Hair11", choice.PrefabName);
        Assert.Equal(AppearanceMatch.PrefabName, choice.Match);

        // And it must NOT claim the reference was matched: nothing confirmed
        // that this build calls Hair11 "Long Braid".
        Assert.False(choice.MatchesReference);
    }

    [Fact]
    public void Appearance_HandlebarBeardIsWhatIsAskedForNowAndMuttonChopsAreNot()
    {
        // CC-NPC-006 replaced the pre-screenshot mutton-chops prose. A build
        // carrying both must pick the one the owner chose.
        var available = new[]
        {
            new AppearanceOption("Beard4", "Mutton Chops"),
            new AppearanceOption("Beard26", "Handlebar"),
        };

        AppearanceChoice choice = AppearancePlan.Choose(available, AppearancePlan.HulgiBeard);

        Assert.Equal("Beard26", choice.PrefabName);
        Assert.True(choice.MatchesReference);
    }

    [Fact]
    public void Appearance_FallsBackThroughShapeThenFamilyThenNothing()
    {
        var shaped = new[]
        {
            new AppearanceOption("BeardX", "Thick Beard"),
            new AppearanceOption("BeardY", "A Fine Moustache"),
        };
        AppearanceChoice byShape = AppearancePlan.Choose(shaped, AppearancePlan.HulgiBeard);
        Assert.Equal("BeardY", byShape.PrefabName);
        Assert.Equal(AppearanceMatch.Shape, byShape.Match);
        Assert.False(byShape.MatchesReference);

        var family = new[] { new AppearanceOption("BeardSomethingUnheardOf") };
        AppearanceChoice byFamily = AppearancePlan.Choose(family, AppearancePlan.HulgiBeard);
        Assert.Equal("BeardSomethingUnheardOf", byFamily.PrefabName);
        Assert.Equal(AppearanceMatch.Family, byFamily.Match);

        Assert.Equal(
            AppearanceMatch.None,
            AppearancePlan.Choose(new AppearanceOption[0], AppearancePlan.HulgiBeard).Match);
        Assert.Null(AppearancePlan.Choose(null, AppearancePlan.HulgiHair).PrefabName);
    }

    [Fact]
    public void Appearance_AnExplicitPlayerChoiceWinsAndAnUnknownOneDoesNotStrand()
    {
        var available = new[]
        {
            new AppearanceOption("Hair11", "Long Braid"),
            new AppearanceOption("Hair23", "Short Curls"),
        };

        AppearanceChoice chosen = AppearancePlan.Choose(
            available, AppearancePlan.HulgiHair, playerOverride: "Short Curls");
        Assert.Equal("Hair23", chosen.PrefabName);
        Assert.Equal(AppearanceMatch.Override, chosen.Match);

        // The override may also be a prefab id.
        Assert.Equal(
            "Hair23",
            AppearancePlan.Choose(available, AppearancePlan.HulgiHair, "hair23").PrefabName);

        // An override naming something this build does not have must fall
        // through to the stock default rather than leaving him bald.
        AppearanceChoice missing = AppearancePlan.Choose(
            available, AppearancePlan.HulgiHair, "NotInThisBuild");
        Assert.Equal("Hair11", missing.PrefabName);
        Assert.Equal(AppearanceMatch.DisplayName, missing.Match);
    }

    [Fact]
    public void Appearance_MatchingIsCaseAndWhitespaceInsensitive()
    {
        var available = new[] { new AppearanceOption("Hair11", "  LONG braid ") };

        AppearanceChoice choice = AppearancePlan.Choose(available, AppearancePlan.HulgiHair);

        Assert.Equal(AppearanceMatch.DisplayName, choice.Match);
    }

    [Fact]
    public void Appearance_FamilyFilterIsCaseInsensitiveAndOrdered()
    {
        IReadOnlyList<AppearanceOption> all = AppearancePlan.FromPrefabNames(
            new[] { "HairShort1", "beardlong1", "SwordIron", "HairBraided1", "BeardShort1" });

        Assert.Equal(
            new[] { "HairBraided1", "HairShort1" },
            Names(AppearancePlan.FilterFamily(all, "Hair")));
        Assert.Equal(
            new[] { "beardlong1", "BeardShort1" },
            Names(AppearancePlan.FilterFamily(all, "Beard")));
        Assert.Empty(AppearancePlan.FilterFamily(all, ""));
        Assert.Empty(AppearancePlan.FilterFamily(null, "Hair"));
    }

    // ------------------------------------------------------------------
    // Appearance colour
    // ------------------------------------------------------------------

    [Fact]
    public void Colour_UsesTheGamesOwnConversionRatherThanTheSliderTuple()
    {
        // Lerp(hairLow, hairHigh, tone) * Lerp(minLevel, maxLevel, level),
        // transcribed from PlayerCustomizaton.Update. Chosen so every factor
        // is distinguishable: a implementation that stored the slider tuple as
        // RGB, or dropped the level multiplier, gets a different answer.
        var palette = new CustomizationPalette(
            skinLow: new ColourTriple(0.2f, 0.2f, 0.2f),
            skinHigh: new ColourTriple(1f, 0.8f, 0.6f),
            hairLow: new ColourTriple(0.1f, 0.05f, 0f),
            hairHigh: new ColourTriple(0.9f, 0.8f, 0.5f),
            minimumLevel: 0.1f,
            maximumLevel: 1f,
            observed: true);

        ColourTriple hair = AppearanceColour.Hair(palette, hairTone: 0.5f, hairLevel: 0.5f);

        // ramp = (0.5, 0.425, 0.25); level = 0.55
        Assert.Equal(0.275f, hair.R, 4);
        Assert.Equal(0.23375f, hair.G, 4);
        Assert.Equal(0.1375f, hair.B, 4);

        ColourTriple skin = AppearanceColour.Skin(palette, 0.25f);
        Assert.Equal(0.4f, skin.R, 4);
        Assert.Equal(0.35f, skin.G, 4);
        Assert.Equal(0.3f, skin.B, 4);
    }

    [Fact]
    public void Colour_AnUnreadPaletteUsesTheDocumentedFallbackAndNoSkinTint()
    {
        CustomizationPalette unobserved = CustomizationPalette.Unobserved;

        ColourTriple hair = AppearanceColour.HulgiHair(unobserved);
        Assert.Equal(AppearanceColour.HairFallback.R, hair.R, 4);
        Assert.Equal(AppearanceColour.HairFallback.G, hair.G, 4);
        Assert.Equal(AppearanceColour.HairFallback.B, hair.B, 4);

        // A guessed body colour is worse than the model's own, so there is
        // deliberately no skin fallback.
        Assert.Null(AppearanceColour.HulgiSkin(unobserved));
    }

    [Fact]
    public void Colour_SliderReadingsOutsideTheRangeBehaveLikeTheGamesClamp()
    {
        var palette = new CustomizationPalette(
            skinLow: new ColourTriple(0f, 0f, 0f),
            skinHigh: new ColourTriple(1f, 1f, 1f),
            hairLow: new ColourTriple(0f, 0f, 0f),
            hairHigh: new ColourTriple(1f, 1f, 1f),
            minimumLevel: 0.1f,
            maximumLevel: 1f,
            observed: true);

        Assert.Equal(1f, AppearanceColour.Skin(palette, 4f).R, 4);
        Assert.Equal(0f, AppearanceColour.Skin(palette, -4f).R, 4);
        Assert.Equal(0f, AppearanceColour.Hair(palette, float.NaN, 1f).R, 4);
    }

    [Fact]
    public void Colour_HulgisSlidersAreTheOwnerReadingsAndReachTheResult()
    {
        Assert.Equal(0.50f, AppearanceColour.HulgiSkinHue, 4);
        Assert.Equal(0.94f, AppearanceColour.HulgiHairTone, 4);
        Assert.Equal(0.74f, AppearanceColour.HulgiHairLevel, 4);

        var palette = new CustomizationPalette(
            skinLow: new ColourTriple(0f, 0f, 0f),
            skinHigh: new ColourTriple(1f, 1f, 1f),
            hairLow: new ColourTriple(0f, 0f, 0f),
            hairHigh: new ColourTriple(1f, 1f, 1f),
            minimumLevel: 0f,
            maximumLevel: 1f,
            observed: true);

        // tone 0.94 along a black-to-white ramp, scaled by level 0.74.
        Assert.Equal(0.94f * 0.74f, AppearanceColour.HulgiHair(palette).R, 4);
        Assert.Equal(0.50f, AppearanceColour.HulgiSkin(palette)!.Value.R, 4);
    }

    // ------------------------------------------------------------------
    // Appearance fit
    // ------------------------------------------------------------------

    [Fact]
    public void Fit_APresetDrawnAwayFromTheHeadIsNotWorn()
    {
        // Observed on 1.0.12: hair reported "attached" and rendered 0.99 m
        // from the head, hanging at standing height over a seated companion.
        // Attaching is not the same as wearing, and only one of them is what
        // the owner asked for.
        Assert.False(AppearanceFit.Fits(0.99f));
        Assert.False(AppearanceFit.Fits(AppearanceFit.ToleranceMetres + 0.01f));
    }

    [Fact]
    public void Fit_ATallHairstyleOrLongBeardStillCounts()
    {
        Assert.True(AppearanceFit.Fits(0f));
        Assert.True(AppearanceFit.Fits(0.3f));
        Assert.True(AppearanceFit.Fits(AppearanceFit.ToleranceMetres));
    }

    [Fact]
    public void Fit_APieceThatCannotBeMeasuredIsNotAssumedToBeRight()
    {
        // No renderer to measure means no evidence it landed anywhere.
        Assert.False(AppearanceFit.Fits(float.NaN));
    }

    private static string[] Names(IReadOnlyList<AppearanceOption> options)
    {
        var names = new string[options.Count];
        for (int index = 0; index < options.Count; index++)
        {
            names[index] = options[index].PrefabName;
        }

        return names;
    }
}
