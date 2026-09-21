using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Orders;

namespace Shared.Settlement.Tests;

public sealed class BuildMaterialJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cc-build-journal", Guid.NewGuid().ToString("N"));
    private readonly SettlementScope _scope = new(4242, new SettlementId("camp"));
    private readonly JournalStore _store;
    private SettlementJournal _journal;
    private CustodyCore _core;
    private readonly Reservation _reservation;
    private int _writes;
    private int _failWrite = -1;
    private bool _falseAfterSave;
    private bool _authority = true;
    private int _chest = 20;
    private int _worker;
    private int _pieceCost;
    private int _effects;

    public BuildMaterialJournalTests()
    {
        Directory.CreateDirectory(_root);
        _store = new JournalStore(_root);
        _journal = new SettlementJournal(_scope);
        Guid epoch = Guid.NewGuid();
        _core = Open(epoch, 0);
        _reservation = new Reservation(new RequestId("piece-0"), new OrderId("build-1"), "exact-source",
            new[] { new MaterialStack("Wood", 2) }, epoch.ToString("N"));
    }

    public void Dispose() => Directory.Delete(_root, true);

    private CustodyCore Open(Guid epoch, double time) => CustodyCore.Open(_journal, Persist, () => 10,
        new WorldLoad(time, epoch), () => _authority);

    private bool Persist()
    {
        if (++_writes == _failWrite) return false;
        Assert.True(_store.Save(_journal).Saved);
        return !_falseAfterSave;
    }

    private CustodyOutcome Run(string operation, Func<bool>? effect = null, Reservation? payload = null)
    {
        payload ??= _reservation;
        effect ??= () =>
        {
            _effects++;
            JournalEntry last = _store.Load(_scope).Journal.Entries.Last();
            Assert.Equal(operation == "commit" ? JournalEntryKind.CommitStarted : JournalEntryKind.OrderTransition, last.Kind);
            Assert.Equal(payload.Request, last.Request);
            switch (operation)
            {
                case "reserve": _chest -= 2; _worker += 2; break;
                case "commit": _worker -= 2; _pieceCost += 2; break;
                case "refund": _worker -= 2; _chest += 2; break;
            }
            return true;
        };
        return operation switch
        {
            "reserve" => _core.BuildMaterials.Reserve(payload, effect, out _),
            "commit" => _core.BuildMaterials.Commit(payload, effect, out _),
            "refund" => _core.BuildMaterials.Refund(payload, effect, out _),
            _ => throw new ArgumentException(operation),
        };
    }

    private void Prepare(string operation)
    {
        if (operation != "reserve") Assert.Equal(CustodyOutcome.Applied, Run("reserve"));
    }

    private void Reload(bool snapshot)
    {
        if (snapshot) _core.OnWorldSaveStarted(100);
        _journal = _store.Load(_scope).Journal;
        Assert.False(_journal.IsReadOnly);
        _core = Open(Guid.NewGuid(), snapshot ? 100 : 0);
    }

    private void Conserved() => Assert.Equal(20, _chest + _worker + _pieceCost);

    [Theory]
    [InlineData("reserve")]
    [InlineData("commit")]
    [InlineData("refund")]
    public void Duplicate_operations_before_and_after_disk_reload_do_not_run_the_effect_twice(string operation)
    {
        Prepare(operation);
        Assert.Equal(CustodyOutcome.Applied, Run(operation));
        int effects = _effects;
        int rows = _journal.Entries.Count;
        Assert.Equal(CustodyOutcome.AlreadySatisfied, Run(operation, () => throw new Exception("double-" + operation + " plant")));
        Assert.Equal(rows, _journal.Entries.Count);
        Reload(snapshot: true);
        Assert.Equal(CustodyOutcome.AlreadySatisfied, Run(operation, () => throw new Exception("reloaded double-" + operation + " plant")));
        Assert.Equal(effects, _effects);
        Conserved();
        Assert.StartsWith("#\tsettlement journal v3", File.ReadAllText(_store.ResolvePath(_scope)));
    }

    [Theory]
    [InlineData("reserve")]
    [InlineData("commit")]
    [InlineData("refund")]
    public void Failure_to_persist_intent_runs_nothing_and_the_same_request_can_retry(string operation)
    {
        Prepare(operation);
        _failWrite = _writes + 1;
        int effects = _effects;
        Assert.Equal(CustodyOutcome.Rejected, Run(operation));
        Assert.Equal(effects, _effects);
        Assert.False(_core.BuildMaterials.NeedsRepair);
        Assert.Equal(CustodyOutcome.Applied, Run(operation));
        Conserved();
    }

    [Theory]
    [InlineData("reserve", false)]
    [InlineData("reserve", true)]
    [InlineData("commit", false)]
    [InlineData("commit", true)]
    [InlineData("refund", false)]
    [InlineData("refund", true)]
    public void Interruption_after_intent_never_reexecutes_or_guesses_on_replay(string operation, bool mutate)
    {
        Prepare(operation);
        Assert.Equal(CustodyOutcome.Rejected, Run(operation, () =>
        {
            if (mutate)
            {
                if (operation == "reserve") { _chest -= 2; _worker += 2; }
                if (operation == "commit") { _worker -= 2; _pieceCost += 2; }
                if (operation == "refund") { _worker -= 2; _chest += 2; }
            }
            throw new InvalidOperationException("killed after intent");
        }));
        // Even a later world save cannot turn a missing receipt into success.
        Reload(snapshot: true);
        Assert.Equal(ReservationState.Uncertain, Assert.Single(_core.BuildMaterials.Ledger.Reservations).State);
        Assert.Equal(CustodyOutcome.Rejected, Run(operation, () => throw new Exception("duplicate plant")));
        string repair = string.Join(" ", _core.BuildMaterials.Reconcile());
        Assert.Contains("build-1", repair);
        Assert.Contains("piece-0", repair);
        Assert.Contains("exact-source", repair);
        Assert.Contains("Repair required", repair);
        Conserved();
    }

    [Theory]
    [InlineData("reserve")]
    [InlineData("commit")]
    [InlineData("refund")]
    public void Missing_receipt_after_the_real_effect_is_uncertain_live_and_after_reload(string operation)
    {
        Prepare(operation);
        _failWrite = _writes + 2;
        Assert.Equal(CustodyOutcome.Rejected, Run(operation));
        int effects = _effects;
        Assert.True(_core.BuildMaterials.NeedsRepair);
        Reload(snapshot: true);
        Assert.Equal(CustodyOutcome.Rejected, Run(operation));
        Assert.Equal(effects, _effects);
        Assert.True(_core.BuildMaterials.Ledger.HasUncertainCustody);
        Conserved();
    }

    [Theory]
    [InlineData("reserve")]
    [InlineData("commit")]
    [InlineData("refund")]
    public void Save_reporting_failure_after_replace_is_still_satisfied_once(string operation)
    {
        Prepare(operation);
        _falseAfterSave = true;
        Assert.Equal(CustodyOutcome.Applied, Run(operation));
        Assert.Equal(CustodyOutcome.AlreadySatisfied, Run(operation));
        Conserved();
    }

    [Theory]
    [InlineData("reserve")]
    [InlineData("commit")]
    [InlineData("refund")]
    public void Same_request_with_changed_order_source_epoch_or_cost_is_rejected(string operation)
    {
        Prepare(operation);
        Assert.Equal(CustodyOutcome.Applied, Run(operation));
        Reservation r = _reservation;
        var changed = new[]
        {
            new Reservation(r.Request, new OrderId("other-build"), r.Container, r.Stacks, r.ContainerEpoch),
            new Reservation(r.Request, r.Order, "other-chest", r.Stacks, r.ContainerEpoch),
            new Reservation(r.Request, r.Order, r.Container, r.Stacks, Guid.NewGuid().ToString("N")),
            new Reservation(r.Request, r.Order, r.Container, new[] { new MaterialStack("Wood", 3) }, r.ContainerEpoch),
        };
        foreach (Reservation payload in changed)
            Assert.Equal(CustodyOutcome.Rejected, Run(operation, () => throw new Exception("different payload plant"), payload));
        Reload(snapshot: true);
        foreach (Reservation payload in changed)
            Assert.Equal(CustodyOutcome.Rejected, Run(operation, () => throw new Exception("reloaded payload plant"), payload));
        Conserved();
    }

    [Theory]
    [InlineData("commit", "refund")]
    [InlineData("refund", "commit")]
    public void A_settled_reservation_cannot_be_settled_the_other_way(string first, string second)
    {
        Prepare(first);
        Assert.Equal(CustodyOutcome.Applied, Run(first));
        Reload(snapshot: true);
        Assert.Equal(CustodyOutcome.Rejected, Run(second, () => throw new Exception("double settlement plant")));
        Conserved();
    }

    [Theory]
    [InlineData("reserve")]
    [InlineData("commit")]
    [InlineData("refund")]
    public void World_rollback_keeps_the_request_uncertain_and_never_repeats_it(string operation)
    {
        Prepare(operation);
        Assert.Equal(CustodyOutcome.Applied, Run(operation));
        Reload(snapshot: false);
        Assert.Equal(CustodyOutcome.Rejected, Run(operation, () => throw new Exception("rollback replay plant")));
        Assert.True(_core.BuildMaterials.NeedsRepair);
    }

    [Fact]
    public void A_held_reservation_after_reload_names_the_original_source_and_requires_repair()
    {
        Assert.Equal(CustodyOutcome.Applied, Run("reserve"));
        Reload(snapshot: true);
        Assert.True(_core.BuildMaterials.NeedsRepair);
        Assert.Equal(CustodyOutcome.Rejected, Run("refund"));
        Assert.Contains("source binding", string.Join(" ", _core.BuildMaterials.Reconcile()));
    }

    [Theory]
    [InlineData("reserve")]
    [InlineData("commit")]
    [InlineData("refund")]
    public void Missing_authority_refuses_before_any_row_or_effect(string operation)
    {
        Prepare(operation);
        _authority = false;
        int rows = _journal.Entries.Count;
        Assert.Equal(CustodyOutcome.Rejected, Run(operation));
        Assert.Equal(rows, _journal.Entries.Count);
        Conserved();
    }

    [Fact]
    public void A_commit_receipt_with_another_payload_cannot_close_the_started_commit()
    {
        Run("reserve");
        _core.Journal.TryRecordMaterial(JournalEntryKind.CommitStarted, _reservation);
        var changed = new Reservation(_reservation.Request, _reservation.Order, "wrong-source", _reservation.Stacks,
            _reservation.ContainerEpoch);
        _core.Journal.TryRecordMaterial(JournalEntryKind.CommitFinished, changed);
        ReplayResult replay = _store.Load(_scope).Journal.Replay();
        Assert.True(replay.Ledger.HasUncertainCustody);
        Assert.True(replay.NeedsRepair);
    }
    [Fact]
    public void A_kill_between_piece_creation_and_payment_is_not_refunded_or_repeated()
    {
        Run("reserve");
        Assert.Equal(CustodyOutcome.Rejected, Run("commit", () =>
        {
            _pieceCost += 2;
            throw new InvalidOperationException("killed before payment");
        }));
        Reload(snapshot: true);
        Assert.Equal(2, _worker);
        Assert.Equal(2, _pieceCost);
        Assert.Equal(18, _chest);
        Assert.True(_core.BuildMaterials.Ledger.HasUncertainCustody);
        Assert.Equal(CustodyOutcome.Rejected, Run("commit"));
        Assert.Equal(CustodyOutcome.Rejected, Run("refund"));
        // The disagreement is real; manufacturing an automatic compensation
        // would conceal it. The original two halves remain for named repair.
        Assert.Equal(2, _worker);
        Assert.Equal(2, _pieceCost);
        Assert.Equal(18, _chest);
    }

    [Theory]
    [InlineData("reserve")]
    [InlineData("refund")]
    public void A_kill_between_the_two_inventory_mutations_never_moves_either_side_again(string operation)
    {
        Prepare(operation);
        Assert.Equal(CustodyOutcome.Rejected, Run(operation, () =>
        {
            if (operation == "reserve") _worker += 2;
            else _chest += 2;
            throw new InvalidOperationException("destination changed; source did not");
        }));
        int chest = _chest, worker = _worker;
        Reload(snapshot: true);
        Assert.True(_core.BuildMaterials.Ledger.HasUncertainCustody);
        Assert.Equal(CustodyOutcome.Rejected, Run(operation));
        Assert.Equal(chest, _chest);
        Assert.Equal(worker, _worker);
    }

    [Fact]
    public void A_malformed_settlement_payload_is_a_named_repair_not_a_replay_exception()
    {
        Run("reserve");
        _journal.Append(JournalEntryKind.CommitFinished, _reservation.Order, _reservation.Request,
            container: "exact-source");
        Assert.True(_journal.Replay().NeedsRepair);
    }

    [Fact]
    public void A_refund_receipt_for_another_order_does_not_settle_the_original_request()
    {
        Run("reserve");
        _core.Journal.TryRecordMaterial(JournalEntryKind.OrderTransition, _reservation, OrderTransition.Cancel);
        _journal.Append(JournalEntryKind.Refunded, new OrderId("wrong-order"), _reservation.Request);
        ReplayResult replay = _journal.Replay();
        Assert.True(replay.Ledger.HasUncertainCustody);
        Assert.True(replay.NeedsRepair);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Payload_free_receipts_settle_only_legacy_reservations(bool production, bool refund)
    {
        if (production) Run("reserve");
        else _journal.Append(JournalEntryKind.Reserved, _reservation.Order, _reservation.Request,
            container: _reservation.Container, stacks: _reservation.Stacks);
        if (refund && production)
            Assert.True(_core.Journal.TryRecordMaterial(JournalEntryKind.OrderTransition, _reservation, OrderTransition.Cancel));
        if (!refund)
        {
            if (production) Assert.True(_core.Journal.TryRecordMaterial(JournalEntryKind.CommitStarted, _reservation));
            else _journal.Append(JournalEntryKind.CommitStarted, _reservation.Order, _reservation.Request);
        }
        _journal.Append(refund ? JournalEntryKind.Refunded : JournalEntryKind.CommitFinished,
            _reservation.Order, _reservation.Request);
        Assert.True(Persist());

        ReplayResult replay = _store.Load(_scope).Journal.Replay();
        Assert.True(replay.Ledger.TryGet(_reservation.Request, out Reservation recorded));
        Assert.Equal(production ? ReservationState.Uncertain :
            refund ? ReservationState.Refunded : ReservationState.Committed, recorded.State);
        Assert.Equal(production, replay.NeedsRepair);
        if (!production) return;

        Assert.Contains(replay.MaterialRepairs, line => line.Contains(_reservation.Request.Value));
        Reload(snapshot: true);
        Assert.True(_core.BuildMaterials.NeedsRepair);
        Assert.Equal(CustodyOutcome.Rejected, Run(refund ? "refund" : "commit",
            () => throw new Exception("payload-free receipt retry plant")));
        Conserved();
    }

    [Fact]
    public void Refreshing_a_legacy_refund_preserves_a_live_production_repair()
    {
        var legacy = new Reservation(new RequestId("old-proof"), new OrderId("old-order"), "old-source",
            new[] { new MaterialStack("Wood", 3) }, _reservation.ContainerEpoch);
        _journal.Append(JournalEntryKind.Reserved, legacy.Order, legacy.Request,
            container: legacy.Container, stacks: legacy.Stacks, containerEpoch: legacy.ContainerEpoch);
        Assert.True(Persist());
        _core = Open(_core.Load.LoadEpoch, 0);
        Assert.Equal(CustodyOutcome.Applied, Run("reserve"));
        Assert.Equal(CustodyOutcome.Rejected, Run("commit", () => false));
        _journal.Append(JournalEntryKind.Refunded, legacy.Order, legacy.Request);
        // This second receipt is invalid for the production request.
        _journal.Append(JournalEntryKind.Refunded, _reservation.Order, _reservation.Request);
        Assert.True(Persist());

        Assert.True(_core.BuildMaterials.Ledger.TryGet(legacy.Request, out Reservation returned));
        Assert.Equal(ReservationState.Refunded, returned.State);
        Assert.True(_core.BuildMaterials.Ledger.TryGet(_reservation.Request, out Reservation uncertain));
        Assert.Equal(ReservationState.Uncertain, uncertain.State);
        Assert.True(_core.BuildMaterials.NeedsRepair);
        Assert.Contains(_core.BuildMaterials.Reconcile(), line => line.Contains(_reservation.Request.Value));
        Assert.Equal(CustodyOutcome.Rejected, Run("refund", () => throw new Exception("guessed refund")));
        Assert.True(_journal.Replay().Ledger.HasUncertainCustody);
        Conserved();
    }

}
