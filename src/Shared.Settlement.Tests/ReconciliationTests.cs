using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using static Shared.Settlement.Tests.CustodyFixtures;

namespace Shared.Settlement.Tests;

/// <summary>The world-save marker rule (CONTRACTS.md §5.5): rows after the
/// loaded save are voided, ambiguous placement is NeedsAttention with the
/// evidence kept — and the decision a load makes stays made across every later
/// session.</summary>
public sealed class ReconciliationTests : IDisposable
{
    private readonly string _root;

    private static readonly SettlementScope Scope = new(worldId: 777, settlement: new SettlementId("marker-camp"));

    public ReconciliationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cc-reconciliation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
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

    private static readonly Guid First = Guid.NewGuid();
    private static readonly Guid Second = Guid.NewGuid();

    private static JournalEntry Effect(SettlementJournal journal, double time, Guid load, int step)
    {
        // A pickup intent: a world effect with no other preconditions.
        return journal.AppendCustody(
            new PickupStartedRow(new RequestId("collect-1-pick-" + step.ToString()), Order, Source("1:" + step.ToString())),
            time, load);
    }

    private static void Marker(SettlementJournal journal, int generation, double time, Guid load) =>
        journal.AppendCustody(new WorldSaveMarkerRow(generation), time, load);

    private static SettlementJournal Accepted()
    {
        var journal = new SettlementJournal(Scope);
        journal.AppendCustody(new CollectionAcceptedRow(Definition()), 1, First);
        return journal;
    }

    private static RowStanding StandingOf(SaveTimelineReport report, SettlementJournal journal, JournalEntry entry) =>
        report.Standings[journal.Entries.ToList().IndexOf(entry)];

    // ------------------------------------------------------------------
    // The rule itself
    // ------------------------------------------------------------------

    [Fact]
    public void RowsBeforeASaveAreSavedAndRowsAfterItAreUnsaved()
    {
        SettlementJournal journal = Accepted();
        JournalEntry before = Effect(journal, 50, First, 1);
        Marker(journal, 1, 60, First);
        JournalEntry after = Effect(journal, 70, First, 2);

        SaveTimelineReport report = SaveTimeline.Classify(journal.Entries);

        Assert.Equal(RowStanding.Record, report.Standings[0]);
        Assert.Equal(RowStanding.Saved, StandingOf(report, journal, before));
        Assert.Equal(RowStanding.Unsaved, StandingOf(report, journal, after));
        Assert.Equal(1, report.TopGeneration);
        Assert.True(report.HasUnsaved);
    }

    [Fact]
    public void ACrashBeforeTheNextSaveVoidsWhatCameAfterTheLoadedSave()
    {
        SettlementJournal journal = Accepted();
        JournalEntry before = Effect(journal, 50, First, 1);
        Marker(journal, 1, 60, First);
        JournalEntry after = Effect(journal, 70, First, 2);

        SaveTimelineReport report = SaveTimeline.Classify(journal.Entries, new WorldLoad(60.3, Second));

        Assert.Equal(RowStanding.Saved, StandingOf(report, journal, before));
        Assert.Equal(RowStanding.Voided, StandingOf(report, journal, after));
        Assert.Equal(1, report.RestatementGeneration);
        Assert.Equal(60.3, report.RestatementTime);
    }

    [Fact]
    public void ASaveThatDidNotCompleteIsNotTheOneLoaded()
    {
        // Killed during the save of generation 2: the world loads generation 1.
        SettlementJournal journal = Accepted();
        Marker(journal, 1, 60, First);
        JournalEntry confirmedByTheFailedSave = Effect(journal, 70, First, 1);
        Marker(journal, 2, 80, First);
        JournalEntry afterIt = Effect(journal, 90, First, 2);

        SaveTimelineReport report = SaveTimeline.Classify(journal.Entries, new WorldLoad(60, Second));

        Assert.Equal(RowStanding.Voided, StandingOf(report, journal, confirmedByTheFailedSave));
        Assert.Equal(RowStanding.Voided, StandingOf(report, journal, afterIt));
        Assert.Equal(1, report.RestatementGeneration);
        Assert.Equal(1, report.TopGeneration);
    }

    [Fact]
    public void TheLatestSaveLoadingCleanlyNeedsNoRestatement()
    {
        SettlementJournal journal = Accepted();
        JournalEntry row = Effect(journal, 50, First, 1);
        Marker(journal, 1, 60, First);

        SaveTimelineReport report = SaveTimeline.Classify(journal.Entries, new WorldLoad(60.5, Second));

        Assert.Equal(RowStanding.Saved, StandingOf(report, journal, row));
        Assert.Null(report.RestatementGeneration);
    }

    [Fact]
    public void AMarkerThatCouldNotBeWrittenMakesEarlierRowsAmbiguousNotVoided()
    {
        // Rows, then a save whose marker failed, then more play and a second
        // save whose marker also failed. The loaded world is far past the last
        // marker, so the rows written before its time may be in it.
        SettlementJournal journal = Accepted();
        Marker(journal, 1, 60, First);
        JournalEntry maybeSaved = Effect(journal, 70, First, 1);
        JournalEntry certainlyNot = Effect(journal, 9000, First, 2);

        SaveTimelineReport report = SaveTimeline.Classify(journal.Entries, new WorldLoad(5000, Second));

        Assert.Equal(RowStanding.Ambiguous, StandingOf(report, journal, maybeSaved));
        Assert.Equal(RowStanding.Voided, StandingOf(report, journal, certainlyNot));
        Assert.Equal(1, report.AmbiguousCount);
    }

    [Fact]
    public void NetTimeThatDidNotAdvanceMakesTheRowsBetweenTwoEqualMarkersAmbiguous()
    {
        SettlementJournal journal = Accepted();
        Marker(journal, 1, 60, First);
        JournalEntry between = Effect(journal, 60, First, 1);
        Marker(journal, 2, 60, First);

        SaveTimelineReport report = SaveTimeline.Classify(journal.Entries, new WorldLoad(60, Second));

        Assert.Equal(RowStanding.Ambiguous, StandingOf(report, journal, between));
        Assert.Equal(2, report.RestatementGeneration);
    }

    [Fact]
    public void AWorldThatPredatesEveryMarkerVoidsRowsAfterItsTime()
    {
        SettlementJournal journal = Accepted();
        JournalEntry row = Effect(journal, 70, First, 1);

        SaveTimelineReport report = SaveTimeline.Classify(journal.Entries, new WorldLoad(40, Second));

        Assert.Equal(RowStanding.Voided, StandingOf(report, journal, row));
        Assert.Equal(0, report.RestatementGeneration);
    }

    [Fact]
    public void RecordOnlyRowsAreNeverVoided()
    {
        SettlementJournal journal = Accepted();
        Marker(journal, 1, 60, First);
        JournalEntry transition = journal.AppendCustody(
            new CollectionTransitionRow(Order, CollectionOrderState.Accepted, CollectionOrderState.Surveying, CollectionAttentionReason.Unspecified),
            70, First);
        JournalEntry completion = journal.AppendCustody(
            new CollectionTransitionRow(Order, CollectionOrderState.Surveying, CollectionOrderState.Collecting, CollectionAttentionReason.Unspecified),
            71, First);

        SaveTimelineReport report = SaveTimeline.Classify(journal.Entries, new WorldLoad(60, Second));

        Assert.Equal(RowStanding.Record, StandingOf(report, journal, transition));
        Assert.Equal(RowStanding.Record, StandingOf(report, journal, completion));
    }

    [Fact]
    public void RowsWrittenBeforeSchemaThreeAreNeverVoided()
    {
        var journal = new SettlementJournal(Scope);
        journal.Append(JournalEntryKind.OrderTransition, new OrderId("cottage-1"), transition: TheConcernedCat.Settlement.Orders.OrderTransition.Approve);

        SaveTimelineReport report = SaveTimeline.Classify(journal.Entries, new WorldLoad(1, Second));

        Assert.Equal(RowStanding.Record, report.Standings[0]);
        Assert.Null(report.RestatementGeneration);
    }

    // ------------------------------------------------------------------
    // The decision survives later sessions
    // ------------------------------------------------------------------

    [Fact]
    public void ACrashedSessionsRowsStayVoidedAfterALaterSessionSaves()
    {
        // The case the restatement exists for. Without it, session two's save
        // marker would sit after session one's rolled-back rows and read as
        // confirming them.
        var world = new FakeWorld();
        var disk = new FaultyDisk(world, new JournalStore(_root));
        FakeInventory worker = world.Inventory("worker");

        CustodyProcess one = CustodyProcess.Start(world, disk);
        Assert.True(one.Core.RecordAccepted(Definition(), out _));
        one.SaveWorld();
        Gather(one, worker, 7);

        // Crash without saving: the seven are gone from the world.
        CustodyProcess two = one.Restart();
        Assert.Equal(0, two.Ledger.CountAt(Order, CustodyPlace.Worker, CollectedResource.Stone));
        Assert.Contains(two.Journal.Entries, e => e.Custody is WorldSaveMarkerRow);

        // Session two gathers three for real and saves, twice, far later.
        world.Tick(3600);
        Gather(two, worker, 3, drop: "1:950");
        two.SaveWorld();
        world.Tick(3600);
        two.SaveWorld();

        CustodyProcess three = two.Restart();
        Assert.Equal(3, three.Ledger.CountAt(Order, CustodyPlace.Worker, CollectedResource.Stone));
        Assert.Equal(3, worker.Peek(Stone));
        Assert.True(CustodyReconciler.Reconcile(three.Ledger, new Observer(worker), false).AllMatch);
        Assert.Null(three.Core.RestatementGeneration);
    }

    [Fact]
    public void AnIdleSessionAfterACrashDoesNotBringTheRolledBackRowsBack()
    {
        var world = new FakeWorld();
        var disk = new FaultyDisk(world, new JournalStore(_root));
        FakeInventory worker = world.Inventory("worker");

        CustodyProcess one = CustodyProcess.Start(world, disk);
        Assert.True(one.Core.RecordAccepted(Definition(), out _));
        one.SaveWorld();
        Gather(one, worker, 7);

        CustodyProcess two = one.Restart();

        // Session two does no custody work at all and saves hours later: no
        // marker is written for an idle settlement.
        int rows = two.Journal.Entries.Count;
        world.Tick(7200);
        two.SaveWorld();
        Assert.Equal(rows, two.Journal.Entries.Count);

        CustodyProcess three = two.Restart();
        Assert.Equal(0, three.Ledger.CountAt(Order, CustodyPlace.Worker, CollectedResource.Stone));
        Assert.Equal(0, three.Core.LoadReplay.Timeline!.AmbiguousCount);
        Assert.False(three.Ledger.HasUncertainTransfer(Order));
    }

    [Fact]
    public void AMarkerThatCouldNotBeWrittenStopsNewWorkUntilItIs()
    {
        var world = new FakeWorld();
        var disk = new FaultyDisk(world, new JournalStore(_root));
        FakeInventory worker = world.Inventory("worker");

        CustodyProcess process = CustodyProcess.Start(world, disk);
        Gather(process, worker, 5);

        disk.FailBeforeWriting(JournalEntryKind.WorldSaveMarker);
        process.SaveWorld();

        Assert.True(process.Core.IsMarkerOwed);
        Assert.Equal(CustodyWriteBlock.MarkerOwed, process.Core.WriteBlock);

        // The next attempt writes the owed marker first, with the failed save's
        // time, and only then takes new work.
        Assert.True(process.Core.IsWritable);
        Assert.False(process.Core.IsMarkerOwed);
        WorldSaveMarkerRow marker = Assert.IsType<WorldSaveMarkerRow>(
            process.Journal.Entries.Last(e => e.Custody is WorldSaveMarkerRow).Custody);
        Assert.Equal(1, marker.Generation);
        Assert.Equal(world.SavedTime, process.Journal.Entries.Last(e => e.Custody is WorldSaveMarkerRow).WorldTime);

        CustodyProcess restarted = process.Restart();
        Assert.Equal(5, restarted.Ledger.CountAt(Order, CustodyPlace.Worker, CollectedResource.Stone));
    }

    [Fact]
    public void ARestatementThatCouldNotBeWrittenBlocksEverythingUntilItIs()
    {
        var world = new FakeWorld();
        var disk = new FaultyDisk(world, new JournalStore(_root));
        FakeInventory worker = world.Inventory("worker");

        CustodyProcess one = CustodyProcess.Start(world, disk);
        Assert.True(one.Core.RecordAccepted(Definition(), out _));
        one.SaveWorld();
        Gather(one, worker, 4);

        disk.FailBeforeWriting(JournalEntryKind.WorldSaveMarker, times: 100);
        CustodyProcess two = one.Restart();

        Assert.Equal(CustodyWriteBlock.RestatementOwed, two.Core.WriteBlock);
        Assert.Null(two.Core.BeginPickup(Order, Source("1:990"), out string reason));
        Assert.Contains("which world save was loaded", reason);

        // A save while it is owed writes no marker: that would confirm rows the
        // load decided the world does not hold.
        int rows = two.Journal.Entries.Count;
        two.SaveWorld();
        Assert.Equal(rows, two.Journal.Entries.Count);

        // Once the disk takes it, work resumes.
        disk.StopFailing(JournalEntryKind.WorldSaveMarker);
        Assert.True(two.Core.IsWritable);
        Assert.Null(two.Core.RestatementGeneration);
    }

    [Fact]
    public void AmbiguousRowsBecomeUncertainAndAPersonSettlesThemForGood()
    {
        var world = new FakeWorld();
        var disk = new FaultyDisk(world, new JournalStore(_root));
        FakeInventory worker = world.Inventory("worker");
        FakeInventory chest = world.Inventory("chest");

        CustodyProcess one = CustodyProcess.Start(world, disk);
        Gather(one, worker, 6);
        one.SaveWorld();

        // A delivery, then saves whose markers all fail, then the world is
        // loaded from one of those unmarked saves.
        TransferIntent delivery = Intent(one.Core, WorkerAt(), ChestAt(Epoch), 6);
        Assert.Equal(TransferOutcome.Completed, one.Core.Executor.Execute(delivery, worker, chest).Outcome);
        world.Tick(600);
        disk.FailBeforeWriting(JournalEntryKind.WorldSaveMarker);
        one.SaveWorld();

        CustodyProcess two = one.Restart();

        Assert.True(two.Ledger.HasUncertainTransfer(Order));
        Assert.True(two.Ledger.TryGetTransfer(delivery.Request, out TransferRecord record));
        Assert.Equal(TransferStatus.Ambiguous, record.Status);
        Assert.Contains(two.Core.LoadReplay.Repairs, repair => repair.Contains("could not be placed"));

        ReconciliationReport report = CustodyReconciler.Reconcile(two.Ledger, new Observer(worker), false);
        ReconciliationFinding finding = Assert.Single(report.Findings, f => !f.Request.IsEmpty);
        Assert.Equal(ReconciliationFindingKind.EffectVisible, finding.Kind);

        Assert.Equal(CustodyLedgerOutcome.Applied, two.Core.Resolve(delivery.Request, TransferSide.Destination, 6, "chest has six", out _));
        two.SaveWorld();

        CustodyProcess three = two.Restart();
        Assert.False(three.Ledger.HasUncertainTransfer(Order));
        Assert.Equal(6, three.Ledger.CountAt(Order, CustodyPlace.Destination, CollectedResource.Stone));
    }
}
