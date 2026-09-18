using System;
using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.ConcernedSteward.Domain;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep;
using TheConcernedCat.ConcernedSteward.Runtime;
using TheConcernedCat.Settlement.Worker;
using UnityEngine;

namespace TheConcernedCat.ConcernedSteward.Tests;

/// <summary>The scene-touching tests share Valheim's statics —
/// <c>ZNetScene.instance</c>, <c>ZDOMan.instance</c>, <c>Player.m_localPlayer</c>,
/// the ward answer, the live-body list — so they run one at a time. Everything
/// else in this suite is pure and runs in parallel.</summary>
[CollectionDefinition("scene", DisableParallelization = true)]
public sealed class SceneCollection
{
}

/// <summary>A stubbed world, assembled the way the real one is: objects carry
/// their own network view, the scene knows about them, and identity comes from
/// the view.</summary>
internal sealed class Scene : IDisposable
{
    private uint _nextId = 1;

    internal Scene()
    {
        ZNetScene.instance = new ZNetScene();
        ZDOMan.instance = new ZDOMan();
        PrivateArea.Access = true;
        Piece.s_allPieces.Clear();
        StewardBody.ForgetAll();
        Epoch = "epoch-" + Guid.NewGuid().ToString("N");
    }

    internal string Epoch { get; }

    internal Humanoid Steward { get; private set; } = null!;

    /// <summary>A body with a pack, standing at the origin.</summary>
    internal Humanoid PlaceSteward(int wood, string fuelName = "$item_wood", int maxStack = 50)
    {
        var body = new GameObject("steward");
        Steward = body.Add(new Humanoid());
        if (wood > 0)
        {
            Steward.GetInventory().AddItem(Stack(fuelName, wood, maxStack));
        }

        return Steward;
    }

    /// <summary>A fire, with its own view, registered in the scene and listed
    /// as a placed piece so the survey can find it.</summary>
    internal (Fireplace fire, ZNetView view, string key) PlaceFire(
        float fuel,
        float maxFuel = 10f,
        StubOwnership ownership = StubOwnership.Us,
        Vector3 at = default,
        string fuelName = "$item_wood",
        bool canRefill = true,
        bool infiniteFuel = false)
    {
        var host = new GameObject("fire");
        host.transform.position = at;
        host.Add(new Piece());
        ZNetView view = host.Add(new ZNetView { Ownership = ownership });
        view.Zdo.m_uid = new ZDOID(7L, _nextId++);

        var fire = host.Add(new Fireplace
        {
            m_maxFuel = maxFuel,
            m_canRefill = canRefill,
            m_infiniteFuel = infiniteFuel,
            m_fuelItem = FuelItem(fuelName),
        });
        fire.Bind(view, fuel);

        Piece.s_allPieces.Add(host.GetComponent<Piece>()!);
        ZNetScene.instance!.Register(view);
        return (fire, view, StewardIdentity.TryIdentify(fire)!);
    }

    internal (Container chest, string key) PlaceChest(
        int wood, string fuelName = "$item_wood", int width = 6, int height = 4)
    {
        var host = new GameObject("chest");
        ZNetView view = host.Add(new ZNetView());
        view.Zdo.m_uid = new ZDOID(7L, _nextId++);
        var chest = host.Add(new Container { Bag = new Inventory("chest", null, width, height) });
        if (wood > 0)
        {
            chest.Bag.AddItem(Stack(fuelName, wood));
        }

        ZNetScene.instance!.Register(view);
        return (chest, StewardIdentity.TryIdentify(chest)!);
    }

    /// <summary>A saved Steward body, as ZDOMan knows about it: not loaded,
    /// just recorded.</summary>
    internal ZDO SaveBody(string? key)
    {
        var zdo = new ZDO { m_uid = new ZDOID(7L, _nextId++) };
        if (key != null)
        {
            zdo.Set(StewardBody.KeyField, key);
        }

        if (!ZDOMan.instance!.Saved.TryGetValue(StewardRole.BodyPrefabName, out List<ZDO>? all))
        {
            all = new List<ZDO>();
            ZDOMan.instance.Saved[StewardRole.BodyPrefabName] = all;
        }

        all.Add(zdo);
        return zdo;
    }

    internal WorldFuelTargets Fires(Func<Humanoid?>? body = null) =>
        new WorldFuelTargets(() => Epoch, body ?? (() => Steward));

    internal static ItemDrop.ItemData Stack(string name, int count, int maxStack = 50) =>
        new ItemDrop.ItemData
        {
            m_stack = count,
            m_shared = new ItemDrop.ItemData.SharedData { m_name = name, m_maxStackSize = maxStack },
        };

    private static ItemDrop FuelItem(string name)
    {
        var host = new GameObject(name);
        return host.Add(new ItemDrop
        {
            m_itemData = new ItemDrop.ItemData
            {
                m_shared = new ItemDrop.ItemData.SharedData { m_name = name },
            },
        });
    }

    public void Dispose()
    {
        ZNetScene.instance = null;
        ZDOMan.instance = null;
        Player.m_localPlayer = null;
        Piece.s_allPieces.Clear();
        StewardBody.ForgetAll();
        Inventory.Trace = null;
    }
}

/// <summary>The fire adapter, against a transcription of the decompiled 1.0.14
/// <c>Fireplace</c>.
///
/// Every claim the audit makes about vanilla is a claim about the stub in
/// <c>VanillaStubs.cs</c>, and the stub was written from the decompile
/// line by line — including the parts that make vanilla lose an item. So these
/// tests are about the real shape, not a convenient one.</summary>
[Collection("scene")]
public sealed class FireAdapterTests
{
    [Fact]
    public void One_unit_goes_in_one_wood_comes_out_and_both_are_measured()
    {
        using var scene = new Scene();
        scene.PlaceSteward(wood: 5);
        (Fireplace fire, _, string key) = scene.PlaceFire(fuel: 2f);
        var pack = new StubPack(scene.Steward);

        FeedMeasurement measurement = scene.Fires()
            .FeedOneUnit(new FuelTargetKey(key, scene.Epoch), pack);

        Assert.Equal(FeedOutcome.Accepted, measurement.Outcome);
        Assert.Equal(1, measurement.Spent);
        Assert.Equal(1f, measurement.Gained, 3);
        Assert.Equal(3f, fire.Fuel, 3);
        Assert.Equal(4, pack.Count("$item_wood"));
    }

    [Fact]
    public void A_fire_that_vanilla_calls_full_takes_nothing_and_costs_nothing()
    {
        using var scene = new Scene();
        scene.PlaceSteward(wood: 5);

        // Ceil(9.5) == 10 == m_maxFuel. Vanilla refuses AND answers true, which
        // is the whole reason the outcome comes from the deltas.
        (Fireplace fire, _, string key) = scene.PlaceFire(fuel: 9.5f);
        var pack = new StubPack(scene.Steward);

        FeedMeasurement measurement = scene.Fires()
            .FeedOneUnit(new FuelTargetKey(key, scene.Epoch), pack);

        Assert.Equal(FeedOutcome.Declined, measurement.Outcome);
        Assert.Equal(0, measurement.Spent);
        Assert.Equal(9.5f, fire.Fuel, 3);
        Assert.Equal(5, pack.Count("$item_wood"));
    }

    [Fact]
    public void Vanilla_really_does_destroy_the_item_on_an_unowned_fire()
    {
        // The finding this product's ownership rule exists for, demonstrated on
        // the transcription rather than asserted in a comment. UseItem removes
        // the wood and RPC_AddFuel's own owner gate then discards the fuel.
        using var scene = new Scene();
        Humanoid body = scene.PlaceSteward(wood: 5);
        (Fireplace fire, _, _) = scene.PlaceFire(fuel: 0f, ownership: StubOwnership.Nobody);

        ItemDrop.ItemData wood = body.GetInventory().GetAllItems()[0];
        bool answer = fire.UseItem(body, wood);

        Assert.True(answer, "vanilla answers true here, which is exactly the trap");
        Assert.Equal(4, body.GetInventory().GetAllItems()[0].m_stack);
        Assert.Equal(0f, fire.Fuel, 3);
    }

    [Fact]
    public void The_adapter_refuses_an_unowned_fire_before_touching_anything()
    {
        using var scene = new Scene();
        scene.PlaceSteward(wood: 5);
        (Fireplace fire, ZNetView view, string key) = scene.PlaceFire(
            fuel: 0f, ownership: StubOwnership.Nobody);
        var pack = new StubPack(scene.Steward);

        FeedMeasurement measurement = scene.Fires()
            .FeedOneUnit(new FuelTargetKey(key, scene.Epoch), pack);

        Assert.Equal(FeedOutcome.Declined, measurement.Outcome);
        Assert.Equal(5, pack.Count("$item_wood"));
        Assert.Equal(0f, fire.Fuel, 3);
        Assert.Contains("destroyed the wood", measurement.Evidence);

        // Nothing was invoked on the object at all, and ownership was not taken.
        Assert.Empty(view.Invoked);
        Assert.Equal(StubOwnership.Nobody, view.Ownership);
    }

    [Fact]
    public void A_fire_another_peer_owns_is_refused_because_the_result_would_be_unobservable()
    {
        using var scene = new Scene();
        scene.PlaceSteward(wood: 5);
        (_, ZNetView view, string key) = scene.PlaceFire(fuel: 0f, ownership: StubOwnership.Other);
        var pack = new StubPack(scene.Steward);

        FeedMeasurement measurement = scene.Fires()
            .FeedOneUnit(new FuelTargetKey(key, scene.Epoch), pack);

        Assert.Equal(FeedOutcome.Declined, measurement.Outcome);
        Assert.Equal(5, pack.Count("$item_wood"));
        Assert.Empty(view.Invoked);
        Assert.Equal(StubOwnership.Other, view.Ownership);
    }

    [Fact]
    public void He_has_to_be_standing_there_because_vanilla_does_not_check()
    {
        using var scene = new Scene();
        scene.PlaceSteward(wood: 5);

        // Fireplace has no distance test anywhere; the only thing stopping a
        // player is the hover targeting, which a worker never goes through.
        (Fireplace fire, _, string key) = scene.PlaceFire(
            fuel: 0f, at: new Vector3(30f, 0f, 0f));
        var pack = new StubPack(scene.Steward);

        FeedMeasurement measurement = scene.Fires()
            .FeedOneUnit(new FuelTargetKey(key, scene.Epoch), pack);

        Assert.Equal(FeedOutcome.Declined, measurement.Outcome);
        Assert.Contains("only reaches", measurement.Evidence);
        Assert.Equal(5, pack.Count("$item_wood"));
        Assert.Equal(0f, fire.Fuel, 3);
    }

    [Fact]
    public void A_key_from_a_previous_session_resolves_to_nothing()
    {
        using var scene = new Scene();
        scene.PlaceSteward(wood: 5);
        (_, _, string key) = scene.PlaceFire(fuel: 0f);
        var pack = new StubPack(scene.Steward);
        WorldFuelTargets fires = scene.Fires();

        Assert.False(fires.TryObserve(new FuelTargetKey(key, "a-previous-session"), out _));

        FeedMeasurement measurement = fires.FeedOneUnit(
            new FuelTargetKey(key, "a-previous-session"), pack);

        Assert.Equal(FeedOutcome.Declined, measurement.Outcome);
        Assert.Equal(5, pack.Count("$item_wood"));
    }

    [Fact]
    public void The_survey_reports_what_it_read_and_never_what_it_assumed()
    {
        using var scene = new Scene();
        scene.PlaceSteward(wood: 5);
        scene.PlaceFire(fuel: 3f, at: new Vector3(4f, 0f, 0f));
        scene.PlaceFire(fuel: 0f, ownership: StubOwnership.Other, at: new Vector3(6f, 0f, 0f));
        scene.PlaceFire(fuel: 0f, canRefill: false, at: new Vector3(8f, 0f, 0f));
        scene.PlaceFire(fuel: 0f, infiniteFuel: true, at: new Vector3(10f, 0f, 0f));
        scene.PlaceFire(fuel: 0f, fuelName: "$item_coal", at: new Vector3(12f, 0f, 0f));

        IReadOnlyList<FuelTargetObservation> found =
            scene.Fires().Survey(new SitePoint(0f, 0f, 0f), 32f);

        Assert.Equal(5, found.Count);
        Assert.All(found, target => Assert.True(target.Key.IsFrom(scene.Epoch)));
        Assert.Single(found, target => !target.OwnedHere);
        Assert.Single(found, target => !target.CanRefill);
        Assert.Single(found, target => target.InfiniteFuel);
        Assert.Single(found, target => target.FuelItemName == "$item_coal");
        Assert.Contains(found, target => Math.Abs(target.Fuel - 3f) < 0.001f);
    }

    [Fact]
    public void A_ward_the_player_does_not_own_makes_a_fire_ineligible()
    {
        using var scene = new Scene();
        scene.PlaceSteward(wood: 5);
        scene.PlaceFire(fuel: 0f, at: new Vector3(4f, 0f, 0f));
        PrivateArea.Access = false;

        IReadOnlyList<FuelTargetObservation> found =
            scene.Fires().Survey(new SitePoint(0f, 0f, 0f), 32f);

        Assert.All(found, target => Assert.False(target.AccessGranted));
    }

    [Fact]
    public void A_fire_up_a_tower_is_still_found()
    {
        using var scene = new Scene();
        scene.PlaceSteward(wood: 5);

        // Piece.GetAllPiecesInRadius measures in three dimensions while a
        // settlement is a circle on the ground; the adapter widens the query
        // and the domain applies the real containment test.
        scene.PlaceFire(fuel: 0f, at: new Vector3(30f, 20f, 0f));

        IReadOnlyList<FuelTargetObservation> found =
            scene.Fires().Survey(new SitePoint(0f, 0f, 0f), 32f);

        Assert.Single(found);
    }

    [Fact]
    public void A_body_that_is_not_here_cannot_feed_anything()
    {
        using var scene = new Scene();
        scene.PlaceSteward(wood: 5);
        (_, _, string key) = scene.PlaceFire(fuel: 0f);
        var pack = new StubPack(scene.Steward);

        FeedMeasurement measurement = scene.Fires(body: () => null)
            .FeedOneUnit(new FuelTargetKey(key, scene.Epoch), pack);

        Assert.Equal(FeedOutcome.Declined, measurement.Outcome);
        Assert.Equal(5, pack.Count("$item_wood"));
    }

    [Fact]
    public void Carrying_nothing_is_answered_rather_than_attempted()
    {
        using var scene = new Scene();
        scene.PlaceSteward(wood: 0);
        (_, ZNetView view, string key) = scene.PlaceFire(fuel: 0f);
        var pack = new StubPack(scene.Steward);

        FeedMeasurement measurement = scene.Fires()
            .FeedOneUnit(new FuelTargetKey(key, scene.Epoch), pack);

        Assert.Equal(FeedOutcome.Declined, measurement.Outcome);
        Assert.Empty(view.Invoked);
    }

    /// <summary>The Steward's pack, over a stub humanoid, without needing a
    /// whole <see cref="StewardBody"/>. Counts exactly as
    /// <see cref="WorldItemStore"/> does: the shared name and nothing
    /// else.</summary>
    private sealed class StubPack : IItemStorePort
    {
        private readonly Humanoid _body;

        internal StubPack(Humanoid body) => _body = body;

        public string Describe => "the Steward's pack";

        public bool IsAvailable => true;

        public IReadOnlyCollection<string> ItemNames =>
            _body.GetInventory().GetAllItems().Select(i => i.m_shared.m_name).Distinct().ToList();

        public int Count(string fuelItemName) =>
            _body.GetInventory().GetAllItems()
                .Where(i => i.m_shared.m_name == fuelItemName).Sum(i => i.m_stack);

        public int RoomFor(string fuelItemName, int count) => count;

        public void MoveTo(IItemStorePort destination, string fuelItemName, int count) =>
            throw new NotSupportedException();
    }
}

/// <summary>Counting and moving, over real vanilla inventories.</summary>
[Collection("scene")]
public sealed class ItemStoreTests
{
    [Fact]
    public void A_stack_from_a_lower_world_level_is_still_counted_because_vanilla_still_burns_it()
    {
        using var scene = new Scene();
        (Container chest, string key) = scene.PlaceChest(wood: 0);

        // Inventory.CountItems would filter this out on m_worldLevel;
        // Fireplace.UseItem would not. Measuring with one predicate and
        // mutating with the other is how a conservation argument gets a hole.
        ItemDrop.ItemData old = Scene.Stack("$item_wood", 12);
        old.m_worldLevel = 0;
        chest.Bag.AddItem(old);
        Game.m_worldLevel = 0;

        var depot = new DepotStore(() => key);

        Assert.Equal(12, depot.Count("$item_wood"));
        Assert.Contains("$item_wood", depot.ItemNames);
    }

    [Fact]
    public void Moving_part_of_a_stack_moves_exactly_that_much_and_adds_before_removing()
    {
        using var scene = new Scene();
        (Container chest, string key) = scene.PlaceChest(wood: 20);
        Humanoid body = scene.PlaceSteward(wood: 0);

        var depot = new DepotStore(() => key);
        var pack = new HumanoidStore(body);

        Inventory.Trace = new List<string>();
        depot.MoveTo(pack, "$item_wood", 7);

        Assert.Equal(13, depot.Count("$item_wood"));
        Assert.Equal(7, pack.Count("$item_wood"));

        // Add to the destination, then remove from the source. A crash between
        // the two leaves a duplicate the measurements expose; the other order
        // destroys real items and leaves no evidence.
        int add = Inventory.Trace.FindIndex(line => line.StartsWith("steward.add"));
        int remove = Inventory.Trace.FindIndex(line => line.StartsWith("chest.remove"));
        Assert.True(add >= 0 && remove > add, string.Join(" | ", Inventory.Trace));
    }

    [Fact]
    public void Moving_a_whole_stack_goes_through_vanillas_own_move()
    {
        using var scene = new Scene();
        (Container chest, string key) = scene.PlaceChest(wood: 6);
        Humanoid body = scene.PlaceSteward(wood: 0);

        Inventory.Trace = new List<string>();
        new DepotStore(() => key).MoveTo(new HumanoidStore(body), "$item_wood", 6);

        Assert.Contains(Inventory.Trace, line => line.StartsWith("steward.move"));
        Assert.Equal(0, chest.Bag.GetAllItems().Sum(i => i.m_stack));
    }

    [Fact]
    public void A_destination_with_no_room_takes_nothing_and_the_source_keeps_it_all()
    {
        using var scene = new Scene();
        (Container chest, string key) = scene.PlaceChest(wood: 20);

        // One slot, already filled by a stack of something else.
        Humanoid body = scene.PlaceSteward(wood: 0);
        body.Bag = new Inventory("steward", null, 1, 1);
        body.Bag.AddItem(Scene.Stack("$item_stone", 1, maxStack: 1));

        var depot = new DepotStore(() => key);
        var pack = new HumanoidStore(body);

        Assert.Equal(0, pack.RoomFor("$item_wood", 5));
        Assert.Equal(20, depot.Count("$item_wood"));
    }

    [Fact]
    public void A_chest_somebody_is_using_is_not_readable_at_all()
    {
        using var scene = new Scene();
        (Container chest, string key) = scene.PlaceChest(wood: 20);
        var depot = new DepotStore(() => key);
        Assert.True(depot.IsAvailable);

        chest.InUse = true;

        Assert.False(depot.IsAvailable);
        Assert.Equal(-1, depot.Count("$item_wood"));
    }

    [Fact]
    public void A_chest_this_session_does_not_own_is_not_readable_either()
    {
        using var scene = new Scene();
        (Container chest, string key) = scene.PlaceChest(wood: 20);
        chest.Owner = false;

        var depot = new DepotStore(() => key);

        Assert.False(depot.IsAvailable);
        Assert.Equal(-1, depot.Count("$item_wood"));
    }

    [Fact]
    public void A_key_that_names_a_different_chest_now_resolves_to_nothing()
    {
        using var scene = new Scene();
        (_, string key) = scene.PlaceChest(wood: 20);
        var depot = new DepotStore(() => key);
        Assert.True(depot.IsAvailable);

        // The world reloads and the ids are handed out again, densely from one.
        ZNetScene.instance = new ZNetScene();

        Assert.False(depot.IsAvailable);
    }

    /// <summary>A humanoid's own inventory behind the store seam, for the move
    /// tests, without needing the whole persistent body.</summary>
    private sealed class HumanoidStore : WorldItemStore
    {
        private readonly Humanoid _body;

        internal HumanoidStore(Humanoid body) => _body = body;

        public override string Describe => "the Steward's pack";

        public override bool IsAvailable => true;

        internal override Inventory? Engine => _body.GetInventory();
    }
}
