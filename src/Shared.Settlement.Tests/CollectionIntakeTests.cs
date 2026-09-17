using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Tools;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;

namespace Shared.Settlement.Tests;

/// <summary>Order acceptance (GATHER-01, GATHER-02, GATHER-06, D12): the whole
/// refusal table, in the order a player would fix things.</summary>
public sealed class CollectionIntakeTests
{
    private static readonly Guid Epoch = new Guid("0f0e0d0c-0b0a-0908-0706-050403020100");

    private static CollectionOrderDefinition Order(
        int stone = 20, int wood = 30, bool hold = false, WorkScopeSource source = WorkScopeSource.HarvestDesignation,
        ParticipationMode mode = ParticipationMode.Solo, bool duplicate = false)
    {
        var quotas = new List<ResourceQuota>();
        if (stone > 0)
        {
            quotas.Add(new ResourceQuota(CollectedResource.Stone, stone));
        }

        if (wood > 0)
        {
            quotas.Add(new ResourceQuota(CollectedResource.Wood, wood));
        }

        if (duplicate)
        {
            quotas.Add(new ResourceQuota(CollectedResource.Stone, 1));
        }

        return new CollectionOrderDefinition(
            new OrderId("collect-1"), new WorkerId("thorstein"), quotas,
            new WorkScope(source, new SitePoint(0f, 30f, 0f), 30f, "anchor", 1, Epoch),
            hold ? DeliveryTarget.HoldForPlayer() : DeliveryTarget.ToContainer("0000000000000001:00000002", Epoch, new SitePoint(3f, 30f, 3f)),
            mode, "TESTER");
    }

    private static CollectionIntakeFacts Facts(
        WorkAuthorityVerdict authority = WorkAuthorityVerdict.Granted,
        bool custody = true,
        bool writable = true,
        bool another = false,
        bool busy = false,
        bool present = true,
        ReadinessVerdict? readiness = null,
        ScopeCheck scope = ScopeCheck.Valid,
        bool previewed = true,
        int stoneYield = 1,
        int woodYield = 1,
        bool destination = true,
        CollectionAttentionReason destinationRefusal = CollectionAttentionReason.Unspecified,
        float carry = 100f,
        float unitWeight = 2f,
        bool hauler = true)
    {
        var yields = new Dictionary<CollectedResource, int>();
        if (stoneYield >= 0)
        {
            yields[CollectedResource.Stone] = stoneYield;
        }

        if (woodYield >= 0)
        {
            yields[CollectedResource.Wood] = woodYield;
        }

        return new CollectionIntakeFacts(
            authority, custody, writable, another, busy, present, readiness ?? FakeWorld.ReadyVerdict(), scope, previewed,
            yields, destination, destinationRefusal, carry, _ => unitWeight, hauler);
    }

    private static CollectionIntakeRefusal Check(CollectionOrderDefinition order, CollectionIntakeFacts facts) =>
        CollectionIntake.Check(order, facts);

    [Fact]
    public void AWellFormedOrderWithEverythingInPlaceIsAccepted()
    {
        Assert.Equal(CollectionIntakeRefusal.Unspecified, Check(Order(), Facts()));
        Assert.Equal(CollectionIntakeRefusal.Unspecified, Check(Order(stone: 10, wood: 0, hold: true), Facts()));
        Assert.Equal(CollectionIntakeRefusal.Unspecified, Check(Order(mode: ParticipationMode.WithHauler), Facts()));
    }

    [Fact]
    public void EachRefusalIsReportedByItsOwnCheck()
    {
        Assert.Equal(CollectionIntakeRefusal.DuplicateResource, Check(Order(duplicate: true), Facts()));
        Assert.Equal(
            CollectionIntakeRefusal.ParticipationMissing, Check(Order(mode: ParticipationMode.Unspecified), Facts()));
        Assert.Equal(CollectionIntakeRefusal.NoAuthority, Check(Order(), Facts(authority: WorkAuthorityVerdict.RuntimeDisabled)));
        Assert.Equal(CollectionIntakeRefusal.NoAuthority, Check(Order(), Facts(authority: WorkAuthorityVerdict.NotHost)));
        Assert.Equal(CollectionIntakeRefusal.NoAuthority, Check(Order(), Facts(authority: WorkAuthorityVerdict.Unspecified)));
        Assert.Equal(
            CollectionIntakeRefusal.OtherPeersConnected, Check(Order(), Facts(authority: WorkAuthorityVerdict.OtherPeersConnected)));
        Assert.Equal(CollectionIntakeRefusal.CustodyUnavailable, Check(Order(), Facts(custody: false)));
        Assert.Equal(CollectionIntakeRefusal.JournalReadOnly, Check(Order(), Facts(writable: false)));
        Assert.Equal(CollectionIntakeRefusal.AnotherOrderActive, Check(Order(), Facts(another: true)));
        Assert.Equal(CollectionIntakeRefusal.WorkerBusy, Check(Order(), Facts(busy: true)));
        Assert.Equal(CollectionIntakeRefusal.WorkerAbsent, Check(Order(), Facts(present: false)));
        Assert.Equal(
            CollectionIntakeRefusal.WorkerNotRecruited, Check(Order(), Facts(readiness: FakeWorld.NotReady(ReadinessRefusal.NotRecruited))));
        Assert.Equal(CollectionIntakeRefusal.ToolMissing, Check(Order(), Facts(readiness: FakeWorld.NotReady(ReadinessRefusal.ToolMissing))));
        Assert.Equal(CollectionIntakeRefusal.ToolBroken, Check(Order(), Facts(readiness: FakeWorld.NotReady(ReadinessRefusal.ToolUnusable))));
        Assert.Equal(CollectionIntakeRefusal.ScopeInvalid, Check(Order(), Facts(scope: ScopeCheck.Invalid)));
        Assert.Equal(CollectionIntakeRefusal.ScopeInvalid, Check(Order(), Facts(scope: ScopeCheck.Changed)));
        Assert.Equal(CollectionIntakeRefusal.ScopeInvalid, Check(Order(), Facts(scope: ScopeCheck.Unspecified)));
        Assert.Equal(CollectionIntakeRefusal.ScopeUnloaded, Check(Order(), Facts(scope: ScopeCheck.Unloaded)));
        Assert.Equal(
            CollectionIntakeRefusal.PreviewRequired,
            Check(Order(source: WorkScopeSource.DefaultCampCircle), Facts(previewed: false)));
        Assert.Equal(CollectionIntakeRefusal.Unspecified, Check(Order(source: WorkScopeSource.HarvestDesignation), Facts(previewed: false)));
        Assert.Equal(CollectionIntakeRefusal.YieldUnknown, Check(Order(), Facts(stoneYield: -1)));
        Assert.Equal(CollectionIntakeRefusal.YieldUnknown, Check(Order(), Facts(woodYield: 0)));
        Assert.Equal(
            CollectionIntakeRefusal.DestinationUnavailable,
            Check(Order(), Facts(destination: false, destinationRefusal: CollectionAttentionReason.DestinationUnavailable)));
        Assert.Equal(
            CollectionIntakeRefusal.DestinationUnavailable,
            Check(Order(), Facts(destination: false, destinationRefusal: CollectionAttentionReason.Unspecified)));
        Assert.Equal(
            CollectionIntakeRefusal.DestinationStale,
            Check(Order(), Facts(destination: false, destinationRefusal: CollectionAttentionReason.DestinationStale)));
        Assert.Equal(
            CollectionIntakeRefusal.DestinationAccessDenied,
            Check(Order(), Facts(destination: false, destinationRefusal: CollectionAttentionReason.DestinationAccessDenied)));
        Assert.Equal(
            CollectionIntakeRefusal.HaulerUnavailable, Check(Order(mode: ParticipationMode.WithHauler), Facts(hauler: false)));
    }

    [Theory]
    [InlineData(2, 20, true)]
    [InlineData(2, 21, false)]
    [InlineData(3, 30, true)]
    [InlineData(3, 31, false)]
    [InlineData(1, 499, true)]
    public void AQuotaMustBeReachableWithoutOvercollecting(int yield, int quota, bool accepted)
    {
        CollectionIntakeRefusal refusal = Check(Order(stone: quota, wood: 0), Facts(stoneYield: yield));
        Assert.Equal(accepted ? CollectionIntakeRefusal.Unspecified : CollectionIntakeRefusal.QuotaNotMultipleOfYield, refusal);
    }

    [Theory]
    [InlineData(20, 30, 100f, true)]
    [InlineData(21, 30, 100f, false)]
    [InlineData(50, 0, 100f, true)]
    [InlineData(10, 10, 30f, false)]
    public void HoldingForThePlayerNeedsEverythingToFitOnHisBack(int stone, int wood, float carry, bool accepted)
    {
        CollectionIntakeRefusal refusal = Check(Order(stone: stone, wood: wood, hold: true), Facts(carry: carry));
        Assert.Equal(accepted ? CollectionIntakeRefusal.Unspecified : CollectionIntakeRefusal.HoldExceedsCarry, refusal);

        // A chest order has no such limit: he walks as many loads as needed.
        Assert.Equal(CollectionIntakeRefusal.Unspecified, Check(Order(stone: stone, wood: wood), Facts(carry: carry)));
    }

    [Fact]
    public void AHoldOrderWithAnUnknownUnitWeightIsRefused()
    {
        Assert.Equal(
            CollectionIntakeRefusal.HoldExceedsCarry, Check(Order(stone: 5, wood: 0, hold: true), Facts(unitWeight: 0f)));
    }

    [Fact]
    public void ChecksRunInTheOrderAPlayerWouldFixThings()
    {
        // Everything wrong at once: the order itself first, then authority.
        CollectionIntakeFacts everythingWrong = Facts(
            authority: WorkAuthorityVerdict.NotHost, custody: false, writable: false, another: true, busy: true,
            present: false, readiness: FakeWorld.NotReady(ReadinessRefusal.NotRecruited), scope: ScopeCheck.Invalid,
            previewed: false, stoneYield: 0, destination: false, hauler: false);

        Assert.Equal(CollectionIntakeRefusal.DuplicateResource, Check(Order(duplicate: true), everythingWrong));
        Assert.Equal(CollectionIntakeRefusal.NoAuthority, Check(Order(), everythingWrong));
    }

    [Fact]
    public void ReadinessMapsToTheSameReasonsAtAcceptanceAndWhileWorking()
    {
        Assert.Equal(CollectionAttentionReason.Unspecified, CollectionIntake.AttentionFor(FakeWorld.ReadyVerdict()));
        Assert.Equal(
            CollectionAttentionReason.WorkerNotRecruited, CollectionIntake.AttentionFor(FakeWorld.NotReady(ReadinessRefusal.NotRecruited)));
        Assert.Equal(CollectionAttentionReason.ToolMissing, CollectionIntake.AttentionFor(FakeWorld.NotReady(ReadinessRefusal.ToolMissing)));
        Assert.Equal(CollectionAttentionReason.ToolBroken, CollectionIntake.AttentionFor(FakeWorld.NotReady(ReadinessRefusal.ToolUnusable)));
        Assert.Equal(CollectionIntakeRefusal.ToolMissing, CollectionIntake.FromReadiness(default));
    }

    [Fact]
    public void PickingNeedsNoToolButAcceptanceNeedsTheAxeAndTheHammer()
    {
        // D12: the per-action list is empty, so picking never wears a tool;
        // the acceptance list is the building pair.
        Assert.Empty(WorkerReadiness.ForGathering);
        Assert.Equal(new[] { ToolKind.Axe, ToolKind.Hammer }, WorkerReadiness.ForBuilding);
    }

    [Fact]
    public void EveryRefusalEveryAttentionReasonAndEveryStateHasASentence()
    {
        foreach (CollectionIntakeRefusal refusal in Enum.GetValues(typeof(CollectionIntakeRefusal)))
        {
            string sentence = CollectionIntake.Describe(refusal);
            Assert.False(string.IsNullOrWhiteSpace(sentence));
            Assert.DoesNotContain("does not know", sentence);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (CollectionAttentionReason reason in Enum.GetValues(typeof(CollectionAttentionReason)))
        {
            string sentence = CollectionSentences.Describe(reason);
            Assert.False(string.IsNullOrWhiteSpace(sentence));
            Assert.DoesNotContain("does not know", sentence);
            Assert.True(seen.Add(sentence), "two reasons share a sentence: " + reason);
        }

        foreach (CollectionOrderState state in Enum.GetValues(typeof(CollectionOrderState)))
        {
            if (state != CollectionOrderState.Unspecified)
            {
                Assert.DoesNotContain("bug", CollectionSentences.Describe(state));
            }
        }
    }
}
