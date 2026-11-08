using System;
using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;

namespace Shared.Settlement.Tests;

/// <summary>D19 inside a collection order (#282 leaf 2, #296's wood scope):
/// small trees are a labour tier after branches, only in an explicit work area,
/// one hit per swing, credited only with the Wood their own fell produced and
/// never more than the order still needs, and a cleared area says it is
/// exhausted.</summary>
public sealed class SmallTreeOrderTests
{
    private static CollectionRig Rig(CollectionParameters? parameters = null, bool withFelling = true) =>
        new CollectionRig(parameters, withFelling: withFelling, scopeSource: WorkScopeSource.HarvestDesignation);

    private static void AssertWoodConserved(CollectionRig rig, int picked)
    {
        ResourceProgress progress = rig.Progress(CollectedResource.Wood);
        Assert.Equal(picked, progress.OnGround + progress.Carried + progress.InCart + progress.Delivered + progress.HandedOver + progress.Lost);
        Assert.Equal(progress.Delivered, rig.Chest.Count(MaterialItem.Of(CollectedResource.Wood)));
        Assert.Equal(progress.Carried, rig.WorkerInventory.Count(MaterialItem.Of(CollectedResource.Wood)));
        Assert.Equal(0, rig.Loop.IllegalTransitionsAttempted);
        Assert.Equal(0, rig.Motion.RefusedCommands);
    }

    // --- The survey ----------------------------------------------------------------------------------------

    private static SurveySnapshot Survey(WorkScopeSource source, params SurveyCandidate[] candidates)
    {
        var probe = new FakeProbe();
        probe.Candidates.AddRange(candidates);
        var scope = new WorkScope(
            source, CollectionRig.Anchor, 30f, "harvest area", ScopeRevision.ForAnchor("bed", CollectionRig.Anchor), CollectionRig.Epoch);
        var survey = new SurveyScheduler(CollectionParameters.Default, scope, probe, 1, SurveyProvenance.SoloForeman, 0f);
        for (int step = 0; step < 100 && !survey.IsComplete; step++)
        {
            survey.Step(step * 0.05f);
        }

        Assert.True(survey.IsComplete);
        return survey.Snapshot!;
    }

    private static SurveyCandidate Tree(
        string prefab = "Beech_small1", float builtPieceMetres = float.PositiveInfinity, AreaAccess ward = AreaAccess.Granted,
        bool plant = false, bool pickable = false, bool ownedHere = true, LocationStanding location = LocationStanding.Outside,
        string id = "00000000cafe0000:00000001") =>
        new SurveyCandidate(
            new SourceKey(prefab, id, CollectionRig.Epoch, new SitePoint(5f, 30f, 5f)),
            CollectionRig.TreeFacts(prefab, plant: plant, pickable: pickable),
            ownedHere, inInterior: false, location, ward, builtPieceMetres, SmallTreeSources.MaxWood(prefab));

    [Fact]
    public void AnExplicitAreaRecordsAnEligibleSmallTreeAsAWoodSourceAtItsMostWood()
    {
        SurveySnapshot snapshot = Survey(WorkScopeSource.HarvestDesignation, Tree("FirTree_small"));

        SourceObservation tree = Assert.Single(snapshot.Sources);
        Assert.Equal(NaturalSourceKind.SmallTree, tree.Kind);
        Assert.Equal(CollectedResource.Wood, tree.Yields);
        Assert.Equal(SourceAvailability.Available, tree.Availability);
        Assert.Equal(5, tree.EstimatedYield);
    }

    [Fact]
    public void TheDefaultCampCircleNeverObservesATreeWhateverTheProbeSends()
    {
        SurveySnapshot snapshot = Survey(WorkScopeSource.DefaultCampCircle, Tree());
        Assert.Empty(snapshot.Sources);
    }

    [Fact]
    public void ATreeBesideABuildingOrUnderAWardIsInaccessibleAndAnUnmeasuredClearanceIsUnknown()
    {
        Assert.Equal(
            SourceAvailability.Inaccessible,
            Assert.Single(Survey(WorkScopeSource.HarvestDesignation, Tree(builtPieceMetres: 14.9f)).Sources).Availability);
        Assert.Equal(
            SourceAvailability.Available,
            Assert.Single(Survey(WorkScopeSource.HarvestDesignation, Tree(builtPieceMetres: 15f)).Sources).Availability);
        Assert.Equal(
            SourceAvailability.Inaccessible,
            Assert.Single(Survey(WorkScopeSource.HarvestDesignation, Tree(ward: AreaAccess.Denied)).Sources).Availability);
        Assert.Equal(
            SourceAvailability.Inaccessible,
            Assert.Single(Survey(WorkScopeSource.HarvestDesignation, Tree(location: LocationStanding.Inside)).Sources).Availability);
        Assert.Equal(
            SourceAvailability.Unknown,
            Assert.Single(Survey(WorkScopeSource.HarvestDesignation, Tree(builtPieceMetres: float.NaN)).Sources).Availability);
        Assert.Equal(
            SourceAvailability.Unknown,
            Assert.Single(Survey(WorkScopeSource.HarvestDesignation, Tree(ownedHere: false)).Sources).Availability);
    }

    [Fact]
    public void FoodPlantedAndUnlistedTreesAreNeverObserved()
    {
        Assert.Empty(Survey(WorkScopeSource.HarvestDesignation, Tree(pickable: true)).Sources);
        Assert.Empty(Survey(WorkScopeSource.HarvestDesignation, Tree(plant: true)).Sources);
        Assert.Empty(Survey(WorkScopeSource.HarvestDesignation, Tree(prefab: "Beech1")).Sources);
        Assert.Empty(Survey(WorkScopeSource.HarvestDesignation, Tree(prefab: "Beech_Sapling")).Sources);
    }

    // --- The selector --------------------------------------------------------------------------------------

    private static SourceObservation Observed(string prefab, NaturalSourceKind kind, float x, int yield, string id) =>
        new SourceObservation(
            new SourceKey(prefab, id, CollectionRig.Epoch, new SitePoint(x, 30f, 0f)), kind, CollectedResource.Wood, yield,
            SourceAvailability.Available, SourceReachability.NotChecked, 1, 0f);

    [Fact]
    public void ABranchFarAwayIsChosenBeforeASmallTreeAtHisFeet()
    {
        SourceObservation tree = Observed("Beech_small1", NaturalSourceKind.SmallTree, 1f, 3, "a");
        SourceObservation branch = Observed("Pickable_Branch", NaturalSourceKind.Branch, 28f, 1, "b");
        var needs = new List<ResourceNeed> { new ResourceNeed(CollectedResource.Wood, 10, 100, ResourceNeed.Unlimited) };

        SelectionResult result = CollectionTargetSelector.Select(
            CollectionParameters.Default, new[] { tree, branch }, needs, new SitePoint(0f, 30f, 0f), null, null);
        Assert.Same(branch, result.Source);

        result = CollectionTargetSelector.Select(
            CollectionParameters.Default, new[] { tree, branch }, needs, new SitePoint(0f, 30f, 0f), null, key => key.SessionId == "b");
        Assert.Same(tree, result.Source);
    }

    [Fact]
    public void ATreeIsPlannedAtWhatIsStillNeededSoTheLastFewUnitsCanComeFromOne()
    {
        SourceObservation fir = Observed("FirTree_small", NaturalSourceKind.SmallTree, 3f, 5, "a");

        // Two still needed, a fir may give five: chosen, and the take limit
        // (not the plan) is what stops overcollection.
        var needs = new List<ResourceNeed> { new ResourceNeed(CollectedResource.Wood, 2, 100, ResourceNeed.Unlimited) };
        Assert.Same(fir, CollectionTargetSelector.Select(CollectionParameters.Default, new[] { fir }, needs, default, null, null).Source);

        // Ten still needed but room for only three on him: deliver first.
        needs = new List<ResourceNeed> { new ResourceNeed(CollectedResource.Wood, 10, 3, ResourceNeed.Unlimited) };
        Assert.Equal(
            SelectionOutcome.CarryFull,
            CollectionTargetSelector.Select(CollectionParameters.Default, new[] { fir }, needs, default, null, null).Outcome);
    }

    // --- The loop ------------------------------------------------------------------------------------------

    [Fact]
    public void BranchesFirstThenSmallTreesOneHitPerSwingCreditedExactlyWithTheirOwnWood()
    {
        var rig = Rig();
        rig.AddSource(CollectedResource.Wood, -6f, -6f);
        rig.AddSource(CollectedResource.Wood, -7f, -6f);
        SourceKey beech = rig.AddTree(2f, 2f, "Beech_small1", hits: 2, wood: 2);
        SourceKey fir = rig.AddTree(3f, 2f, "FirTree_small", hits: 3, wood: 4);

        Assert.Equal(CollectionIntakeRefusal.Unspecified, rig.Accept(rig.Order(stone: 0, wood: 8)));
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed));

        // Both branches were picked before the first swing.
        Assert.Equal(2, rig.Pickup.Picked.Count);
        Assert.Equal(new[] { beech, fir }.OrderBy(k => k.SessionId), rig.Pickup.Felled.OrderBy(k => k.SessionId));
        Assert.Equal(5, rig.Pickup.Strikes.Count);
        Assert.Equal(2, rig.Loop.Fells);

        // 2 branches + 2 beech + 4 fir = 8, exactly the quota, all delivered.
        Assert.Equal(8, rig.Progress(CollectedResource.Wood).Delivered);
        AssertWoodConserved(rig, picked: 8);
    }

    [Fact]
    public void HitsOnOneTreeAreSpacedByTheSwingCadence()
    {
        var rig = Rig(new CollectionParameters(fellSwingSeconds: 2f));
        rig.AddTree(2f, 2f, hits: 3, wood: 1);
        rig.Accept(rig.Order(stone: 0, wood: 1));

        Assert.True(rig.RunUntil(() => rig.Pickup.Strikes.Count == 1));
        float first = rig.Now;
        Assert.True(rig.RunUntil(() => rig.Pickup.Strikes.Count == 2));
        Assert.True(rig.Now - first >= 2f - 0.001f, "a second hit landed before the swing cadence allowed");
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed));
        Assert.Equal(3, rig.Pickup.Strikes.Count);
    }

    [Fact]
    public void ATreeIsNeverCreditedWithMoreThanTheOrderStillNeeds()
    {
        var rig = Rig();
        rig.AddSource(CollectedResource.Wood, -6f, -6f);
        rig.AddTree(2f, 2f, "FirTree_small", hits: 1, wood: 5);
        rig.Accept(rig.Order(stone: 0, wood: 3));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed));

        // One branch, then the fir with a limit of the two still needed; the
        // other three Wood it dropped lie on the ground, never counted.
        Assert.Equal(2, Assert.Single(rig.Pickup.Strikes).TakeLimit);
        Assert.Equal(3, rig.Pickup.WoodLeftOnGround);
        Assert.Equal(3, rig.Progress(CollectedResource.Wood).Delivered);
        AssertWoodConserved(rig, picked: 3);
    }

    [Fact]
    public void TheTakeLimitIsAlsoWhatFitsOnHim()
    {
        // Wood weighs 2 in the fake world: 10 units of carry weight is 5 Wood.
        var rig = Rig(new CollectionParameters(workerCarryWeight: 10f));
        rig.AddTree(2f, 2f, "FirTree_small", hits: 1, wood: 5);
        rig.AddTree(4f, 2f, "FirTree_small", hits: 1, wood: 5);
        rig.Accept(rig.Order(stone: 0, wood: 10));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed || rig.Loop.State == CollectionOrderState.Paused));
        Assert.All(rig.Pickup.Strikes, strike => Assert.True(strike.TakeLimit <= 5));
        AssertWoodConserved(rig, picked: rig.Progress(CollectedResource.Wood).Delivered + rig.Progress(CollectedResource.Wood).Carried);
    }

    [Fact]
    public void AClearedAreaEndsPausedWithTheExhaustedReason()
    {
        var rig = Rig();
        rig.AddTree(2f, 2f, hits: 1, wood: 2);
        rig.Accept(rig.Order(stone: 0, wood: 10));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused));
        Assert.Equal(CollectionAttentionReason.SourcesExhausted, rig.Loop.Reason);
        Assert.Equal(2, rig.Progress(CollectedResource.Wood).Delivered);
        AssertWoodConserved(rig, picked: 2);
    }

    [Fact]
    public void WithoutTheFellingSeamTreesAreNeverChosen()
    {
        var rig = Rig(withFelling: false);
        rig.AddTree(2f, 2f);
        rig.Accept(rig.Order(stone: 0, wood: 3));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused));
        Assert.Empty(rig.Pickup.Strikes);
        Assert.NotEqual(CollectionAttentionReason.SourcesExhausted, rig.Loop.Reason);
    }

    [Fact]
    public void ThePlayerStandingByMakesHimWaitThenLeaveTheTreeForThisSurveyNeverSwinging()
    {
        var rig = Rig(new CollectionParameters(fellPlayerWaitSeconds: 3f));
        SourceKey tree = rig.AddTree(2f, 2f, hits: 1, wood: 1);
        bool near = true;
        rig.Pickup.PlayerNear = _ => near;
        rig.Accept(rig.Order(stone: 0, wood: 1));

        Assert.True(rig.RunUntil(() => rig.Pickup.Strikes.Count >= 1));
        Assert.Equal(CollectionPhase.Felling, rig.Loop.Phase);
        Assert.True(rig.Book.IsHeldBy(tree, rig.Loop.Order!.Order), "he keeps the tree while he waits");
        Assert.True(rig.RunUntil(() => rig.Loop.Phase != CollectionPhase.Felling, maxTicks: 200));
        Assert.True(rig.Pickup.TreeStands(tree));
        Assert.Empty(rig.Pickup.Felled);

        // Once the player steps back, a later survey offers it again.
        near = false;
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed));
        Assert.Single(rig.Pickup.Felled);
    }

    [Fact]
    public void ARefusedTreeIsNotStruckAgainByThisOrder()
    {
        var rig = Rig();
        rig.AddTree(2f, 2f, hits: 1, wood: 1);
        rig.Pickup.ForcedStrikes.Enqueue(FellStrikeOutcome.Refused);
        rig.Accept(rig.Order(stone: 0, wood: 1));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused));
        Assert.Single(rig.Pickup.Strikes);
        Assert.Equal(0, rig.Progress(CollectedResource.Wood).Delivered);
    }

    [Fact]
    public void AnUncertainHitNeedsAttentionAndIsNeverRetried()
    {
        var rig = Rig();
        rig.AddTree(2f, 2f, hits: 1, wood: 1);
        rig.Pickup.ForcedStrikes.Enqueue(FellStrikeOutcome.Uncertain);
        rig.Accept(rig.Order(stone: 0, wood: 1));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.NeedsAttention));
        Assert.Equal(CollectionAttentionReason.TransferUncertain, rig.Loop.Reason);
        rig.Tick(200);
        Assert.Single(rig.Pickup.Strikes);
    }

    [Fact]
    public void ATreeThatWillNotFallIsGivenUpAfterTheStrikeCeiling()
    {
        var rig = Rig(new CollectionParameters(fellMaxStrikes: 4, fellSwingSeconds: 0.05f));
        rig.AddTree(2f, 2f, hits: 1000, wood: 1);
        rig.Accept(rig.Order(stone: 0, wood: 1));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused));
        Assert.Equal(4, rig.Pickup.Strikes.Count);
        Assert.Equal(CollectionAttentionReason.SourceUnreachable, rig.Loop.Reason);
    }

    [Fact]
    public void AFellThatDroppedNoWoodCreditsNothingAndIsNotAnError()
    {
        var rig = Rig();
        rig.AddTree(2f, 2f, hits: 1, wood: 0);
        rig.Accept(rig.Order(stone: 0, wood: 2));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused));
        Assert.Equal(CollectionAttentionReason.SourcesExhausted, rig.Loop.Reason);
        Assert.Equal(1, rig.Loop.Fells);
        AssertWoodConserved(rig, picked: 0);
    }

    [Fact]
    public void PausingMidTreeReleasesItAndAReloadOfTheLoopNeverCountsItTwice()
    {
        var rig = Rig(new CollectionParameters(fellSwingSeconds: 5f));
        SourceKey tree = rig.AddTree(2f, 2f, hits: 2, wood: 2);
        rig.Accept(rig.Order(stone: 0, wood: 2));

        Assert.True(rig.RunUntil(() => rig.Pickup.Strikes.Count == 1));
        Assert.Equal(ControlOutcome.Done, rig.Loop.Pause(rig.Now).Outcome);
        Assert.False(rig.Book.IsHeldBy(tree, rig.Loop.Order!.Order));
        Assert.Equal(ControlOutcome.Done, rig.Loop.Resume(rig.Now).Outcome);

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed));
        Assert.Single(rig.Pickup.Felled);
        Assert.Equal(2, rig.Pickup.Strikes.Count);
        Assert.Equal(2, rig.Progress(CollectedResource.Wood).Delivered);
        AssertWoodConserved(rig, picked: 2);
    }

    [Fact]
    public void ThereIsNoBrushTierSourceInThisGame()
    {
        foreach (NaturalSourceKind kind in Enum.GetValues(typeof(NaturalSourceKind)))
        {
            Assert.NotEqual(LabourTier.Brush, SmallTreeSources.TierOf(kind));
        }

        Assert.True(SmallTreeSources.TierOf(NaturalSourceKind.Branch) < SmallTreeSources.TierOf(NaturalSourceKind.SmallTree));
    }
}
