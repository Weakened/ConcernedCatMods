using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Domain.Construction;
using TheConcernedCat.ConcernedForeman.Runtime.Construction;
using TheConcernedCat.ConcernedForeman.Runtime.Custody;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;
using UnityEngine;

namespace ConcernedForeman.Tests;

/// <summary>The build order's material adapter, against the real inventory
/// ports: out of the chest the player marked, into the worker's own persisted
/// inventory, and out again when a piece has gone up.
///
/// <b>Every assertion is a measured delta.</b> What the adapter reports has to
/// agree with what the two vanilla inventories actually hold afterwards - that is
/// the whole of its contract, and the reason it does not trust what an add or a
/// remove returned.</summary>
public sealed class WorldBuildMaterialsTests : IDisposable
{
    private readonly List<string> _log = new List<string>();
    private readonly WorkerBody _body;
    private readonly Container _chest;
    private readonly FakeCustody _custody;
    private readonly SupplyChest _supply;

    public WorldBuildMaterialsTests()
    {
        Game.instance ??= new Game();
        ForemanFixtures.Prefab("Wood");
        ForemanFixtures.Prefab("DeerHide", weight: 1f);
        _body = ForemanFixtures.Body();
        _chest = ForemanFixtures.Chest();
        _custody = new FakeCustody(
            new WorkerInventoryPort(_body, () => 300f),
            new ContainerInventoryPort(_chest, ForemanFixtures.KeyOf(_chest), () => Vector3.zero, 3f));
        _supply = new SupplyChest(ForemanFixtures.KeyOf(_chest), new SitePoint(4f, 0f, 0f));
    }

    public void Dispose()
    {
        WorkerBody.Loaded = null;
        WorkerBody.Died = null;
        WorkerBody.ErrorLog = null;
    }

    private WorldBuildMaterials Materials(params string[] kinds) => new WorldBuildMaterials(
        _custody,
        new WorkerKey("foreman", "thorstein"),
        () => _supply,
        () => kinds.Length == 0 ? new[] { "Wood", "DeerHide" } : kinds,
        _log.Add);

    private void PutInChest(string item, int count) =>
        _chest.GetInventory()!.AddItem(ForemanFixtures.Stack(new MaterialItem(item, 1, 0), count));

    private int InChest(string item) =>
        EngineInventoryPort.CountIn(_chest.GetInventory()!, new MaterialItem(item, 1, 0));

    private int OnWorker(string item) =>
        EngineInventoryPort.CountIn(_body.Inventory!, new MaterialItem(item, 1, 0));

    private static MaterialTally Want(params (string Item, int Amount)[] lines)
    {
        var tally = new MaterialTally();
        foreach ((string item, int amount) in lines)
        {
            tally.Add(item, amount);
        }

        return tally;
    }

    // ---- the draw --------------------------------------------------------

    [Fact]
    public void A_draw_moves_exactly_what_it_reports_and_the_chest_loses_exactly_that()
    {
        PutInChest("Wood", 40);

        BuildDraw draw = Materials().Draw(Want(("Wood", 18)));

        Assert.False(draw.IsRefused);
        Assert.Equal(18, draw.Drawn.UnitsOf("Wood"));
        Assert.True(draw.Short.IsEmpty);
        Assert.Equal(18, OnWorker("Wood"));
        Assert.Equal(22, InChest("Wood"));
    }

    [Fact]
    public void A_chest_that_is_short_reports_what_moved_and_what_is_missing()
    {
        PutInChest("Wood", 5);

        BuildDraw draw = Materials().Draw(Want(("Wood", 18)));

        Assert.False(draw.IsRefused);
        Assert.Equal(5, draw.Drawn.UnitsOf("Wood"));
        Assert.Equal(13, draw.Short.UnitsOf("Wood"));
        Assert.Equal(5, OnWorker("Wood"));
        Assert.Equal(0, InChest("Wood"));
    }

    [Fact]
    public void A_record_that_cannot_be_written_takes_nothing_out_of_the_chest()
    {
        PutInChest("Wood", 40);
        _custody.IsWritable = false;

        BuildDraw draw = Materials().Draw(Want(("Wood", 18)));

        Assert.True(draw.IsRefused);
        Assert.Contains("record cannot be written", draw.Refusal);
        Assert.Equal(40, InChest("Wood"));
        Assert.Equal(0, OnWorker("Wood"));
    }

    [Fact]
    public void No_marked_chest_is_a_refusal_and_never_a_different_chest()
    {
        PutInChest("Wood", 40);
        var materials = new WorldBuildMaterials(
            _custody,
            new WorkerKey("foreman", "thorstein"),
            () => default,
            () => new[] { "Wood" },
            _log.Add);

        Assert.False(materials.TrySupply(out SitePoint _, out string refusal));
        Assert.Contains("cf_settle supply", refusal);
        Assert.True(materials.Draw(Want(("Wood", 4))).IsRefused);
        Assert.Equal(40, InChest("Wood"));
    }

    [Fact]
    public void A_chest_custody_will_not_open_is_a_refusal_naming_the_reason()
    {
        _custody.ChestPort = null;

        BuildDraw draw = Materials().Draw(Want(("Wood", 4)));

        Assert.True(draw.IsRefused);
        Assert.Contains("marked supply chest could not be opened", draw.Refusal);
    }

    [Fact]
    public void A_worker_custody_will_not_open_is_a_refusal_naming_the_reason()
    {
        _custody.WorkerPort = null;

        BuildDraw draw = Materials().Draw(Want(("Wood", 4)));

        Assert.True(draw.IsRefused);
        Assert.Contains("own inventory could not be opened", draw.Refusal);
    }

    [Fact]
    public void Only_the_orders_own_kinds_are_counted_as_carried()
    {
        PutInChest("Wood", 10);
        WorldBuildMaterials materials = Materials("Wood");
        materials.Draw(Want(("Wood", 10)));

        // A hammer in his hand is not building material and never becomes part of
        // what the order thinks it is carrying.
        _body.Inventory!.AddItem(ForemanFixtures.Hammer());

        Assert.Equal(1, materials.Carried.Kinds);
        Assert.Equal(10, materials.Carried.UnitsOf("Wood"));
    }

    // ---- the spend -------------------------------------------------------

    [Fact]
    public void A_spend_takes_the_pieces_real_cost_and_nothing_else()
    {
        PutInChest("Wood", 20);
        PutInChest("DeerHide", 10);
        WorldBuildMaterials materials = Materials();
        materials.Draw(Want(("Wood", 20), ("DeerHide", 10)));

        bool spent = materials.Spend(
            PieceRecipe.Known("bed", new[] { new PieceCost("Wood", 8), new PieceCost("DeerHide", 4) }),
            out MaterialTally paid,
            out string failure);

        Assert.True(spent, failure);
        Assert.Equal(8, paid.UnitsOf("Wood"));
        Assert.Equal(4, paid.UnitsOf("DeerHide"));
        Assert.Equal(12, OnWorker("Wood"));
        Assert.Equal(6, OnWorker("DeerHide"));
    }

    [Fact]
    public void A_spend_he_cannot_afford_takes_nothing_at_all()
    {
        PutInChest("Wood", 4);
        PutInChest("DeerHide", 10);
        WorldBuildMaterials materials = Materials();
        materials.Draw(Want(("Wood", 4), ("DeerHide", 10)));

        bool spent = materials.Spend(
            PieceRecipe.Known("bed", new[] { new PieceCost("Wood", 8), new PieceCost("DeerHide", 4) }),
            out MaterialTally paid,
            out string failure);

        // The partial spend is the failure that matters: the hide must not be
        // gone for a bed nobody could pay the wood for.
        Assert.False(spent);
        Assert.Contains("carrying only 4 Wood", failure);
        Assert.True(paid.IsEmpty);
        Assert.Equal(4, OnWorker("Wood"));
        Assert.Equal(10, OnWorker("DeerHide"));
    }

    [Fact]
    public void An_unreadable_recipe_spends_nothing()
    {
        PutInChest("Wood", 20);
        WorldBuildMaterials materials = Materials();
        materials.Draw(Want(("Wood", 20)));

        Assert.False(materials.Spend(
            PieceRecipe.Unknown("wood_wall", "no such piece"), out MaterialTally paid, out string failure));
        Assert.Contains("never established", failure);
        Assert.True(paid.IsEmpty);
        Assert.Equal(20, OnWorker("Wood"));
    }

    [Fact]
    public void A_spend_that_cannot_be_made_durable_fails_closed_and_latches()
    {
        // Review finding 2. WorkerInventoryPort.Remove mutates the inventory and
        // THEN calls VerifyPersisted, which throws when the body's own ZDO write
        // failed. Measuring the in-memory delta and calling that a payment is the
        // one way this file can mint material: the wall is standing, the loop says
        // it paid, and the next load hands the wood back.
        PutInChest("Wood", 20);
        WorldBuildMaterials materials = Materials("Wood");
        materials.Draw(Want(("Wood", 20)));
        _custody.WorkerPort = new UnpersistedPort(new WorkerInventoryPort(_body, () => 300f));

        bool spent = materials.Spend(
            PieceRecipe.Known("wood_wall", new[] { new PieceCost("Wood", 2) }),
            out MaterialTally paid,
            out string failure);

        Assert.False(spent);
        Assert.Contains("could not be made durable", failure);
        Assert.NotNull(materials.Uncertain);

        // The units really did leave the in-memory inventory - that is the whole
        // problem - and it is reported rather than absorbed.
        Assert.Equal(2, paid.UnitsOf("Wood"));

        // Latched: nothing else moves until a person has looked.
        _custody.WorkerPort = new WorkerInventoryPort(_body, () => 300f);
        Assert.True(materials.Draw(Want(("Wood", 2))).IsRefused);
        Assert.False(materials.Spend(
            PieceRecipe.Known("wood_wall", new[] { new PieceCost("Wood", 2) }),
            out MaterialTally _, out string _));
        Assert.True(materials.PutBack(out string _).IsEmpty);
    }

    /// <summary>The worker's own port's behaviour when its body could not write
    /// its inventory: the remove happens, and then it throws. Not contrived -
    /// <c>EngineInventoryPort.Remove</c> calls <c>VerifyPersisted</c> after the
    /// mutation and <c>WorkerInventoryPort</c> throws there.</summary>
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

    // ---- putting it back -------------------------------------------------

    [Fact]
    public void Putting_it_back_returns_everything_he_holds_of_the_orders_kinds()
    {
        PutInChest("Wood", 20);
        WorldBuildMaterials materials = Materials();
        materials.Draw(Want(("Wood", 20)));
        Assert.Equal(0, InChest("Wood"));

        MaterialTally back = materials.PutBack(out string failure);

        Assert.Equal(string.Empty, failure);
        Assert.Equal(20, back.UnitsOf("Wood"));
        Assert.Equal(20, InChest("Wood"));
        Assert.Equal(0, OnWorker("Wood"));
    }

    [Fact]
    public void A_chest_with_no_room_says_what_is_still_on_him()
    {
        // A one-slot chest: twenty wood goes in, eighteen comes back out, and the
        // rest stays where it is with a sentence about it.
        Container tiny = ForemanFixtures.Chest(key: "0000000000000002:00000001", width: 1, height: 1);
        PutInChest("Wood", 100);
        WorldBuildMaterials materials = Materials("Wood");
        materials.Draw(Want(("Wood", 100)));
        _custody.ChestPort = new ContainerInventoryPort(
            tiny, ForemanFixtures.KeyOf(tiny), () => Vector3.zero, 3f);

        MaterialTally back = materials.PutBack(out string failure);

        Assert.Equal(50, back.UnitsOf("Wood"));
        Assert.Contains("is still in his own inventory", failure);
        Assert.Equal(50, OnWorker("Wood"));
    }

    [Fact]
    public void A_draw_reports_what_actually_arrived_and_never_what_was_asked_for()
    {
        // CanAccept is an ESTIMATE - the port says so in its own comment, because
        // vanilla's stack merging depends on quality, world level and the cheated
        // flag. So a draw that trusted the number it asked for instead of the
        // delta it measured would report material Thorstein is not carrying, and
        // the loop would walk him to a wall with nothing in his hands.
        PutInChest("Wood", 40);
        _custody.WorkerPort = new OptimisticPort(new WorkerInventoryPort(_body, () => 300f));

        BuildDraw draw = Materials("Wood").Draw(Want(("Wood", 18)));

        Assert.False(draw.IsRefused);
        Assert.Equal(9, draw.Drawn.UnitsOf("Wood"));
        Assert.Equal(9, draw.Short.UnitsOf("Wood"));
        Assert.Equal(9, OnWorker("Wood"));
        Assert.Equal(31, InChest("Wood"));
    }

    /// <summary>An inventory that says it can take everything and then takes half
    /// of it. Not perverse: that is what a stack-merge estimate looks like when it
    /// is wrong, and the adapter's contract is to believe the measurement rather
    /// than the promise.</summary>
    private sealed class OptimisticPort : IInventoryPort, IInventoryMoveTarget
    {
        private readonly WorkerInventoryPort _real;

        internal OptimisticPort(WorkerInventoryPort real)
        {
            _real = real;
        }

        public string Describe => _real.Describe;

        public bool IsAvailable => _real.IsAvailable;

        public int Count(MaterialItem item) => _real.Count(item);

        public int CanAccept(MaterialItem item, int count) => count;

        public int Add(MaterialItem item, int count) => _real.Add(item, count);

        public int Remove(MaterialItem item, int count) => _real.Remove(item, count);

        public bool CanMoveFrom(IInventoryPort source) => _real.CanMoveFrom(source);

        public void MoveFrom(IInventoryPort source, MaterialItem item, int count) =>
            _real.MoveFrom(source, item, count / 2);
    }

    // ---- the uncertain latch ---------------------------------------------

    [Fact]
    public void Two_measurements_that_disagree_latch_and_nothing_moves_afterwards()
    {
        PutInChest("Wood", 40);
        _custody.ChestPort = new LyingPort(
            new ContainerInventoryPort(_chest, ForemanFixtures.KeyOf(_chest), () => Vector3.zero, 3f));
        WorldBuildMaterials materials = Materials("Wood");

        BuildDraw draw = materials.Draw(Want(("Wood", 8)));

        Assert.True(draw.IsRefused);
        Assert.Contains("do not agree", draw.Refusal);
        Assert.NotNull(materials.Uncertain);

        // Latched: a second attempt does not move anything either, even with an
        // honest chest, because nothing is put right on a guess.
        _custody.ChestPort = new ContainerInventoryPort(
            _chest, ForemanFixtures.KeyOf(_chest), () => Vector3.zero, 3f);
        Assert.True(materials.Draw(Want(("Wood", 8))).IsRefused);
        Assert.False(materials.Spend(
            PieceRecipe.Known("wood_wall", new[] { new PieceCost("Wood", 2) }), out MaterialTally _, out string _));
        Assert.True(materials.PutBack(out string _).IsEmpty);
    }

    /// <summary>A chest that reports one number before a move and a different one
    /// after, for reasons of its own. Exactly the shape of a container somebody
    /// else is also writing to, and the one thing the adapter must never absorb
    /// silently.</summary>
    private sealed class LyingPort : IInventoryPort, IInventoryMoveTarget
    {
        private readonly ContainerInventoryPort _real;
        private int _asked;

        internal LyingPort(ContainerInventoryPort real)
        {
            _real = real;
        }

        public string Describe => _real.Describe;

        public bool IsAvailable => _real.IsAvailable;

        public int Count(MaterialItem item)
        {
            // The count after the move is three short of the truth, so the two
            // deltas cannot agree.
            int real = _real.Count(item);
            return _asked++ == 0 ? real : real - 3;
        }

        public int CanAccept(MaterialItem item, int count) => _real.CanAccept(item, count);

        public int Add(MaterialItem item, int count) => _real.Add(item, count);

        public int Remove(MaterialItem item, int count) => _real.Remove(item, count);

        public bool CanMoveFrom(IInventoryPort source) => _real.CanMoveFrom(source);

        public void MoveFrom(IInventoryPort source, MaterialItem item, int count) =>
            _real.MoveFrom(source, item, count);
    }
}
