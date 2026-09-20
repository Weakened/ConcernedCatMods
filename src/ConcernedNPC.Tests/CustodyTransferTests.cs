using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

public class CustodyTransferTests
{
    private readonly NpcWorldEpoch _world = Identities.AWorld();
    private readonly NpcCustodyLedger _ledger = new NpcCustodyLedger();
    private readonly FakeJournal _journal = new FakeJournal();

    private bool _authorised = true;
    private bool _mayWrite = true;

    public CustodyTransferTests()
    {
        _ledger.OpenJob(Custody.Job);
        _ledger.Acquire(Custody.Step(0), Custody.Body(_world), Custody.Timber, 10);
    }

    [Fact]
    public void The_intention_is_written_down_before_anything_moves()
    {
        var from = new FakeInventory("the body").With(Custody.Timber, 10);
        var to = new FakeInventory("the chest");

        NpcTransferReceipt receipt = Execute(5, from, to);

        Assert.Equal(NpcTransferOutcome.Completed, receipt.Outcome);
        Assert.Equal(2, _journal.Written.Count);
        Assert.StartsWith("intent", _journal.Written[0], StringComparison.Ordinal);
        Assert.StartsWith("receipt", _journal.Written[1], StringComparison.Ordinal);
    }

    [Fact]
    public void An_intention_that_could_not_be_written_moves_nothing()
    {
        _journal.RefuseIntent = true;
        var from = new FakeInventory("the body").With(Custody.Timber, 10);
        var to = new FakeInventory("the chest");

        NpcTransferReceipt receipt = Execute(5, from, to);

        Assert.Equal(NpcTransferOutcome.Refused, receipt.Outcome);
        Assert.Equal(0, from.Removes);
        Assert.Equal(0, to.Adds);
        Assert.Equal(10, _ledger.HoldingAt(Custody.Job, Custody.Body(_world), Custody.Timber));
    }

    [Fact]
    public void A_journal_that_throws_is_a_refusal_and_not_a_lost_transfer()
    {
        _journal.ThrowOnIntent = true;
        var from = new FakeInventory("the body").With(Custody.Timber, 10);
        var to = new FakeInventory("the chest");

        Assert.Equal(NpcTransferOutcome.Refused, Execute(5, from, to).Outcome);
        Assert.Equal(0, to.Adds);

        // And the same for the writability question itself, which is asked
        // first and over a file.
        _journal.ThrowOnIntent = false;
        _journal.ThrowOnWritable = true;
        Assert.Equal(NpcTransferOutcome.Refused, Execute(5, from, to).Outcome);
    }

    [Fact]
    public void The_destination_is_added_to_before_the_source_is_removed_from()
    {
        // The asymmetry that decides which failure is possible. A crash between
        // the two leaves a duplicate, which the persisted intent and the counts
        // expose and a person resolves; the other order loses real items with
        // no evidence left.
        var from = new FakeInventory("the body").With(Custody.Timber, 10);
        var to = new FakeInventory("the chest");
        to.WatchDuringAdd = () => from.Count(Custody.Timber);

        Execute(5, from, to);

        Assert.Equal(10, to.Observed);
        Assert.Equal(1, to.Adds);
        Assert.Equal(1, from.Removes);
    }

    [Fact]
    public void Only_what_the_destination_actually_gained_is_removed_from_the_source()
    {
        // The engine's add said five; the inventory kept three. Believing the
        // return value would destroy two units of the player's material.
        var from = new FakeInventory("the body").With(Custody.Timber, 10);
        var to = new FakeInventory("the chest") { SwallowOnAdd = 2 };

        NpcTransferReceipt receipt = Execute(5, from, to);

        Assert.Equal(NpcTransferOutcome.Partial, receipt.Outcome);
        Assert.Equal(3, receipt.Accepted);
        Assert.Equal(7, from.Count(Custody.Timber));
        Assert.Equal(3, to.Count(Custody.Timber));
        Assert.Equal(3, _ledger.HoldingAt(Custody.Job, Custody.Chest(_world), Custody.Timber));
        Assert.Equal(7, _ledger.HoldingAt(Custody.Job, Custody.Body(_world), Custody.Timber));
    }

    [Fact]
    public void A_source_that_will_not_give_the_units_up_is_uncertain_and_never_compensated()
    {
        // The duplicate: the chest gained five and the body kept its ten. That
        // is exactly the state the add-before-remove order makes possible, and
        // exactly the state a person has to settle.
        var from = new FakeInventory("the body") { RefuseToRemove = true }.With(Custody.Timber, 10);
        var to = new FakeInventory("the chest");

        NpcTransferReceipt receipt = Execute(5, from, to);

        Assert.Equal(NpcTransferOutcome.Uncertain, receipt.Outcome);
        Assert.Equal(0, receipt.Accepted);

        // Nothing is undone, nothing is credited, and the evidence says what
        // was seen on both sides.
        Assert.Equal(10, _ledger.HoldingAt(Custody.Job, Custody.Body(_world), Custody.Timber));
        Assert.Equal(0, _ledger.HoldingAt(Custody.Job, Custody.Chest(_world), Custody.Timber));
        Assert.Contains("10 -> 10", receipt.Evidence, StringComparison.Ordinal);
        Assert.Contains("0 -> 5", receipt.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void An_engine_that_throws_halfway_is_uncertain_with_the_fault_named()
    {
        var from = new FakeInventory("the body").With(Custody.Timber, 10);
        var to = new FakeInventory("the chest") { ThrowOnAdd = true };

        NpcTransferReceipt receipt = Execute(5, from, to);

        Assert.Equal(NpcTransferOutcome.Uncertain, receipt.Outcome);
        Assert.Contains("fault", receipt.Evidence, StringComparison.Ordinal);
        Assert.Equal(10, _ledger.HoldingAt(Custody.Job, Custody.Body(_world), Custody.Timber));
    }

    [Fact]
    public void A_result_that_could_not_be_written_down_is_uncertain_rather_than_applied()
    {
        // The units moved. The record does not say so. Only the next session's
        // reconciliation can settle it, and crediting it now would make the
        // record and the world disagree with nothing to notice.
        _journal.RefuseReceipt = true;
        var from = new FakeInventory("the body").With(Custody.Timber, 10);
        var to = new FakeInventory("the chest");

        NpcTransferReceipt receipt = Execute(5, from, to);

        Assert.Equal(NpcTransferOutcome.Uncertain, receipt.Outcome);
        Assert.Contains("could not be written down", receipt.Evidence, StringComparison.Ordinal);
        Assert.True(_ledger.TryGetTransfer(receipt.Request, out NpcTransferRecord record));
        Assert.Equal(NpcTransferStatus.Uncertain, record.Status);
        Assert.Equal(10, _ledger.HoldingAt(Custody.Job, Custody.Body(_world), Custody.Timber));
    }

    [Fact]
    public void An_inventory_that_cannot_be_counted_refuses_before_anything_moves()
    {
        var from = new FakeInventory("the body") { ThrowOnCount = true }.With(Custody.Timber, 10);
        var to = new FakeInventory("the chest");

        Assert.Equal(NpcTransferOutcome.Refused, Execute(5, from, to).Outcome);
        Assert.Equal(0, to.Adds);

        var negative = new FakeInventory("the body") { CountIsNegative = true };
        Assert.Equal(NpcTransferOutcome.Refused, Execute(5, negative, to).Outcome);
        Assert.Equal(0, to.Adds);
    }

    [Fact]
    public void A_destination_with_no_room_refuses_and_says_so()
    {
        var from = new FakeInventory("the body").With(Custody.Timber, 10);
        var to = new FakeInventory("the chest") { Room = 0 };

        NpcTransferReceipt receipt = Execute(5, from, to);

        Assert.Equal(NpcTransferOutcome.Refused, receipt.Outcome);
        Assert.Contains("no room", receipt.Evidence, StringComparison.Ordinal);
        Assert.Contains("the chest", receipt.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void A_destination_with_some_room_takes_what_fits()
    {
        var from = new FakeInventory("the body").With(Custody.Timber, 10);
        var to = new FakeInventory("the chest") { Room = 2 };

        NpcTransferReceipt receipt = Execute(5, from, to);

        Assert.Equal(NpcTransferOutcome.Partial, receipt.Outcome);
        Assert.Equal(2, receipt.Accepted);
        Assert.Equal(8, from.Count(Custody.Timber));
    }

    [Fact]
    public void An_unavailable_inventory_refuses_rather_than_failing()
    {
        var from = new FakeInventory("the body").With(Custody.Timber, 10);
        var to = new FakeInventory("the chest") { IsAvailable = false };

        Assert.Equal(NpcTransferOutcome.Refused, Execute(5, from, to).Outcome);
        Assert.Equal(NpcTransferOutcome.Refused, Execute(5, from, null!).Outcome);
    }

    [Fact]
    public void Work_that_is_not_authorised_moves_nothing()
    {
        _authorised = false;
        var from = new FakeInventory("the body").With(Custody.Timber, 10);
        var to = new FakeInventory("the chest");

        Assert.Equal(NpcTransferOutcome.Refused, Execute(5, from, to).Outcome);

        _authorised = true;
        _mayWrite = false;
        Assert.Equal(NpcTransferOutcome.Refused, Execute(5, from, to).Outcome);
        Assert.Equal(0, to.Adds);
    }

    [Fact]
    public void A_transfer_that_already_happened_is_answered_rather_than_repeated()
    {
        // The retry after an interruption. Whatever authority says now, a
        // transfer that happened has happened: refusing it here would leave the
        // record and the world disagreeing for a reason unrelated to either.
        var from = new FakeInventory("the body").With(Custody.Timber, 10);
        var to = new FakeInventory("the chest");
        var executor = new NpcTransferExecutor(_ledger, _journal, () => _authorised, () => _mayWrite);
        var intent = new NpcTransferIntent(
            Custody.Step(1), Custody.Body(_world), Custody.Chest(_world), Custody.Timber, 5, _ledger.Revision);

        Assert.Equal(NpcTransferOutcome.Completed, executor.Execute(intent, from, to).Outcome);

        _authorised = false;
        var again = new NpcTransferIntent(
            Custody.Step(1), Custody.Body(_world), Custody.Chest(_world), Custody.Timber, 5, 0);
        NpcTransferReceipt replay = executor.Execute(again, from, to);

        Assert.Equal(NpcTransferOutcome.AlreadySatisfied, replay.Outcome);
        Assert.Equal(5, replay.Accepted);
        Assert.Equal(1, to.Adds);
        Assert.Equal(5, to.Count(Custody.Timber));
    }

    [Fact]
    public void The_same_name_for_a_different_transfer_is_refused_and_moves_nothing()
    {
        var from = new FakeInventory("the body").With(Custody.Timber, 10);
        var to = new FakeInventory("the chest");
        var elsewhere = new FakeInventory("another chest");
        var executor = new NpcTransferExecutor(_ledger, _journal, () => _authorised, () => _mayWrite);

        executor.Execute(
            new NpcTransferIntent(
                Custody.Step(1), Custody.Body(_world), Custody.Chest(_world), Custody.Timber, 5,
                _ledger.Revision),
            from,
            to);

        NpcTransferReceipt reused = executor.Execute(
            new NpcTransferIntent(
                Custody.Step(1), Custody.Body(_world), Custody.Chest(_world, "chest-b"), Custody.Timber, 5, 0),
            from,
            elsewhere);

        Assert.Equal(NpcTransferOutcome.RejectedDifferentPayload, reused.Outcome);
        Assert.Equal(0, elsewhere.Adds);
    }

    [Fact]
    public void An_engine_that_moves_in_one_call_is_still_judged_on_the_counts()
    {
        var from = new FakeInventory("the body").With(Custody.Timber, 10);
        var to = new MovingChest(from);

        NpcTransferReceipt receipt = Execute(5, from, to);

        Assert.Equal(NpcTransferOutcome.Completed, receipt.Outcome);
        Assert.Equal(5, receipt.Accepted);
        Assert.Contains("one engine move", receipt.Evidence, StringComparison.Ordinal);
        Assert.Equal(5, from.Count(Custody.Timber));
    }

    [Fact]
    public void An_engine_move_that_only_half_worked_is_uncertain()
    {
        var from = new FakeInventory("the body").With(Custody.Timber, 10);
        var to = new MovingChest(from) { AddOnly = true };

        Assert.Equal(NpcTransferOutcome.Uncertain, Execute(5, from, to).Outcome);
        Assert.Equal(10, _ledger.HoldingAt(Custody.Job, Custody.Body(_world), Custody.Timber));
    }

    private NpcTransferReceipt Execute(int count, INpcInventoryPort from, INpcInventoryPort to)
    {
        var executor = new NpcTransferExecutor(_ledger, _journal, () => _authorised, () => _mayWrite);
        var intent = new NpcTransferIntent(
            Custody.Step(1), Custody.Body(_world), Custody.Chest(_world), Custody.Timber, count,
            _ledger.Revision);
        return executor.Execute(intent, from, to);
    }

    /// <summary>A chest whose engine moves items in one call, keeping their
    /// identity - and which can be told to do only half of it.</summary>
    private sealed class MovingChest : INpcInventoryPort, INpcInventoryMoveTarget
    {
        private readonly FakeInventory _source;
        private readonly FakeInventory _own = new FakeInventory("the chest");

        internal MovingChest(FakeInventory source)
        {
            _source = source;
        }

        internal bool AddOnly { get; set; }

        public string Describe => "the chest";

        public bool IsAvailable => true;

        public int Count(NpcMaterial material) => _own.Count(material);

        public int CanAccept(NpcMaterial material, int count) => count;

        public int Add(NpcMaterial material, int count) => throw new InvalidOperationException("never reached");

        public int Remove(NpcMaterial material, int count) => _own.Remove(material, count);

        public bool CanMoveFrom(INpcInventoryPort source) => ReferenceEquals(source, _source);

        public void MoveFrom(INpcInventoryPort source, NpcMaterial material, int count)
        {
            _own.Add(material, count);
            if (!AddOnly)
            {
                source.Remove(material, count);
            }
        }
    }
}
