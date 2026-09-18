using System;
using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Tools;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;

namespace Shared.Settlement.Tests;

/// <summary>The solo collection loop (GATHER-01..06, CONTRACTS.md §4 and §7)
/// against fake ports: legal transitions only, pauses with their reasons,
/// bounded retries, no overcollection, conservation of every unit, and the
/// hand-off to and from cooperative delivery.</summary>
public sealed class CollectionLoopTests
{
    private static void AddStones(CollectionRig rig, int count, float startX = 4f)
    {
        for (int index = 0; index < count; index++)
        {
            rig.AddSource(CollectedResource.Stone, startX + (index % 10), -10f + ((index / 10) * 3f));
        }
    }

    private static void AddBranches(CollectionRig rig, int count)
    {
        for (int index = 0; index < count; index++)
        {
            rig.AddSource(CollectedResource.Wood, -4f - (index % 10), -10f + ((index / 10) * 3f));
        }
    }

    private static void AssertConserved(CollectionRig rig, CollectedResource resource)
    {
        ResourceProgress progress = rig.Progress(resource);
        int picked = rig.Pickup.Picked.Count(key => (key.PrefabName == "Pickable_Stone") == (resource == CollectedResource.Stone));
        Assert.Equal(picked, progress.OnGround + progress.Carried + progress.InCart + progress.Delivered + progress.HandedOver + progress.Lost);
        Assert.Equal(progress.Delivered, rig.Chest.Count(MaterialItem.Of(resource)));
        Assert.Equal(progress.Carried, rig.WorkerInventory.Count(MaterialItem.Of(resource)));
    }

    private static void AssertClean(CollectionRig rig)
    {
        Assert.Equal(0, rig.Loop.IllegalTransitionsAttempted);
        Assert.Equal(0, rig.Motion.RefusedCommands);
    }

    // --- The happy path ------------------------------------------------------------------------------------

    [Fact]
    public void ASoloOrderCollectsMixedResourcesAndDeliversThemInSeveralTrips()
    {
        var rig = new CollectionRig(new CollectionParameters(workerCarryWeight: 40f));
        AddStones(rig, 30);
        AddBranches(rig, 40);

        Assert.Equal(CollectionIntakeRefusal.Unspecified, rig.Accept(rig.Order(stone: 20, wood: 30)));
        Assert.Single(rig.Custody.Accepted);
        Assert.Equal(ActorMode.Surveying, rig.Modes.Mode);

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed));

        Assert.Equal(20, rig.Progress(CollectedResource.Stone).Delivered);
        Assert.Equal(30, rig.Progress(CollectedResource.Wood).Delivered);
        Assert.Equal(20, rig.Chest.Count(MaterialItem.Of(CollectedResource.Stone)));
        Assert.Equal(30, rig.Chest.Count(MaterialItem.Of(CollectedResource.Wood)));
        Assert.Equal(50, rig.Pickup.Picks);
        Assert.True(rig.Loop.Trips >= 2, "a 40-weight budget needs at least three loads for 50 units");
        Assert.True(rig.Custody.Transitions.Count(step => step.From == CollectionOrderState.Delivering && step.To == CollectionOrderState.Collecting) >= 2);
        Assert.Equal(ActorMode.Resting, rig.Modes.Mode);
        Assert.Equal(0, rig.Book.Count);
        AssertConserved(rig, CollectedResource.Stone);
        AssertConserved(rig, CollectedResource.Wood);
        AssertClean(rig);

        // Every deposit used a fresh request id.
        Assert.Equal(rig.Custody.Executed.Count, rig.Custody.Executed.Select(intent => intent.Request.Value).Distinct().Count());
    }

    [Fact]
    public void ItNeverCarriesMoreThanItsBudgetAndNeverCollectsMoreThanAskedFor()
    {
        var rig = new CollectionRig(new CollectionParameters(workerCarryWeight: 20f));
        AddStones(rig, 40);
        rig.Accept(rig.Order(stone: 25, wood: 0));

        int maxCarried = 0;
        Assert.True(rig.RunUntil(() =>
        {
            maxCarried = Math.Max(maxCarried, rig.WorkerInventory.Count(MaterialItem.Of(CollectedResource.Stone)));
            return rig.Loop.State == CollectionOrderState.Completed;
        }));

        Assert.Equal(10, maxCarried);
        Assert.Equal(25, rig.Pickup.Picks);
        Assert.Equal(25, rig.Progress(CollectedResource.Stone).Delivered);
        AssertConserved(rig, CollectedResource.Stone);
        AssertClean(rig);
    }

    [Fact]
    public void ItDoesNotStopAtAMerelySurveyedQuota()
    {
        var rig = new CollectionRig();
        AddStones(rig, 3);
        rig.Accept(rig.Order(stone: 5, wood: 0));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused));

        // Everything that existed was delivered, and the order says it is not
        // done: three is not five, whatever the first survey saw.
        Assert.Equal(3, rig.Progress(CollectedResource.Stone).Delivered);
        Assert.Equal(CollectionAttentionReason.NoEligibleSources, rig.Loop.Reason);
        Assert.NotEqual(CollectionOrderState.Completed, rig.Loop.State);
        AssertConserved(rig, CollectedResource.Stone);
        AssertClean(rig);
    }

    [Fact]
    public void PickedBranchesAreReportedExhaustedNotMissing()
    {
        var rig = new CollectionRig();
        AddBranches(rig, 4);
        rig.Accept(rig.Order(stone: 0, wood: 6));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused));

        Assert.Equal(CollectionAttentionReason.SourcesExhausted, rig.Loop.Reason);
        Assert.Equal(4, rig.Progress(CollectedResource.Wood).Delivered);
        Assert.Equal(4, rig.Loop.LastAccounting!.Exhausted(CollectedResource.Wood));
    }

    public static TheoryData<int> EmptyScopeCases() => new() { 0, 1, 2, 3 };

    [Theory]
    [MemberData(nameof(EmptyScopeCases))]
    public void AnAreaWithNothingToCollectPausesWithTheHonestReason(int scenario)
    {
        var rig = new CollectionRig();
        CollectionAttentionReason expected;
        switch (scenario)
        {
            case 0:
                expected = CollectionAttentionReason.NoEligibleSources;
                break;
            case 1:
                // Half the area is not loaded: not "no sources".
                rig.Probe.Loaded = point => point.X >= 0f;
                expected = CollectionAttentionReason.ScopeUnloaded;
                break;
            case 2:
                // Sources whose ward could not be asked: not "no sources".
                rig.AddSource(CollectedResource.Stone, 5f, 5f, availability: SourceAvailability.Unknown);
                expected = CollectionAttentionReason.SurveyIncomplete;
                break;
            default:
                // Only warded sources: there is nothing he may take.
                rig.AddSource(CollectedResource.Stone, 5f, 5f, availability: SourceAvailability.Inaccessible);
                expected = CollectionAttentionReason.NoEligibleSources;
                break;
        }

        Assert.Equal(CollectionIntakeRefusal.Unspecified, rig.Accept(rig.Order(stone: 5, wood: 0)));
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused));

        Assert.Equal(expected, rig.Loop.Reason);
        Assert.Equal(0, rig.Pickup.Picks);
        AssertClean(rig);
    }

    [Fact]
    public void HoldForThePlayerHoldsUntilHandedOverAndIsNeverReportedDelivered()
    {
        var rig = new CollectionRig();
        AddStones(rig, 12);
        rig.Accept(rig.Order(stone: 10, wood: 0, hold: true));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.HoldingForPlayer));
        rig.Tick(200);

        Assert.Equal(CollectionOrderState.HoldingForPlayer, rig.Loop.State);
        Assert.Equal(10, rig.Progress(CollectedResource.Stone).Carried);
        Assert.Equal(0, rig.Progress(CollectedResource.Stone).Delivered);
        Assert.Equal(0, rig.Chest.Count(MaterialItem.Of(CollectedResource.Stone)));
        Assert.Empty(rig.Custody.Executed);

        // The handover itself is custody's (agent D/E); completion follows it.
        ResourceProgress progress = rig.Progress(CollectedResource.Stone);
        progress.Carried -= 10;
        progress.HandedOver += 10;
        rig.WorkerInventory.Set(CollectedResource.Stone, 0);
        rig.Tick();

        Assert.Equal(CollectionOrderState.Completed, rig.Loop.State);
        Assert.Equal(ActorMode.Resting, rig.Modes.Mode);
        AssertClean(rig);
    }

    // --- Scope ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AScopeThatStopsHoldingPausesAndNeverFallsBack(int change)
    {
        var rig = new CollectionRig();
        AddStones(rig, 10);
        rig.Accept(rig.Order(stone: 10, wood: 0));
        Assert.True(rig.RunUntil(() => rig.Pickup.Picks >= 3));

        CollectionAttentionReason expected;
        switch (change)
        {
            case 0:
                rig.World.ScopeRevision++;
                expected = CollectionAttentionReason.ScopeChanged;
                break;
            case 1:
                rig.World.ScopePresent = false;
                expected = CollectionAttentionReason.ScopeInvalid;
                break;
            case 2:
                rig.World.Epoch = Guid.NewGuid();
                expected = CollectionAttentionReason.ScopeInvalid;
                break;
            default:
                rig.World.Loaded = _ => false;
                expected = CollectionAttentionReason.ScopeUnloaded;
                break;
        }

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused, 50));
        Assert.Equal(expected, rig.Loop.Reason);

        int picks = rig.Pickup.Picks;
        rig.Tick(400);
        Assert.Equal(picks, rig.Pickup.Picks);
        Assert.Equal(CollectionWalkStatus.Idle, rig.Motion.Status);
        Assert.Equal(0, rig.Book.Count);

        ControlResult resume = rig.Loop.Resume(rig.Now);
        Assert.Equal(ControlOutcome.Refused, resume.Outcome);
        Assert.Equal(CollectionOrderState.Paused, rig.Loop.State);
        Assert.Equal(expected, rig.Loop.Reason);
        AssertConserved(rig, CollectedResource.Stone);
        AssertClean(rig);
    }

    // --- Destination ----------------------------------------------------------------------------------------

    [Fact]
    public void AFullChestPausesWithTheMaterialsKeptAndResumesWhenThereIsRoom()
    {
        var rig = new CollectionRig();
        AddStones(rig, 25);
        rig.Chest.Limit(CollectedResource.Stone, 5);
        rig.Accept(rig.Order(stone: 20, wood: 0));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused));

        Assert.Equal(CollectionAttentionReason.DestinationFull, rig.Loop.Reason);
        Assert.Equal(5, rig.Chest.Count(MaterialItem.Of(CollectedResource.Stone)));
        Assert.Equal(5, rig.Progress(CollectedResource.Stone).Delivered);
        Assert.Equal(5, rig.Pickup.Picks);
        AssertConserved(rig, CollectedResource.Stone);

        rig.Chest.Limit(CollectedResource.Stone, 100);
        Assert.Equal(ControlOutcome.Done, rig.Loop.Resume(rig.Now).Outcome);
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed));

        Assert.Equal(20, rig.Progress(CollectedResource.Stone).Delivered);
        Assert.Equal(20, rig.Pickup.Picks);
        AssertConserved(rig, CollectedResource.Stone);
        AssertClean(rig);
    }

    [Fact]
    public void AChestThatFillsDuringTheWalkTakesWhatFitsAndHeKeepsTheRest()
    {
        var rig = new CollectionRig();
        AddStones(rig, 10);
        rig.Accept(rig.Order(stone: 10, wood: 0));
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Delivering));

        rig.Chest.Limit(CollectedResource.Stone, 4);
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused));

        Assert.Equal(CollectionAttentionReason.DestinationFull, rig.Loop.Reason);
        Assert.Equal(4, rig.Progress(CollectedResource.Stone).Delivered);
        Assert.Equal(6, rig.Progress(CollectedResource.Stone).Carried);
        AssertConserved(rig, CollectedResource.Stone);

        // Resuming a delivery goes back to delivering what he carries.
        rig.Chest.Limit(CollectedResource.Stone, 100);
        Assert.Equal(ControlOutcome.Done, rig.Loop.Resume(rig.Now).Outcome);
        Assert.Equal(CollectionOrderState.Delivering, rig.Loop.State);
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed));
        AssertConserved(rig, CollectedResource.Stone);
        AssertClean(rig);
    }

    [Theory]
    [InlineData((int)CollectionAttentionReason.DestinationUnavailable)]
    [InlineData((int)CollectionAttentionReason.DestinationStale)]
    [InlineData((int)CollectionAttentionReason.DestinationAccessDenied)]
    [InlineData((int)CollectionAttentionReason.JournalReadOnly)]
    public void AnUnusableChestStopsCollectingBeforeAnythingIsPickedAndNoOtherChestIsUsed(int refusal)
    {
        var rig = new CollectionRig();
        AddStones(rig, 10);
        rig.Accept(rig.Order(stone: 10, wood: 0));
        rig.Custody.DestinationRefusal = (CollectionAttentionReason)refusal;

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused));

        CollectionAttentionReason expected = (CollectionAttentionReason)refusal == CollectionAttentionReason.JournalReadOnly
            ? CollectionAttentionReason.DestinationUnavailable
            : (CollectionAttentionReason)refusal;
        Assert.Equal(expected, rig.Loop.Reason);
        Assert.Equal(0, rig.Pickup.Picks);
        Assert.Empty(rig.Custody.Executed);
        AssertClean(rig);
    }

    [Fact]
    public void AChestThatBecomesUnavailableWhileHeCarriesPausesWithTheLoadRetained()
    {
        var rig = new CollectionRig();
        AddStones(rig, 10);
        rig.Accept(rig.Order(stone: 10, wood: 0));
        Assert.True(rig.RunUntil(() => rig.Progress(CollectedResource.Stone).Carried >= 4));

        rig.Custody.DestinationRefusal = CollectionAttentionReason.DestinationUnavailable;
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused));

        Assert.Equal(CollectionAttentionReason.DestinationUnavailable, rig.Loop.Reason);
        Assert.True(rig.Progress(CollectedResource.Stone).Carried >= 4);
        Assert.Empty(rig.Custody.Executed);
        AssertConserved(rig, CollectedResource.Stone);
    }

    [Fact]
    public void AnUnreachableChestPausesAfterBoundedRetries()
    {
        var rig = new CollectionRig();
        AddStones(rig, 3);
        int walksToChest = 0;
        rig.Motion.DeferralFor = point =>
        {
            if (point.Equals(CollectionRig.ChestAt))
            {
                walksToChest++;
                return WorkerDeferralReason.Unreachable;
            }

            return WorkerDeferralReason.None;
        };

        rig.Accept(rig.Order(stone: 3, wood: 0));
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused));

        Assert.Equal(CollectionAttentionReason.DestinationUnavailable, rig.Loop.Reason);
        Assert.Equal(3, walksToChest);
        Assert.Equal(3, rig.Progress(CollectedResource.Stone).Carried);
        AssertConserved(rig, CollectedResource.Stone);
        AssertClean(rig);
    }

    // --- Pickup outcomes ------------------------------------------------------------------------------------

    [Fact]
    public void ARefusedPickMovesOnAndThatSourceIsNotTriedAgain()
    {
        var rig = new CollectionRig();
        SourceKey nearest = rig.AddSource(CollectedResource.Stone, 1f, 0f);
        SourceKey middle = rig.AddSource(CollectedResource.Stone, 5f, 0f);
        SourceKey far = rig.AddSource(CollectedResource.Stone, 9f, 0f);
        rig.Pickup.ForcedOutcomes.Enqueue(PickupOutcome.Refused);
        rig.Accept(rig.Order(stone: 2, wood: 0));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed));

        Assert.DoesNotContain(nearest, rig.Pickup.Picked);
        Assert.Contains(middle, rig.Pickup.Picked);
        Assert.Contains(far, rig.Pickup.Picked);
        AssertClean(rig);
    }

    [Fact]
    public void AnUncertainPickNeedsAttentionAndGrantsNothing()
    {
        var rig = new CollectionRig();
        AddStones(rig, 5);
        rig.Pickup.ForcedOutcomes.Enqueue(PickupOutcome.Uncertain);
        rig.Accept(rig.Order(stone: 5, wood: 0));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.NeedsAttention));

        Assert.Equal(CollectionAttentionReason.TransferUncertain, rig.Loop.Reason);
        Assert.Equal(0, rig.Progress(CollectedResource.Stone).Carried);
        Assert.Equal(0, rig.WorkerInventory.Count(MaterialItem.Of(CollectedResource.Stone)));
        rig.Tick(200);
        Assert.Equal(CollectionOrderState.NeedsAttention, rig.Loop.State);
        Assert.Equal(0, rig.Pickup.Picks);

        // A person looked and resumed: the order goes through Paused and works on.
        Assert.Equal(ControlOutcome.Done, rig.Loop.Resume(rig.Now).Outcome);
        Assert.Contains(rig.Custody.Transitions, step => step.From == CollectionOrderState.NeedsAttention && step.To == CollectionOrderState.Paused);
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused || rig.Loop.State == CollectionOrderState.Completed));
        AssertClean(rig);
    }

    [Fact]
    public void AnUnresolvedUncertainTransferKeepsTheOrderStopped()
    {
        var rig = new CollectionRig();
        AddStones(rig, 5);
        rig.Accept(rig.Order(stone: 5, wood: 0));
        Assert.True(rig.RunUntil(() => rig.Pickup.Picks >= 2));

        rig.Custody.Uncertain = true;
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.NeedsAttention, 50));
        Assert.Equal(CollectionAttentionReason.TransferUncertain, rig.Loop.Reason);

        ControlResult resume = rig.Loop.Resume(rig.Now);
        Assert.Equal(ControlOutcome.Refused, resume.Outcome);
        Assert.Equal(CollectionOrderState.NeedsAttention, rig.Loop.State);
        Assert.Equal(ControlOutcome.Refused, rig.Loop.Pause(rig.Now).Outcome);

        rig.Custody.Uncertain = false;
        Assert.Equal(ControlOutcome.Done, rig.Loop.Resume(rig.Now).Outcome);
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed));
        AssertConserved(rig, CollectedResource.Stone);
        AssertClean(rig);
    }

    [Fact]
    public void ADropThatCouldNotBeTakenNeedsAttentionAndIsNeverRetaken()
    {
        var rig = new CollectionRig();
        AddStones(rig, 5);
        rig.Pickup.TakeShortfall = 1;
        rig.Accept(rig.Order(stone: 5, wood: 0));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.NeedsAttention));

        Assert.Equal(CollectionAttentionReason.CarryFull, rig.Loop.Reason);
        Assert.Equal(1, rig.Progress(CollectedResource.Stone).OnGround);
        Assert.Equal(0, rig.Progress(CollectedResource.Stone).Carried);
        AssertConserved(rig, CollectedResource.Stone);
        AssertClean(rig);
    }

    // --- Deposit outcomes -----------------------------------------------------------------------------------

    [Fact]
    public void AnUncertainDepositIsNeverRetriedOrCompensated()
    {
        var rig = new CollectionRig();
        AddStones(rig, 5);
        rig.Custody.ForcedOutcomes.Enqueue(TransferOutcome.Uncertain);
        rig.Accept(rig.Order(stone: 5, wood: 0));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.NeedsAttention));
        rig.Tick(500);

        Assert.Equal(CollectionAttentionReason.TransferUncertain, rig.Loop.Reason);
        Assert.Single(rig.Custody.Executed);
        Assert.Equal(ControlOutcome.Refused, rig.Loop.Resume(rig.Now).Outcome);
        Assert.Equal(0, rig.Chest.Count(MaterialItem.Of(CollectedResource.Stone)));
        AssertClean(rig);
    }

    [Fact]
    public void ARefusedDepositRetriesWithADoublingBackoffThenPauses()
    {
        var rig = new CollectionRig();
        AddStones(rig, 5);
        for (int index = 0; index < 3; index++)
        {
            rig.Custody.ForcedOutcomes.Enqueue(TransferOutcome.Refused);
        }

        rig.Accept(rig.Order(stone: 5, wood: 0));
        Assert.True(rig.RunUntil(() => rig.Custody.Executed.Count == 1));
        float firstAttempt = rig.Now;

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused));

        Assert.Equal(3, rig.Custody.Executed.Count);
        Assert.True(rig.Now - firstAttempt >= 6f - 0.1f, "2 s then 4 s of backoff");
        Assert.Equal(CollectionAttentionReason.DestinationUnavailable, rig.Loop.Reason);
        Assert.Equal(5, rig.Progress(CollectedResource.Stone).Carried);
        AssertConserved(rig, CollectedResource.Stone);
        AssertClean(rig);
    }

    [Fact]
    public void AStaleDepositIsAskedAgainAndSucceeds()
    {
        var rig = new CollectionRig();
        AddStones(rig, 4);
        rig.Custody.ForcedOutcomes.Enqueue(TransferOutcome.Stale);
        rig.Accept(rig.Order(stone: 4, wood: 0));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed));

        Assert.Equal(2, rig.Custody.Executed.Count);
        Assert.NotEqual(rig.Custody.Executed[0].Request, rig.Custody.Executed[1].Request);
        AssertConserved(rig, CollectedResource.Stone);
        AssertClean(rig);
    }

    // --- Authority and readiness ----------------------------------------------------------------------------

    [Theory]
    [InlineData((int)WorkAuthorityVerdict.RuntimeDisabled, (int)CollectionAttentionReason.NoAuthority)]
    [InlineData((int)WorkAuthorityVerdict.NoWorld, (int)CollectionAttentionReason.NoAuthority)]
    [InlineData((int)WorkAuthorityVerdict.NotHost, (int)CollectionAttentionReason.NoAuthority)]
    [InlineData((int)WorkAuthorityVerdict.OtherPeersConnected, (int)CollectionAttentionReason.OtherPeersConnected)]
    public void LosingAuthorityPausesBeforeTheNextMutation(int verdict, int expected)
    {
        var rig = new CollectionRig();
        AddStones(rig, 10);
        rig.Accept(rig.Order(stone: 10, wood: 0));
        Assert.True(rig.RunUntil(() => rig.Pickup.Picks >= 2));

        rig.World.Authority = (WorkAuthorityVerdict)verdict;
        int picks = rig.Pickup.Picks;
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused, 50));
        Assert.Equal((CollectionAttentionReason)expected, rig.Loop.Reason);

        // Not one pick after authority was lost.
        rig.Tick(300);
        Assert.Equal(picks, rig.Pickup.Picks);

        rig.World.Authority = WorkAuthorityVerdict.Granted;
        Assert.Equal(ControlOutcome.Done, rig.Loop.Resume(rig.Now).Outcome);
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed));
        AssertConserved(rig, CollectedResource.Stone);
        AssertClean(rig);
    }

    [Fact]
    public void ABrokenToolPausesAtTheNextTripWithoutRelockingAndTheLoadIsStillDelivered()
    {
        var rig = new CollectionRig(new CollectionParameters(workerCarryWeight: 20f));
        AddStones(rig, 30);
        rig.Accept(rig.Order(stone: 20, wood: 0));
        Assert.True(rig.RunUntil(() => rig.Pickup.Picks >= 3));

        rig.World.Readiness = FakeWorld.NotReady(ReadinessRefusal.ToolUnusable);
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused));

        Assert.Equal(CollectionAttentionReason.ToolBroken, rig.Loop.Reason);
        Assert.Equal(10, rig.Progress(CollectedResource.Stone).Delivered);
        Assert.Equal(0, rig.Progress(CollectedResource.Stone).Carried);
        Assert.True(rig.Loop.HasActiveOrder);

        Assert.Equal(ControlOutcome.Refused, rig.Loop.Resume(rig.Now).Outcome);
        rig.World.Readiness = FakeWorld.ReadyVerdict();
        Assert.Equal(ControlOutcome.Done, rig.Loop.Resume(rig.Now).Outcome);
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed));
        Assert.Equal(20, rig.Progress(CollectedResource.Stone).Delivered);
        AssertClean(rig);
    }

    [Fact]
    public void ReadinessIsAskedAgainWhileWorkingNotOnlyAtAcceptance()
    {
        var rig = new CollectionRig(new CollectionParameters(readinessRecheckSeconds: 1f));
        AddStones(rig, 40);
        rig.Accept(rig.Order(stone: 40, wood: 0));
        Assert.True(rig.RunUntil(() => rig.Pickup.Picks >= 2));

        rig.World.Readiness = FakeWorld.NotReady(ReadinessRefusal.ToolMissing);
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused, 100));

        Assert.Equal(CollectionAttentionReason.ToolMissing, rig.Loop.Reason);
        Assert.True(rig.Progress(CollectedResource.Stone).Carried < 40);
    }

    // --- Walking --------------------------------------------------------------------------------------------

    [Fact]
    public void AnUnreachableSourceIsRetriedWithBackoffThenLeftAlone()
    {
        var rig = new CollectionRig();
        SourceKey blocked = rig.AddSource(CollectedResource.Stone, 2f, 0f);
        SourceKey open = rig.AddSource(CollectedResource.Stone, 10f, 0f);
        int walksToBlocked = 0;
        rig.Motion.DeferralFor = point =>
        {
            if (point.Equals(blocked.Position))
            {
                walksToBlocked++;
                return WorkerDeferralReason.Unreachable;
            }

            return WorkerDeferralReason.None;
        };

        rig.Accept(rig.Order(stone: 1, wood: 0));
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed));

        Assert.Equal(3, walksToBlocked);
        Assert.Equal(new[] { open }, rig.Pickup.Picked);
        AssertClean(rig);
    }

    [Theory]
    [InlineData((int)WorkerDeferralReason.TooFar)]
    [InlineData((int)WorkerDeferralReason.Hazardous)]
    public void ADeterministicWalkRefusalIsNotRetried(int reason)
    {
        var rig = new CollectionRig();
        SourceKey blocked = rig.AddSource(CollectedResource.Stone, 2f, 0f);
        rig.AddSource(CollectedResource.Stone, 10f, 0f);
        int walksToBlocked = 0;
        rig.Motion.DeferralFor = point =>
        {
            if (point.Equals(blocked.Position))
            {
                walksToBlocked++;
                return (WorkerDeferralReason)reason;
            }

            return WorkerDeferralReason.None;
        };

        rig.Accept(rig.Order(stone: 1, wood: 0));
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed));
        Assert.Equal(1, walksToBlocked);
    }

    [Fact]
    public void WhenEverySourceIsUnreachableTheOrderSaysSo()
    {
        var rig = new CollectionRig();
        AddStones(rig, 2);
        rig.Motion.DeferralFor = point => point.Equals(CollectionRig.ChestAt) ? WorkerDeferralReason.None : WorkerDeferralReason.Unreachable;
        rig.Accept(rig.Order(stone: 2, wood: 0));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused));

        Assert.Equal(CollectionAttentionReason.SourceUnreachable, rig.Loop.Reason);
        Assert.Equal(0, rig.Pickup.Picks);
    }

    [Fact]
    public void AWalkThatNeverArrivesHitsItsDeadline()
    {
        var rig = new CollectionRig(new CollectionParameters(walkLegDeadlineSeconds: 2f));
        AddStones(rig, 1);
        rig.Motion.Speed = 0.001f;
        rig.Accept(rig.Order(stone: 1, wood: 0));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused));

        Assert.Equal(CollectionAttentionReason.SourceUnreachable, rig.Loop.Reason);
        Assert.Equal(0, rig.Pickup.Picks);
    }

    /// <summary>A motion port that claims arrival while standing still.</summary>
    private sealed class LyingMotion : ICollectionMotion
    {
        private readonly FakeMotion _inner;

        public LyingMotion(FakeMotion inner) => _inner = inner;

        public bool IsPresent => _inner.IsPresent;

        public SitePoint Position => _inner.Position;

        public CollectionWalkStatus Status => CollectionWalkStatus.Arrived;

        public WorkerDeferralReason LastDeferral => WorkerDeferralReason.None;

        public bool WalkTo(SitePoint point, float arrivalTolerance, string jobId) => true;

        public void Stop(string jobId)
        {
        }
    }

    [Fact]
    public void AFalseArrivalCountsAsAFailedApproachAndNothingIsPicked()
    {
        var parameters = CollectionParameters.Default;
        var modes = new ActorModeOwner(WorkerKey.Thorstein);
        var book = new SourceReservationBook(CollectionRig.Epoch);
        var rig = new CollectionRig();
        AddStones(rig, 2, startX: 10f);
        var motion = new LyingMotion(new FakeMotion(modes, CollectionRig.Anchor) { Speed = 0f });
        var loop = new SoloCollectionLoop(
            parameters, WorkerKey.Thorstein, new WorkerId("thorstein"), modes, book, motion, rig.Custody,
            new ReachCheckingPickup(), rig.Probe, rig.World, null, new CollectionRequestIds(CollectionRig.Epoch));

        Assert.Equal(CollectionIntakeRefusal.Unspecified, loop.Accept(rig.Order(stone: 2, wood: 0), true, CollectionRig.OnePerPick(), 0f));
        float now = 0f;
        for (int tick = 0; tick < 4000 && loop.State != CollectionOrderState.Paused; tick++)
        {
            now += 0.05f;
            loop.Tick(now);
        }

        Assert.Equal(CollectionOrderState.Paused, loop.State);
        Assert.Equal(CollectionAttentionReason.SourceUnreachable, loop.Reason);
        Assert.Equal(0, loop.Picks);
    }

    private sealed class ReachCheckingPickup : ISourcePickupPort
    {
        public PickupResult TryPick(SourceKey source, OrderId order) => throw new InvalidOperationException("picked out of reach");

        public int TryTakeDrop(SpawnedDrop drop, IInventoryPort worker) => throw new InvalidOperationException("took a drop");
    }

    // --- Cooperation -----------------------------------------------------------------------------------------

    [Fact]
    public void AHaulerOrderHandsOffAtTheCheckpointAndTakesBackOnCollectMore()
    {
        var rig = new CollectionRig(new CollectionParameters(workerCarryWeight: 10f), withHauler: true);
        AddStones(rig, 12);
        CollectionOrderDefinition order = rig.Order(stone: 10, wood: 0, mode: ParticipationMode.WithHauler);
        Assert.Equal(CollectionIntakeRefusal.Unspecified, rig.Accept(order));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.WaitingForHauler));
        Assert.Equal(5, rig.Progress(CollectedResource.Stone).Carried);
        Assert.Empty(rig.Custody.Executed);

        // Agent E loads the cart (through custody), then says collect more.
        rig.Cooperation!.Script.Enqueue((CollectionHandOff.Working, CollectionAttentionReason.Unspecified));
        rig.Tick(3);
        Assert.Equal(CollectionOrderState.WaitingForHauler, rig.Loop.State);
        ResourceProgress progress = rig.Progress(CollectedResource.Stone);
        progress.Carried -= 5;
        progress.InCart += 5;
        rig.WorkerInventory.Set(CollectedResource.Stone, 0);
        rig.Cooperation.Script.Enqueue((CollectionHandOff.CollectMore, CollectionAttentionReason.Unspecified));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Collecting));
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.WaitingForHauler));
        Assert.Equal(5, rig.Progress(CollectedResource.Stone).Carried);
        Assert.Equal(10, rig.Pickup.Picks);

        // Agent E hauls and unloads both loads, then says delivered.
        progress.Carried -= 5;
        progress.InCart -= 5;
        progress.Delivered += 10;
        rig.WorkerInventory.Set(CollectedResource.Stone, 0);
        rig.Chest.Set(CollectedResource.Stone, 10);
        rig.Cooperation.Script.Enqueue((CollectionHandOff.Delivered, CollectionAttentionReason.Unspecified));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed));
        Assert.Contains(rig.Custody.Transitions, step => step.From == CollectionOrderState.Collecting && step.To == CollectionOrderState.WaitingForHauler);
        Assert.Contains(rig.Custody.Transitions, step => step.From == CollectionOrderState.WaitingForHauler && step.To == CollectionOrderState.Collecting);
        Assert.Contains(rig.Custody.Transitions, step => step.From == CollectionOrderState.WaitingForHauler && step.To == CollectionOrderState.Delivering);
        Assert.Contains(rig.Custody.Transitions, step => step.From == CollectionOrderState.Delivering && step.To == CollectionOrderState.Completed);
        Assert.Empty(rig.Custody.Executed);
        AssertClean(rig);
    }

    [Fact]
    public void AHaulerClaimingDeliveryIsCheckedAgainstCustody()
    {
        var rig = new CollectionRig(new CollectionParameters(workerCarryWeight: 10f), withHauler: true);
        AddStones(rig, 12);
        rig.Accept(rig.Order(stone: 10, wood: 0, mode: ParticipationMode.WithHauler));
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.WaitingForHauler));

        // "Delivered", but custody shows nothing delivered: he keeps working.
        ResourceProgress progress = rig.Progress(CollectedResource.Stone);
        progress.Carried -= 5;
        progress.InCart += 5;
        rig.WorkerInventory.Set(CollectedResource.Stone, 0);
        rig.Cooperation!.Script.Enqueue((CollectionHandOff.Delivered, CollectionAttentionReason.Unspecified));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Collecting));
        Assert.NotEqual(CollectionOrderState.Completed, rig.Loop.State);
    }

    [Theory]
    [InlineData((int)CollectionHandOff.Paused, (int)CollectionAttentionReason.RendezvousTimedOut, (int)CollectionOrderState.Paused, (int)CollectionAttentionReason.RendezvousTimedOut)]
    [InlineData((int)CollectionHandOff.Paused, (int)CollectionAttentionReason.Unspecified, (int)CollectionOrderState.Paused, (int)CollectionAttentionReason.HaulerUnavailable)]
    [InlineData((int)CollectionHandOff.NeedsAttention, (int)CollectionAttentionReason.CartLeaseLost, (int)CollectionOrderState.NeedsAttention, (int)CollectionAttentionReason.CartLeaseLost)]
    [InlineData((int)CollectionHandOff.NeedsAttention, (int)CollectionAttentionReason.Unspecified, (int)CollectionOrderState.NeedsAttention, (int)CollectionAttentionReason.HaulerNeedsAttention)]
    [InlineData((int)CollectionHandOff.Unspecified, (int)CollectionAttentionReason.Unspecified, (int)CollectionOrderState.Paused, (int)CollectionAttentionReason.HaulerUnavailable)]
    public void TheHaulersStopsArePassedOnWithAReason(int step, int reason, int state, int expected)
    {
        var rig = new CollectionRig(new CollectionParameters(workerCarryWeight: 10f), withHauler: true);
        AddStones(rig, 12);
        rig.Accept(rig.Order(stone: 10, wood: 0, mode: ParticipationMode.WithHauler));
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.WaitingForHauler));

        rig.Cooperation!.Script.Enqueue(((CollectionHandOff)step, (CollectionAttentionReason)reason));
        rig.Tick();

        Assert.Equal((CollectionOrderState)state, rig.Loop.State);
        Assert.Equal((CollectionAttentionReason)expected, rig.Loop.Reason);
        AssertClean(rig);
    }

    [Fact]
    public void AHaulerOrderIsRefusedWithoutAnAvailableHauler()
    {
        var withoutPort = new CollectionRig();
        Assert.Equal(
            CollectionIntakeRefusal.HaulerUnavailable,
            withoutPort.Accept(withoutPort.Order(stone: 5, wood: 0, mode: ParticipationMode.WithHauler)));

        var unavailable = new CollectionRig(withHauler: true);
        unavailable.Cooperation!.Available = false;
        Assert.Equal(
            CollectionIntakeRefusal.HaulerUnavailable,
            unavailable.Accept(unavailable.Order(stone: 5, wood: 0, mode: ParticipationMode.WithHauler)));
        Assert.Equal(ActorMode.Resting, unavailable.Modes.Mode);
    }

    [Fact]
    public void AHaulerLostAtTheCheckpointPausesAndNothingMovesIntoSoloDelivery()
    {
        var rig = new CollectionRig(new CollectionParameters(workerCarryWeight: 10f), withHauler: true);
        AddStones(rig, 12);
        rig.Accept(rig.Order(stone: 10, wood: 0, mode: ParticipationMode.WithHauler));
        Assert.True(rig.RunUntil(() => rig.Progress(CollectedResource.Stone).Carried >= 2));

        rig.Cooperation!.Available = false;
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused));

        Assert.Equal(CollectionAttentionReason.HaulerUnavailable, rig.Loop.Reason);
        Assert.Empty(rig.Custody.Executed);
        Assert.Equal(0, rig.Chest.Count(MaterialItem.Of(CollectedResource.Stone)));
    }

    // --- Player controls ------------------------------------------------------------------------------------

    [Fact]
    public void PauseStopsHimAndReleasesHisClaimsAndResumeSurveysAfresh()
    {
        var rig = new CollectionRig();
        AddStones(rig, 10);
        rig.Accept(rig.Order(stone: 10, wood: 0));
        Assert.True(rig.RunUntil(() => rig.Loop.Phase == CollectionPhase.WalkingToSource && rig.Pickup.Picks >= 2));

        ControlResult pause = rig.Loop.Pause(rig.Now);
        Assert.Equal(ControlOutcome.Done, pause.Outcome);
        Assert.Equal(CollectionOrderState.Paused, rig.Loop.State);
        Assert.True(rig.Loop.PausedByPlayer);
        Assert.Equal(CollectionAttentionReason.PausedByPlayer, rig.Loop.Reason);
        Assert.Equal(CollectionWalkStatus.Idle, rig.Motion.Status);
        Assert.Equal(0, rig.Book.Count);
        Assert.Equal(ActorMode.Paused, rig.Modes.Mode);
        Assert.Contains("(by you)", rig.Loop.Describe());

        int picks = rig.Pickup.Picks;
        rig.Tick(200);
        Assert.Equal(picks, rig.Pickup.Picks);
        Assert.Equal(ControlOutcome.Unchanged, rig.Loop.Pause(rig.Now).Outcome);

        Assert.Equal(ControlOutcome.Done, rig.Loop.Resume(rig.Now).Outcome);
        Assert.Equal(CollectionOrderState.Surveying, rig.Loop.State);
        Assert.Equal(ControlOutcome.Unchanged, rig.Loop.Resume(rig.Now).Outcome);
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed));

        Assert.Equal(ControlOutcome.Refused, rig.Loop.Resume(rig.Now).Outcome);
        Assert.Equal(ControlOutcome.Refused, rig.Loop.Pause(rig.Now).Outcome);
        Assert.Equal(ControlOutcome.Refused, rig.Loop.Cancel(rig.Now).Outcome);
        AssertConserved(rig, CollectedResource.Stone);
        AssertClean(rig);
    }

    [Fact]
    public void CancelIsNotARefund()
    {
        var rig = new CollectionRig();
        AddStones(rig, 10);
        rig.Accept(rig.Order(stone: 10, wood: 0));
        Assert.True(rig.RunUntil(() => rig.Progress(CollectedResource.Stone).Carried >= 3));
        int carried = rig.Progress(CollectedResource.Stone).Carried;

        ControlResult cancel = rig.Loop.Cancel(rig.Now);

        Assert.Equal(ControlOutcome.Done, cancel.Outcome);
        Assert.Equal(CollectionOrderState.Cancelled, rig.Loop.State);
        Assert.Equal(carried, rig.WorkerInventory.Count(MaterialItem.Of(CollectedResource.Stone)));
        Assert.Equal(0, rig.Chest.Count(MaterialItem.Of(CollectedResource.Stone)));
        Assert.Equal(ActorMode.Resting, rig.Modes.Mode);
        Assert.Equal(0, rig.Book.Count);
        Assert.Equal(CollectionWalkStatus.Idle, rig.Motion.Status);
        Assert.False(rig.Loop.HasActiveOrder);

        int picks = rig.Pickup.Picks;
        rig.Tick(200);
        Assert.Equal(picks, rig.Pickup.Picks);
        AssertConserved(rig, CollectedResource.Stone);
        AssertClean(rig);
    }

    [Fact]
    public void CancellingAHaulerOrderCancelsTheHaulAndParksTheCart()
    {
        var rig = new CollectionRig(withHauler: true);
        AddStones(rig, 5);
        rig.Accept(rig.Order(stone: 5, wood: 0, mode: ParticipationMode.WithHauler));
        rig.Tick(5);

        Assert.Equal(ControlOutcome.Done, rig.Loop.Cancel(rig.Now).Outcome);
        Assert.Equal(new[] { true }, rig.Cooperation!.Cancels);
    }

    [Fact]
    public void CancelIsAllowedFromEveryStateThatIsNotFinal()
    {
        foreach (CollectionOrderState state in Enum.GetValues(typeof(CollectionOrderState)))
        {
            if (state == CollectionOrderState.Unspecified || CollectionOrderStates.IsTerminal(state))
            {
                continue;
            }

            Assert.True(CollectionOrderStates.CanTransition(state, CollectionOrderState.Cancelled), state.ToString());
        }
    }

    // --- Acceptance and records -----------------------------------------------------------------------------

    [Fact]
    public void NothingStartsUnlessTheAcceptanceIsJournaled()
    {
        var rig = new CollectionRig();
        AddStones(rig, 5);
        rig.Custody.FailRecordAccepted = true;

        Assert.Equal(CollectionIntakeRefusal.NotRecorded, rig.Accept(rig.Order(stone: 5, wood: 0)));
        rig.Tick(200);

        Assert.False(rig.Loop.HasActiveOrder);
        Assert.Equal(ActorMode.Resting, rig.Modes.Mode);
        Assert.Equal(0, rig.Motion.WalksIssued);
        Assert.Equal(0, rig.Pickup.Picks);
    }

    [Fact]
    public void AStopHappensEvenUnrecordedButWorkNeverResumesUnrecorded()
    {
        var rig = new CollectionRig();
        AddStones(rig, 5);
        rig.Accept(rig.Order(stone: 5, wood: 0));
        rig.Custody.FailRecordTransitions = true;

        rig.Tick(50);

        Assert.Equal(CollectionOrderState.Paused, rig.Loop.State);
        Assert.Equal(CollectionAttentionReason.JournalReadOnly, rig.Loop.Reason);
        Assert.Equal(1, rig.Loop.UnrecordedTransitions);
        Assert.Equal(0, rig.Pickup.Picks);

        Assert.Equal(ControlOutcome.Refused, rig.Loop.Resume(rig.Now).Outcome);
        Assert.Equal(CollectionOrderState.Paused, rig.Loop.State);

        rig.Custody.FailRecordTransitions = false;
        Assert.Equal(ControlOutcome.Done, rig.Loop.Resume(rig.Now).Outcome);
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed));

        // A stop that cannot be written still stops.
        var stopping = new CollectionRig();
        AddStones(stopping, 5);
        stopping.Accept(stopping.Order(stone: 5, wood: 0));
        Assert.True(stopping.RunUntil(() => stopping.Pickup.Picks >= 1));
        stopping.Custody.FailRecordTransitions = true;
        stopping.World.Authority = WorkAuthorityVerdict.OtherPeersConnected;
        stopping.Tick(3);
        Assert.Equal(CollectionOrderState.Paused, stopping.Loop.State);
        Assert.Equal(CollectionAttentionReason.OtherPeersConnected, stopping.Loop.Reason);
        Assert.True(stopping.Loop.UnrecordedTransitions >= 1);
    }

    [Fact]
    public void AJournalThatStopsBeingWritablePausesTheOrder()
    {
        var rig = new CollectionRig();
        AddStones(rig, 5);
        rig.Accept(rig.Order(stone: 5, wood: 0));
        Assert.True(rig.RunUntil(() => rig.Pickup.Picks >= 1));

        rig.Custody.Writable = false;
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused, 50));
        Assert.Equal(CollectionAttentionReason.JournalReadOnly, rig.Loop.Reason);
    }

    [Fact]
    public void OnlyOneOrderAtATimeAndOnlyTheJobHoldingThorsteinCommandsHim()
    {
        var rig = new CollectionRig();
        AddStones(rig, 10);
        Assert.Equal(CollectionIntakeRefusal.Unspecified, rig.Accept(rig.Order(stone: 3, wood: 0)));
        Assert.Equal(CollectionIntakeRefusal.AnotherOrderActive, rig.Accept(rig.Order(stone: 3, wood: 0, id: "collect-2")));
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed));
        Assert.Equal(CollectionIntakeRefusal.Unspecified, rig.Accept(rig.Order(stone: 3, wood: 0, id: "collect-3")));
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed));
        AssertClean(rig);

        var busy = new CollectionRig();
        AddStones(busy, 3);
        busy.Modes.Enter(ActorMode.Working, "haul-9");
        Assert.Equal(CollectionIntakeRefusal.WorkerBusy, busy.Accept(busy.Order(stone: 3, wood: 0)));
        busy.Tick(100);
        Assert.Equal(0, busy.Motion.WalksIssued);
        Assert.Equal("haul-9", busy.Modes.JobId);
    }

    [Fact]
    public void TheDefaultCircleNeedsAPreviewAndTheOrderMustBeForThisWorker()
    {
        var rig = new CollectionRig();
        CollectionOrderDefinition order = rig.Order(stone: 3, wood: 0);
        Assert.Equal(
            CollectionIntakeRefusal.PreviewRequired, rig.Loop.Accept(order, defaultCirclePreviewed: false, CollectionRig.OnePerPick(), 0f));

        var gunnarsOrder = new CollectionOrderDefinition(
            new OrderId("collect-9"), new WorkerId("gunnar"), order.Quotas, order.Scope, order.Delivery, order.Participation, "TESTER");
        Assert.Equal(CollectionIntakeRefusal.WrongWorker, rig.Loop.Accept(gunnarsOrder, true, CollectionRig.OnePerPick(), 0f));
        Assert.Equal(ActorMode.Resting, rig.Modes.Mode);
    }

    [Fact]
    public void ASourceHeldByAnotherOrderIsNeverClaimedTwice()
    {
        var rig = new CollectionRig();
        SourceKey heldElsewhere = rig.AddSource(CollectedResource.Stone, 1f, 0f);
        SourceKey free = rig.AddSource(CollectedResource.Stone, 8f, 0f);
        Assert.Equal(ReservationOutcome.Reserved, rig.Book.Reserve(heldElsewhere, new OrderId("other-order")));

        rig.Accept(rig.Order(stone: 1, wood: 0));
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed));

        Assert.Equal(new[] { free }, rig.Pickup.Picked);
        Assert.True(rig.Book.IsHeldBy(heldElsewhere, new OrderId("other-order")));
    }

    // --- The body ---------------------------------------------------------------------------------------------

    [Fact]
    public void WhenHisTickStopsArrivingTheOrderStopsInsteadOfClaimingToWork()
    {
        var loaded = new CollectionRig();
        AddStones(loaded, 5);
        loaded.Accept(loaded.Order(stone: 5, wood: 0));
        loaded.Tick(3);
        CollectionOrderState before = loaded.Loop.State;
        loaded.Loop.Supervise(loaded.Now + 1f);
        Assert.Equal(before, loaded.Loop.State);
        loaded.Loop.Supervise(loaded.Now + 5f);
        Assert.Equal(CollectionOrderState.Paused, loaded.Loop.State);
        Assert.Equal(CollectionAttentionReason.WorkerBodyLost, loaded.Loop.Reason);

        var unloaded = new CollectionRig();
        AddStones(unloaded, 5);
        unloaded.Accept(unloaded.Order(stone: 5, wood: 0));
        unloaded.Tick(3);
        unloaded.World.Loaded = _ => false;
        unloaded.Loop.Supervise(unloaded.Now + 5f);
        Assert.Equal(CollectionAttentionReason.ScopeUnloaded, unloaded.Loop.Reason);

        var carrying = new CollectionRig();
        AddStones(carrying, 5);
        carrying.Accept(carrying.Order(stone: 5, wood: 0));
        Assert.True(carrying.RunUntil(() => carrying.Progress(CollectedResource.Stone).Carried >= 2));
        carrying.Loop.Supervise(carrying.Now + 5f);
        Assert.Equal(CollectionOrderState.NeedsAttention, carrying.Loop.State);
        Assert.Equal(CollectionAttentionReason.WorkerBodyLost, carrying.Loop.Reason);
    }

    [Fact]
    public void ABodyThatDisappearsMidWorkStopsAtTheNextCheckpoint()
    {
        var rig = new CollectionRig();
        AddStones(rig, 5);
        rig.Accept(rig.Order(stone: 5, wood: 0));
        Assert.True(rig.RunUntil(() => rig.Pickup.Picks >= 1));

        rig.Motion.IsPresent = false;
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.NeedsAttention || rig.Loop.State == CollectionOrderState.Paused, 50));
        Assert.Equal(CollectionAttentionReason.WorkerBodyLost, rig.Loop.Reason);
    }

    [Fact]
    public void AWorldUnloadDropsTheLoopsMemoryWithoutWritingAnything()
    {
        var rig = new CollectionRig();
        AddStones(rig, 5);
        rig.Accept(rig.Order(stone: 5, wood: 0));
        Assert.True(rig.RunUntil(() => rig.Pickup.Picks >= 1));
        int recorded = rig.Custody.Transitions.Count;

        rig.Loop.Abandon();

        Assert.False(rig.Loop.HasActiveOrder);
        Assert.Equal(CollectionOrderState.Unspecified, rig.Loop.State);
        Assert.Equal(ActorMode.Resting, rig.Modes.Mode);
        Assert.Equal(recorded, rig.Custody.Transitions.Count);
        Assert.Equal("No collection order.", rig.Loop.Describe());
    }

    // --- Everything at once ------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(2026)]
    public void UnderRandomFaultsEveryTransitionIsLegalAndEveryUnitIsAccountedFor(int seed)
    {
        var random = new Random(seed);
        var rig = new CollectionRig(new CollectionParameters(workerCarryWeight: 30f), withHauler: false);
        AddStones(rig, 40);
        AddBranches(rig, 40);
        rig.Accept(rig.Order(stone: 20, wood: 20));

        for (int step = 0; step < 6000 && !CollectionOrderStates.IsTerminal(rig.Loop.State); step++)
        {
            int roll = random.Next(1000);
            switch (roll)
            {
                case 0: rig.World.Authority = WorkAuthorityVerdict.OtherPeersConnected; break;
                case 1: rig.World.Authority = WorkAuthorityVerdict.Granted; break;
                case 2: rig.World.Readiness = FakeWorld.NotReady(ReadinessRefusal.ToolUnusable); break;
                case 3: rig.World.Readiness = FakeWorld.ReadyVerdict(); break;
                case 4: rig.Custody.DestinationRefusal = CollectionAttentionReason.DestinationUnavailable; break;
                case 5: rig.Custody.DestinationRefusal = CollectionAttentionReason.Unspecified; break;
                case 6: rig.Pickup.ForcedOutcomes.Enqueue(PickupOutcome.Refused); break;
                case 7: rig.Custody.ForcedOutcomes.Enqueue(TransferOutcome.Refused); break;
                case 8: rig.Loop.Pause(rig.Now); break;
                case 9: case 10: case 11: case 12: rig.Loop.Resume(rig.Now); break;
                case 13: rig.Custody.FailRecordTransitions = true; break;
                case 14: rig.Custody.FailRecordTransitions = false; break;
                case 15: rig.Motion.DeferralFor = _ => WorkerDeferralReason.OutsideLoadedGround; break;
                case 16: rig.Motion.DeferralFor = _ => WorkerDeferralReason.None; break;
                case 17: rig.Chest.Limit(CollectedResource.Stone, rig.Chest.Count(MaterialItem.Of(CollectedResource.Stone))); break;
                case 18: rig.Chest.Limit(CollectedResource.Stone, 1000); break;
            }

            rig.Tick();
            if (rig.Loop.State == CollectionOrderState.Paused && random.Next(20) == 0)
            {
                rig.World.Authority = WorkAuthorityVerdict.Granted;
                rig.World.Readiness = FakeWorld.ReadyVerdict();
                rig.Custody.DestinationRefusal = CollectionAttentionReason.Unspecified;
                rig.Custody.FailRecordTransitions = false;
                rig.Motion.DeferralFor = _ => WorkerDeferralReason.None;
                rig.Chest.Limit(CollectedResource.Stone, 1000);
                rig.Loop.Resume(rig.Now);
            }
        }

        Assert.Equal(0, rig.Loop.IllegalTransitionsAttempted);
        AssertConserved(rig, CollectedResource.Stone);
        AssertConserved(rig, CollectedResource.Wood);
        Assert.True(rig.Progress(CollectedResource.Stone).Delivered + rig.Progress(CollectedResource.Stone).Carried <= 20);
        Assert.True(rig.Progress(CollectedResource.Wood).Delivered + rig.Progress(CollectedResource.Wood).Carried <= 20);
        Assert.True(rig.WorkerInventory.Count(MaterialItem.Of(CollectedResource.Stone)) * 2f +
            rig.WorkerInventory.Count(MaterialItem.Of(CollectedResource.Wood)) * 2f <= 30f);
        if (rig.Loop.State == CollectionOrderState.Completed)
        {
            Assert.Equal(20, rig.Progress(CollectedResource.Stone).Delivered);
            Assert.Equal(20, rig.Progress(CollectedResource.Wood).Delivered);
        }
    }

    [Fact]
    public void TheStatusReportSaysWhatIsEstimatedAndWhatIsReal()
    {
        var rig = new CollectionRig();
        AddStones(rig, 5);
        rig.Accept(rig.Order(stone: 5, wood: 0));
        Assert.True(rig.RunUntil(() => rig.Loop.Phase == CollectionPhase.WalkingToSource));

        string report = rig.Loop.Describe();
        Assert.Contains("Order collect-1", report);
        Assert.Contains("requested 5", report);
        Assert.Contains("reserved estimate 1, not counted", report);
        Assert.Contains("Last solo survey", report);
        Assert.Contains("still to collect 5", report);
    }

    // --- R2 M4: the harness has teeth, and the pick-time recheck is load-bearing ---------------------------

    [Fact]
    public void TheConservationCheckFailsWhenTheRecordCreditsWhatWasAskedRatherThanWhatMoved()
    {
        // The whole conservation argument rests on crediting measured counts.
        // With the record crediting the intent's count instead, the chest and
        // the ledger part company — and the check that is named for catching
        // that must actually catch it.
        var rig = new CollectionRig();
        AddStones(rig, 6);
        rig.Custody.CreditWithoutMeasuring = true;
        rig.Accept(rig.Order(stone: 6, wood: 0));
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Delivering));

        // The chest can take two of the six he carries. A record that credits
        // the six it asked to move has parted company with the chest.
        rig.Chest.Limit(CollectedResource.Stone, 2);
        Assert.True(rig.RunUntil(() => rig.Custody.Executed.Count >= 1));
        rig.Tick(5);

        Assert.Equal(2, rig.Chest.Count(MaterialItem.Of(CollectedResource.Stone)));
        Assert.Equal(6, rig.Progress(CollectedResource.Stone).Delivered);
        Assert.Throws<Xunit.Sdk.EqualException>(() => AssertConserved(rig, CollectedResource.Stone));
    }

    [Fact]
    public void AChestThatFillsDuringTheWalkIsCaughtBeforeThePickNotAfterIt()
    {
        var rig = new CollectionRig();
        rig.AddSource(CollectedResource.Stone, 2f, 0f);
        rig.AddSource(CollectedResource.Stone, 12f, 0f);
        rig.Motion.Speed = 1f;
        rig.Accept(rig.Order(stone: 5, wood: 0));

        Assert.True(rig.RunUntil(() => rig.Pickup.Picks == 1 && rig.Loop.Phase == CollectionPhase.WalkingToSource));

        // The selector already approved this source; only the check in the same
        // tick as the pick can see the chest fill up behind him.
        rig.Chest.Limit(CollectedResource.Stone, 1);
        Assert.True(rig.RunUntil(() => rig.Loop.Phase != CollectionPhase.WalkingToSource));

        Assert.Equal(1, rig.Pickup.Picks);
        Assert.Equal(0, rig.Book.Count);
        AssertConserved(rig, CollectedResource.Stone);
        AssertClean(rig);
    }

    [Fact]
    public void APackThatFillsDuringTheWalkIsCaughtBeforeThePick()
    {
        var rig = new CollectionRig();
        rig.AddSource(CollectedResource.Stone, 2f, 0f);
        rig.AddSource(CollectedResource.Stone, 12f, 0f);
        rig.Motion.Speed = 1f;
        rig.Accept(rig.Order(stone: 5, wood: 0));

        Assert.True(rig.RunUntil(() => rig.Pickup.Picks == 1 && rig.Loop.Phase == CollectionPhase.WalkingToSource));

        // His inventory has no room for another stone by the time he arrives.
        rig.WorkerInventory.Limit(CollectedResource.Stone, 1);
        Assert.True(rig.RunUntil(() => rig.Loop.Phase != CollectionPhase.WalkingToSource));

        Assert.Equal(1, rig.Pickup.Picks);
        AssertConserved(rig, CollectedResource.Stone);
        AssertClean(rig);
    }

    [Fact]
    public void AnAreaSurveyedWhileTheWorldWasStillFillingSaysTheLookWasShort()
    {
        // R2 M2: a survey over a scene that was still being created has not
        // seen the ground; it must not answer "there is nothing here".
        var rig = new CollectionRig();
        rig.Probe.SceneMoved = true;
        rig.Accept(rig.Order(stone: 5, wood: 0));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused));

        Assert.Equal(CollectionAttentionReason.SurveyIncomplete, rig.Loop.Reason);
        Assert.NotEqual(CollectionAttentionReason.NoEligibleSources, rig.Loop.Reason);
    }

    [Fact]
    public void AHoldOrderSaysHowItsMaterialsComeBack()
    {
        // R2 M1: hold mode ends when the settlement record shows the materials
        // handed over. The order must say so, and cancelling must always be
        // available so nothing is stranded on him.
        var rig = new CollectionRig();
        AddStones(rig, 6);
        rig.Accept(rig.Order(stone: 5, wood: 0, hold: true));
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.HoldingForPlayer));

        string report = rig.Loop.Describe();
        Assert.Contains("holding everything for you", report);
        Assert.Contains("handed over", report);
        Assert.Contains("cf_collect cancel", report);

        Assert.Equal(ControlOutcome.Done, rig.Loop.Cancel(rig.Now).Outcome);
        Assert.Equal(5, rig.WorkerInventory.Count(MaterialItem.Of(CollectedResource.Stone)));
        Assert.Equal(ActorMode.Resting, rig.Modes.Mode);
    }
}
