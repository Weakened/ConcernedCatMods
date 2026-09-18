using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Worker;
using static Shared.Settlement.Tests.CustodyFixtures;

namespace Shared.Settlement.Tests;

/// <summary>C2: resuming an order after a reload. The order is recovered from
/// the record, and a player-confirmed rebind replaces only its scope snapshot and
/// delivery target — never its quotas, progress or custody.</summary>
public sealed class CustodyRecoveryTests : IDisposable
{
    private readonly string _root;

    public CustodyRecoveryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cc-custody-recovery", Guid.NewGuid().ToString("N"));
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

    private (FakeWorld World, CustodyProcess Process, FakeInventory Worker) Carrying(int units)
    {
        var world = new FakeWorld();
        var disk = new FaultyDisk(world, new JournalStore(_root));
        FakeInventory worker = world.Inventory("worker");
        CustodyProcess process = CustodyProcess.Start(world, disk);
        Gather(process, worker, units);
        Assert.True(process.Core.RecordTransition(Order, CollectionOrderState.Accepted, CollectionOrderState.Surveying, CollectionAttentionReason.Unspecified, out _));
        process.SaveWorld();
        return (world, process, worker);
    }

    private static WorkScope ScopeFor(Guid epoch, WorkScopeSource source = WorkScopeSource.DefaultCampCircle) =>
        new(source, new SitePoint(10f, 20f, 30f), 30f, "your bed", 4, epoch);

    [Fact]
    public void TheWorkersActiveOrderIsRecoveredAfterAReload()
    {
        (FakeWorld _, CustodyProcess process, FakeInventory _) = Carrying(5);
        CustodyProcess restarted = process.Restart();

        Assert.True(restarted.Core.TryRecoverOrder(Worker, out CollectionOrderDefinition? order, out CollectionOrderState state));
        Assert.Equal(Order, order!.Order);
        Assert.Equal(CollectionOrderState.Surveying, state);

        // Its epochs are the previous load's until the player rebinds.
        Assert.NotEqual(restarted.Core.Load.LoadEpoch, order.Scope.WorldLoadEpoch);
        Assert.False(restarted.Core.TryRecoverOrder(new TheConcernedCat.Settlement.Identity.WorkerId("somebody-else"), out _, out _));
    }

    [Fact]
    public void ATerminalOrderIsNotRecovered()
    {
        (FakeWorld _, CustodyProcess process, FakeInventory _) = Carrying(5);
        Assert.True(process.Core.RecordTransition(Order, CollectionOrderState.Surveying, CollectionOrderState.Cancelled, CollectionAttentionReason.Unspecified, out _));

        CustodyProcess restarted = process.Restart();

        Assert.False(restarted.Core.TryRecoverOrder(Worker, out CollectionOrderDefinition? order, out CollectionOrderState state));
        Assert.Null(order);
        Assert.Equal(CollectionOrderState.Unspecified, state);
    }

    [Fact]
    public void ARebindReplacesOnlyTheScopeAndTheDeliveryAndSurvivesTheNextReload()
    {
        (FakeWorld _, CustodyProcess process, FakeInventory _) = Carrying(5);
        CustodyProcess restarted = process.Restart();
        Guid now = restarted.Core.Load.LoadEpoch;
        int revision = restarted.Ledger.Revision;

        var chest = DeliveryTarget.ToContainer("1:4a", now, new SitePoint(12f, 20f, 31f));
        Assert.True(restarted.Core.RecordRebound(Order, ScopeFor(now), chest, out string reason), reason);
        Assert.Equal(revision + 1, restarted.Ledger.Revision);

        // The same rebind again writes nothing.
        int rows = restarted.Journal.Entries.Count;
        Assert.True(restarted.Core.RecordRebound(Order, ScopeFor(now), chest, out _));
        Assert.Equal(rows, restarted.Journal.Entries.Count);

        restarted.SaveWorld();
        CustodyProcess again = restarted.Restart();

        Assert.True(again.Core.TryRecoverOrder(Worker, out CollectionOrderDefinition? order, out _));
        Assert.Equal(now, order!.Scope.WorldLoadEpoch);
        Assert.Equal("1:4a", order.Delivery.ContainerKey);
        Assert.Equal(now, order.Delivery.WorldLoadEpoch);

        // Quotas and custody untouched.
        Assert.Equal(20, order.Quotas[0].Requested);
        Assert.Equal(5, again.Ledger.HoldingAt(Order, WorkerAt(), Stone));
    }

    [Fact]
    public void AReloadRestatesWhyAnOrderIsParkedAndKeepsThePlayersOwnPause()
    {
        // Review R2, m8: an order already in the target state kept whatever
        // reason stopped it last session, because a same-state transition was
        // answered "already satisfied" and wrote nothing.
        (FakeWorld _, CustodyProcess process, FakeInventory _) = Carrying(5);
        Assert.True(process.Core.RecordTransition(
            Order, CollectionOrderState.Surveying, CollectionOrderState.Paused, CollectionAttentionReason.DestinationFull, out _));

        CustodyProcess restarted = process.Restart();
        Assert.True(restarted.Ledger.TryGetOrder(Order, out CollectionOrderRecord parked));
        Assert.Equal(CollectionAttentionReason.DestinationFull, parked.Reason);

        Assert.True(restarted.Core.RecordTransition(
            Order, CollectionOrderState.Paused, CollectionOrderState.Paused, CollectionAttentionReason.DestinationStale, out string why), why);
        Assert.Equal(CollectionAttentionReason.DestinationStale, parked.Reason);

        // Written down, so the next load reads the same reason.
        restarted.SaveWorld();
        CustodyProcess again = restarted.Restart();
        Assert.True(again.Ledger.TryGetOrder(Order, out CollectionOrderRecord reloaded));
        Assert.Equal(CollectionAttentionReason.DestinationStale, reloaded.Reason);

        // The same reason again changes nothing.
        int rows = again.Journal.Entries.Count;
        Assert.True(again.Core.RecordTransition(
            Order, CollectionOrderState.Paused, CollectionOrderState.Paused, CollectionAttentionReason.DestinationStale, out _));
        Assert.Equal(rows, again.Journal.Entries.Count);
    }

    [Fact]
    public void ATransferTheWorldRolledBackIsNeverStartedAgainUnderItsOwnId()
    {
        // The replay registers a voided intent so its id answers Stale rather
        // than looking unused. Nothing asserted this before (review R2, M4).
        var world = new FakeWorld();
        var disk = new FaultyDisk(world, new JournalStore(_root));
        FakeInventory worker = world.Inventory("worker");
        FakeInventory chest = world.Inventory("chest");
        CustodyProcess process = CustodyProcess.Start(world, disk);
        Gather(process, worker, 10);
        process.SaveWorld();

        TransferIntent delivery = Intent(process.Core, WorkerAt(), ChestAt(Epoch), 10);
        Assert.Equal(TransferOutcome.Completed, process.Core.Executor.Execute(delivery, worker, chest).Outcome);

        // Killed before the next save: the delivery is rolled back with the world.
        CustodyProcess restarted = process.Restart();
        Assert.Equal(10, restarted.Ledger.HoldingAt(Order, WorkerAt(), Stone));
        Assert.True(restarted.Ledger.TryGetTransfer(delivery.Request, out TransferRecord voided));
        Assert.Equal(TransferStatus.Voided, voided.Status);

        TransferReceipt again = restarted.Core.Executor.Execute(delivery, worker, chest);
        Assert.Equal(TransferOutcome.Stale, again.Outcome);
        Assert.Contains("rolled back", again.Evidence);
        Assert.Equal(10, restarted.Ledger.HoldingAt(Order, WorkerAt(), Stone));
    }

    [Fact]
    public void AVoidedRowStillCountsTowardTheRevisionThatMintsIds()
    {
        // Ids are minted from the revision, and the revision counts EVERY row
        // the record holds, voided ones included; otherwise a reload would mint
        // an id the file already carries. The old test asserted ">=" against a
        // premise that never produced a voided row at all (review R2, M4).
        var world = new FakeWorld();
        var disk = new FaultyDisk(world, new JournalStore(_root));
        FakeInventory worker = world.Inventory("worker");
        FakeInventory chest = world.Inventory("chest");
        CustodyProcess process = CustodyProcess.Start(world, disk);
        Gather(process, worker, 10);
        process.SaveWorld();

        int rowsBefore = process.Journal.Entries.Count;
        int revisionBefore = process.Ledger.Revision;
        TransferIntent delivery = Intent(process.Core, WorkerAt(), ChestAt(Epoch), 10);
        Assert.Equal(TransferOutcome.Completed, process.Core.Executor.Execute(delivery, worker, chest).Outcome);
        Assert.Equal(rowsBefore + 2, process.Journal.Entries.Count);

        CustodyProcess restarted = process.Restart();

        // Two voided rows, one restatement marker: every one of them counted.
        Assert.Equal(revisionBefore + 3, restarted.Ledger.Revision);
        Assert.NotEqual(delivery.Request.Value, CustodyIds.ForTransfer(Order, restarted.Ledger).Value);
    }

    [Fact]
    public void ARebindMustBelongToThisLoadAndKeepItsKinds()
    {
        (FakeWorld _, CustodyProcess process, FakeInventory _) = Carrying(5);
        CustodyProcess restarted = process.Restart();
        Guid now = restarted.Core.Load.LoadEpoch;
        Guid old = Epoch;
        var chest = DeliveryTarget.ToContainer("1:4a", now, new SitePoint(12f, 20f, 31f));
        int rows = restarted.Journal.Entries.Count;

        Assert.False(restarted.Core.RecordRebound(Order, ScopeFor(old), chest, out string staleScope));
        Assert.Contains("this world load", staleScope);

        Assert.False(restarted.Core.RecordRebound(Order, ScopeFor(now), DeliveryTarget.ToContainer("1:4a", old, default), out string staleChest));
        Assert.Contains("this world load", staleChest);

        Assert.False(restarted.Core.RecordRebound(Order, ScopeFor(now, WorkScopeSource.HarvestDesignation), chest, out string otherSource));
        Assert.Contains("same kind of work area", otherSource);

        Assert.False(restarted.Core.RecordRebound(Order, ScopeFor(now), DeliveryTarget.HoldForPlayer(), out string otherDelivery));
        Assert.Contains("same kind of delivery", otherDelivery);

        restarted.World.Authority = false;
        Assert.False(restarted.Core.RecordRebound(Order, ScopeFor(now), chest, out string unauthorised));
        Assert.Contains("not authorised", unauthorised);

        Assert.Equal(rows, restarted.Journal.Entries.Count);
    }
}
