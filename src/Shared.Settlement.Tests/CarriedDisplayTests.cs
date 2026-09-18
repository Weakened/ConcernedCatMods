using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;

namespace Shared.Settlement.Tests;

/// <summary>CF-SET-007 (#284): what a worker is seen holding is decided by the
/// ledger and by nothing else, so it cannot become a decoration that disagrees
/// with it.</summary>
public sealed class CarriedDisplayTests
{
    private static readonly OrderId Order = new OrderId("order-1");

    private sealed class LedgerView : IMaterialCustodyView
    {
        private readonly Dictionary<CollectedResource, int> _worker = new();

        public int Revision { get; private set; }

        public int Reads { get; private set; }

        public bool Throws { get; set; }

        public LedgerView Carrying(CollectedResource resource, int count)
        {
            _worker[resource] = count;
            Revision++;
            return this;
        }

        public int CountAt(OrderId order, CustodyPlace place, CollectedResource resource)
        {
            Reads++;
            if (Throws)
            {
                throw new InvalidOperationException("the ledger is unreadable");
            }

            // Anywhere but the worker's own hands is not what he is holding.
            return place == CustodyPlace.Worker && _worker.TryGetValue(resource, out int count)
                ? count
                : 0;
        }

        public ResourceProgress ProgressFor(CollectionOrderDefinition order, CollectedResource resource) =>
            throw new NotSupportedException("the display must not need progress");

        public bool HasUncertainTransfer(OrderId order) =>
            throw new NotSupportedException("the display must not need transfer state");
    }

    [Fact]
    public void AnEmptyLedgerIsEmptyHands()
    {
        CarriedDisplay display = CarriedDisplay.For(new LedgerView(), Order);

        Assert.False(display.ShowsSomething);
        Assert.Equal(CollectedResource.Unspecified, display.Resource);
        Assert.Equal(0, display.Count);
        Assert.Equal("nothing", display.ToString());
    }

    [Fact]
    public void OneResourceIsTheOneHeHolds()
    {
        // Not a [Theory]: CollectedResource is internal to the shared source
        // this project compiles, so it cannot appear in a public signature.
        foreach (CollectedResource resource in CollectedResources.All)
        {
            CarriedDisplay display = CarriedDisplay.For(new LedgerView().Carrying(resource, 7), Order);

            Assert.True(display.ShowsSomething);
            Assert.Equal(resource, display.Resource);
            Assert.Equal(7, display.Count);
        }
    }

    [Fact]
    public void WithTwoHeHoldsTheOneHeHasMostOf()
    {
        var view = new LedgerView().Carrying(CollectedResource.Stone, 3).Carrying(CollectedResource.Wood, 11);

        Assert.Equal(CollectedResource.Wood, CarriedDisplay.For(view, Order).Resource);
    }

    [Fact]
    public void ATieIsBrokenTheSameWayEveryTime()
    {
        // Not because stone is more important, but because a hand that flickered
        // between a stone and a log whenever the ledger revision changed would
        // be worse than one holding neither.
        var view = new LedgerView().Carrying(CollectedResource.Wood, 5).Carrying(CollectedResource.Stone, 5);

        Assert.Equal(CollectedResource.Stone, CarriedDisplay.For(view, Order).Resource);
        Assert.Equal(CollectedResource.Stone, CarriedDisplay.For(view, Order).Resource);
    }

    [Fact]
    public void OnlyWhatIsInHisOwnHandsCounts()
    {
        // The view answers 0 for every place but Worker, so a ledger holding
        // material in the cart, at the source or in the chest shows nothing.
        var view = new LedgerView();

        Assert.False(CarriedDisplay.For(view, Order).ShowsSomething);
        Assert.True(view.Reads > 0);
    }

    [Fact]
    public void ANegativeCountIsNotSomethingToHold()
    {
        var view = new LedgerView().Carrying(CollectedResource.Stone, -3);

        Assert.False(CarriedDisplay.For(view, Order).ShowsSomething);
    }

    [Fact]
    public void NoLedgerIsEmptyHandsRatherThanAGuess()
    {
        Assert.False(CarriedDisplay.For(null, Order).ShowsSomething);
    }

    [Fact]
    public void ALedgerThatThrowsIsEmptyHandsRatherThanAnException()
    {
        var view = new LedgerView().Carrying(CollectedResource.Stone, 4);
        view.Throws = true;

        Assert.False(CarriedDisplay.For(view, Order).ShowsSomething);
    }

    [Fact]
    public void TheDecisionOnlyEverReadsAndOnlyEverReadsTheWorkersPlace()
    {
        // The whole point: a presentation cannot mutate a ledger it can only
        // count from. ProgressFor and HasUncertainTransfer throw if touched.
        var view = new LedgerView().Carrying(CollectedResource.Wood, 2);
        int revisionBefore = view.Revision;

        CarriedDisplay.For(view, Order);

        Assert.Equal(revisionBefore, view.Revision);
    }

    [Fact]
    public void EveryCollectableResourceIsConsidered()
    {
        // If a third resource is added, this fails rather than the new one
        // silently never appearing in a worker's hand.
        foreach (CollectedResource resource in CollectedResources.All)
        {
            CarriedDisplay display = CarriedDisplay.For(new LedgerView().Carrying(resource, 1), Order);
            Assert.Equal(resource, display.Resource);
        }

        Assert.DoesNotContain(CollectedResource.Unspecified, CollectedResources.All);
    }

    [Fact]
    public void TwoDisplaysOfTheSameThingAreTheSameValue()
    {
        // The adapter skips the engine call when the answer has not changed, so
        // equality is load-bearing rather than decorative.
        var view = new LedgerView().Carrying(CollectedResource.Stone, 9);

        CarriedDisplay first = CarriedDisplay.For(view, Order);
        CarriedDisplay second = CarriedDisplay.For(view, Order);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, CarriedDisplay.Nothing);
    }

    [Fact]
    public void ADisplayDescribesItselfForADiagnostic()
    {
        CarriedDisplay display = CarriedDisplay.For(new LedgerView().Carrying(CollectedResource.Wood, 12), Order);

        Assert.Equal("12 Wood", display.ToString());
    }
}
