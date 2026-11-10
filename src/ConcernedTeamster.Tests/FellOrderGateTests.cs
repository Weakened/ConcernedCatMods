using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.Workers;
using TheConcernedCat.Workers.Felling;

namespace ConcernedTeamster.Tests;

/// <summary>Gunnar's half of D19 (#381 item 5): the order gate of one
/// <c>ct_collect fell</c>, and the game-free rules that decide which of the
/// drops one felling hit spawned he takes and what is credited. The tree, the
/// area, the clearances and the axe are the shared <c>SmallTreeFelling</c>
/// rule's, re-measured by the felling port in the frame of the hit.</summary>
public sealed class FellOrderGateTests
{
    private static FellRequest Ready(
        bool collectionEnabled = true,
        bool fellingEnabled = true,
        bool worldIsUp = true,
        WorkAuthorityVerdict authority = WorkAuthorityVerdict.Granted,
        bool seamAvailable = true,
        bool workerPresent = true,
        bool workerRecordUnwritable = false,
        bool workerRecordUnreadable = false,
        bool transferInFlight = false,
        bool interruptedTourOpen = false,
        bool areaChosen = true) =>
        new FellRequest(
            collectionEnabled, fellingEnabled, worldIsUp, authority, seamAvailable, workerPresent,
            workerRecordUnwritable, workerRecordUnreadable, transferInFlight, interruptedTourOpen, areaChosen);

    [Fact]
    public void AReadyRequestIsAdmitted() => Assert.Equal(FellRefusal.None, FellOrderGate.Evaluate(Ready()));

    [Fact]
    public void ADefaultedRequestIsRefused() =>
        Assert.Equal(FellRefusal.CollectionOff, FellOrderGate.Evaluate(default));

    [Fact]
    public void FellingIsOffByDefaultAndASwitchOfItsOwn()
    {
        Assert.False(GunnarCollectionDefaults.FellingEnabled);
        Assert.Equal(FellRefusal.FellingOff, FellOrderGate.Evaluate(Ready(fellingEnabled: false)));
        Assert.Equal(FellRefusal.CollectionOff, FellOrderGate.Evaluate(Ready(collectionEnabled: false)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void AnythingButGrantedAuthorityRefuses(int verdict)
    {
        Assert.NotEqual(WorkAuthorityVerdict.Granted, (WorkAuthorityVerdict)verdict);
        Assert.Equal(FellRefusal.WorkRefused, FellOrderGate.Evaluate(Ready(authority: (WorkAuthorityVerdict)verdict)));
    }

    [Fact]
    public void EveryOtherGateRefusesOnItsOwn()
    {
        Assert.Equal(FellRefusal.WorldIsGoingAway, FellOrderGate.Evaluate(Ready(worldIsUp: false)));
        Assert.Equal(FellRefusal.SeamUnavailable, FellOrderGate.Evaluate(Ready(seamAvailable: false)));
        Assert.Equal(FellRefusal.WorkerRecordUnwritable, FellOrderGate.Evaluate(Ready(workerRecordUnwritable: true)));
        Assert.Equal(FellRefusal.WorkerRecordUnreadable, FellOrderGate.Evaluate(Ready(workerRecordUnreadable: true)));
        Assert.Equal(FellRefusal.NoWorker, FellOrderGate.Evaluate(Ready(workerPresent: false)));
        Assert.Equal(FellRefusal.AlreadyWorking, FellOrderGate.Evaluate(Ready(transferInFlight: true)));
        Assert.Equal(FellRefusal.InterruptedTourOpen, FellOrderGate.Evaluate(Ready(interruptedTourOpen: true)));
    }

    [Fact]
    public void ThereIsNoDefaultAreaToFellIn() =>
        Assert.Equal(FellRefusal.NoArea, FellOrderGate.Evaluate(Ready(areaChosen: false)));

    [Fact]
    public void AnUnwritableRecordRefusesEvenWithNoWorker() =>
        Assert.Equal(
            FellRefusal.WorkerRecordUnwritable,
            FellOrderGate.Evaluate(Ready(workerRecordUnwritable: true, workerPresent: false)));

    [Fact]
    public void EveryRefusalHasASentence()
    {
        foreach (FellRefusal refusal in System.Enum.GetValues(typeof(FellRefusal)))
        {
            Assert.False(string.IsNullOrWhiteSpace(FellOrderGate.Describe(refusal, WorkAuthorityVerdict.Granted)));
        }

        foreach (FellSiteRefusal refusal in System.Enum.GetValues(typeof(FellSiteRefusal)))
        {
            string said = FellOrderGate.DescribeSite(refusal);
            Assert.False(string.IsNullOrWhiteSpace(said));
            Assert.DoesNotContain("Thorstein", said);
        }
    }

    // --- the shared site rule, as Gunnar uses it ---------------------------------------------------

    private static FellSiteFacts Site(
        bool featureEnabled = true,
        bool explicitArea = true,
        float nearestPlayerMetres = 20f,
        float nearestBuiltPieceMetres = 20f,
        float workerDistanceMetres = 1f,
        bool hasUsableAxe = true) =>
        new FellSiteFacts(
            featureEnabled, authorityGranted: true, explicitArea, insideArea: true, ownedHere: true, wardAllows: true,
            nearestPlayerMetres, nearestBuiltPieceMetres, workerDistanceMetres, hasUsableAxe, axeToolTier: 0,
            treeMinToolTier: 0);

    [Fact]
    public void TheSiteRuleKeepsThorsteinsScopeForGunnar()
    {
        const float reach = 2.2f;
        Assert.Equal(FellSiteRefusal.Allowed, SmallTreeFelling.CheckSite(Site(), reach));
        Assert.Equal(FellSiteRefusal.FeatureOff, SmallTreeFelling.CheckSite(Site(featureEnabled: false), reach));
        Assert.Equal(FellSiteRefusal.NoExplicitArea, SmallTreeFelling.CheckSite(Site(explicitArea: false), reach));
        Assert.Equal(FellSiteRefusal.PlayerTooClose, SmallTreeFelling.CheckSite(Site(nearestPlayerMetres: 14.9f), reach));
        Assert.Equal(FellSiteRefusal.BuiltPieceTooClose,
            SmallTreeFelling.CheckSite(Site(nearestBuiltPieceMetres: 14.9f), reach));
        Assert.Equal(FellSiteRefusal.WorkerOutOfReach, SmallTreeFelling.CheckSite(Site(workerDistanceMetres: 3f), reach));
        Assert.Equal(FellSiteRefusal.NoUsableAxe, SmallTreeFelling.CheckSite(Site(hasUsableAxe: false), reach));
        Assert.Equal(4, SmallTreeFelling.Allowlist.Count);
    }

    // --- only the Wood his own hit dropped, only what fits -----------------------------------------

    private static readonly FelledDrop Wood = new FelledDrop(isWood: true, ownedHere: true, stack: 1);

    [Fact]
    public void OnlyWoodOwnedHereIsChosen()
    {
        var drops = new List<FelledDrop>
        {
            Wood,
            new FelledDrop(isWood: false, ownedHere: true, stack: 1),
            new FelledDrop(isWood: true, ownedHere: false, stack: 1),
            Wood,
        };
        var chosen = new List<int>();
        int left = FelledWoodTake.Choose(drops, room: 10, chosen);
        Assert.Equal(new[] { 0, 3 }, chosen);
        Assert.Equal(2, left);
    }

    [Fact]
    public void NothingBeyondTheCarryLimitIsChosenAndNothingIsSplit()
    {
        var drops = new List<FelledDrop> { Wood, new FelledDrop(true, true, 3), Wood, Wood };
        var chosen = new List<int>();
        int left = FelledWoodTake.Choose(drops, room: 2, chosen);
        Assert.Equal(new[] { 0, 2 }, chosen);
        Assert.Equal(2, left);

        Assert.Equal(4, FelledWoodTake.Choose(drops, room: 0, chosen));
        Assert.Empty(chosen);
        Assert.Equal(4, FelledWoodTake.Choose(drops, room: -5, chosen));
        Assert.Empty(chosen);
    }

    [Fact]
    public void AnEmptyStackIsNeverChosen()
    {
        var chosen = new List<int>();
        FelledWoodTake.Choose(new List<FelledDrop> { new FelledDrop(true, true, 0) }, room: 5, chosen);
        Assert.Empty(chosen);
    }

    [Fact]
    public void ACleanTakeCreditsTheWholeDrop()
    {
        FelledTakeStep step = FelledWoodTake.Credit(threw: false, added: true, before: 4, after: 5, stack: 1, dropGone: true);
        Assert.Equal(1, step.Credited);
        Assert.False(step.Uncertain);
    }

    [Fact]
    public void ARefusedTakeWithNothingMovedIsNotUncertain()
    {
        FelledTakeStep step = FelledWoodTake.Credit(threw: false, added: false, before: 4, after: 4, stack: 1, dropGone: false);
        Assert.Equal(0, step.Credited);
        Assert.False(step.Uncertain);
    }

    [Theory]
    // The drop vanished but nothing arrived: never credited.
    [InlineData(false, true, 4, 4, 1, true, 0)]
    // More arrived than the drop held: credited no more than the drop.
    [InlineData(false, true, 4, 7, 1, true, 1)]
    // It threw: what measurably arrived, and uncertain.
    [InlineData(true, false, 4, 5, 1, true, 1)]
    // Added, but the drop is still there.
    [InlineData(false, true, 4, 5, 1, false, 1)]
    // His count went down.
    [InlineData(false, true, 4, 2, 1, true, 0)]
    public void AnythingThatDoesNotAddUpIsUncertainAndCreditsOnlyWhatArrived(
        bool threw, bool added, int before, int after, int stack, bool gone, int credited)
    {
        FelledTakeStep step = FelledWoodTake.Credit(threw, added, before, after, stack, gone);
        Assert.True(step.Uncertain);
        Assert.Equal(credited, step.Credited);
    }

    [Fact]
    public void TheTakeIsWoodOnly() => Assert.Equal("Wood", FelledWoodTake.WoodPrefab);
}
