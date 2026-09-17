using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Tools;
using static Shared.Settlement.Tests.CustodyFixtures;

namespace Shared.Settlement.Tests;

/// <summary>#316: kill the process, or fail a step, before and after every
/// persistence write and every engine mutation of pickup, carry-to-cart,
/// cart-to-container, worker-to-container and tool handover — with the world
/// save rolled back or not — and prove that no path mints, silently loses or
/// replays an uncertain transfer (CONTRACTS.md §5.2, SPEC DATA-03).
///
/// "Mints" is checked against the record: the ledger never holds more units
/// than entered custody. "Silently loses" is checked against the places: every
/// unit the record expects at the worker or a cart is there, or reconciliation
/// says it is not. "Replays" is checked against the engine: after a restart
/// nothing moves until a person or a new request says so.</summary>
public sealed class FaultInjectionTests : IDisposable
{
    private readonly string _root;

    public FaultInjectionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cc-fault-injection", Guid.NewGuid().ToString("N"));
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

    private (FakeWorld World, FaultyDisk Disk) NewWorld()
    {
        var world = new FakeWorld();
        var disk = new FaultyDisk(world, new JournalStore(Path.Combine(_root, Guid.NewGuid().ToString("N"))));
        return (world, disk);
    }

    private static int RecordedUnits(MaterialCustodyLedger ledger)
    {
        int total = 0;
        foreach (Holding holding in ledger.Holdings)
        {
            total += holding.Count;
        }

        return total;
    }

    /// <summary>Every place the record expects units at is either right, or
    /// reconciliation names it.</summary>
    private static void AssertNothingSilentlyLost(CustodyProcess process, FakeInventory worker, FakeInventory? cart = null)
    {
        ReconciliationReport report = CustodyReconciler.Reconcile(
            process.Ledger, new Observer(worker, cart), ordersWereRunning: false);

        foreach (Holding holding in process.Ledger.Holdings)
        {
            if (holding.Location.Place != CustodyPlace.Worker && holding.Location.Place != CustodyPlace.Cart)
            {
                continue;
            }

            FakeInventory place = holding.Location.Place == CustodyPlace.Worker ? worker : cart!;
            int expected = process.Ledger.TotalAt(holding.Location, holding.Item);
            if (place.Peek(holding.Item) == expected)
            {
                continue;
            }

            Assert.Contains(report.Findings, finding => finding.NeedsAttention);
        }
    }

    // ------------------------------------------------------------------
    // Worker -> container, the two-step engine path
    // ------------------------------------------------------------------

    public static IEnumerable<object[]> WorkerToChestKillPoints => new[]
    {
        new object[] { "persist.before.TransferStarted" },
        new object[] { "persist.after.TransferStarted" },
        new object[] { "chest.add.before" },
        new object[] { "chest.add.after" },
        new object[] { "worker.remove.before" },
        new object[] { "worker.remove.after" },
        new object[] { "persist.before.TransferFinished" },
        new object[] { "persist.after.TransferFinished" },
    };

    [Theory]
    [MemberData(nameof(WorkerToChestKillPoints))]
    public void AKilledDeliveryRollsBackToTheLastSaveWithNothingCreditedOrLost(string killAt)
    {
        (FakeWorld world, FaultyDisk disk) = NewWorld();
        FakeInventory worker = world.Inventory("worker");
        FakeInventory chest = world.Inventory("chest");

        CustodyProcess process = CustodyProcess.Start(world, disk);
        Gather(process, worker, 10);
        process.SaveWorld();

        world.KillAt = killAt;
        Assert.ThrowsAny<Exception>(() =>
        {
            TransferReceipt receipt = process.Core.Executor.Execute(
                Intent(process.Core, WorkerAt(), ChestAt(Epoch), 10), worker, chest);

            // A kill inside a port is caught and classified; the process is dead
            // anyway, so make the harness see it the way a real kill would.
            world.Guard();
            _ = receipt;
        });

        CustodyProcess restarted = process.Restart();
        int mutationsAtLoad = worker.Mutations + chest.Mutations;

        // The world is its last save: the worker still carries the ten.
        Assert.Equal(10, worker.Peek(Stone));
        Assert.Equal(0, chest.Peek(Stone));

        // The record agrees: whatever reached the journal after that save was
        // rolled back with the world and is voided, never credited.
        Assert.Equal(10, restarted.Ledger.HoldingAt(Order, WorkerAt(), Stone));
        Assert.Equal(0, restarted.Ledger.CountAt(Order, CustodyPlace.Destination, CollectedResource.Stone));
        Assert.False(restarted.Ledger.HasUncertainTransfer(Order));
        Assert.Equal(10, RecordedUnits(restarted.Ledger));

        ReconciliationReport report = CustodyReconciler.Reconcile(restarted.Ledger, new Observer(worker), false);
        Assert.True(report.AllMatch, string.Join(" | ", report.Findings));

        // Nothing replayed at load.
        Assert.Equal(mutationsAtLoad, worker.Mutations + chest.Mutations);
    }

    [Theory]
    [MemberData(nameof(WorkerToChestKillPoints))]
    public void AKilledDeliveryIsSafeWhenTheWorldHadNeverBeenSaved(string killAt)
    {
        // No save at all before the kill: the loaded world predates every row.
        (FakeWorld world, FaultyDisk disk) = NewWorld();
        FakeInventory worker = world.Inventory("worker");
        FakeInventory chest = world.Inventory("chest");

        CustodyProcess process = CustodyProcess.Start(world, disk);
        world.Tick();
        Gather(process, worker, 10);

        world.KillAt = killAt;
        Assert.ThrowsAny<Exception>(() =>
        {
            process.Core.Executor.Execute(Intent(process.Core, WorkerAt(), ChestAt(Epoch), 10), worker, chest);
            world.Guard();
        });

        CustodyProcess restarted = process.Restart();

        Assert.Equal(0, worker.Peek(Stone));
        Assert.Equal(0, chest.Peek(Stone));
        Assert.Equal(0, RecordedUnits(restarted.Ledger));
        Assert.False(restarted.Ledger.HasUncertainTransfer(Order));
    }

    [Fact]
    public void AFaultBetweenAddAndRemoveIsUncertainNeverCompensatedAndSurvivesASave()
    {
        // Not a kill: the engine throws after adding. The destination has the
        // units and the source still does -- a visible duplicate. The executor
        // says uncertain, moves nothing in the record, and the world then saves
        // WITH the duplicate in it (the "not rolled back" half).
        (FakeWorld world, FaultyDisk disk) = NewWorld();
        FakeInventory worker = world.Inventory("worker");
        FakeInventory chest = world.Inventory("chest");

        CustodyProcess process = CustodyProcess.Start(world, disk);
        Gather(process, worker, 10);
        process.SaveWorld();

        chest.ThrowAfterAdd = true;
        TransferIntent intent = Intent(process.Core, WorkerAt(), ChestAt(Epoch), 10);
        TransferReceipt receipt = process.Core.Executor.Execute(intent, worker, chest);

        Assert.Equal(TransferOutcome.Uncertain, receipt.Outcome);
        Assert.Equal(0, receipt.Accepted);
        Assert.Contains("fault", receipt.Evidence);
        Assert.Equal(10, worker.Peek(Stone));
        Assert.Equal(10, chest.Peek(Stone));

        // A retry of the same request is never started again.
        int before = worker.Mutations + chest.Mutations;
        chest.ThrowAfterAdd = false;
        Assert.Equal(TransferOutcome.Uncertain, process.Core.Executor.Execute(intent, worker, chest).Outcome);
        Assert.Equal(before, worker.Mutations + chest.Mutations);

        process.SaveWorld();
        CustodyProcess restarted = process.Restart();

        // The record still credits nothing and still waits for a person.
        Assert.True(restarted.Ledger.HasUncertainTransfer(Order));
        Assert.Equal(10, restarted.Ledger.HoldingAt(Order, WorkerAt(), Stone));
        Assert.Equal(10, RecordedUnits(restarted.Ledger));
        Assert.Contains(restarted.LoadReport.Journal.Replay().Repairs, repair => repair.Contains("source|destination"));

        // The worker side is unchanged, so the counts say "did not move"; the
        // chest cannot be checked, and the duplicate there is never credited.
        ReconciliationReport report = CustodyReconciler.Reconcile(restarted.Ledger, new Observer(worker), false);
        ReconciliationFinding finding = Assert.Single(report.Findings, f => !f.Request.IsEmpty);
        Assert.Equal(ReconciliationFindingKind.NoEffect, finding.Kind);
        Assert.Equal(CollectionAttentionReason.TransferUncertain, report.ReasonFor(Order));

        // A person answers; only then does the record change, and it cannot
        // be answered twice.
        Assert.Equal(CustodyLedgerOutcome.Applied, restarted.Core.Resolve(intent.Request, TransferSide.Source, 0, "checked", out _));
        Assert.Equal(CustodyLedgerOutcome.AlreadySatisfied, restarted.Core.Resolve(intent.Request, TransferSide.Destination, 10, "again", out _));
        Assert.False(restarted.Ledger.HasUncertainTransfer(Order));
        Assert.Equal(10, restarted.Ledger.HoldingAt(Order, WorkerAt(), Stone));
    }

    [Fact]
    public void AReceiptThatCouldNotBeWrittenIsUncertainAndTheNextLoadSeesTheEffect()
    {
        (FakeWorld world, FaultyDisk disk) = NewWorld();
        FakeInventory worker = world.Inventory("worker");
        FakeInventory chest = world.Inventory("chest");

        CustodyProcess process = CustodyProcess.Start(world, disk);
        Gather(process, worker, 10);
        process.SaveWorld();

        disk.FailBeforeWriting(JournalEntryKind.TransferFinished);
        TransferIntent intent = Intent(process.Core, WorkerAt(), ChestAt(Epoch), 10);
        TransferReceipt receipt = process.Core.Executor.Execute(intent, worker, chest);

        // It really moved; the record could not say so.
        Assert.Equal(TransferOutcome.Uncertain, receipt.Outcome);
        Assert.Contains("could not be written", receipt.Evidence);
        Assert.Equal(0, worker.Peek(Stone));
        Assert.Equal(10, chest.Peek(Stone));
        Assert.True(process.Ledger.HasUncertainTransfer(Order));

        // The unsaved receipt row is gone from memory too: no later save carries
        // it to disk behind the ledger's back.
        // (The one receipt left is the take's, from gathering.)
        Assert.Single(process.Journal.Entries, e => e.Kind == JournalEntryKind.TransferFinished);

        process.SaveWorld();
        CustodyProcess restarted = process.Restart();

        Assert.True(restarted.Ledger.HasUncertainTransfer(Order));
        ReconciliationReport report = CustodyReconciler.Reconcile(restarted.Ledger, new Observer(worker), false);
        ReconciliationFinding finding = Assert.Single(report.Findings, f => !f.Request.IsEmpty);
        Assert.Equal(ReconciliationFindingKind.EffectVisible, finding.Kind);
        Assert.Contains("destination", finding.Resolution);

        // Never applied until a person confirms.
        Assert.Equal(10, restarted.Ledger.HoldingAt(Order, WorkerAt(), Stone));
        Assert.Equal(CustodyLedgerOutcome.Applied, restarted.Core.Resolve(intent.Request, TransferSide.Destination, 10, "chest has it", out _));
        Assert.Equal(10, restarted.Ledger.CountAt(Order, CustodyPlace.Destination, CollectedResource.Stone));
        Assert.Equal(0, restarted.Ledger.HoldingAt(Order, WorkerAt(), Stone));
        Assert.Equal(10, RecordedUnits(restarted.Ledger));
    }

    [Fact]
    public void AnIntentThatCouldNotBeWrittenMovesNothingAndLeavesNoRow()
    {
        (FakeWorld world, FaultyDisk disk) = NewWorld();
        FakeInventory worker = world.Inventory("worker");
        FakeInventory chest = world.Inventory("chest");

        CustodyProcess process = CustodyProcess.Start(world, disk);
        Gather(process, worker, 10);

        int rows = process.Journal.Entries.Count;
        int mutations = worker.Mutations + chest.Mutations;
        disk.FailBeforeWriting(JournalEntryKind.TransferStarted);

        TransferReceipt receipt = process.Core.Executor.Execute(Intent(process.Core, WorkerAt(), ChestAt(Epoch), 10), worker, chest);

        Assert.Equal(TransferOutcome.Refused, receipt.Outcome);
        Assert.Equal(rows, process.Journal.Entries.Count);
        Assert.Equal(mutations, worker.Mutations + chest.Mutations);
        Assert.Equal(10, process.Ledger.HoldingAt(Order, WorkerAt(), Stone));
    }

    [Fact]
    public void AnIntentWhoseSaveReportedFailureButReachedDiskIsTreatedAsWritten()
    {
        // #299's review: a save reporting failure after the journal half
        // reached disk. The row is on disk, so the transfer goes ahead and the
        // record is complete -- telling the player "nothing was written" and
        // stopping would leave a phantom intent behind.
        (FakeWorld world, FaultyDisk disk) = NewWorld();
        FakeInventory worker = world.Inventory("worker");
        FakeInventory chest = world.Inventory("chest");

        CustodyProcess process = CustodyProcess.Start(world, disk);
        Gather(process, worker, 10);

        disk.FailAfterWriting(JournalEntryKind.TransferStarted);
        TransferReceipt receipt = process.Core.Executor.Execute(Intent(process.Core, WorkerAt(), ChestAt(Epoch), 10), worker, chest);

        Assert.Equal(TransferOutcome.Completed, receipt.Outcome);
        Assert.Equal(10, chest.Peek(Stone));
        Assert.Equal(10, process.Ledger.CountAt(Order, CustodyPlace.Destination, CollectedResource.Stone));
        Assert.False(process.Ledger.HasUncertainTransfer(Order));
    }

    [Fact]
    public void PartialAcceptanceCreditsOnlyWhatArrivedAndLeavesTheRestWithTheWorker()
    {
        (FakeWorld world, FaultyDisk disk) = NewWorld();
        FakeInventory worker = world.Inventory("worker");
        FakeInventory chest = world.Inventory("chest");

        CustodyProcess process = CustodyProcess.Start(world, disk);
        Gather(process, worker, 10);

        // CanAccept says ten; the engine takes six.
        chest.AddLimit = 6;
        TransferReceipt receipt = process.Core.Executor.Execute(Intent(process.Core, WorkerAt(), ChestAt(Epoch), 10), worker, chest);

        Assert.Equal(TransferOutcome.Partial, receipt.Outcome);
        Assert.Equal(6, receipt.Accepted);
        Assert.Equal(4, worker.Peek(Stone));
        Assert.Equal(6, chest.Peek(Stone));
        Assert.Equal(4, process.Ledger.HoldingAt(Order, WorkerAt(), Stone));
        Assert.Equal(6, process.Ledger.CountAt(Order, CustodyPlace.Destination, CollectedResource.Stone));

        // And a chest with room for three is asked first: three are moved.
        chest.AddLimit = null;
        chest.Capacity = chest.Total + 3;
        receipt = process.Core.Executor.Execute(Intent(process.Core, WorkerAt(), ChestAt(Epoch), 4), worker, chest);
        Assert.Equal(TransferOutcome.Partial, receipt.Outcome);
        Assert.Equal(3, receipt.Accepted);
        Assert.Equal(1, process.Ledger.HoldingAt(Order, WorkerAt(), Stone));
        Assert.Equal(10, RecordedUnits(process.Ledger));
    }

    [Fact]
    public void ARemoveThatTakesLessThanWasAddedIsUncertainWithTheEvidence()
    {
        (FakeWorld world, FaultyDisk disk) = NewWorld();
        FakeInventory worker = world.Inventory("worker");
        FakeInventory chest = world.Inventory("chest");

        CustodyProcess process = CustodyProcess.Start(world, disk);
        Gather(process, worker, 10);

        worker.RemoveShortfall = 3;
        TransferReceipt receipt = process.Core.Executor.Execute(Intent(process.Core, WorkerAt(), ChestAt(Epoch), 10), worker, chest);

        Assert.Equal(TransferOutcome.Uncertain, receipt.Outcome);
        Assert.Contains("worker Stone: 10 -> 3", receipt.Evidence);
        Assert.Contains("chest: 0 -> 10", receipt.Evidence);
        Assert.Equal(10, process.Ledger.HoldingAt(Order, WorkerAt(), Stone));
        Assert.Equal(10, RecordedUnits(process.Ledger));
    }

    [Fact]
    public void APortThatMisreportsItsAddIsClassifiedFromTheCounts()
    {
        (FakeWorld world, FaultyDisk disk) = NewWorld();
        FakeInventory worker = world.Inventory("worker");
        FakeInventory chest = world.Inventory("chest");

        CustodyProcess process = CustodyProcess.Start(world, disk);
        Gather(process, worker, 10);

        // Add says 12; it added 10, and both counts agree it moved 10.
        chest.AddReturnSkew = 2;
        TransferReceipt receipt = process.Core.Executor.Execute(Intent(process.Core, WorkerAt(), ChestAt(Epoch), 10), worker, chest);

        Assert.Equal(TransferOutcome.Completed, receipt.Outcome);
        Assert.Equal(10, receipt.Accepted);
        Assert.Contains("add said 12", receipt.Evidence);
    }

    // ------------------------------------------------------------------
    // Worker -> cart and cart -> container, the one-call engine move
    // ------------------------------------------------------------------

    public static IEnumerable<object[]> CartKillPoints => new[]
    {
        new object[] { "persist.after.TransferStarted" },
        new object[] { "cart.move.before" },
        new object[] { "cart.move.added" },
        new object[] { "cart.move.after" },
        new object[] { "persist.after.TransferFinished" },
    };

    [Theory]
    [MemberData(nameof(CartKillPoints))]
    public void AKilledCartLoadRollsBackAndPreExistingCargoIsNeverCounted(string killAt)
    {
        (FakeWorld world, FaultyDisk disk) = NewWorld();
        FakeInventory worker = world.Inventory("worker");
        FakeInventory cart = world.Inventory("cart", moves: true);
        cart.External(Stone, 15);
        cart.Snapshot();

        CustodyProcess process = CustodyProcess.Start(world, disk);
        Gather(process, worker, 10);

        Guid provider = Guid.NewGuid();
        Assert.True(process.Core.RecordCartBaseline(
            Order, "lease-1", "1:77", provider, new[] { new ItemCount(Stone, 15) }, out string why), why);
        process.SaveWorld();

        CustodyLocation cartLocation = new(CustodyPlace.Cart, "1:77", provider);
        world.KillAt = killAt;
        Assert.ThrowsAny<Exception>(() =>
        {
            process.Core.Executor.Execute(Intent(process.Core, WorkerAt(), cartLocation, 10), worker, cart);
            world.Guard();
        });

        CustodyProcess restarted = process.Restart();

        Assert.Equal(10, worker.Peek(Stone));
        Assert.Equal(15, cart.Peek(Stone));
        Assert.Equal(10, restarted.Ledger.HoldingAt(Order, WorkerAt(), Stone));
        Assert.Equal(0, restarted.Ledger.CountAt(Order, CustodyPlace.Cart, CollectedResource.Stone));
        Assert.Equal(10, RecordedUnits(restarted.Ledger));

        // The fifteen already in the cart are nobody's gathered material.
        Assert.True(restarted.Ledger.TryGetCartBaseline("1:77", provider, out CartBaseline baseline));
        ReconciliationReport report = CustodyReconciler.Reconcile(
            restarted.Ledger, new Observer(worker, cart, item => baseline.CountOf(item)), false);
        Assert.True(report.AllMatch, string.Join(" | ", report.Findings));
    }

    [Theory]
    [MemberData(nameof(CartKillPoints))]
    public void AKilledCartUnloadRollsBackWithTheCargoStillInTheCart(string killAt)
    {
        (FakeWorld world, FaultyDisk disk) = NewWorld();
        FakeInventory worker = world.Inventory("worker");
        FakeInventory cart = world.Inventory("cart", moves: true);
        FakeInventory chest = world.Inventory("chest", moves: true);

        CustodyProcess process = CustodyProcess.Start(world, disk);
        Gather(process, worker, 10);
        Guid provider = Guid.NewGuid();
        Assert.True(process.Core.RecordCartBaseline(Order, "lease-1", "1:77", provider, Array.Empty<ItemCount>(), out _));
        CustodyLocation cartLocation = new(CustodyPlace.Cart, "1:77", provider);

        Assert.Equal(
            TransferOutcome.Completed,
            process.Core.Executor.Execute(Intent(process.Core, WorkerAt(), cartLocation, 10), worker, cart).Outcome);
        process.SaveWorld();

        // The kill points name "cart.move.*"; the unload moves into the chest.
        world.KillAt = killAt.Replace("cart.move", "chest.move");
        Assert.ThrowsAny<Exception>(() =>
        {
            process.Core.Executor.Execute(Intent(process.Core, cartLocation, ChestAt(Epoch), 10), cart, chest);
            world.Guard();
        });

        CustodyProcess restarted = process.Restart();

        Assert.Equal(10, cart.Peek(Stone));
        Assert.Equal(0, chest.Peek(Stone));
        Assert.Equal(10, restarted.Ledger.HoldingAt(Order, cartLocation, Stone));
        Assert.Equal(0, restarted.Ledger.CountAt(Order, CustodyPlace.Destination, CollectedResource.Stone));
        Assert.False(restarted.Ledger.HasUncertainTransfer(Order));
        AssertNothingSilentlyLost(restarted, worker, cart);
    }

    [Fact]
    public void AMoveThatFailsBetweenItsAddAndItsRemoveIsUncertain()
    {
        (FakeWorld world, FaultyDisk disk) = NewWorld();
        FakeInventory worker = world.Inventory("worker");
        var cart = (FakeMovingInventory)world.Inventory("cart", moves: true);

        CustodyProcess process = CustodyProcess.Start(world, disk);
        Gather(process, worker, 10);
        Guid provider = Guid.NewGuid();
        Assert.True(process.Core.RecordCartBaseline(Order, "lease-1", "1:77", provider, Array.Empty<ItemCount>(), out _));
        CustodyLocation cartLocation = new(CustodyPlace.Cart, "1:77", provider);

        cart.ThrowBetweenAddAndRemove = true;
        TransferReceipt receipt = process.Core.Executor.Execute(Intent(process.Core, WorkerAt(), cartLocation, 10), worker, cart);

        Assert.Equal(TransferOutcome.Uncertain, receipt.Outcome);
        Assert.Contains("one engine move", receipt.Evidence);
        Assert.Equal(10, process.Ledger.HoldingAt(Order, WorkerAt(), Stone));

        // Both sides are observable in session, and the counts show the add
        // without the remove: neither outcome, so a person looks.
        ReconciliationReport report = CustodyReconciler.Reconcile(process.Ledger, new Observer(worker, cart), true);
        ReconciliationFinding finding = Assert.Single(report.Findings, f => !f.Request.IsEmpty);
        Assert.Equal(ReconciliationFindingKind.Unclear, finding.Kind);
    }

    // ------------------------------------------------------------------
    // Pickup and take
    // ------------------------------------------------------------------

    public static IEnumerable<object[]> PickupKillPoints => new[]
    {
        new object[] { "persist.before.PickupStarted" },
        new object[] { "persist.after.PickupStarted" },
        new object[] { "pick" },
        new object[] { "persist.after.PickupFinished" },
        new object[] { "persist.after.TransferStarted" },
        new object[] { "take.before" },
        new object[] { "take.after" },
        new object[] { "persist.before.TransferFinished" },
        new object[] { "persist.after.TransferFinished" },
    };

    [Theory]
    [MemberData(nameof(PickupKillPoints))]
    public void AKilledPickupCreditsNothingTheWorldDoesNotHold(string killAt)
    {
        (FakeWorld world, FaultyDisk disk) = NewWorld();
        FakeInventory worker = world.Inventory("worker");

        CustodyProcess process = CustodyProcess.Start(world, disk);
        Assert.True(process.Core.RecordAccepted(Definition(), out _));
        process.SaveWorld();

        world.KillAt = killAt;
        Assert.ThrowsAny<Exception>(() =>
        {
            Gather(process, worker, 1);
            world.Guard();
        });

        CustodyProcess restarted = process.Restart();

        Assert.Equal(0, worker.Peek(Stone));
        Assert.Equal(0, RecordedUnits(restarted.Ledger));
        Assert.False(restarted.Ledger.HasUncertainTransfer(Order));

        // The acceptance is a record, not a world effect: the order survives.
        Assert.True(restarted.Ledger.TryGetOrder(Order, out CollectionOrderRecord _));
    }

    [Fact]
    public void APickWhoseResultCouldNotBeWrittenIsUncertainAndCreditsNothing()
    {
        (FakeWorld world, FaultyDisk disk) = NewWorld();
        FakeInventory worker = world.Inventory("worker");

        CustodyProcess process = CustodyProcess.Start(world, disk);
        Assert.True(process.Core.RecordAccepted(Definition(), out _));
        RequestId? pickup = process.Core.BeginPickup(Order, Source(), out _);
        Assert.True(pickup.HasValue);

        disk.FailBeforeWriting(JournalEntryKind.PickupFinished);
        var result = new PickupResult(PickupOutcome.Picked, new[] { new SpawnedDrop("1:901", Stone, 1) }, "");
        Assert.False(process.Core.FinishPickup(pickup!.Value, result, out _));

        Assert.True(process.Ledger.HasUncertainTransfer(Order));
        Assert.Equal(0, RecordedUnits(process.Ledger));

        // Its drop cannot be taken into custody: there is no recorded drop.
        var take = Intent(process.Core, new CustodyLocation(CustodyPlace.SourceGround, "1:901", Epoch), WorkerAt(), 1);
        Assert.Equal(TransferOutcome.Refused, process.Core.BeginTransfer(take, out _));

        // A person acknowledges; nothing is granted.
        Assert.Equal(CustodyLedgerOutcome.Applied, process.Core.Resolve(pickup.Value, TransferSide.Source, 0, "saw it", out _));
        Assert.False(process.Ledger.HasUncertainTransfer(Order));
        Assert.Equal(0, RecordedUnits(process.Ledger));
    }

    // ------------------------------------------------------------------
    // Cancellation, retries, external changes
    // ------------------------------------------------------------------

    [Fact]
    public void CancellingIsNotARefundAndCarriedMaterialCanStillBeReleased()
    {
        (FakeWorld world, FaultyDisk disk) = NewWorld();
        FakeInventory worker = world.Inventory("worker");
        FakeInventory chest = world.Inventory("chest");

        CustodyProcess process = CustodyProcess.Start(world, disk);
        Gather(process, worker, 10);
        Assert.True(process.Core.RecordTransition(Order, CollectionOrderState.Accepted, CollectionOrderState.Cancelled, CollectionAttentionReason.Unspecified, out _));

        // Nothing moved by cancelling.
        Assert.Equal(10, process.Ledger.HoldingAt(Order, WorkerAt(), Stone));
        Assert.Equal(10, worker.Peek(Stone));

        // "Release everything": the cancelled order's carried material goes to
        // a chest through an ordinary recorded transfer.
        TransferReceipt release = process.Core.Executor.Execute(Intent(process.Core, WorkerAt(), ChestAt(Epoch), 10), worker, chest);
        Assert.Equal(TransferOutcome.Completed, release.Outcome);

        CustodyProcess restarted = process.Restart();
        Assert.Equal(0, restarted.Ledger.HoldingAt(Order, WorkerAt(), Stone));
    }

    [Fact]
    public void ARetryIsSatisfiedAndADifferentPayloadUnderTheSameIdIsRejected()
    {
        (FakeWorld world, FaultyDisk disk) = NewWorld();
        FakeInventory worker = world.Inventory("worker");
        FakeInventory chest = world.Inventory("chest");

        CustodyProcess process = CustodyProcess.Start(world, disk);
        Gather(process, worker, 10);

        TransferIntent intent = Intent(process.Core, WorkerAt(), ChestAt(Epoch), 4);
        Assert.Equal(TransferOutcome.Completed, process.Core.Executor.Execute(intent, worker, chest).Outcome);

        int mutations = worker.Mutations + chest.Mutations;
        TransferReceipt again = process.Core.Executor.Execute(intent, worker, chest);
        Assert.Equal(TransferOutcome.AlreadySatisfied, again.Outcome);
        Assert.Equal(4, again.Accepted);

        var different = new TransferIntent(intent.Request, Order, WorkerAt(), ChestAt(Epoch), Stone, 5, process.Ledger.Revision);
        Assert.Equal(TransferOutcome.RejectedDifferentPayload, process.Core.Executor.Execute(different, worker, chest).Outcome);
        Assert.Equal(mutations, worker.Mutations + chest.Mutations);

        // The same answers after a reload.
        process.SaveWorld();
        CustodyProcess restarted = process.Restart();
        Assert.Equal(TransferOutcome.AlreadySatisfied, restarted.Core.Executor.Execute(intent, worker, chest).Outcome);
        Assert.Equal(mutations, worker.Mutations + chest.Mutations);
    }

    [Fact]
    public void AStalePlanMovesNothing()
    {
        (FakeWorld world, FaultyDisk disk) = NewWorld();
        FakeInventory worker = world.Inventory("worker");
        FakeInventory chest = world.Inventory("chest");

        CustodyProcess process = CustodyProcess.Start(world, disk);
        Gather(process, worker, 10);

        TransferIntent planned = Intent(process.Core, WorkerAt(), ChestAt(Epoch), 5);
        Assert.True(process.Core.RecordTransition(Order, CollectionOrderState.Accepted, CollectionOrderState.Surveying, CollectionAttentionReason.Unspecified, out _));

        int mutations = worker.Mutations + chest.Mutations;
        Assert.Equal(TransferOutcome.Stale, process.Core.Executor.Execute(planned, worker, chest).Outcome);
        Assert.Equal(mutations, worker.Mutations + chest.Mutations);
    }

    [Fact]
    public void MaterialThePlayerTookIsNeverReplacedAndCanBeRecordedAsLost()
    {
        (FakeWorld world, FaultyDisk disk) = NewWorld();
        FakeInventory worker = world.Inventory("worker");
        FakeInventory chest = world.Inventory("chest");

        CustodyProcess process = CustodyProcess.Start(world, disk);
        Gather(process, worker, 10);
        process.SaveWorld();

        // The player takes three from the worker, outside custody.
        worker.External(Stone, 7);

        TransferReceipt refused = process.Core.Executor.Execute(Intent(process.Core, WorkerAt(), ChestAt(Epoch), 10), worker, chest);
        Assert.Equal(TransferOutcome.Refused, refused.Outcome);
        Assert.Contains("actually holds 7", refused.Evidence);

        ReconciliationReport report = CustodyReconciler.Reconcile(process.Ledger, new Observer(worker), ordersWereRunning: false);
        ReconciliationFinding shortfall = Assert.Single(report.ShortfallsFor(Order));
        Assert.Equal(10, shortfall.Expected);
        Assert.Equal(7, shortfall.Actual);
        Assert.Equal(CollectionAttentionReason.PlayerRemovedMaterial, report.ReasonFor(Order));

        Assert.Equal(CustodyLedgerOutcome.Applied, process.Core.RecordLoss(Order, WorkerAt(), Stone, 3, "taken by the player", out _));
        Assert.Equal(7, process.Ledger.HoldingAt(Order, WorkerAt(), Stone));
        Assert.Equal(3, process.Ledger.CountAt(Order, CustodyPlace.Lost, CollectedResource.Stone));

        // The record's total never exceeded what entered custody.
        Assert.Equal(10, RecordedUnits(process.Ledger));

        process.SaveWorld();
        CustodyProcess restarted = process.Restart();
        Assert.True(CustodyReconciler.Reconcile(restarted.Ledger, new Observer(worker), false).AllMatch);
        Assert.Equal(3, restarted.Ledger.CountAt(Order, CustodyPlace.Lost, CollectedResource.Stone));
    }

    [Fact]
    public void MoreThanTheRecordExpectsIsNeverCredited()
    {
        (FakeWorld world, FaultyDisk disk) = NewWorld();
        FakeInventory worker = world.Inventory("worker");

        CustodyProcess process = CustodyProcess.Start(world, disk);
        Gather(process, worker, 10);
        worker.External(Stone, 14);

        ReconciliationReport report = CustodyReconciler.Reconcile(process.Ledger, new Observer(worker), false);
        ReconciliationFinding above = Assert.Single(report.Findings);
        Assert.Equal(ReconciliationFindingKind.AboveExpected, above.Kind);
        Assert.Equal(CollectionAttentionReason.ReconciliationMismatch, report.ReasonFor(Order));
        Assert.Equal(10, process.Ledger.HoldingAt(Order, WorkerAt(), Stone));
    }

    [Fact]
    public void WithoutAuthorityNothingIsWrittenOrMoved()
    {
        (FakeWorld world, FaultyDisk disk) = NewWorld();
        FakeInventory worker = world.Inventory("worker");
        FakeInventory chest = world.Inventory("chest");

        CustodyProcess process = CustodyProcess.Start(world, disk);
        Gather(process, worker, 10);

        world.Authority = false;
        int rows = process.Journal.Entries.Count;
        int mutations = worker.Mutations + chest.Mutations;

        Assert.Equal(TransferOutcome.Refused, process.Core.Executor.Execute(Intent(process.Core, WorkerAt(), ChestAt(Epoch), 10), worker, chest).Outcome);
        Assert.Null(process.Core.BeginPickup(Order, Source("1:501"), out _));
        Assert.Equal(CustodyLedgerOutcome.Rejected, process.Core.RecordLoss(Order, WorkerAt(), Stone, 1, "no", out _));

        Assert.Equal(rows, process.Journal.Entries.Count);
        Assert.Equal(mutations, worker.Mutations + chest.Mutations);
    }

    // ------------------------------------------------------------------
    // Tool handover
    // ------------------------------------------------------------------

    public static IEnumerable<object[]> ToolKillPoints => new[]
    {
        new object[] { "persist.before.ToolHandoverStarted" },
        new object[] { "persist.after.ToolHandoverStarted" },
        new object[] { "worker.tool.add.before" },
        new object[] { "worker.tool.add.after" },
        new object[] { "player.tool.remove.before" },
        new object[] { "player.tool.remove.after" },
        new object[] { "persist.before.ToolHandoverFinished" },
        new object[] { "persist.after.ToolHandoverFinished" },
    };

    [Theory]
    [MemberData(nameof(ToolKillPoints))]
    public void AKilledToolHandoverRollsBackWithTheAxeInExactlyOnePlace(string killAt)
    {
        (FakeWorld world, FaultyDisk disk) = NewWorld();
        FakeToolInventory player = world.Tools("player");
        FakeToolInventory worker = world.Tools("worker");
        object axe = new();
        player.Put(axe);
        player.Snapshot();

        CustodyProcess process = CustodyProcess.Start(world, disk);
        process.SaveWorld();

        var specimen = new ToolSpecimen(ToolKind.Axe, "$item_axe_bronze", 1, 100f, 2);
        var transaction = new RequestId("give-1");
        var stamp = new JournalStamp(world.Clock, process.Core.Load.LoadEpoch);

        world.KillAt = killAt;
        Assert.ThrowsAny<Exception>(() =>
        {
            ToolHandoverProcedure.Give(
                player, worker, axe, specimen, Worker, transaction, process.Journal.Replay().Tools,
                process.Journal, () => disk.Persist(process.Journal), stamp, out _);
            world.Guard();
        });

        CustodyProcess restarted = process.Restart();
        ToolLedger tools = restarted.Journal.Replay(new WorldLoad(world.Clock, Guid.NewGuid())).Tools;

        // Rolled back with the world: the player has it, he does not, and the
        // record agrees -- no holding, no unanswerable repair.
        Assert.Single(player.Items);
        Assert.Empty(worker.Items);
        Assert.Empty(tools.HeldBy(Worker));
        Assert.False(tools.HasUncertainHandover(Worker));
    }

    [Fact]
    public void AToolHandoverThatFailsMidwayAndIsThenSavedIsAnsweredByAPerson()
    {
        (FakeWorld world, FaultyDisk disk) = NewWorld();
        FakeToolInventory player = world.Tools("player");
        FakeToolInventory worker = world.Tools("worker");
        object axe = new();
        player.Put(axe);

        CustodyProcess process = CustodyProcess.Start(world, disk);
        var specimen = new ToolSpecimen(ToolKind.Axe, "$item_axe_bronze", 1, 100f, 2);
        var transaction = new RequestId("give-2");
        var stamp = new JournalStamp(world.Clock, process.Core.Load.LoadEpoch);

        player.RefuseRemove = true;
        ToolLedger ledger = process.Journal.Replay().Tools;
        Assert.Equal(
            HandoverOutcome.Uncertain,
            ToolHandoverProcedure.Give(player, worker, axe, specimen, Worker, transaction, ledger, process.Journal,
                () => disk.Persist(process.Journal), stamp, out string message));
        Assert.Contains("cf_settle resolve give-2", message);

        // Saved with the axe referenced twice, then the process dies.
        process.SaveWorld();
        CustodyProcess restarted = process.Restart();

        ReplayResult replay = restarted.Journal.Replay();
        Assert.True(replay.Tools.HasUncertainHandover(Worker));
        Assert.Contains(replay.Repairs, repair => repair.Contains("cf_settle resolve give-2"));

        Assert.True(ToolResolution.TryRecord(
            transaction, workerHasIt: true, replay.Tools, restarted.Journal, () => disk.Persist(restarted.Journal), out _));
        Assert.True(restarted.Journal.Replay().Tools.TryGetHeld(Worker, ToolKind.Axe, out _));
    }
}
