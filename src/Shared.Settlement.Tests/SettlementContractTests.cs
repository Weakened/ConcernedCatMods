using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Orders;

namespace Shared.Settlement.Tests;

/// <summary>CF-SET-001: the game-free half of the settlement runtime.
///
/// The cases that matter here are all failure cases. A settlement that works
/// when nothing goes wrong is not interesting; one that neither conjures nor
/// loses material when the process dies between two writes is the entire
/// point, and that is what most of these exercise.</summary>
public sealed class SettlementContractTests : IDisposable
{
    private readonly string _root;
    private readonly JournalStore _store;

    private static readonly OrderId Cottage = new("cottage-1");
    private static readonly SettlementScope Scope =
        new(worldId: 4242, settlement: new SettlementId("first-camp"));

    public SettlementContractTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(), "cc-settlement-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _store = new JournalStore(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // A locked temp file must never fail the suite.
        }
    }

    private static MaterialStack[] Wood(int count = 20)
    {
        return new[] { new MaterialStack("Wood", count) };
    }

    private static Reservation Held(int step, int count = 20, string container = "chest-a")
    {
        return new Reservation(RequestId.For(Cottage, step), Cottage, container, Wood(count));
    }

    // ------------------------------------------------------------------
    // The order state machine
    // ------------------------------------------------------------------

    [Fact]
    public void Orders_AdvanceThroughTheWholeLifecycle()
    {
        OrderState state = OrderState.Draft;
        foreach (OrderTransition transition in new[]
        {
            OrderTransition.Approve,
            OrderTransition.Reserve,
            OrderTransition.BeginGathering,
            OrderTransition.BeginBuilding,
            OrderTransition.Complete,
        })
        {
            Assert.Equal(
                OrderTransitionOutcome.Advanced,
                OrderStateMachine.TryApply(state, transition, out state));
        }

        Assert.Equal(OrderState.Completed, state);
    }

    [Fact]
    public void Orders_AreTotal_EveryStatePlusEveryTransitionHasAnAnswer()
    {
        foreach (OrderState state in Enum.GetValues(typeof(OrderState)))
        {
            foreach (OrderTransition transition in Enum.GetValues(typeof(OrderTransition)))
            {
                OrderTransitionOutcome outcome =
                    OrderStateMachine.TryApply(state, transition, out OrderState next);
                Assert.True(Enum.IsDefined(typeof(OrderTransitionOutcome), outcome));
                Assert.True(OrderStateMachine.IsKnown(next));
            }
        }
    }

    [Fact]
    public void Orders_NeverWalkBackwards()
    {
        foreach (OrderState state in Enum.GetValues(typeof(OrderState)))
        {
            foreach (OrderTransition transition in Enum.GetValues(typeof(OrderTransition)))
            {
                OrderStateMachine.TryApply(state, transition, out OrderState next);
                Assert.True(
                    OrderStateMachine.Rank(next) >= OrderStateMachine.Rank(state),
                    $"{state} + {transition} produced {next}, which ranks lower.");
            }
        }
    }

    [Fact]
    public void Orders_AReplayedEarlierTransitionIsSatisfiedNotApplied()
    {
        OrderStateMachine.TryApply(OrderState.Building, OrderTransition.Reserve, out OrderState next);

        Assert.Equal(OrderState.Building, next);
        Assert.Equal(
            OrderTransitionOutcome.AlreadySatisfied,
            OrderStateMachine.TryApply(OrderState.Building, OrderTransition.Reserve, out _));
    }

    [Fact]
    public void Orders_AFinishedCottageCannotBeCancelled()
    {
        Assert.Equal(
            OrderTransitionOutcome.Rejected,
            OrderStateMachine.TryApply(OrderState.Completed, OrderTransition.Cancel, out _));
    }

    [Fact]
    public void Orders_RepairOutranksEverythingIncludingCancellation()
    {
        // An order whose material state is unknown must not be quietly tidied
        // away by a cancel.
        Assert.Equal(
            OrderTransitionOutcome.Advanced,
            OrderStateMachine.TryApply(OrderState.Cancelled, OrderTransition.FlagForRepair, out OrderState next));
        Assert.Equal(OrderState.NeedsRepair, next);

        Assert.Equal(
            OrderTransitionOutcome.Rejected,
            OrderStateMachine.TryApply(OrderState.NeedsRepair, OrderTransition.Cancel, out _));
    }

    [Fact]
    public void Orders_NothingAutomaticGetsAnOrderOutOfRepair()
    {
        foreach (OrderTransition transition in Enum.GetValues(typeof(OrderTransition)))
        {
            OrderStateMachine.TryApply(OrderState.NeedsRepair, transition, out OrderState next);
            Assert.Equal(OrderState.NeedsRepair, next);
        }
    }

    // ------------------------------------------------------------------
    // Custody
    // ------------------------------------------------------------------

    [Fact]
    public void Custody_ADuplicateRequestReservesNothingExtra()
    {
        var ledger = new CustodyLedger();

        Assert.Equal(CustodyOutcome.Applied, ledger.Reserve(Held(0)));
        Assert.Equal(CustodyOutcome.AlreadySatisfied, ledger.Reserve(Held(0, count: 999)));

        Assert.Single(ledger.Reservations);
        Assert.Equal(20, ledger.Totals(ReservationState.Held)["Wood"]);
    }

    [Fact]
    public void Custody_ARefundCannotBeTakenTwice()
    {
        var ledger = new CustodyLedger();
        ledger.Reserve(Held(0));

        Assert.Equal(CustodyOutcome.Applied, ledger.Refund(RequestId.For(Cottage, 0)));
        Assert.Equal(CustodyOutcome.AlreadySatisfied, ledger.Refund(RequestId.For(Cottage, 0)));
        Assert.Empty(ledger.Totals(ReservationState.Held));
    }

    [Fact]
    public void Custody_RefundedMaterialCannotThenBeCommitted()
    {
        var ledger = new CustodyLedger();
        ledger.Reserve(Held(0));
        ledger.Refund(RequestId.For(Cottage, 0));

        // The exact double-spend this type exists to prevent.
        Assert.Equal(CustodyOutcome.Rejected, ledger.Commit(RequestId.For(Cottage, 0)));
    }

    [Fact]
    public void Custody_AnUnknownRequestIsRefusedRatherThanInvented()
    {
        var ledger = new CustodyLedger();

        Assert.Equal(CustodyOutcome.Rejected, ledger.Commit(RequestId.For(Cottage, 7)));
        Assert.Equal(CustodyOutcome.Rejected, ledger.Refund(RequestId.For(Cottage, 7)));
    }

    [Fact]
    public void Custody_CancellingReturnsOnlyWhatIsStillHeld()
    {
        var ledger = new CustodyLedger();
        ledger.Reserve(Held(0));
        ledger.Reserve(Held(1));
        ledger.Commit(RequestId.For(Cottage, 0));

        IReadOnlyList<Reservation> owed = ledger.HeldFor(Cottage);

        Assert.Single(owed);
        Assert.Equal(RequestId.For(Cottage, 1), owed[0].Request);
    }

    [Fact]
    public void Custody_RefundsGoBackToTheContainerTheyCameFrom()
    {
        var ledger = new CustodyLedger();
        ledger.Reserve(Held(0, container: "chest-a"));
        ledger.Reserve(Held(1, container: "chest-b"));

        IReadOnlyList<Reservation> owed = ledger.HeldFor(Cottage);

        Assert.Equal(new[] { "chest-a", "chest-b" }, owed.Select(r => r.Container).ToArray());
    }

    [Fact]
    public void Custody_AnEmptyReservationIsRefusedAtConstruction()
    {
        Assert.Throws<ArgumentException>(() =>
            new Reservation(RequestId.For(Cottage, 0), Cottage, "chest-a", Array.Empty<MaterialStack>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MaterialStack("Wood", 0));
    }

    // ------------------------------------------------------------------
    // Replay
    // ------------------------------------------------------------------

    [Fact]
    public void Replay_OfACleanRunReachesTheExpectedState()
    {
        SettlementJournal journal = BuildJournalThroughCommit(finishTheCommit: true);

        ReplayResult result = journal.Replay();

        Assert.False(result.NeedsRepair);
        Assert.Equal(OrderState.Building, result.StateOf(Cottage));
        Assert.Equal(20, result.Ledger.Totals(ReservationState.Committed)["Wood"]);
    }

    [Fact]
    public void Replay_IsIdempotent()
    {
        SettlementJournal journal = BuildJournalThroughCommit(finishTheCommit: true);

        ReplayResult first = journal.Replay();
        ReplayResult second = journal.Replay();

        Assert.Equal(first.StateOf(Cottage), second.StateOf(Cottage));
        Assert.Equal(first.Ledger.Reservations.Count, second.Ledger.Reservations.Count);
        Assert.Equal(
            first.Ledger.Totals(ReservationState.Committed),
            second.Ledger.Totals(ReservationState.Committed));
    }

    [Fact]
    public void Replay_AStartedCommitWithNoFinishIsNeitherSpentNorReturned()
    {
        // The process died between consuming and placing. Both available
        // assumptions are wrong in one direction: one conjures a piece that may
        // not exist, the other returns material that may already be a wall.
        SettlementJournal journal = BuildJournalThroughCommit(finishTheCommit: false);

        ReplayResult result = journal.Replay();

        Assert.True(result.NeedsRepair);
        Assert.Equal(OrderState.NeedsRepair, result.StateOf(Cottage));
        Assert.True(result.Ledger.HasUncertainCustody);
        Assert.Empty(result.Ledger.Totals(ReservationState.Committed));
        Assert.Empty(result.Ledger.Totals(ReservationState.Refunded));
        Assert.Empty(result.Ledger.Totals(ReservationState.Held));
    }

    [Fact]
    public void Replay_TheRepairMessageNamesTheOrderAndTheRequest()
    {
        ReplayResult result = BuildJournalThroughCommit(finishTheCommit: false).Replay();

        string message = Assert.Single(result.Repairs);
        Assert.Contains(Cottage.Value, message);
        Assert.Contains(RequestId.For(Cottage, 0).Value, message);
        Assert.Contains("paused", message);
    }

    [Fact]
    public void Replay_ReinterpretingAnUncertainCommitIsNotSomethingThisBuildWillDo()
    {
        ReplayResult result = BuildJournalThroughCommit(finishTheCommit: false).Replay();
        RequestId request = RequestId.For(Cottage, 0);

        Assert.Equal(CustodyOutcome.Rejected, result.Ledger.Commit(request));
        Assert.Equal(CustodyOutcome.Rejected, result.Ledger.Refund(request));
    }

    [Fact]
    public void Replay_TwoSettlementsDoNotSeeEachOther()
    {
        var other = new SettlementScope(4242, new SettlementId("second-camp"));

        SettlementJournal first = BuildJournalThroughCommit(finishTheCommit: true);
        var second = new SettlementJournal(other);
        second.Append(JournalEntryKind.OrderTransition, new OrderId("shed-1"), transition: OrderTransition.Approve);

        Assert.NotEqual(_store.ResolvePath(Scope), _store.ResolvePath(other));
        Assert.Empty(second.Replay().Ledger.Reservations);
        Assert.Equal(OrderState.Draft, second.Replay().StateOf(Cottage));
        Assert.NotEqual(OrderState.Draft, first.Replay().StateOf(Cottage));
    }

    // ------------------------------------------------------------------
    // Persistence
    // ------------------------------------------------------------------

    [Fact]
    public void Persistence_RoundTripsEveryEntry()
    {
        SettlementJournal journal = BuildJournalThroughCommit(finishTheCommit: true);
        Assert.True(_store.Save(journal).Saved);

        JournalStore.LoadReport report = _store.Load(Scope);

        Assert.Equal(JournalLoadOutcome.Loaded, report.Outcome);
        Assert.False(report.ReadOnly);
        Assert.Equal(journal.Entries.Count, report.Journal.Entries.Count);
        Assert.Equal(
            journal.Replay().StateOf(Cottage), report.Journal.Replay().StateOf(Cottage));
    }

    [Fact]
    public void Persistence_SurvivesARestartAtEveryTransition()
    {
        var steps = new (JournalEntryKind Kind, OrderTransition Transition)[]
        {
            (JournalEntryKind.OrderTransition, OrderTransition.Approve),
            (JournalEntryKind.OrderTransition, OrderTransition.Reserve),
            (JournalEntryKind.OrderTransition, OrderTransition.BeginGathering),
            (JournalEntryKind.OrderTransition, OrderTransition.BeginBuilding),
        };

        for (int cut = 0; cut <= steps.Length; cut++)
        {
            string root = Path.Combine(_root, "cut" + cut);
            var store = new JournalStore(root);
            var journal = new SettlementJournal(Scope);

            for (int index = 0; index < cut; index++)
            {
                journal.Append(steps[index].Kind, Cottage, transition: steps[index].Transition);
                Assert.True(store.Save(journal).Saved);
            }

            // "Restart" here: reload from disk and confirm the state matches
            // what was written, with nothing invented for the steps that never
            // happened.
            ReplayResult reloaded = store.Load(Scope).Journal.Replay();
            ReplayResult live = journal.Replay();
            Assert.Equal(live.StateOf(Cottage), reloaded.StateOf(Cottage));
            Assert.False(reloaded.NeedsRepair);
        }
    }

    [Fact]
    public void Persistence_AJournalFromANewerBuildIsLeftAloneAndRefusesWrites()
    {
        File.WriteAllLines(_store.ResolvePath(Scope), new[]
        {
            "v\t99\t" + Scope.ToStorageKey(),
        });

        JournalStore.LoadReport report = _store.Load(Scope);

        Assert.Equal(JournalLoadOutcome.UnsupportedSchema, report.Outcome);
        Assert.True(report.ReadOnly);
        Assert.NotNull(report.Notice);
        Assert.False(_store.Save(report.Journal, report.ReadOnly).Saved);
    }

    [Fact]
    public void Persistence_AJournalFromAnotherSettlementIsNeverOverwritten()
    {
        var foreign = new SettlementScope(4242, new SettlementId("someone-elses-camp"));
        File.WriteAllLines(_store.ResolvePath(Scope), new[]
        {
            "v\t1\t" + foreign.ToStorageKey(),
        });

        JournalStore.LoadReport report = _store.Load(Scope);

        Assert.Equal(JournalLoadOutcome.ScopeMismatch, report.Outcome);
        Assert.True(report.ReadOnly);
        Assert.False(_store.Save(report.Journal, report.ReadOnly).Saved);
    }

    [Fact]
    public void Persistence_ADamagedLineIsKeptRatherThanRewrittenAway()
    {
        SettlementJournal journal = BuildJournalThroughCommit(finishTheCommit: true);
        _store.Save(journal);

        string path = _store.ResolvePath(Scope);
        var lines = new List<string>(File.ReadAllLines(path));
        lines.Insert(3, "e\tnot-a-number\tgarbage");
        File.WriteAllLines(path, lines);

        JournalStore.LoadReport report = _store.Load(Scope);

        Assert.Equal(JournalLoadOutcome.LoadedWithSkippedLines, report.Outcome);
        Assert.Equal(1, report.SkippedLines);

        // A partial record cannot account for materials, so the settlement goes
        // read-only rather than rewriting the file and losing the evidence.
        Assert.True(report.ReadOnly);
        Assert.False(_store.Save(report.Journal, report.ReadOnly).Saved);
        Assert.True(File.ReadAllLines(path).Length == lines.Count);
    }

    [Fact]
    public void Persistence_AnInterruptedWriteLeavesTheOldRecordIntact()
    {
        SettlementJournal journal = BuildJournalThroughCommit(finishTheCommit: true);
        Assert.True(_store.Save(journal).Saved);
        string good = File.ReadAllText(_store.ResolvePath(Scope));

        // A file where the directory needs to be: every write fails, and the
        // live record must be untouched by the attempt.
        var blocked = new JournalStore(Path.Combine(_root, "blocked"));
        File.WriteAllText(Path.Combine(_root, "blocked"), "not a directory");
        journal.Append(JournalEntryKind.OrderTransition, Cottage, transition: OrderTransition.Complete);

        Assert.False(blocked.Save(journal).Saved);
        Assert.Equal(good, File.ReadAllText(_store.ResolvePath(Scope)));
    }

    [Fact]
    public void Persistence_ContainerNamesWithTabsAndStarsSurvive()
    {
        var journal = new SettlementJournal(Scope);
        journal.Append(
            JournalEntryKind.Reserved, Cottage,
            request: RequestId.For(Cottage, 0),
            container: "odd\tname*with%specials",
            stacks: new[] { new MaterialStack("Fine\tWood*", 3) });
        Assert.True(_store.Save(journal).Saved);

        JournalEntry restored = Assert.Single(_store.Load(Scope).Journal.Entries);

        Assert.Equal("odd\tname*with%specials", restored.Container);
        Assert.Equal("Fine\tWood*", restored.Stacks[0].Item);
        Assert.Equal(3, restored.Stacks[0].Count);
    }

    [Fact]
    public void Identity_RejectsAnythingThatCouldEscapeAPath()
    {
        foreach (string bad in new[] { "", "../etc", "Camp", "a--b", "-lead", "trail-", new string('x', 49) })
        {
            Assert.ThrowsAny<ArgumentException>(() => new SettlementId(bad));
        }

        Assert.Equal("4242", new SettlementScope(0x4242, new SettlementId("camp")).ToStorageKey()
            .Substring(12, 4));
    }

    [Fact]
    public void Identity_RequestIdsAreDeterministicAcrossRestarts()
    {
        // The same step of the same order must produce the same id after a
        // restart, or a replayed attempt is not recognisable as the same one.
        Assert.Equal(RequestId.For(Cottage, 3), RequestId.For(Cottage, 3));
        Assert.NotEqual(RequestId.For(Cottage, 3), RequestId.For(Cottage, 4));
    }

    private SettlementJournal BuildJournalThroughCommit(bool finishTheCommit)
    {
        var journal = new SettlementJournal(Scope);
        RequestId request = RequestId.For(Cottage, 0);

        journal.Append(JournalEntryKind.OrderTransition, Cottage, transition: OrderTransition.Approve);
        journal.Append(
            JournalEntryKind.Reserved, Cottage, request: request,
            container: "chest-a", stacks: Wood());
        journal.Append(JournalEntryKind.OrderTransition, Cottage, transition: OrderTransition.Reserve);
        journal.Append(JournalEntryKind.OrderTransition, Cottage, transition: OrderTransition.BeginBuilding);
        journal.Append(JournalEntryKind.CommitStarted, Cottage, request: request);

        if (finishTheCommit)
        {
            journal.Append(JournalEntryKind.CommitFinished, Cottage, request: request);
        }

        return journal;
    }
}
