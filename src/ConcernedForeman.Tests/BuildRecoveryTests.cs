using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheConcernedCat.ConcernedForeman.Domain.Construction;
using TheConcernedCat.ConcernedForeman.Runtime.Construction;
using TheConcernedCat.ConcernedForeman.Runtime.Custody;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Orders;
using TheConcernedCat.Settlement.Register;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;
using UnityEngine;

namespace ConcernedForeman.Tests;

/// <summary>Disk reopen through the shipped order desk, journal, custody core,
/// material adapter, world sight, loop, authority gate and host installer.
/// Only the game, motion and the actor arbiter are stand-ins. No real saves.</summary>
public sealed class BuildRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cf-285-" + Guid.NewGuid().ToString("N"));
    private readonly JournalStore _store;
    private readonly SettlementScope _scope = new SettlementScope(285, new SettlementId("home"));
    private readonly WorkerBody _body;
    private readonly Container _chest;
    private readonly FakeMotion _motion = new FakeMotion(WorkerKey.Thorstein);
    private readonly FakeModes _modes = new FakeModes(WorkerKey.Thorstein);
    private readonly Installer _installer = new Installer();
    private readonly List<string> _log = new List<string>();
    private FakeCustody _custody = null!;
    private BuildOrderJournal _journal = null!;
    private BuildOrderRuntime _orders = null!;
    private ShelterConstructionRuntime _runtime = null!;
    private Guid _epoch = Guid.NewGuid();
    private double _time = 100;
    private bool _authority = true;
    private bool _recruited = true;
    private bool _supply = true;
    private SupplyChest _designation;
    private Func<SettlementJournal, bool>? _persistOverride;

    public BuildRecoveryTests()
    {
        Piece.s_allPieces.Clear();
        ZoneSystem.instance = new ZoneSystem();
        ZNetScene.instance = new ZNetScene();
        ObjectDB.instance = new ObjectDB();
        Game.instance ??= new Game();
        PrivateArea.Access = true;
        Character.Interiors.Clear();
        Location.NoBuild.Clear();
        CraftingStation.InRange.Clear();
        Heightmap.Here = Heightmap.Biome.Meadows;
        Player.m_localPlayer = new GameObject("player").Add(new Player());
        foreach (string prefab in ShelterBlueprint.Prefabs)
        {
            Piece piece = new GameObject(prefab).Add(new Piece());
            piece.m_resources = new[] { new Piece.Requirement
                { m_resItem = ForemanFixtures.Prefab("Wood").GetComponent<ItemDrop>(), m_amount = 2 } };
            ZNetScene.instance.Prefabs[prefab] = piece.gameObject;
        }
        _body = ForemanFixtures.Body();
        _chest = ForemanFixtures.Chest();
        _chest.transform.position = new Vector3(20, 0, 0);
        _chest.GetInventory()!.AddItem(ForemanFixtures.Stack(ForemanFixtures.Wood, 100));
        _designation = new SupplyChest(ForemanFixtures.KeyOf(_chest), new SitePoint(20, 0, 0));
        _store = new JournalStore(_root);
        Open(0);
    }

    private void Open(double loaded)
    {
        SettlementJournal record = _store.Load(_scope).Journal;
        _custody = new FakeCustody(new WorkerInventoryPort(_body, () => 1000), Port(_chest), record,
            j => _persistOverride != null ? _persistOverride(j) : _store.Save(j).Saved,
            _epoch, loaded, () => _time);
        _journal = new BuildOrderJournal(_custody.Core, ForemanFixtures.Worker, () => _recruited);
        _orders = new BuildOrderRuntime(() => _authority, () => "NoAuthority", _log.Add, () => _journal);
        _runtime = new ShelterConstructionRuntime(_orders, _modes, _custody, _motion, () => _authority,
            () => _supply ? _designation : default, () => (float)_time, _log.Add, new FakePose(), _installer);
        _orders.Refresh();
    }

    private ContainerInventoryPort Port(Container chest) =>
        new ContainerInventoryPort(chest, ForemanFixtures.KeyOf(chest), () => _motion.Position, 3);

    private void Confirm()
    {
        _orders.Execute(new[] { "here" });
        Assert.Contains("Authorised", _orders.Execute(new[] { "confirm" }));
        Assert.NotNull(_journal.Current);
    }

    private void Tick(int times = 1)
    {
        for (int i = 0; i < times; i++) { _time += 2; _runtime.Tick(); }
    }

    private void Until(Func<bool> reached)
    {
        for (int i = 0; i < 120 && !reached(); i++) Tick();
        Assert.True(reached(), _orders.Execute(new[] { "status" }));
    }

    private void SaveWorld() => _custody.Core.OnWorldSaveStarted(_time);

    private void Reopen(bool newWorldLoad = true, double? loaded = null)
    {
        _runtime.OnWorldUnloaded();
        _orders.Forget();
        if (newWorldLoad) { _epoch = Guid.NewGuid(); _supply = false; }
        Open(loaded ?? _time);
    }

    private int ChestWood => EngineInventoryPort.CountIn(_chest.GetInventory()!, ForemanFixtures.Wood);
    private int CarriedWood => EngineInventoryPort.CountIn(_body.Inventory!, ForemanFixtures.Wood);
    private Reservation[] Held => _journal.Reservations.Where(r => r.State == ReservationState.Held).ToArray();

    private SettlementRegister ActiveRegister()
    {
        var register = new SettlementRegister(_scope);
        register.UseIdentityEpoch(_epoch.ToString("N"));
        Assert.True(register.Restore(
            new Designation(DesignationKind.SettlementArea, default, 30, null)));
        Assert.True(register.Restore(new Designation(
            DesignationKind.SupplyContainer, _designation.At, 0,
            _designation.ContainerKey, _epoch.ToString("N"))));
        return register;
    }

    [Fact]
    public void Approved_marker_is_durable_before_the_first_tick_and_keeps_order_and_worker_identity()
    {
        Player.m_localPlayer!.transform.position = new Vector3(31.123456f, 4.5f, -8.25f);
        Player.m_localPlayer.transform.rotation = Quaternion.Euler(0, 123.5f, 0);
        Confirm();
        BuildOrderRecordedRow original = _journal.Current!;
        SaveWorld(); Reopen();
        Assert.True(_orders.IsAuthorised);
        Assert.Equal(original.Order, _journal.Current!.Order);
        Assert.Equal(original.Worker, _journal.Current.Worker);
        Assert.Equal(original.Payload, BuildOrderJournal.Encode(_orders.Plan()));
        Assert.Empty(_installer.Keys);
    }

    [Fact]
    public void Saved_phase_resumes_at_next_missing_piece_and_never_pays_or_places_standing_pieces_twice()
    {
        Confirm();
        Until(() => _installer.Keys.Count == 4 && Held.Length == 0);
        string order = _journal.Current!.Order.Value;
        SaveWorld(); Reopen();
        _supply = true; // A fresh explicit designation authorises NEW draws, with no old holding.
        Tick();
        Assert.Equal(order, _modes.JobId);
        Until(() => _runtime.Loop.Step == BuildStep.Finished);
        Assert.Equal(Enumerable.Range(0, 17).Select(i => i.ToString()), _installer.Keys);
        Assert.Equal(66, ChestWood);
        Assert.Equal(0, CarriedWood);
        Assert.Equal(17, _custody.Journal.Entries.Count(e => e.Kind == JournalEntryKind.CommitFinished));
        Assert.All(Player.m_localPlayer!.Placed, p => { Assert.Contains("attack=False", p); Assert.Contains("cheated=False", p); });
    }

    [Fact]
    public void A_player_built_piece_is_reread_after_reopen_and_skipped_without_a_reservation()
    {
        Confirm();
        CostedPiece first = _orders.Plan().Pieces[0];
        Stand(first.Placement);
        SaveWorld(); Reopen(); _supply = true;
        Until(() => _runtime.Loop.Step == BuildStep.Finished);
        Assert.DoesNotContain(first.Key, _installer.Keys);
        Assert.Equal(68, ChestWood);
        Assert.DoesNotContain(_journal.Reservations, r => r.Request.Equals(_journal.RequestFor(first)));
    }

    [Fact]
    public void Held_material_is_attributable_but_a_reloaded_source_is_never_guessed_even_if_redesignated()
    {
        Confirm(); Until(() => Held.Length > 0);
        Reservation[] held = Held;
        int chest = ChestWood, carried = CarriedWood;
        SaveWorld(); Reopen(); _supply = true;
        int sourceResolutions = 0;
        _custody.ResolveChest = _ => { sourceResolutions++; return Port(_chest); };
        Tick(10);
        Assert.Contains("SourceIdentityStale", _runtime.Describe());
        Assert.Equal(chest, ChestWood); Assert.Equal(carried, CarriedWood);
        Assert.Empty(_installer.Keys);
        Assert.Equal(held.Select(r => r.Request), Held.Select(r => r.Request));
        Assert.Equal(held.Select(r => r.ContainerEpoch), Held.Select(r => r.ContainerEpoch));
        Assert.Contains("withdrawn", _orders.Execute(new[] { "cancel" }));
        Tick(); SaveWorld(); Reopen(); Tick();
        Assert.False(_orders.IsAuthorised);
        Assert.Contains("no automatic refund", _runtime.Describe());
        Assert.DoesNotContain(_custody.Journal.Entries, e => e.Kind == JournalEntryKind.Refunded);
        Assert.Equal(0, sourceResolutions); // Even a recycled key cannot reach the inventory port.
    }

    [Fact]
    public void Same_load_reconstruction_restores_holdings_and_cancellation_returns_only_unspent_to_exact_source_once()
    {
        Confirm(); Until(() => _installer.Keys.Count == 1);
        int unspent = CarriedWood, chest = ChestWood;
        Assert.True(unspent > 0);
        SaveWorld(); Reopen(newWorldLoad: false);
        Container other = ForemanFixtures.Chest("0000000000000001:00000002");
        _designation = new SupplyChest(ForemanFixtures.KeyOf(other), default);
        _custody.ResolveChest = target => target.ContainerKey == ForemanFixtures.KeyOf(_chest) ? Port(_chest) : Port(other);
        _motion.Position = _chest.transform.position;
        _orders.Execute(new[] { "cancel" }); Tick(3);
        Assert.Equal(chest + unspent, ChestWood); Assert.Equal(0, CarriedWood);
        Assert.Equal(0, EngineInventoryPort.CountIn(other.GetInventory()!, ForemanFixtures.Wood));
        Assert.Single(_installer.Keys);
        Assert.Contains("went back where it came from", _orders.Execute(new[] { "status" }));
        int receipts = _custody.Journal.Entries.Count(e => e.Kind == JournalEntryKind.Refunded);
        SaveWorld(); Reopen(); Tick(10);
        Assert.False(_orders.IsAuthorised);
        Assert.Equal(receipts, _custody.Journal.Entries.Count(e => e.Kind == JournalEntryKind.Refunded));
        Assert.Equal(98, ChestWood);
    }

    [Theory]
    [InlineData("worker")]
    [InlineData("source")]
    [InlineData("authority")]
    [InlineData("recruitment")]
    public void Restart_waits_for_real_worker_source_recruitment_and_authority(string absent)
    {
        Confirm(); SaveWorld(); Reopen();
        _supply = absent != "source";
        _motion.IsPresent = absent != "worker";
        _authority = absent != "authority";
        _recruited = absent != "recruitment";
        Tick(10);
        Assert.Empty(_installer.Keys); Assert.Equal(100, ChestWood);
        Assert.True(_orders.IsAuthorised);
        _supply = _motion.IsPresent = _authority = _recruited = true;
        Until(() => _installer.Keys.Count > 0);
    }

    [Fact]
    public void Authority_loss_does_not_cancel_or_refund_and_return_requires_a_fresh_swing()
    {
        Confirm(); Until(() => CarriedWood > 0);
        int carried = CarriedWood;
        _authority = false; Tick(10);
        Assert.Equal(carried, CarriedWood); Assert.Null(_modes.JobId);
        Assert.DoesNotContain(_custody.Journal.Entries, e => e.Kind == JournalEntryKind.Refunded);
        _authority = true; Tick(); Assert.Empty(_installer.Keys);
        Until(() => _installer.Keys.Count > 0);
    }

    [Fact]
    public void New_approval_at_the_same_marker_gets_its_own_order_and_requests_after_refund()
    {
        Confirm(); Until(() => CarriedWood > 0);
        OrderId first = _journal.Current!.Order;
        _motion.Position = _chest.transform.position;
        _orders.Execute(new[] { "cancel" }); Tick();
        Assert.Empty(Held);
        Assert.Contains("Authorised", _orders.Execute(new[] { "confirm" }));
        Assert.NotEqual(first, _journal.Current!.Order);
        Until(() => _runtime.Loop.Step == BuildStep.Finished);
        Assert.Equal(66, ChestWood);
    }

    [Fact]
    public void A_proposed_replacement_does_not_change_the_cancelled_orders_recovery_payload()
    {
        Confirm(); Tick(); // The old order has a body hold, but has not drawn yet.
        _orders.Execute(new[] { "cancel" });
        Player.m_localPlayer!.transform.position = new Vector3(50, 0, 0);
        _orders.Execute(new[] { "here" });
        Tick();
        Assert.Empty(_orders.RecoveryRefusal);
        Assert.Null(_modes.JobId);
        Assert.Contains("Authorised", _orders.Execute(new[] { "confirm" }));
        Until(() => _installer.Keys.Count > 0);
        Assert.Equal(_journal.Current!.Order.Value, _motion.LastJob);
    }

    [Fact]
    public void Reapproval_between_ticks_releases_the_previous_actor_hold()
    {
        Confirm(); Tick();
        string previous = _modes.JobId!;
        Assert.NotNull(previous);
        _orders.Execute(new[] { "cancel" });
        _orders.Execute(new[] { "confirm" });
        string replacement = _journal.Current!.Order.Value;
        Assert.NotEqual(previous, replacement);
        Tick();
        Assert.Equal(replacement, _modes.JobId);
        Assert.Equal(replacement, _motion.LastJob);
        Assert.True(_modes.Releases > 0);
    }

    [Fact]
    public void Cancel_returns_unspent_custody_even_when_a_previously_paid_piece_is_missing()
    {
        Confirm(); Until(() => _installer.Keys.Count == 1);
        Piece.s_allPieces.Clear();
        Tick();
        Assert.Contains("SavedPieceMissing", _runtime.Describe());
        _motion.Position = _chest.transform.position;
        Assert.Contains("withdrawn", _orders.Execute(new[] { "cancel" }));
        Tick(3);
        Assert.Empty(Held);
        Assert.Equal(98, ChestWood);
        Assert.Equal(0, CarriedWood);
        Assert.Single(_installer.Keys);
    }

    [Fact]
    public void Finished_order_retries_only_unspent_returns_when_the_original_chest_becomes_reachable()
    {
        Confirm(); Until(() => CarriedWood > 0);
        foreach (CostedPiece piece in _orders.Plan().Pieces) Stand(piece.Placement);
        _motion.Position = Vector3.zero;
        Tick();
        Assert.Equal(BuildStep.Finished, _runtime.Loop.Step);
        Assert.NotEmpty(Held);
        Assert.Null(_modes.JobId);
        _motion.Position = _chest.transform.position;
        Tick(6);
        Assert.Empty(Held);
        Assert.Equal(100, ChestWood);
        Assert.Equal(0, CarriedWood);
        Assert.Empty(_installer.Keys);
        int refunds = _custody.Journal.Entries.Count(e => e.Kind == JournalEntryKind.Refunded);
        Tick(12);
        Assert.Equal(refunds, _custody.Journal.Entries.Count(e => e.Kind == JournalEntryKind.Refunded));
    }

    [Theory]
    [InlineData("authority")]
    [InlineData("worker")]
    [InlineData("other-job")]
    public void Finished_order_retains_unspent_custody_until_recovery_can_own_the_worker(string block)
    {
        Confirm(); Until(() => CarriedWood > 0);
        foreach (CostedPiece piece in _orders.Plan().Pieces) Stand(piece.Placement);
        _motion.Position = Vector3.zero;
        Tick();
        Assert.Equal(BuildStep.Finished, _runtime.Loop.Step);
        _motion.Position = _chest.transform.position;
        if (block == "authority") _authority = false;
        if (block == "worker") _motion.IsPresent = false;
        if (block == "other-job") _modes.Enter(ActorMode.Working, "another-job");
        Tick(12);
        Assert.Equal(8, CarriedWood);
        Assert.Equal(92, ChestWood);
        Assert.DoesNotContain(_custody.Journal.Entries, e => e.Kind == JournalEntryKind.Refunded);
        Assert.Empty(_installer.Keys);
        if (block == "other-job")
        {
            Assert.Equal("another-job", _modes.JobId);
            _modes.Release("another-job");
        }
        _authority = _motion.IsPresent = true;
        Tick(6);
        Assert.Empty(Held);
        Assert.Equal(100, ChestWood);
        Assert.Equal(0, CarriedWood);
        Assert.Null(_modes.JobId);
    }

    [Fact]
    public void Per_piece_refunds_do_not_withdraw_the_approved_build_in_the_register()
    {
        Confirm(); Until(() => CarriedWood > 0);
        foreach (CostedPiece piece in _orders.Plan().Pieces) Stand(piece.Placement);
        _motion.Position = _chest.transform.position;
        Tick();
        Assert.Empty(Held);
        Assert.True(_orders.IsAuthorised);
        SaveWorld(); Reopen(); // Replay must retain approval after all per-piece returns.
        Assert.True(_orders.IsAuthorised);
        ReplayResult state = _custody.Journal.Replay();
        Assert.False(OrderStateMachine.IsTerminal(state.StateOf(_journal.Current!.Order)));
        var register = new SettlementRegister(_scope);
        Assert.True(register.Restore(new Designation(DesignationKind.SettlementArea, default, 30, null)));
        Assert.Contains(_journal.Current.Order,
            register.PlanUndesignation(DesignationKind.SettlementArea, state, true).OrdersToCancel);
    }

    [Fact]
    public void A_new_proposal_after_withdrawal_does_not_replace_the_payload_used_for_cleanup()
    {
        Confirm(); Tick(); // Walking to supply, with no material drawn yet.
        Assert.Empty(Held);
        Assert.NotNull(_modes.JobId);
        string approved = _journal.Current!.Payload;
        _orders.Execute(new[] { "cancel" });
        Player.m_localPlayer!.transform.position = new Vector3(80, 0, 0);
        _orders.Execute(new[] { "here" });
        Tick();
        Assert.Equal(approved, BuildOrderJournal.Encode(_orders.ApprovedPlan()));
        Assert.Empty(_orders.RecoveryRefusal);
        Assert.Null(_modes.JobId);
        Assert.Contains("Authorised", _orders.Execute(new[] { "confirm" }));
        Until(() => _installer.Keys.Count == 1);
    }

    [Fact]
    public void Damage_to_a_paid_piece_blocks_rebuilding_but_not_return_of_proven_unspent_custody()
    {
        Confirm(); Until(() => _installer.Keys.Count == 1);
        Piece.s_allPieces.RemoveAt(0); // A player destroys the already paid floor.
        Tick();
        Assert.Contains("SavedPieceMissing", _runtime.Describe());
        Assert.Contains("withdrawn", _orders.Execute(new[] { "cancel" }));
        _motion.Position = _chest.transform.position;
        Tick(3);
        Assert.Equal(98, ChestWood); // Its spent cost is never refunded.
        Assert.Equal(0, CarriedWood);
        Assert.Empty(Held);
        Assert.Single(_installer.Keys); // No replacement is placed.
    }

    [Fact]
    public void Reopened_cancellation_reports_the_durable_spent_returned_and_retained_totals()
    {
        Confirm(); Until(() => _installer.Keys.Count == 1);
        _orders.Execute(new[] { "cancel" });
        _motion.Position = _chest.transform.position;
        Tick(); SaveWorld(); Reopen();
        string status = _orders.Execute(new[] { "status" });
        Assert.Contains("spent at placement: 2 Wood", status);
        Assert.Contains("returned to recorded sources: 6 Wood", status);
        Assert.Contains("held: nothing", status);
        Assert.Contains("uncertain: nothing", status);
    }

    [Fact]
    public void Same_load_reconstruction_during_a_phase_continues_each_request_exactly_once()
    {
        Confirm(); Until(() => _installer.Keys.Count == 1);
        Assert.Equal(6, CarriedWood);
        SaveWorld(); Reopen(newWorldLoad: false);
        Until(() => _runtime.Loop.Step == BuildStep.Finished);
        Assert.Equal(Enumerable.Range(0, 17).Select(i => i.ToString()), _installer.Keys);
        Assert.Equal(17, _journal.Reservations.Count());
        Assert.Equal(66, ChestWood);
        Assert.Equal(0, CarriedWood);
    }

    [Fact]
    public void Missing_worker_inventory_after_reconstruction_is_named_and_never_refunded_on_a_guess()
    {
        Confirm(); Until(() => CarriedWood > 0);
        SaveWorld(); Reopen(newWorldLoad: false);
        _custody.WorkerPort = null; Tick();
        Assert.Contains("WorkerInventoryUnavailable", _runtime.Describe());
        Assert.Empty(_installer.Keys);
        _custody.WorkerPort = new WorkerInventoryPort(_body, () => 1000);
        _custody.WorkerPort.Remove(ForemanFixtures.Wood, 1); Tick();
        Assert.Contains("HeldInventoryMismatch", _runtime.Describe());
        Assert.Empty(_installer.Keys);
    }

    [Theory]
    [InlineData("world")]
    [InlineData("settlement")]
    [InlineData("truncated")]
    public void A_corrupt_or_foreign_file_cannot_recover_authority(string damage)
    {
        Confirm();
        string path = _store.ResolvePath(_scope);
        if (damage == "truncated")
        {
            string[] lines = File.ReadAllLines(path);
            File.WriteAllLines(path, lines.Take(lines.Length - 1));
        }
        else
        {
            var foreign = new SettlementScope(damage == "world" ? 999 : _scope.WorldId,
                new SettlementId(damage == "settlement" ? "elsewhere" : "home"));
            File.Copy(path, _store.ResolvePath(foreign));
            Assert.True(_store.Load(foreign).ReadOnly);
            return;
        }
        Reopen(); Tick();
        Assert.Contains("RecordUnreadable", _runtime.Describe()); Assert.Empty(_installer.Keys);
    }

    [Fact]
    public void A_world_older_than_approval_itself_cannot_resume_the_marker()
    {
        Confirm(); Reopen(loaded: 50); _supply = true; Tick();
        Assert.Contains("WorldSaveRollback", _runtime.Describe()); Assert.Empty(_installer.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_existing_register_cascade_withdraws_durable_build_authority_live_and_after_reopen(bool alreadyBuilt)
    {
        Confirm();
        if (alreadyBuilt) Until(() => _installer.Keys.Count == 4 && Held.Length == 0);
        var register = new SettlementRegister(_scope);
        Assert.True(register.Restore(new Designation(DesignationKind.SettlementArea, default, 30, null)));
        UndesignationPlan clear = register.PlanUndesignation(DesignationKind.SettlementArea, _custody.Journal.Replay(), true);
        Assert.Contains(_journal.Current!.Order, clear.OrdersToCancel);
        Assert.Equal(UndesignationOutcome.Removed, register.ApplyUndesignation(clear, _custody.Journal, true));
        _store.Save(_custody.Journal);
        Tick(); Assert.False(_orders.IsAuthorised);
        int placed = _installer.Keys.Count;
        SaveWorld(); Reopen(); _supply = true; Tick(10);
        Assert.False(_orders.IsAuthorised); Assert.Equal(placed, _installer.Keys.Count);
    }

    [Fact]
    public void Clearing_supply_cancels_held_build_material_then_measured_cleanup_returns_it_exactly_once()
    {
        Confirm(); Until(() => CarriedWood > 0);
        Reservation[] held = Held;
        int heldWood = held.Sum(r => r.Stacks.Sum(stack => stack.Count));
        int chestBefore = ChestWood;
        Assert.Equal(heldWood, CarriedWood);

        SettlementRegister register = ActiveRegister();
        UndesignationPlan clear = register.PlanUndesignation(
            DesignationKind.SupplyContainer, _custody.Journal.Replay(), authorised: true);
        Assert.Contains(_journal.Current!.Order, clear.OrdersToCancel);
        Assert.Equal(held.Select(r => r.Request), clear.ToRefund.Select(r => r.Request));
        Assert.Equal(held.Select(r => r.Request),
            clear.PendingMeasuredReturns.Select(r => r.Request));
        Assert.Contains("production custody pending measured return", clear.Describe());
        Assert.Equal(UndesignationOutcome.Removed,
            register.ApplyUndesignation(clear, _custody.Journal, authorised: true));

        ReplayResult cancelled = _custody.Journal.Replay();
        Assert.Equal(OrderState.Cancelled, cancelled.StateOf(_journal.Current.Order));
        Assert.Empty(cancelled.MaterialRepairs);
        Assert.Equal(held.Select(r => r.Request), cancelled.Ledger.Reservations
            .Where(r => r.State == ReservationState.Held).Select(r => r.Request));
        Assert.DoesNotContain(_custody.Journal.Entries, e => e.Kind == JournalEntryKind.Refunded);
        Assert.True(_store.Save(_custody.Journal).Saved);

        _supply = false;
        _motion.Position = _chest.transform.position;
        Tick(6);
        Assert.False(_orders.IsAuthorised);
        Assert.Empty(Held);
        Assert.Equal(chestBefore + heldWood, ChestWood);
        Assert.Equal(0, CarriedWood);
        Assert.Empty(_installer.Keys);
        JournalEntry[] receipts = _custody.Journal.Entries
            .Where(e => e.Kind == JournalEntryKind.Refunded).ToArray();
        Assert.Equal(held.Length, receipts.Length);
        Assert.All(receipts, receipt => Assert.Contains(_custody.Journal.Entries, intent =>
            intent.Sequence < receipt.Sequence &&
            intent.Kind == JournalEntryKind.OrderTransition &&
            intent.Transition == OrderTransition.Cancel &&
            intent.Request.Equals(receipt.Request)));
        Assert.Empty(_custody.Journal.Replay().MaterialRepairs);
        Assert.Contains("went back where it came from", _orders.Execute(new[] { "status" }));

        SaveWorld(); Reopen(newWorldLoad: false); Tick(8);
        ReplayResult reopened = _custody.Journal.Replay();
        Assert.False(_orders.IsAuthorised);
        Assert.Equal(OrderState.Cancelled, reopened.StateOf(_journal.Current!.Order));
        Assert.Empty(reopened.MaterialRepairs);
        Assert.Empty(reopened.Ledger.Totals(ReservationState.Held));
        Assert.Equal(heldWood, reopened.Ledger.Totals(ReservationState.Refunded)["Wood"]);
        Assert.Equal(chestBefore + heldWood, ChestWood);
        Assert.Equal(0, CarriedWood);
        Assert.Empty(_installer.Keys);
        Assert.Equal(held.Length,
            _custody.Journal.Entries.Count(e => e.Kind == JournalEntryKind.Refunded));
    }

    [Fact]
    public void Clearing_settlement_cancels_but_retains_production_custody_when_binding_cannot_be_restored()
    {
        Confirm(); Until(() => CarriedWood > 0);
        Reservation[] held = Held;
        int totalWood = ChestWood + CarriedWood;
        int chestBefore = ChestWood;
        int carriedBefore = CarriedWood;

        SettlementRegister register = ActiveRegister();
        UndesignationPlan clear = register.PlanUndesignation(
            DesignationKind.SettlementArea, _custody.Journal.Replay(), authorised: true);
        Assert.Contains(_journal.Current!.Order, clear.OrdersToCancel);
        Assert.Contains("production custody pending measured return", clear.Describe());
        Assert.Equal(UndesignationOutcome.Removed,
            register.ApplyUndesignation(clear, _custody.Journal, authorised: true));

        ReplayResult cancelled = _custody.Journal.Replay();
        Assert.Equal(OrderState.Cancelled, cancelled.StateOf(_journal.Current.Order));
        Assert.Empty(cancelled.MaterialRepairs);
        Assert.DoesNotContain(_custody.Journal.Entries, e => e.Kind == JournalEntryKind.Refunded);
        Assert.True(_store.Save(_custody.Journal).Saved);

        // Production binds BuildOrderJournal to the standing settlement area.
        // Once that parent designation is gone, the lifecycle has no honest
        // restoration path, so cleanup must retain proven custody fail-closed.
        _recruited = false;
        _motion.Position = _chest.transform.position;
        Tick(6);
        Assert.False(_orders.IsAuthorised);
        Assert.Contains("WorkerOrSettlementUnavailable", _runtime.Describe());
        Assert.Equal(chestBefore, ChestWood);
        Assert.Equal(carriedBefore, CarriedWood);
        Assert.Equal(totalWood, ChestWood + CarriedWood);
        Assert.Equal(held.Select(r => r.Request), Held.Select(r => r.Request));
        Assert.DoesNotContain(_custody.Journal.Entries, e => e.Kind == JournalEntryKind.Refunded);
        Assert.Empty(_custody.Journal.Replay().MaterialRepairs);

        SaveWorld(); Reopen(newWorldLoad: false); Tick(8);
        ReplayResult reopened = _custody.Journal.Replay();
        Assert.False(_orders.IsAuthorised);
        Assert.Equal(OrderState.Cancelled, reopened.StateOf(_journal.Current!.Order));
        Assert.Empty(reopened.MaterialRepairs);
        Assert.Equal(chestBefore, ChestWood);
        Assert.Equal(carriedBefore, CarriedWood);
        Assert.Equal(totalWood, ChestWood + CarriedWood);
        Assert.Equal(held.Select(r => r.Request), Held.Select(r => r.Request));
        Assert.DoesNotContain(_custody.Journal.Entries, e => e.Kind == JournalEntryKind.Refunded);
    }

    [Fact]
    public void Unknown_approval_fields_fail_closed_in_the_real_file_reader()
    {
        Confirm();
        _journal.Current!.Unknown = new JournalFields().Add("unrecognized-authority", "yes");
        File.WriteAllLines(_store.ResolvePath(_scope), JournalStore.Serialize(_custody.Journal));
        Reopen(); Tick();
        Assert.Contains("RecordUnreadable", _runtime.Describe()); Assert.Empty(_installer.Keys);
    }

    [Fact]
    public void A_paid_piece_missing_from_the_loaded_world_never_authorises_a_second_payment()
    {
        Confirm(); Until(() => _installer.Keys.Count == 4 && Held.Length == 0);
        SaveWorld(); Piece.s_allPieces.RemoveAt(0); Reopen(); _supply = true; Tick(10);
        Assert.Contains("SavedPieceMissing", _runtime.Describe());
        Assert.Equal(4, _installer.Keys.Count); Assert.Equal(92, ChestWood);
    }

    [Fact]
    public void Unknown_standing_ground_is_a_wait_and_is_reread_before_resume()
    {
        Confirm(); Until(() => _installer.Keys.Count == 4 && Held.Length == 0);
        SaveWorld(); Reopen(); _supply = true;
        ZoneSystem.instance!.Unloaded.Add(ZoneSystem.Key(Piece.s_allPieces[0].transform.position));
        Tick(4); Assert.Equal(4, _installer.Keys.Count);
        Assert.Contains("StandingWorldUnavailable", _runtime.Describe());
        ZoneSystem.instance.Unloaded.Clear(); Until(() => _installer.Keys.Count == 5);
    }

    [Fact]
    public void Older_world_save_blocks_order_and_material_without_callbacks()
    {
        Confirm(); SaveWorld(); double saved = _time;
        Until(() => _installer.Keys.Count == 1);
        int chest = ChestWood, carried = CarriedWood;
        Reopen(loaded: saved); _supply = true; Tick(10);
        Assert.Contains("CustodyNeedsReconciliation", _runtime.Describe());
        Assert.Equal(chest, ChestWood); Assert.Equal(carried, CarriedWood); Assert.Single(_installer.Keys);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("reordered-pieces")]
    [InlineData("cost")]
    [InlineData("worker")]
    [InlineData("reordered-rows")]
    [InlineData("legacy")]
    public void Ambiguous_or_changed_approval_never_reaches_placement(string damage)
    {
        Confirm();
        BuildOrderRecordedRow old = _journal.Current!;
        if (damage == "duplicate") _custody.Core.Journal.TryRecord(old);
        else if (damage == "legacy")
            _custody.Journal.Append(JournalEntryKind.Reserved, new OrderId("legacy"), new RequestId("legacy-cost"),
                container: "old", stacks: new[] { new MaterialStack("Wood", 2) });
        else
        {
            string payload = old.Payload;
            string[] lines = payload.Split('\n');
            if (damage == "reordered-pieces") { (lines[1], lines[2]) = (lines[2], lines[1]); payload = string.Join("\n", lines); }
            if (damage == "cost") payload = payload.Replace("Wood=2", "Wood=3");
            var changed = new BuildOrderRecordedRow(old.Order,
                damage == "worker" ? new WorkerId("other") : old.Worker, old.Transition, payload);
            var record = new SettlementJournal(_scope);
            record.AppendCustody(changed, _time, _epoch);
            if (damage == "reordered-rows") record.Restore(new JournalEntry(0, changed, _time, _epoch));
            File.WriteAllLines(_store.ResolvePath(_scope), JournalStore.Serialize(record));
        }
        if (damage == "duplicate" || damage == "legacy") _store.Save(_custody.Journal);
        Reopen(); _supply = true; Tick(5);
        Assert.Empty(_installer.Keys); Assert.Equal(100, ChestWood);
        Assert.Contains("refus", _runtime.Describe(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_approval_or_withdrawal_write_cannot_change_durable_authority(bool cancel)
    {
        if (cancel) Confirm();
        else _orders.Execute(new[] { "here" });
        _persistOverride = _ => false;
        Assert.Contains("NotDurable", _orders.Execute(new[] { cancel ? "cancel" : "confirm" }));
        Assert.Equal(cancel, _orders.IsAuthorised);
        _persistOverride = null;
        Reopen(); Assert.Equal(cancel, _orders.IsAuthorised);
    }

    [Theory]
    [InlineData("draw-intent")]
    [InlineData("draw-receipt")]
    [InlineData("commit-intent")]
    [InlineData("commit-receipt")]
    [InlineData("refund-intent")]
    [InlineData("refund-receipt")]
    public void Reopen_at_every_material_transition_keeps_approval_and_never_repeats_uncertain_effects(string cut)
    {
        Confirm();
        bool refund = cut.StartsWith("refund", StringComparison.Ordinal);
        if (refund) { Until(() => CarriedWood > 0); _orders.Execute(new[] { "cancel" }); _motion.Position = _chest.transform.position; }
        bool cutReached = false;
        _persistOverride = record =>
        {
            JournalEntry last = record.Entries.Last();
            bool target = cut switch
            {
                "draw-intent" => JournalEntryKinds.IsMaterialIntent(last) && last.Transition == OrderTransition.Reserve,
                "draw-receipt" => last.Kind == JournalEntryKind.Reserved,
                "commit-intent" => last.Kind == JournalEntryKind.CommitStarted,
                "commit-receipt" => last.Kind == JournalEntryKind.CommitFinished,
                "refund-intent" => JournalEntryKinds.IsMaterialIntent(last) && last.Transition == OrderTransition.Cancel,
                _ => last.Kind == JournalEntryKind.Refunded,
            };
            if (!target) return _store.Save(record).Saved;
            cutReached = true;
            return false; // Deny persistence; the real writer decides whether an effect is allowed.
        };
        Until(() => cutReached);
        int chest = ChestWood, carried = CarriedWood, placements = _installer.Keys.Count;
        _persistOverride = null;
        SaveWorld(); Reopen(); _supply = true; Tick(10);
        Assert.Equal(!refund, _orders.IsAuthorised);
        // An intent refused before a draw has no effect and is safe to retry.
        if (cut == "draw-intent") Assert.True(_installer.Keys.Count > 0);
        else
        {
            Assert.Equal(chest, ChestWood); Assert.Equal(carried, CarriedWood);
            Assert.Equal(placements, _installer.Keys.Count);
            Assert.True(_journal.HasHeld);
        }
    }

    [Fact]
    public void Production_composition_supplies_the_durable_port_and_ticks_the_real_runtime()
    {
        string directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "ConcernedCatMods.sln"))) directory = Directory.GetParent(directory)!.FullName;
        string plugin = File.ReadAllText(Path.Combine(directory, "src/ConcernedForeman/Plugin.cs"));
        string custody = File.ReadAllText(Path.Combine(directory, "src/ConcernedForeman/Runtime/Custody/ForemanCustodyRuntime.cs"));
        Assert.Contains("() => custody.BuildOrders", plugin);
        Assert.Contains("_construction?.Tick();", plugin);
        Assert.Contains("BuildOrders = new BuildOrderJournal(_core, ToolWorker", custody);
        Assert.Contains("BuildOrders = null;", custody);
    }

    internal static void Stand(PiecePlacement placement)
    {
        var host = new GameObject(placement.Piece.Prefab);
        host.transform.position = new Vector3(placement.At.X, placement.At.Y, placement.At.Z);
        host.transform.rotation = Quaternion.Euler(0, placement.Yaw, 0);
        Piece.s_allPieces.Add(host.Add(new Piece()));
    }

    private sealed class Installer : IPieceInstaller
    {
        internal List<string> Keys { get; } = new List<string>();
        private readonly HostPlayerPieceInstaller _host = new HostPlayerPieceInstaller();
        public bool Install(in PiecePlacement placement, out string failure)
        {
            if (!_host.Install(in placement, out failure)) return false;
            Keys.Add(placement.Key); Stand(placement); return true;
        }
    }

    public void Dispose()
    {
        _runtime.OnWorldUnloaded();
        Piece.s_allPieces.Clear(); Player.m_localPlayer = null;
        WorkerBody.Loaded = null; WorkerBody.Died = null; WorkerBody.ErrorLog = null;
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
