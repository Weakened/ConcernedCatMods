using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Custody;

namespace Shared.Settlement.Tests;

/// <summary>R2 M4: the rule that classifies one take of a drop the order's own
/// pick spawned. It is the place a single inverted condition would mint or lose
/// a unit, so it is decided game-free, from what was measured, and pinned here.
/// </summary>
public sealed class CollectionTakeTests
{
    private static TransferOutcome Classify(
        int expected, bool threw, bool returnedTrue, int before, int after, bool dropGone, out int accepted)
    {
        TransferOutcome outcome = TakeClassification.Classify(
            new TakeObservation(expected, threw, returnedTrue, before, after, dropGone), out accepted, out string evidence);
        Assert.False(string.IsNullOrWhiteSpace(evidence));
        return outcome;
    }

    [Fact]
    public void OnlyAnInventoryThatGainedTheStackWithTheDropGoneIsCompleted()
    {
        Assert.Equal(
            TransferOutcome.Completed,
            Classify(expected: 1, threw: false, returnedTrue: true, before: 4, after: 5, dropGone: true, out int accepted));
        Assert.Equal(1, accepted);

        Assert.Equal(
            TransferOutcome.Completed,
            Classify(expected: 3, threw: false, returnedTrue: true, before: 0, after: 3, dropGone: true, out accepted));
        Assert.Equal(3, accepted);
    }

    [Fact]
    public void TheGamesOwnBooleanIsNeverTheVerdict()
    {
        // "It says it took it" with nothing arriving is the regression this
        // rule exists to stop: it is uncertain, never completed.
        Assert.Equal(
            TransferOutcome.Uncertain,
            Classify(expected: 1, threw: false, returnedTrue: true, before: 4, after: 4, dropGone: true, out int accepted));
        Assert.Equal(0, accepted);

        // And the reverse: it says it refused while the inventory grew.
        Assert.Equal(
            TransferOutcome.Uncertain,
            Classify(expected: 1, threw: false, returnedTrue: false, before: 4, after: 5, dropGone: true, out accepted));
        Assert.Equal(1, accepted);
    }

    [Fact]
    public void AnItemThatIsInHisHandsAndStillInTheWorldIsUncertain()
    {
        Assert.Equal(
            TransferOutcome.Uncertain,
            Classify(expected: 1, threw: false, returnedTrue: true, before: 0, after: 1, dropGone: false, out int accepted));
        Assert.Equal(1, accepted);
    }

    [Fact]
    public void OnlyAPlainRefusalThatChangedNothingIsRefused()
    {
        Assert.Equal(
            TransferOutcome.Refused,
            Classify(expected: 1, threw: false, returnedTrue: false, before: 4, after: 4, dropGone: false, out int accepted));
        Assert.Equal(0, accepted);

        // The drop went away while the game said no: that is not "nothing
        // happened".
        Assert.Equal(
            TransferOutcome.Uncertain,
            Classify(expected: 1, threw: false, returnedTrue: false, before: 4, after: 4, dropGone: true, out accepted));
        Assert.Equal(0, accepted);
    }

    [Fact]
    public void AThrowIsAlwaysUncertainHoweverItLooks()
    {
        Assert.Equal(
            TransferOutcome.Uncertain,
            Classify(expected: 1, threw: true, returnedTrue: true, before: 0, after: 1, dropGone: true, out int accepted));
        Assert.Equal(1, accepted);

        Assert.Equal(
            TransferOutcome.Uncertain,
            Classify(expected: 1, threw: true, returnedTrue: false, before: 0, after: 0, dropGone: false, out accepted));
        Assert.Equal(0, accepted);
    }

    [Theory]
    [InlineData(2, 0, 1)]
    [InlineData(2, 0, 3)]
    [InlineData(1, 0, 2)]
    public void AnInventoryThatGainedSomethingOtherThanTheStackIsUncertainAndCreditsOnlyWhatArrived(
        int expected, int before, int after)
    {
        Assert.Equal(
            TransferOutcome.Uncertain,
            Classify(expected, threw: false, returnedTrue: true, before: before, after: after, dropGone: true, out int accepted));
        Assert.Equal(after - before, accepted);
    }

    [Fact]
    public void ATakeOfNothingIsNeverCompleted()
    {
        Assert.Equal(
            TransferOutcome.Uncertain,
            Classify(expected: 0, threw: false, returnedTrue: true, before: 3, after: 3, dropGone: true, out int accepted));
        Assert.Equal(0, accepted);
    }
}
