using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Tools;

namespace Shared.Settlement.Tests;

/// <summary>Settling an interrupted handover, the way a person says it went.
///
/// This lives in the shared layer because it was written in the Foreman adapter,
/// where nothing could reach it — the same mistake the two-file write rule made
/// before it, and the same fix. Its whole job is to keep a record and a ledger
/// agreeing about a real tool, and that is exactly the kind of logic that should
/// not be reachable only through the game.</summary>
public sealed class ToolResolutionTests
{
    private static readonly WorkerId Thorstein = new("thorstein");
    private static readonly OrderId Job = new("handover");

    private static readonly SettlementScope Scope =
        new(worldId: 99, settlement: new SettlementId("resolve-camp"));

    private static readonly ToolSpecimen Axe =
        new(ToolKind.Axe, "$item_axe_bronze", 1, 80f, 2);

    private static RequestId Transaction => RequestId.For(Job, 0);

    private static (ToolLedger Ledger, SettlementJournal Journal) Interrupted()
    {
        var journal = new SettlementJournal(Scope);
        journal.Append(
            JournalEntryKind.ToolHandoverStarted, default, Transaction,
            worker: Thorstein, tool: Axe);

        ToolLedger ledger = journal.Replay().Tools;
        Assert.True(ledger.HasUncertainHandover(Thorstein));
        return (ledger, journal);
    }

    private static bool Works() => true;

    private static bool Fails() => false;

    private static bool Throws() => throw new IOException("the disk said no");

    // ------------------------------------------------------------------
    // Only a person answers, and only from unknown
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnAnswerIsRecordedAndSettlesTheHolding(bool workerHasIt)
    {
        (ToolLedger ledger, SettlementJournal journal) = Interrupted();

        Assert.True(ToolResolution.TryRecord(
            Transaction, workerHasIt, ledger, journal, Works, out string message));

        Assert.Contains("Recorded", message);
        Assert.False(ledger.HasUncertainHandover(Thorstein));

        Assert.True(ledger.TryGet(Transaction, out ToolHolding holding));
        Assert.Equal(
            workerHasIt ? ToolHoldingState.Held : ToolHoldingState.Returned,
            holding.State);

        // And the answer is in the record, not only in memory.
        Assert.Contains(
            journal.Entries,
            e => e.Kind == (workerHasIt
                ? JournalEntryKind.ToolResolvedToWorker
                : JournalEntryKind.ToolResolvedToPlayer));
    }

    [Fact]
    public void AHandoverThatIsNotWaitingOnAnAnswerRefusesOne()
    {
        var journal = new SettlementJournal(Scope);
        journal.Append(
            JournalEntryKind.ToolHandoverStarted, default, Transaction,
            worker: Thorstein, tool: Axe);
        journal.Append(
            JournalEntryKind.ToolHandoverFinished, default, Transaction,
            worker: Thorstein, tool: Axe);

        ToolLedger ledger = journal.Replay().Tools;
        int before = journal.Entries.Count;

        Assert.False(ToolResolution.TryRecord(
            Transaction, true, ledger, journal, Works, out string message));

        Assert.Contains("not waiting", message);
        Assert.Equal(before, journal.Entries.Count);
    }

    [Fact]
    public void AnUnknownHandoverIsRefusedRatherThanInvented()
    {
        (ToolLedger ledger, SettlementJournal journal) = Interrupted();

        Assert.False(ToolResolution.TryRecord(
            RequestId.For(Job, 7), true, ledger, journal, Works, out string message));

        Assert.Contains("no record", message);
    }

    [Fact]
    public void AReadOnlyRecordRefusesAnAnswerRatherThanLosingIt()
    {
        (ToolLedger ledger, SettlementJournal journal) = Interrupted();
        journal.MarkReadOnly();

        Assert.False(ToolResolution.TryRecord(
            Transaction, true, ledger, journal, Works, out string message));

        Assert.Contains("could not be fully read", message);
        Assert.True(ledger.HasUncertainHandover(Thorstein));
    }

    // ------------------------------------------------------------------
    // The ledger is settled only once the record is safe
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnAnswerThatCouldNotBeWrittenSettlesNothing(bool workerHasIt)
    {
        // The rollback this replaces was asymmetric and silently failed for one
        // of the two answers: "you have it" leaves the holding Returned, and
        // MarkUncertain refuses to move a settled holding -- correctly -- so the
        // undo only ever worked for "he has it". Settling after the record is
        // safe removes the need for an undo at all.
        (ToolLedger ledger, SettlementJournal journal) = Interrupted();
        int before = journal.Entries.Count;

        Assert.False(ToolResolution.TryRecord(
            Transaction, workerHasIt, ledger, journal, Fails, out string message));

        Assert.Contains("nothing has been settled", message);

        // Still unknown, still exactly one holding, and the record is unchanged.
        Assert.True(ledger.HasUncertainHandover(Thorstein));
        Assert.True(ledger.TryGet(Transaction, out ToolHolding holding));
        Assert.Equal(ToolHoldingState.Uncertain, holding.State);
        Assert.Equal(before, journal.Entries.Count);
    }

    [Fact]
    public void ASaveThatThrowsIsTreatedExactlyAsOneThatSaidNo()
    {
        (ToolLedger ledger, SettlementJournal journal) = Interrupted();
        int before = journal.Entries.Count;

        Assert.False(ToolResolution.TryRecord(
            Transaction, true, ledger, journal, Throws, out string message));

        Assert.Contains("nothing has been settled", message);
        Assert.True(ledger.HasUncertainHandover(Thorstein));
        Assert.Equal(before, journal.Entries.Count);
    }

    [Fact]
    public void AFailedAnswerCanSimplyBeGivenAgain()
    {
        // The state after a failed write has to be the state before it, or the
        // advice to "try again" is wrong.
        (ToolLedger ledger, SettlementJournal journal) = Interrupted();

        Assert.False(ToolResolution.TryRecord(
            Transaction, true, ledger, journal, Fails, out _));

        Assert.True(ToolResolution.TryRecord(
            Transaction, true, ledger, journal, Works, out string second));

        Assert.Contains("Recorded", second);
        Assert.Single(journal.Entries.Where(
            e => e.Kind == JournalEntryKind.ToolResolvedToWorker));
    }

    [Fact]
    public void NothingIsSettledWithoutTheThingsItNeeds()
    {
        (ToolLedger ledger, SettlementJournal journal) = Interrupted();

        Assert.False(ToolResolution.TryRecord(Transaction, true, null!, journal, Works, out _));
        Assert.False(ToolResolution.TryRecord(Transaction, true, ledger, null!, Works, out _));
        Assert.False(ToolResolution.TryRecord(Transaction, true, ledger, journal, null!, out _));
        Assert.False(ToolResolution.TryRecord(default, true, ledger, journal, Works, out _));

        Assert.True(ledger.HasUncertainHandover(Thorstein));
    }
}
