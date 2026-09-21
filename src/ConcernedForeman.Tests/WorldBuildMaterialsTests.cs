using System;
using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.ConcernedForeman.Domain.Construction;
using TheConcernedCat.ConcernedForeman.Runtime.Construction;
using TheConcernedCat.ConcernedForeman.Runtime.Custody;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;
using UnityEngine;

namespace ConcernedForeman.Tests;

/// <summary>The shipped material adapter and journal writer over the real
/// inventory ports, with only the engine stubbed. No live gameplay is claimed.</summary>
public sealed class WorldBuildMaterialsTests : IDisposable
{
    private readonly List<string> _log = new List<string>();
    private readonly WorkerBody _body;
    private readonly Container _chest;
    private readonly FakeCustody _custody;
    private SupplyChest _supply;

    public WorldBuildMaterialsTests()
    {
        Game.instance ??= new Game();
        ForemanFixtures.Prefab("Wood");
        ForemanFixtures.Prefab("DeerHide", weight: 1f);
        _body = ForemanFixtures.Body();
        _chest = ForemanFixtures.Chest();
        _custody = new FakeCustody(new WorkerInventoryPort(_body, () => 300f),
            new ContainerInventoryPort(_chest, ForemanFixtures.KeyOf(_chest), () => Vector3.zero, 3f));
        _supply = new SupplyChest(ForemanFixtures.KeyOf(_chest), new SitePoint(4f, 0f, 0f));
    }

    public void Dispose()
    {
        WorkerBody.Loaded = null;
        WorkerBody.Died = null;
        WorkerBody.ErrorLog = null;
    }

    private WorldBuildMaterials Materials() => new(_custody, new WorkerKey("foreman", "thorstein"), () => _supply, _log.Add);
    private void PutInChest(string item, int count) =>
        _chest.GetInventory()!.AddItem(ForemanFixtures.Stack(new MaterialItem(item, 1, 0), count));
    private int InChest(string item = "Wood") => EngineInventoryPort.CountIn(_chest.GetInventory()!, new MaterialItem(item, 1, 0));
    private int OnWorker(string item = "Wood") => EngineInventoryPort.CountIn(_body.Inventory!, new MaterialItem(item, 1, 0));
    private static CostedPiece Piece(int index = 0, int wood = 2, int hide = 0)
    {
        var costs = new List<PieceCost> { new("Wood", wood) };
        if (hide > 0) costs.Add(new PieceCost("DeerHide", hide));
        var blueprint = new BlueprintPiece(index, "wood_wall", BuildPhase.Walls, 0, 0, 0, 0);
        return new CostedPiece(new PiecePlacement(blueprint, default, 0), PieceRecipe.Known("wood_wall", costs));
    }

    [Fact]
    public void A_phase_draw_records_each_whole_cost_and_same_piece_retry_draws_nothing()
    {
        PutInChest("Wood", 40);
        var materials = Materials();
        var pieces = new[] { Piece(0), Piece(1) };
        BuildDraw draw = materials.Draw(pieces);
        Assert.False(draw.IsRefused);
        Assert.Equal(4, draw.Drawn.UnitsOf("Wood"));
        Assert.Equal(36, InChest());
        Assert.Equal(4, OnWorker());
        Assert.Equal(2, _custody.Journal.Entries.Count(e => e.Kind == JournalEntryKind.Reserved));
        int rows = _custody.Journal.Entries.Count;
        Assert.True(materials.Draw(pieces).Drawn.IsEmpty);
        Assert.Equal(rows, _custody.Journal.Entries.Count);
        Assert.Equal(36, InChest());
    }

    [Fact]
    public void Short_supply_takes_only_complete_costs_and_never_an_uncommittable_partial_reservation()
    {
        PutInChest("Wood", 5);
        var draw = Materials().Draw(new[] { Piece(0), Piece(1), Piece(2) });
        Assert.Equal(4, draw.Drawn.UnitsOf("Wood"));
        Assert.Equal(2, draw.Short.UnitsOf("Wood"));
        Assert.Equal(1, InChest());
        Assert.Equal(4, OnWorker());
    }

    [Fact]
    public void Unwritable_record_takes_nothing()
    {
        PutInChest("Wood", 40);
        _custody.IsWritable = false;
        Assert.True(Materials().Draw(new[] { Piece() }).IsRefused);
        Assert.Equal(40, InChest());
        Assert.Equal(0, OnWorker());
        Assert.Empty(_custody.Journal.Entries);
    }

    [Fact]
    public void No_marked_chest_is_a_named_refusal()
    {
        _supply = default;
        var materials = Materials();
        Assert.False(materials.TrySupply(out _, out string refusal));
        Assert.Contains("cf_settle supply", refusal);
        Assert.True(materials.Draw(new[] { Piece() }).IsRefused);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Unavailable_inventory_refuses_before_a_journal_intent(bool worker)
    {
        if (worker) _custody.WorkerPort = null;
        else _custody.ChestPort = null;
        Assert.True(Materials().Draw(new[] { Piece() }).IsRefused);
        Assert.Empty(_custody.Journal.Entries);
    }

    [Fact]
    public void Unrecorded_items_of_the_same_kind_and_tools_are_not_building_credit_or_refunds()
    {
        _body.Inventory!.AddItem(ForemanFixtures.Stack(new MaterialItem("Wood", 1, 0), 10));
        _body.Inventory.AddItem(ForemanFixtures.Hammer());
        PutInChest("Wood", 4);
        var materials = Materials();
        materials.Draw(new[] { Piece() });
        Assert.Equal(2, materials.Carried.UnitsOf("Wood"));
        Assert.Equal(2, materials.PutBack(out _).UnitsOf("Wood"));
        Assert.Equal(10, OnWorker());
        Assert.Equal(4, InChest());
    }

    [Fact]
    public void Commit_is_started_before_placement_and_finished_only_after_the_measured_cost()
    {
        PutInChest("Wood", 10);
        PutInChest("DeerHide", 6);
        var materials = Materials();
        CostedPiece piece = Piece(wood: 8, hide: 4);
        materials.Draw(new[] { piece });
        int placements = 0;
        Assert.True(materials.Commit(piece, () =>
        {
            Assert.Equal(JournalEntryKind.CommitStarted, _custody.Journal.Entries.Last().Kind);
            Assert.True(_custody.Journal.IsSaved(_custody.Journal.Entries.Count - 1));
            Assert.Equal(8, OnWorker());
            placements++;
            return true;
        }, out MaterialTally spent, out string failure), failure);
        Assert.Equal(8, spent.UnitsOf("Wood"));
        Assert.Equal(4, spent.UnitsOf("DeerHide"));
        Assert.Equal(0, OnWorker());
        Assert.Equal(JournalEntryKind.CommitFinished, _custody.Journal.Entries.Last().Kind);
        Assert.True(materials.Commit(piece, () => { placements++; return true; }, out _, out _));
        Assert.Equal(1, placements); // double-commit plant
        Assert.True(materials.PutBack(out _).IsEmpty);
        Assert.Equal(2, InChest());
    }

    [Fact]
    public void A_piece_without_a_matching_reservation_cannot_place_or_spend()
    {
        var materials = Materials();
        Assert.False(materials.Commit(Piece(), () => throw new Exception("unauthorised place"), out _, out _));
        Assert.Empty(_custody.Journal.Entries);
    }

    [Fact]
    public void Changed_piece_payload_is_refused_for_both_draw_and_commit()
    {
        PutInChest("Wood", 20);
        var materials = Materials();
        materials.Draw(new[] { Piece() });
        Assert.True(materials.Draw(new[] { Piece(wood: 3) }).IsRefused);
        Assert.False(materials.Commit(Piece(wood: 3), () => throw new Exception("wrong cost"), out _, out _));
        Assert.Equal(18, InChest());
        Assert.Equal(2, OnWorker());
    }

    [Fact]
    public void Unconfirmed_piece_keeps_cost_and_replays_uncertain_instead_of_refunding()
    {
        PutInChest("Wood", 20);
        var materials = Materials();
        materials.Draw(new[] { Piece() });
        Assert.False(materials.Commit(Piece(), () => false, out MaterialTally paid, out _));
        Assert.True(paid.IsEmpty);
        Assert.Equal(2, OnWorker());
        Assert.True(_custody.Journal.Replay().Ledger.HasUncertainCustody);
        Assert.True(materials.PutBack(out _).IsEmpty);
        Assert.Equal(18, InChest());
    }

    [Fact]
    public void A_payment_whose_worker_persistence_fails_never_records_a_finished_commit()
    {
        PutInChest("Wood", 20);
        var materials = Materials();
        materials.Draw(new[] { Piece() });
        _custody.WorkerPort = new UnpersistedPort(new WorkerInventoryPort(_body, () => 300f));
        Assert.False(materials.Commit(Piece(), () => true, out MaterialTally paid, out string failure));
        Assert.Equal(2, paid.UnitsOf("Wood"));
        Assert.Contains("could not be made durable", failure);
        Assert.NotNull(materials.Uncertain);
        Assert.DoesNotContain(_custody.Journal.Entries, e => e.Kind == JournalEntryKind.CommitFinished);
        Assert.True(_custody.Journal.Replay().Ledger.HasUncertainCustody);
        Assert.True(materials.Draw(new[] { Piece(1) }).IsRefused);
        Assert.True(materials.PutBack(out _).IsEmpty);
    }

    [Fact]
    public void Cancel_returns_only_unspent_reservations_to_the_original_source_once()
    {
        PutInChest("Wood", 20);
        var materials = Materials();
        materials.Draw(new[] { Piece(0), Piece(1) });
        materials.Commit(Piece(0), () => true, out _, out _);
        string original = _supply.ContainerKey;
        _supply = new SupplyChest("other-chest", new SitePoint(99, 0, 0));
        Assert.Equal(2, materials.PutBack(out string failure).UnitsOf("Wood"));
        Assert.Empty(failure);
        Assert.Equal(original, _custody.LastContainer.ContainerKey);
        int rows = _custody.Journal.Entries.Count;
        Assert.True(materials.PutBack(out _).IsEmpty); // double-refund plant
        Assert.Equal(rows, _custody.Journal.Entries.Count);
        Assert.Equal(18, InChest());
        Assert.Equal(0, OnWorker());
        Assert.Single(_custody.Journal.Entries, e => e.Kind == JournalEntryKind.Refunded);
    }

    [Fact]
    public void Refund_cannot_move_a_partial_reservation_into_a_full_chest()
    {
        PutInChest("Wood", 50);
        var materials = Materials();
        materials.Draw(new[] { Piece(wood: 50) });
        Container tiny = ForemanFixtures.Chest(key: "0000000000000002:00000001", width: 1, height: 1);
        tiny.GetInventory()!.AddItem(ForemanFixtures.Stack(new MaterialItem("Wood", 1, 0), 49));
        _custody.ChestPort = new ContainerInventoryPort(tiny, ForemanFixtures.KeyOf(tiny), () => Vector3.zero, 3f);
        Assert.True(materials.PutBack(out string failure).IsEmpty);
        Assert.Contains("still in his own inventory", failure);
        Assert.Equal(50, OnWorker());
        Assert.DoesNotContain(_custody.Journal.Entries, e => e.Kind == JournalEntryKind.Refunded);
    }

    [Fact]
    public void A_partial_measured_draw_is_a_named_repair_and_cannot_be_repeated()
    {
        PutInChest("Wood", 20);
        _custody.WorkerPort = new PartialMovePort(new WorkerInventoryPort(_body, () => 300f));
        var materials = Materials();
        Assert.True(materials.Draw(new[] { Piece(wood: 8) }).IsRefused);
        Assert.Equal(4, OnWorker());
        Assert.Equal(16, InChest());
        Assert.True(_custody.Journal.Replay().Ledger.HasUncertainCustody);
        Assert.True(materials.Draw(new[] { Piece(wood: 8) }).IsRefused);
        Assert.True(materials.PutBack(out _).IsEmpty);
        Assert.Equal(4, OnWorker());
    }

    [Fact]
    public void Material_for_another_piece_does_not_pay_for_an_unreserved_rebuild()
    {
        PutInChest("Wood", 20);
        var materials = Materials();
        materials.Draw(new[] { Piece(0), Piece(1) });
        materials.Commit(Piece(0), () => true, out _, out _);
        Assert.Equal(2, materials.Carried.UnitsOf("Wood"));
        Assert.False(materials.IsReserved(Piece(0)));
        Assert.True(materials.IsReserved(Piece(1)));
        Assert.Equal(2, materials.Draw(new[] { Piece(0), Piece(1) }).Drawn.UnitsOf("Wood"));
        Assert.True(materials.IsReserved(Piece(0)));
        Assert.Equal(3, _custody.Journal.Entries.Count(e => e.Kind == JournalEntryKind.Reserved));
    }

    [Fact]
    public void Failure_to_count_after_payment_cannot_be_interpreted_as_an_empty_inventory()
    {
        PutInChest("Wood", 20);
        var materials = Materials();
        materials.Draw(new[] { Piece() });
        _custody.WorkerPort = new UncountableAfterRemoval(new WorkerInventoryPort(_body, () => 300f));
        Assert.False(materials.Commit(Piece(), () => true, out _, out _));
        Assert.DoesNotContain(_custody.Journal.Entries, e => e.Kind == JournalEntryKind.CommitFinished);
        Assert.True(_custody.Journal.Replay().Ledger.HasUncertainCustody);
    }

    private sealed class UncountableAfterRemoval : IInventoryPort
    {
        private readonly WorkerInventoryPort _real;
        private bool _removed;
        internal UncountableAfterRemoval(WorkerInventoryPort real) => _real = real;
        public string Describe => _real.Describe;
        public bool IsAvailable => _real.IsAvailable;
        public int Count(MaterialItem item) => _removed ? throw new InvalidOperationException("count failed") : _real.Count(item);
        public int CanAccept(MaterialItem item, int count) => _real.CanAccept(item, count);
        public int Add(MaterialItem item, int count) => _real.Add(item, count);
        public int Remove(MaterialItem item, int count)
        {
            int taken = _real.Remove(item, count);
            _removed = true;
            return taken;
        }
    }

    private sealed class PartialMovePort : IInventoryPort, IInventoryMoveTarget
    {
        private readonly WorkerInventoryPort _real;
        private readonly bool _duplicate;
        internal PartialMovePort(WorkerInventoryPort real, bool duplicate = false)
        {
            _real = real;
            _duplicate = duplicate;
        }
        public string Describe => _real.Describe;
        public bool IsAvailable => _real.IsAvailable;
        public int Count(MaterialItem item) => _real.Count(item);
        public int CanAccept(MaterialItem item, int count) => _real.CanAccept(item, count);
        public int Add(MaterialItem item, int count) => _real.Add(item, count);
        public int Remove(MaterialItem item, int count) => _real.Remove(item, count);
        public bool CanMoveFrom(IInventoryPort source) => _real.CanMoveFrom(source);
        public void MoveFrom(IInventoryPort source, MaterialItem item, int count)
        {
            if (_duplicate) _real.Engine!.AddItem(ForemanFixtures.Stack(item, count));
            else _real.MoveFrom(source, item, count / 2);
        }
    }

    [Fact]
    public void Disagreeing_inventory_deltas_leave_a_durable_repair_without_compensation()
    {
        PutInChest("Wood", 20);
        _custody.WorkerPort = new PartialMovePort(new WorkerInventoryPort(_body, () => 300f), duplicate: true);
        var materials = Materials();
        BuildDraw draw = materials.Draw(new[] { Piece() });
        Assert.True(draw.IsRefused);
        Assert.Contains("do not agree", draw.Refusal);
        Assert.NotNull(materials.Uncertain);
        Assert.True(_custody.Journal.Replay().Ledger.HasUncertainCustody);
        Assert.True(materials.PutBack(out _).IsEmpty);
        Assert.True(materials.Draw(new[] { Piece() }).IsRefused);
        Assert.Equal(20, InChest());
        Assert.Equal(2, OnWorker());
    }

    private sealed class UnpersistedPort : IInventoryPort, IInventoryMoveTarget
    {
        private readonly WorkerInventoryPort _real;

        internal UnpersistedPort(WorkerInventoryPort real)
        {
            _real = real;
        }

        public string Describe => _real.Describe;

        public bool IsAvailable => _real.IsAvailable;

        public int Count(MaterialItem item) => _real.Count(item);

        public int CanAccept(MaterialItem item, int count) => _real.CanAccept(item, count);

        public int Add(MaterialItem item, int count) => _real.Add(item, count);

        public int Remove(MaterialItem item, int count)
        {
            _real.Remove(item, count);
            throw new InvalidOperationException("the worker (foreman/thorstein) could not save what it carries");
        }

        public bool CanMoveFrom(IInventoryPort source) => _real.CanMoveFrom(source);

        public void MoveFrom(IInventoryPort source, MaterialItem item, int count) =>
            _real.MoveFrom(source, item, count);
    }

}
