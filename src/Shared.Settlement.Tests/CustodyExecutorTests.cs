using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Journal;
using static Shared.Settlement.Tests.CustodyFixtures;

namespace Shared.Settlement.Tests;

/// <summary>The executor's order at the port level (CONTRACTS.md §5.2): the
/// regression test Gate E asks for, "adapter ordering, not only ledger replay".
/// </summary>
public sealed class CustodyExecutorTests : IDisposable
{
    private readonly string _root;

    public CustodyExecutorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cc-custody-executor", Guid.NewGuid().ToString("N"));
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

    private (FakeWorld World, CustodyProcess Process, FakeInventory Worker) Gathered(int units = 10)
    {
        var world = new FakeWorld();
        var disk = new FaultyDisk(world, new JournalStore(_root));
        FakeInventory worker = world.Inventory("worker");
        CustodyProcess process = CustodyProcess.Start(world, disk);
        Gather(process, worker, units);
        return (world, process, worker);
    }

    private static IReadOnlyList<string> StepsAfter(FakeWorld world, int from)
    {
        var steps = new List<string>();
        for (int index = from; index < world.Steps.Count; index++)
        {
            steps.Add(world.Steps[index]);
        }

        return steps;
    }

    [Fact]
    public void IntentThenAddThenRemoveThenReceiptInThatOrderExactly()
    {
        (FakeWorld world, CustodyProcess process, FakeInventory worker) = Gathered();
        FakeInventory chest = world.Inventory("chest");
        int mark = world.Steps.Count;

        TransferReceipt receipt = process.Core.Executor.Execute(Intent(process.Core, WorkerAt(), ChestAt(Epoch), 10), worker, chest);

        Assert.Equal(TransferOutcome.Completed, receipt.Outcome);
        Assert.Equal(
            new[]
            {
                "persist.before.TransferStarted",
                "persist.after.TransferStarted",
                "chest.add.before",
                "chest.add.after",
                "worker.remove.before",
                "worker.remove.after",
                "persist.before.TransferFinished",
                "persist.after.TransferFinished",
            },
            StepsAfter(world, mark));
    }

    [Fact]
    public void TheOneCallMoveKeepsTheSameOrderAroundIt()
    {
        (FakeWorld world, CustodyProcess process, FakeInventory worker) = Gathered();
        FakeInventory chest = world.Inventory("chest", moves: true);
        int mark = world.Steps.Count;

        Assert.Equal(
            TransferOutcome.Completed,
            process.Core.Executor.Execute(Intent(process.Core, WorkerAt(), ChestAt(Epoch), 10), worker, chest).Outcome);

        Assert.Equal(
            new[]
            {
                "persist.before.TransferStarted",
                "persist.after.TransferStarted",
                "chest.move.before",
                "chest.move.added",
                "chest.move.after",
                "persist.before.TransferFinished",
                "persist.after.TransferFinished",
            },
            StepsAfter(world, mark));
    }

    public static IEnumerable<object[]> Refusals => new[]
    {
        new object[] { "no authority" },
        new object[] { "read-only record" },
        new object[] { "source unavailable" },
        new object[] { "destination unavailable" },
        new object[] { "stale revision" },
        new object[] { "record holds fewer" },
        new object[] { "source actually holds fewer" },
        new object[] { "destination full" },
        new object[] { "unknown order" },
        new object[] { "delivered material moved again" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void EveryCheckRefusesBeforeAnythingIsWrittenOrMoved(string refusal)
    {
        (FakeWorld world, CustodyProcess process, FakeInventory worker) = Gathered();
        FakeInventory chest = world.Inventory("chest");
        TransferIntent intent = Intent(process.Core, WorkerAt(), ChestAt(Epoch), 10);

        switch (refusal)
        {
            case "no authority": world.Authority = false; break;
            case "read-only record": process.Journal.MarkReadOnly(); break;
            case "source unavailable": worker.Available = false; break;
            case "destination unavailable": chest.Available = false; break;
            case "stale revision":
                intent = new TransferIntent(intent.Request, Order, WorkerAt(), ChestAt(Epoch), Stone, 10, process.Ledger.Revision - 1);
                break;
            case "record holds fewer":
                intent = Intent(process.Core, WorkerAt(), ChestAt(Epoch), 11);
                worker.External(Stone, 11);
                break;
            case "source actually holds fewer": worker.External(Stone, 9); break;
            case "destination full": chest.Capacity = 0; break;
            case "unknown order":
                intent = new TransferIntent(intent.Request, new TheConcernedCat.Settlement.Identity.OrderId("other-1"), WorkerAt(), ChestAt(Epoch), Stone, 10, process.Ledger.Revision);
                break;
            case "delivered material moved again":
                intent = Intent(process.Core, ChestAt(Epoch), WorkerAt(), 1);
                break;
        }

        int rows = process.Journal.Entries.Count;
        int mutations = worker.Mutations + chest.Mutations;
        int revision = process.Ledger.Revision;

        TransferReceipt receipt = process.Core.Executor.Execute(intent, worker, chest);

        Assert.True(
            receipt.Outcome == TransferOutcome.Refused || receipt.Outcome == TransferOutcome.Stale,
            refusal + " gave " + receipt.Outcome);
        Assert.Equal(0, receipt.Accepted);
        Assert.False(string.IsNullOrEmpty(receipt.Evidence));
        Assert.Equal(rows, process.Journal.Entries.Count);
        Assert.Equal(mutations, worker.Mutations + chest.Mutations);
        Assert.Equal(revision, process.Ledger.Revision);
    }

    [Fact]
    public void ADestinationThatAcceptsNothingAfterTheIntentIsRecordedAsRefusedFromTheCounts()
    {
        (FakeWorld world, CustodyProcess process, FakeInventory worker) = Gathered();
        FakeInventory chest = world.Inventory("chest");

        // CanAccept says yes; the add takes nothing.
        chest.AddLimit = 0;
        TransferReceipt receipt = process.Core.Executor.Execute(Intent(process.Core, WorkerAt(), ChestAt(Epoch), 10), worker, chest);

        Assert.Equal(TransferOutcome.Refused, receipt.Outcome);
        Assert.Equal(10, worker.Peek(Stone));
        Assert.Equal(10, process.Ledger.HoldingAt(Order, WorkerAt(), Stone));
        Assert.True(process.Ledger.TryGetTransfer(receipt.Request, out TransferRecord record));
        Assert.Equal(TransferStatus.Refused, record.Status);
        Assert.False(process.Ledger.HasUncertainTransfer(Order));
    }

    [Fact]
    public void ARequestIdMintedFromTheRevisionIsNeverReusedAcrossAReload()
    {
        (FakeWorld world, CustodyProcess process, FakeInventory worker) = Gathered();
        FakeInventory chest = world.Inventory("chest");

        var seen = new HashSet<string>();
        TransferIntent first = Intent(process.Core, WorkerAt(), ChestAt(Epoch), 3);
        Assert.True(seen.Add(first.Request.Value));
        process.Core.Executor.Execute(first, worker, chest);

        // Crash before any save: everything rolls back and is voided -- and the
        // next id is still new, because voided rows still count.
        CustodyProcess restarted = process.Restart();
        TransferIntent next = Intent(restarted.Core, WorkerAt(), ChestAt(Epoch), 3);
        Assert.True(seen.Add(next.Request.Value), next.Request.Value + " was used before the crash");
        Assert.True(restarted.Ledger.Revision >= process.Ledger.Revision);
    }

    [Fact]
    public void IdsStayValidSlugsForLongOrderIds()
    {
        var order = new TheConcernedCat.Settlement.Identity.OrderId(new string('a', 48));
        TheConcernedCat.Settlement.Identity.RequestId id = CustodyIds.Mint(order, "pick", 123456);

        Assert.True(id.Value.Length <= 48);
        Assert.EndsWith("-pick-123456", id.Value);
    }

    [Fact]
    public void ProgressCountsEveryUnitInExactlyOneBucket()
    {
        (FakeWorld world, CustodyProcess process, FakeInventory worker) = Gathered(20);
        FakeInventory cart = world.Inventory("cart", moves: true);
        FakeInventory chest = world.Inventory("chest", moves: true);
        Guid provider = Guid.NewGuid();
        Assert.True(process.Core.RecordCartBaseline(Order, "lease-1", "1:77", provider, Array.Empty<ItemCount>(), out _));
        var cartLocation = new CustodyLocation(CustodyPlace.Cart, "1:77", provider);

        process.Core.Executor.Execute(Intent(process.Core, WorkerAt(), cartLocation, 12), worker, cart);
        process.Core.Executor.Execute(Intent(process.Core, cartLocation, ChestAt(Epoch), 5), cart, chest);
        Assert.Equal(CustodyLedgerOutcome.Applied, process.Core.RecordLoss(Order, cartLocation, Stone, 2, "fell off", out _));

        ResourceProgress progress = process.Ledger.ProgressFor(Definition(), CollectedResource.Stone);
        Assert.Equal(8, progress.Carried);
        Assert.Equal(5, progress.InCart);
        Assert.Equal(5, progress.Delivered);
        Assert.Equal(2, progress.Lost);
        Assert.Equal(0, progress.OnGround);
        Assert.Equal(18, progress.Committed);
        Assert.Equal(2, progress.StillToCollect);
        Assert.False(progress.IsDelivered);
    }
}
