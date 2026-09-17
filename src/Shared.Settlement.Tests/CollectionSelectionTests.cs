using System;
using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;

namespace Shared.Settlement.Tests;

/// <summary>Quantity-aware target selection (GATHER-04), carry and return
/// space (GATHER-06), scope revalidation (GATHER-02) and request ids.</summary>
public sealed class CollectionSelectionTests
{
    private static readonly Guid Epoch = new Guid("0f0e0d0c-0b0a-0908-0706-050403020100");
    private static readonly SitePoint Worker = new SitePoint(0f, 30f, 0f);

    private static SourceObservation Source(
        CollectedResource resource, float x, float z, string session, int yield = 1,
        SourceAvailability availability = SourceAvailability.Available,
        SourceReachability reachability = SourceReachability.NotChecked, float y = 30f)
    {
        bool stone = resource == CollectedResource.Stone;
        return new SourceObservation(
            new SourceKey(stone ? "Pickable_Stone" : "Pickable_Branch", session, Epoch, new SitePoint(x, y, z)),
            stone ? NaturalSourceKind.LooseStone : NaturalSourceKind.Branch, resource, yield, availability, reachability, 1, 0f);
    }

    private static ResourceNeed Need(CollectedResource resource, int remaining, int carry = 50, int returnRoom = ResourceNeed.Unlimited) =>
        new ResourceNeed(resource, remaining, carry, returnRoom);

    private static SelectionResult Select(
        IReadOnlyList<SourceObservation> sources, IReadOnlyList<ResourceNeed> needs, SitePoint? returnPoint = null,
        Func<SourceKey, bool>? excluded = null) =>
        CollectionTargetSelector.Select(CollectionParameters.Default, sources, needs, Worker, returnPoint, excluded);

    [Fact]
    public void ItChoosesTheCheapestSourceOfANeededResource()
    {
        var sources = new[]
        {
            Source(CollectedResource.Stone, 20f, 0f, "a"),
            Source(CollectedResource.Stone, 5f, 0f, "b"),
            Source(CollectedResource.Wood, 8f, 0f, "c"),
        };

        SelectionResult result = Select(sources, new[] { Need(CollectedResource.Stone, 10), Need(CollectedResource.Wood, 10) });

        Assert.Equal(SelectionOutcome.Chosen, result.Outcome);
        Assert.Equal("b", result.Source!.Key.SessionId);
    }

    [Fact]
    public void ClimbingCostsMoreThanWalkingTheSameDistance()
    {
        var sources = new[]
        {
            Source(CollectedResource.Stone, 5f, 0f, "uphill", y: 36f),
            Source(CollectedResource.Stone, 6f, 0f, "flat"),
        };

        Assert.Equal("flat", Select(sources, new[] { Need(CollectedResource.Stone, 10) }).Source!.Key.SessionId);
    }

    [Fact]
    public void ASatisfiedResourceStopsWhileAnotherContinues()
    {
        var sources = new[]
        {
            Source(CollectedResource.Stone, 1f, 0f, "stone-near"),
            Source(CollectedResource.Wood, 25f, 0f, "wood-far"),
        };

        SelectionResult result = Select(sources, new[] { Need(CollectedResource.Stone, 0), Need(CollectedResource.Wood, 3) });

        Assert.Equal(SelectionOutcome.Chosen, result.Outcome);
        Assert.Equal("wood-far", result.Source!.Key.SessionId);
    }

    [Fact]
    public void WhenEveryNeedIsCoveredNothingIsChosenHoweverManySourcesWereSeen()
    {
        var sources = Enumerable.Range(0, 40).Select(index => Source(CollectedResource.Stone, index, 0f, "s" + index)).ToArray();

        SelectionResult result = Select(sources, new[] { Need(CollectedResource.Stone, 0) });

        Assert.Equal(SelectionOutcome.AllNeedsCovered, result.Outcome);
        Assert.Null(result.Source);
    }

    [Fact]
    public void APickThatWouldGiveMoreThanIsStillNeededIsNeverTaken()
    {
        var sources = new[] { Source(CollectedResource.Stone, 1f, 0f, "double", yield: 2) };

        SelectionResult result = Select(sources, new[] { Need(CollectedResource.Stone, 1) });

        Assert.Equal(SelectionOutcome.NoCandidates, result.Outcome);
        Assert.True(result.OvercollectionBlocked);
        Assert.Equal(SelectionOutcome.Chosen, Select(sources, new[] { Need(CollectedResource.Stone, 2) }).Outcome);
    }

    [Fact]
    public void CarryAndReturnLimitsAreReportedAsSuch()
    {
        var sources = new[] { Source(CollectedResource.Wood, 1f, 0f, "w") };

        Assert.Equal(SelectionOutcome.CarryFull, Select(sources, new[] { Need(CollectedResource.Wood, 5, carry: 0) }).Outcome);
        Assert.Equal(
            SelectionOutcome.NoReturnSpace, Select(sources, new[] { Need(CollectedResource.Wood, 5, returnRoom: 0) }).Outcome);

        // Carry is the more useful answer: delivering makes room for both.
        Assert.Equal(
            SelectionOutcome.CarryFull,
            Select(sources, new[] { Need(CollectedResource.Wood, 5, carry: 0, returnRoom: 0) }).Outcome);

        // Another resource that still fits is chosen instead of stopping.
        var mixed = new[] { Source(CollectedResource.Wood, 1f, 0f, "w"), Source(CollectedResource.Stone, 9f, 0f, "s") };
        SelectionResult result = Select(mixed, new[] { Need(CollectedResource.Wood, 5, carry: 0), Need(CollectedResource.Stone, 5) });
        Assert.Equal("s", result.Source!.Key.SessionId);
    }

    [Fact]
    public void OnlyAvailableReachableUnexcludedSourcesOfTheOrderAreConsidered()
    {
        var sources = new[]
        {
            Source(CollectedResource.Stone, 1f, 0f, "exhausted", availability: SourceAvailability.Exhausted),
            Source(CollectedResource.Stone, 1f, 1f, "inaccessible", availability: SourceAvailability.Inaccessible),
            Source(CollectedResource.Stone, 1f, 2f, "unknown", availability: SourceAvailability.Unknown),
            Source(CollectedResource.Stone, 1f, 3f, "unloaded", availability: SourceAvailability.Unloaded),
            Source(CollectedResource.Stone, 1f, 4f, "unreachable", reachability: SourceReachability.Unreachable),
            Source(CollectedResource.Stone, 1f, 5f, "excluded"),
            Source(CollectedResource.Wood, 1f, 6f, "not-ordered"),
            Source(CollectedResource.Stone, 30f, 0f, "the-one"),
        };

        SelectionResult result = Select(sources, new[] { Need(CollectedResource.Stone, 4) }, excluded: key => key.SessionId == "excluded");

        Assert.Equal("the-one", result.Source!.Key.SessionId);
        Assert.Equal(
            SelectionOutcome.NoCandidates,
            Select(sources.Take(7).ToArray(), new[] { Need(CollectedResource.Stone, 4) }, excluded: key => key.SessionId == "excluded").Outcome);
    }

    [Fact]
    public void TiesBreakTheSameWayWhateverTheInputOrder()
    {
        var sources = new List<SourceObservation>
        {
            Source(CollectedResource.Wood, 0f, 5f, "0000000000000001:00000009"),
            Source(CollectedResource.Stone, 5f, 0f, "0000000000000001:00000007"),
            Source(CollectedResource.Stone, -5f, 0f, "0000000000000001:00000003"),
            Source(CollectedResource.Wood, 0f, -5f, "0000000000000001:00000001"),
        };
        var needs = new[] { Need(CollectedResource.Stone, 5), Need(CollectedResource.Wood, 5) };

        var random = new Random(4);
        for (int round = 0; round < 25; round++)
        {
            List<SourceObservation> shuffled = sources.OrderBy(_ => random.Next()).ToList();
            SelectionResult result = Select(shuffled, needs);

            // Equal cost: the order's first resource, then the lowest session id.
            Assert.Equal("0000000000000001:00000003", result.Source!.Key.SessionId);
        }
    }

    [Fact]
    public void TheWalkBackToTheChestWeighsOnTheChoice()
    {
        var chest = new SitePoint(-10f, 30f, 0f);
        var sources = new[]
        {
            Source(CollectedResource.Stone, 6f, 0f, "away-from-chest"),
            Source(CollectedResource.Stone, -6.5f, 0f, "toward-chest"),
        };

        Assert.Equal("away-from-chest", Select(sources, new[] { Need(CollectedResource.Stone, 5) }).Source!.Key.SessionId);
        Assert.Equal("toward-chest", Select(sources, new[] { Need(CollectedResource.Stone, 5) }, chest).Source!.Key.SessionId);
    }

    [Theory]
    [InlineData(100f, 0f, 2f, 1000, 50)]
    [InlineData(100f, 90f, 2f, 1000, 5)]
    [InlineData(100f, 99f, 2f, 1000, 0)]
    [InlineData(100f, 100f, 2f, 1000, 0)]
    [InlineData(100f, 120f, 2f, 1000, 0)]
    [InlineData(100f, 0f, 2f, 7, 7)]
    [InlineData(100f, 0f, 0f, 1000, 0)]
    [InlineData(100f, 0f, -1f, 1000, 0)]
    [InlineData(100f, 0f, 2f, 0, 0)]
    [InlineData(40f, 0f, 2f, 1000, 20)]
    public void CarryRoomIsTheSmallerOfTheWeightBudgetAndTheRealFit(
        float budget, float carried, float unit, int fit, int expected)
    {
        Assert.Equal(expected, CarryPlanner.CarryRoomUnits(budget, carried, unit, fit));
    }

    [Fact]
    public void ReturnRoomIsWhatTheDestinationTakesBeyondTheLoad()
    {
        Assert.Equal(20, CarryPlanner.ReturnRoomUnits(30, 10));
        Assert.Equal(0, CarryPlanner.ReturnRoomUnits(5, 10));
        Assert.Equal(15, CarryPlanner.ReturnRoomUnits(15, 0));
    }

    [Fact]
    public void AHoldOrderWeighsEverythingAskedFor()
    {
        var quotas = new[] { new ResourceQuota(CollectedResource.Stone, 20), new ResourceQuota(CollectedResource.Wood, 30) };
        Assert.Equal(100f, CarryPlanner.QuotaWeight(quotas, _ => 2f));
        Assert.True(float.IsPositiveInfinity(CarryPlanner.QuotaWeight(quotas, resource => resource == CollectedResource.Wood ? 0f : 2f)));
    }

    // --- Scope checkpoint ----------------------------------------------------------------------------------

    private static WorkScope Scope(int revision = 11) =>
        new WorkScope(WorkScopeSource.HarvestDesignation, new SitePoint(50f, 30f, 50f), 20f, "harvest area", revision, Epoch);

    [Fact]
    public void AScopeIsValidOnlyWhileItsSourceRevisionAndWorldLoadHold()
    {
        WorkScope scope = Scope();
        Func<SitePoint, bool> loaded = _ => true;

        Assert.Equal(ScopeCheck.Valid, ScopeCheckpoint.Revalidate(scope, new ScopeObservation(true, 11, Epoch), loaded));
        Assert.Equal(ScopeCheck.Changed, ScopeCheckpoint.Revalidate(scope, new ScopeObservation(true, 12, Epoch), loaded));
        Assert.Equal(ScopeCheck.Invalid, ScopeCheckpoint.Revalidate(scope, new ScopeObservation(false, 11, Epoch), loaded));
        Assert.Equal(ScopeCheck.Invalid, ScopeCheckpoint.Revalidate(scope, new ScopeObservation(true, 11, Guid.NewGuid()), loaded));
        Assert.Equal(ScopeCheck.Invalid, ScopeCheckpoint.Revalidate(scope, new ScopeObservation(true, 11, Guid.Empty), loaded));
        Assert.Equal(ScopeCheck.Unloaded, ScopeCheckpoint.Revalidate(scope, new ScopeObservation(true, 11, Epoch), _ => false));
        Assert.Equal(ScopeCheck.Invalid, ScopeCheckpoint.Revalidate(null!, new ScopeObservation(true, 11, Epoch), loaded));

        // Invalid wins over changed, changed over unloaded: the most permanent
        // problem is the one reported.
        Assert.Equal(ScopeCheck.Invalid, ScopeCheckpoint.Revalidate(scope, new ScopeObservation(false, 99, Epoch), _ => false));
        Assert.Equal(ScopeCheck.Changed, ScopeCheckpoint.Revalidate(scope, new ScopeObservation(true, 99, Epoch), _ => false));
    }

    [Fact]
    public void APartlyLoadedScopeIsNotUnloaded()
    {
        WorkScope scope = Scope();
        Assert.True(ScopeCheckpoint.AnyLoaded(scope, point => point.X > 60f));
        Assert.True(ScopeCheckpoint.AnyLoaded(scope, point => point.X == 50f && point.Z == 50f));
        Assert.False(ScopeCheckpoint.AnyLoaded(scope, point => point.X > 100f));
        Assert.False(ScopeCheckpoint.AnyLoaded(scope, null!));
    }

    [Fact]
    public void RevisionsAreStableAndSensitiveToWhatTheyDescribe()
    {
        var bed = new SitePoint(-36.8f, 84.125f, 23.9f);
        Assert.Equal(ScopeRevision.ForAnchor("bed", bed), ScopeRevision.ForAnchor("bed", new SitePoint(-36.8f, 84.125f, 23.9f)));
        Assert.NotEqual(ScopeRevision.ForAnchor("bed", bed), ScopeRevision.ForAnchor("start", bed));
        Assert.NotEqual(ScopeRevision.ForAnchor("bed", bed), ScopeRevision.ForAnchor("bed", new SitePoint(-36.8f, 84.125f, 23.91f)));
        Assert.Equal(ScopeRevision.ForArea(bed, 20f), ScopeRevision.ForArea(bed, 20f));
        Assert.NotEqual(ScopeRevision.ForArea(bed, 20f), ScopeRevision.ForArea(bed, 21f));
        Assert.NotEqual(ScopeRevision.ForArea(bed, 20f), ScopeRevision.ForAnchor("bed", bed));
    }

    // --- Request ids ---------------------------------------------------------------------------------------

    [Fact]
    public void RequestIdsAreUniqueValidSlugsEvenForLongOrders()
    {
        var ids = new CollectionRequestIds(Epoch);
        var longOrder = new OrderId("a-very-long-collection-order-name-that-goes-on");
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int index = 0; index < 500; index++)
        {
            RequestId take = ids.Next(new OrderId("collect-1"), "take");
            RequestId deposit = ids.Next(longOrder, "deposit");
            Assert.True(seen.Add(take.Value));
            Assert.True(seen.Add(deposit.Value));
            Assert.True(SettlementSlug.IsValid(take.Value), take.Value);
            Assert.True(SettlementSlug.IsValid(deposit.Value), deposit.Value);
        }

        Assert.StartsWith("collect-1-0f0e0d-t", ids.Next(new OrderId("collect-1"), "take").Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => ids.Next(new OrderId("collect-1"), "grant"));
        Assert.Throws<ArgumentException>(() => new CollectionRequestIds(Guid.Empty));
    }

    [Fact]
    public void TwoWorldLoadsNeverMintTheSameId()
    {
        var first = new CollectionRequestIds(Epoch);
        var second = new CollectionRequestIds(new Guid("a1b2c3d4-0000-0000-0000-000000000000"));
        var order = new OrderId("collect-1");

        Assert.NotEqual(first.Next(order, "take").Value, second.Next(order, "take").Value);
    }

    [Fact]
    public void TwoLongOrdersSharingAPrefixGetDifferentIds()
    {
        var ids = new CollectionRequestIds(Epoch);
        string a = ids.Next(new OrderId("collection-order-for-thorstein-number-one"), "take").Value;
        string b = ids.Next(new OrderId("collection-order-for-thorstein-number-two"), "take").Value;

        Assert.NotEqual(a.Substring(0, 20), b.Substring(0, 20));
    }
}
