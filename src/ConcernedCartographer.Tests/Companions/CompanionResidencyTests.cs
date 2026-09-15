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
        AnchorValidity validity = AnchorValidity.Valid)
    {
        return new ResidencyInputs(
            recruited, visible, actorPresent, supported, current, placed, validity);
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
    public void Appearance_PrefersTheFirstPreferenceThatExists()
    {
        var available = new[] { "BeardShort1", "BeardMuttonchops1", "BeardThick1" };

        AppearanceChoice choice = AppearancePlan.Choose(
            available, AppearancePlan.BeardPreferences, AppearanceCatalogPrefix);

        Assert.Equal("BeardMuttonchops1", choice.PrefabName);
        Assert.Equal(AppearanceMatch.Preferred, choice.Match);
    }

    [Fact]
    public void Appearance_FallsBackToTheSameFamilyWhenNoPreferenceExists()
    {
        var available = new[] { "BeardSomethingUnheardOf" };

        AppearanceChoice choice = AppearancePlan.Choose(
            available, AppearancePlan.BeardPreferences, AppearanceCatalogPrefix);

        Assert.Equal("BeardSomethingUnheardOf", choice.PrefabName);
        Assert.Equal(AppearanceMatch.Alternate, choice.Match);
    }

    [Fact]
    public void Appearance_AnEmptyBuildYieldsNoItemRatherThanAGuess()
    {
        Assert.Equal(
            AppearanceMatch.None,
            AppearancePlan.Choose(
                new string[0], AppearancePlan.BeardPreferences, AppearanceCatalogPrefix).Match);
        Assert.Null(AppearancePlan.Choose(
            null, AppearancePlan.HairPreferences, "Hair").PrefabName);
    }

    [Fact]
    public void Appearance_FamilyFilterIsCaseInsensitiveAndOrdered()
    {
        var all = new[] { "HairShort1", "beardlong1", "SwordIron", "HairBraided1", "BeardShort1" };

        Assert.Equal(new[] { "HairBraided1", "HairShort1" }, AppearancePlan.FilterFamily(all, "Hair"));
        Assert.Equal(new[] { "beardlong1", "BeardShort1" }, AppearancePlan.FilterFamily(all, "Beard"));
        Assert.Empty(AppearancePlan.FilterFamily(all, ""));
        Assert.Empty(AppearancePlan.FilterFamily(null, "Hair"));
    }

    [Fact]
    public void Appearance_PreferenceMatchingIsCaseInsensitive()
    {
        // The audit could not establish the game's own spelling of these names,
        // which is exactly why the match must not depend on it.
        AppearanceChoice choice = AppearancePlan.Choose(
            new[] { "BeardMUTTONCHOPS2" }, AppearancePlan.BeardPreferences, AppearanceCatalogPrefix);

        Assert.Equal(AppearanceMatch.Preferred, choice.Match);
    }

    /// <summary>The beard family prefix, spelled here rather than referenced so
    /// these tests stay game-free.</summary>
    private const string AppearanceCatalogPrefix = "Beard";
}
