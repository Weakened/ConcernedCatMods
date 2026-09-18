using System;
using System.Collections.Generic;
using System.Reflection;
using TheConcernedCat.ConcernedForeman.Runtime.Custody;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;
using UnityEngine;

namespace ConcernedForeman.Tests;

/// <summary>The world these tests build: stub game objects wired the way the
/// plugin wires the real ones, plus a real custody core over an in-memory
/// journal. Nothing here re-implements anything the adapters do.</summary>
internal static class ForemanFixtures
{
    internal static readonly SettlementScope Scope =
        new SettlementScope(worldId: 316, settlement: new SettlementId("adapter-camp"));

    internal static readonly OrderId Order = new OrderId("collect-1");

    internal static readonly WorkerId Worker = new WorkerId("thorstein");

    internal static readonly Guid Epoch = new Guid("5c2c6d7e-2a57-4a44-8d8e-3f3d9b5ac001");

    internal static readonly MaterialItem Stone = MaterialItem.Of(CollectedResource.Stone);

    internal static readonly MaterialItem Wood = MaterialItem.Of(CollectedResource.Wood);

    internal static CustodyLocation WorkerAt() =>
        new CustodyLocation(CustodyPlace.Worker, WorkerKey.Thorstein.Value, Guid.Empty);

    internal static CustodyLocation ChestAt() => new CustodyLocation(CustodyPlace.Destination, "1:42", Epoch);

    internal static CustodyLocation CartAt() => new CustodyLocation(CustodyPlace.Cart, "1:77", Epoch);

    // ------------------------------------------------------------------
    // The game
    // ------------------------------------------------------------------

    /// <summary>An item prefab registered in the object database, so the ports
    /// can find the weight and stack size of an item the record names.</summary>
    internal static GameObject Prefab(string name, int maxStack = 50, float weight = 2f)
    {
        ObjectDB db = ObjectDB.instance ??= new ObjectDB();
        if (db.Prefabs.TryGetValue(name, out GameObject? existing))
        {
            return existing;
        }

        var prefab = new GameObject(name);
        ItemDrop drop = prefab.Add(new ItemDrop());
        drop.m_itemData.m_shared.m_name = "$item_" + name.ToLowerInvariant();
        drop.m_itemData.m_shared.m_maxStackSize = maxStack;
        drop.m_itemData.m_shared.m_weight = weight;
        drop.m_itemData.m_dropPrefab = prefab;
        db.Prefabs[name] = prefab;
        return prefab;
    }

    /// <summary>One real stack of a material.</summary>
    internal static ItemDrop.ItemData Stack(MaterialItem item, int count, int maxStack = 50, float weight = 2f)
    {
        GameObject prefab = Prefab(item.PrefabName, maxStack, weight);
        ItemDrop.ItemData template = prefab.GetComponent<ItemDrop>()!.m_itemData;
        ItemDrop.ItemData stack = template.Clone();
        stack.m_stack = count;
        stack.m_quality = item.Quality;
        stack.m_variant = item.Variant;
        return stack;
    }

    /// <summary>A bronze axe: chop is its dominant damage, which is what makes
    /// it an axe to the classifier.</summary>
    internal static ItemDrop.ItemData Axe()
    {
        GameObject prefab = Prefab("AxeBronze", maxStack: 1, weight: 4f);
        ItemDrop.ItemData axe = prefab.GetComponent<ItemDrop>()!.m_itemData.Clone();
        axe.m_shared = new ItemDrop.ItemData.SharedData
        {
            m_name = "$item_axe_bronze",
            m_maxStackSize = 1,
            m_weight = 4f,
            m_useDurability = true,
            m_toolTier = 2,
            m_damages = new HitData.DamageTypes { m_chop = 30f, m_damage = 20f },
        };
        axe.m_stack = 1;
        axe.m_durability = 80f;
        return axe;
    }

    /// <summary>A hammer: a build table with a real piece in it.</summary>
    internal static ItemDrop.ItemData Hammer()
    {
        var piece = new GameObject("wood_wall");
        piece.Add(new Piece());
        var table = new GameObject("_HammerPieceTable");
        PieceTable pieces = table.Add(new PieceTable());
        pieces.m_pieces.Add(piece);

        ItemDrop.ItemData hammer = Prefab("Hammer", maxStack: 1, weight: 2f).GetComponent<ItemDrop>()!.m_itemData.Clone();
        hammer.m_shared = new ItemDrop.ItemData.SharedData
        {
            m_name = "$item_hammer",
            m_maxStackSize = 1,
            m_weight = 2f,
            m_useDurability = true,
            m_toolTier = 0,
            m_buildPieces = pieces,
        };
        hammer.m_stack = 1;
        return hammer;
    }

    /// <summary>A chest with its own network object, the way a placed container
    /// has one.</summary>
    internal static Container Chest(string key = "0000000000000001:00000001", int width = 6, int height = 4)
    {
        var chest = new GameObject("piece_chest_wood");
        Container container = chest.Add(new Container());
        ZNetView view = chest.Add(new ZNetView());
        view.Zdo = new ZDO { m_uid = KeyToId(key) };
        chest.Add(new Piece());
        container.Bag = new Inventory("chest", null, width, height);
        Game.instance ??= new Game();
        return container;
    }

    internal static string KeyOf(Container container) =>
        TheConcernedCat.ConcernedForeman.Runtime.Settlement.SettlementTargets.TryIdentify(container)!;

    private static ZDOID KeyToId(string key)
    {
        string[] halves = key.Split(':');
        return new ZDOID(
            unchecked((long)Convert.ToUInt64(halves[0], 16)),
            Convert.ToUInt32(halves[1], 16));
    }

    /// <summary>A cart with a container, as `Vagon` has one.</summary>
    internal static Vagon Cart()
    {
        var cart = new GameObject("Cart");
        Vagon wagon = cart.Add(new Vagon());
        Container container = cart.Add(new Container());
        container.Bag = new Inventory("cart", null, 8, 4);
        container.m_wagon = wagon;
        wagon.m_container = container;
        return wagon;
    }

    /// <summary>A worker body: the real <see cref="WorkerBody"/> on a stub
    /// creature, loaded exactly as Unity would load it.</summary>
    internal static WorkerBody Body(string key = "foreman/thorstein", bool owner = true, ZDO? zdo = null)
    {
        var creature = new GameObject("CF_SettlementWorker");
        ZNetView view = creature.Add(new ZNetView());
        view.Zdo = zdo ?? new ZDO();
        view.Owner = owner;
        Humanoid humanoid = creature.Add(new Humanoid());
        humanoid.Bag = new Inventory("worker", null, 8, 4);
        var body = creature.Add(new WorkerBody());

        if (key.Length > 0 && zdo == null)
        {
            Assert.True(WorkerBody.TryStamp(creature, key));
        }

        Start(body);
        return body;
    }

    /// <summary>Unity calls <c>Start</c>; a test has to.</summary>
    internal static void Start(WorkerBody body) =>
        typeof(WorkerBody).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(body, Array.Empty<object>());

    internal static void Death(WorkerBody body) =>
        typeof(WorkerBody).GetMethod("OnDeath", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(body, Array.Empty<object>());

    // ------------------------------------------------------------------
    // The record
    // ------------------------------------------------------------------

    internal static CollectionOrderDefinition Definition(int stone = 20, int wood = 0)
    {
        var quotas = new List<ResourceQuota> { new ResourceQuota(CollectedResource.Stone, stone) };
        if (wood > 0)
        {
            quotas.Add(new ResourceQuota(CollectedResource.Wood, wood));
        }

        return new CollectionOrderDefinition(
            Order,
            Worker,
            quotas,
            new WorkScope(WorkScopeSource.DefaultCampCircle, new SitePoint(10f, 20f, 30f), 30f, "your bed", 3, Epoch),
            DeliveryTarget.ToContainer("1:42", Epoch, new SitePoint(12f, 20f, 30f)),
            ParticipationMode.Solo,
            "Tester");
    }

    /// <summary>A custody core over an in-memory journal, as a world load opens
    /// one, with the order accepted.</summary>
    internal static CustodyCore Custody(double worldTime = 1000.0)
    {
        var journal = new SettlementJournal(Scope);
        CustodyCore core = CustodyCore.Open(
            journal,
            () => true,
            () => worldTime,
            new WorldLoad(worldTime, Epoch),
            () => true);

        Assert.True(core.RecordAccepted(Definition(), out string refusal), refusal);
        return core;
    }

    /// <summary>Puts <paramref name="count"/> units of material into the
    /// worker's hands, in the record and in his inventory, the way a pick and a
    /// take do: recorded intent, the engine change, recorded receipt.</summary>
    internal static void Carry(CustodyCore core, Inventory worker, MaterialItem item, int count, int maxStack = 50, float weight = 2f)
    {
        var ground = new CustodyLocation(CustodyPlace.SourceGround, "1:900", Epoch);
        RequestId pickup = core.BeginPickup(Order, new SourceKey("Pickable_Stone", "1:500", Epoch, new SitePoint(11f, 20f, 31f)), out string why)
            ?? throw new InvalidOperationException(why);
        Assert.True(core.FinishPickup(
            pickup,
            new PickupResult(PickupOutcome.Picked, new[] { new SpawnedDrop("1:900", item, count) }, string.Empty),
            out why), why);

        var take = new TransferIntent(
            CustodyIds.ForTransfer(Order, core.Ledger), Order, ground, WorkerAt(), item, count, core.Ledger.Revision);
        Assert.Equal(TransferOutcome.Unspecified, core.BeginTransfer(take, out why));
        worker.AddItem(Stack(item, count, maxStack, weight));
        Assert.True(core.FinishTransfer(
            new TransferReceipt(take.Request, TransferOutcome.Completed, count, ground, "taken"), out why), why);
    }

    internal static TransferIntent Intent(CustodyCore core, CustodyLocation from, CustodyLocation to, MaterialItem item, int count) =>
        new TransferIntent(CustodyIds.ForTransfer(Order, core.Ledger), Order, from, to, item, count, core.Ledger.Revision);
}
