using System;
using System.Collections.Generic;
using System.Reflection;
using TheConcernedCat.ConcernedForeman.Runtime.Custody;
using UnityEngine;
using static ConcernedForeman.Tests.ForemanFixtures;
using TheConcernedCat.ConcernedNPC.Body;
using TheConcernedCat.ConcernedNPC.Roles;

namespace ConcernedForeman.Tests;

/// <summary>D9: the worker body's own persistence, against a vanilla inventory
/// and a vanilla network object modelled on 1.0.14.
///
/// This is the code that makes a relog, a zone unload and a despawn refusal
/// honest, and until review R2's M4 it was proved by reading alone.
///
/// #372: the component is Concerned NPC's <see cref="NpcBody"/> now - the copy
/// this product had was moved into the library - and these are the same tests,
/// run against the shipped library file on this project's stubs, unchanged in
/// what they assert.</summary>
public sealed class WorkerBodyTests : IDisposable
{
    private readonly List<string> _errors = new List<string>();

    public WorkerBodyTests()
    {
        ResetBodies();
        ObjectDB.instance = new ObjectDB();
        NpcBody.ErrorLog += _errors.Add;
    }

    public void Dispose() => ResetBodies();

    [Fact]
    public void AChangeIsWrittenToHisObjectInTheSameCallThatMadeIt()
    {
        NpcBody body = Body();
        ZDO zdo = body.View!.Zdo;
        int revision = body.Revision;

        body.Inventory!.AddItem(Stack(Stone, 7));

        // Not queued, not at the next save: written inside AddItem, through the
        // inventory's own change callback, exactly as a Container does it.
        Assert.True(body.LastChangePersisted);
        Assert.True(body.Revision > revision);
        Assert.NotNull(zdo.GetByteArray(ForemanWorkerBodies.InventoryField, null));
        Assert.Equal(body.Revision, zdo.GetInt(ForemanWorkerBodies.RevisionField, -1));
    }

    [Fact]
    public void NothingIsWrittenWhileHisStoredInventoryIsBeingLoaded()
    {
        // The loading guard is what stops an empty inventory being written over
        // a carried one when a body wakes up.
        NpcBody first = Body();
        first.Inventory!.AddItem(Stack(Stone, 4));
        ZDO zdo = first.View!.Zdo;
        int revisionOnDisk = zdo.GetInt(ForemanWorkerBodies.RevisionField, -1);
        zdo.Written.Clear();

        NpcBody second = Body(key: string.Empty, zdo: zdo);

        Assert.True(second.IsLoaded);
        Assert.Empty(zdo.Written);
        Assert.Equal(revisionOnDisk, zdo.GetInt(ForemanWorkerBodies.RevisionField, -1));
        Assert.Equal(4, EngineInventoryPort.CountIn(second.Inventory!, Stone));
    }

    [Fact]
    public void WhatHeCarriesAndWhatHeWasHoldingComeBackWithHim()
    {
        NpcBody first = Body();
        first.Inventory!.AddItem(Stack(Stone, 12));
        ItemDrop.ItemData axe = Axe();
        axe.m_equipped = true;
        first.Inventory.AddItem(axe);

        NpcBody second = Body(key: string.Empty, zdo: first.View!.Zdo);

        Assert.Equal("foreman/thorstein", second.StoredKey);
        Assert.Equal(12, EngineInventoryPort.CountIn(second.Inventory!, Stone));
        Assert.Equal(2, second.ItemCount);

        // An item saved as equipped comes back in his hands rather than loose.
        Assert.Single(second.Humanoid!.Equipped);
        Assert.Equal("$item_axe_bronze", second.Humanoid.Equipped[0].m_shared.m_name);
    }

    [Fact]
    public void ABodyThatCannotReadWhatItCarriesIsInertAndNeverSavesOverIt()
    {
        NpcBody first = Body();
        first.Inventory!.AddItem(Stack(Stone, 5));
        ZDO zdo = first.View!.Zdo;

        // The stored package is unreadable: the body must not come up empty and
        // then write that emptiness over what it was carrying.
        zdo.Set(ForemanWorkerBodies.InventoryField, new byte[] { 1, 2, 3 });
        zdo.Written.Clear();
        NpcBody broken = Body(key: string.Empty, zdo: zdo);

        Assert.False(broken.IsLoaded);
        Assert.NotNull(broken.Fault);
        Assert.Empty(zdo.Written);
        Assert.DoesNotContain(broken, NpcBody.Live);
        Assert.Contains(_errors, line => line.Contains("is inert"));

        // And it refuses to be a place: no work can move anything through it.
        Assert.False(new WorkerInventoryPort(broken, () => 100f).IsAvailable);
    }

    [Fact]
    public void OnlyHisOwnThreeFieldsAreEverWritten()
    {
        NpcBody body = Body();
        body.Inventory!.AddItem(Stack(Stone, 3));
        body.Inventory.AddItem(Axe());

        foreach (string key in body.View!.Zdo.Written)
        {
            Assert.Contains(key, new[] { ForemanWorkerBodies.KeyField, ForemanWorkerBodies.InventoryField, ForemanWorkerBodies.RevisionField });
        }
    }

    [Fact]
    public void AWriteThatFailsIsReportedRatherThanAssumed()
    {
        NpcBody body = Body();
        body.View!.Zdo.FailWrites = true;

        body.Inventory!.AddItem(Stack(Stone, 2));

        Assert.False(body.LastChangePersisted);
        Assert.Contains(_errors, line => line.Contains("could not write its inventory"));

        // It can be retried, and the retry is what clears the flag.
        body.View.Zdo.FailWrites = false;
        Assert.True(body.TryPersist());
    }

    [Fact]
    public void ADeathPutsEverythingOnTheGroundAndSaysWhereEvenWhenOneDropFails()
    {
        // Review R2's m3: one throw inside the drop loop used to destroy every
        // remaining stack with the body and skip the report, so nothing recorded
        // what was lost.
        NpcBody body = Body();
        body.Inventory!.AddItem(Stack(Stone, 6));
        body.Inventory.AddItem(Axe());
        body.Inventory.AddItem(Stack(Wood, 4));

        body.Humanoid!.DropThrows = item => item.m_shared.m_name == "$item_axe_bronze";

        var dropped = new List<string>();
        Vector3? where = null;
        NpcBody.Died += (who, items, place) =>
        {
            Assert.Same(body, who);
            where = place;
            foreach (NpcDroppedItem item in items)
            {
                dropped.Add(item.PrefabName + ":" + item.Count);
            }
        };

        Death(body);

        // The axe could not be dropped; the stone and the wood still reached the
        // ground, and the runtime was told what did.
        Assert.NotNull(where);
        Assert.Equal(new[] { "Stone:6", "Wood:4" }, dropped);
        Assert.Equal(2, body.Humanoid.Dropped.Count);
        Assert.Contains(_errors, line => line.Contains("could not be dropped, so it is lost with the body"));
    }

    [Fact]
    public void ADeathWithNothingCarriedStillReportsIt()
    {
        NpcBody body = Body();
        var raised = 0;
        NpcBody.Died += (_, items, _) =>
        {
            raised++;
            Assert.Empty(items);
        };

        Death(body);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void AnUnstampedBodyIsNotHisAndIsLeftAlone()
    {
        // A body from an older build carries no key: it is reported, never
        // adopted, and never written to.
        var creature = new GameObject("CF_SettlementWorker");
        ZNetView view = creature.Add(new ZNetView());
        Humanoid humanoid = creature.Add(new Humanoid());
        humanoid.Bag = new Inventory("worker", null, 8, 4);
        var body = creature.Add(new NpcBody());
        Start(body);

        Assert.True(body.IsLoaded);
        Assert.Equal(string.Empty, body.StoredKey);
        Assert.Null(ForemanWorkerBodies.FindLive("foreman/thorstein"));
    }

    [Fact]
    public void AStampNeedsAnOwnedValidObject()
    {
        var creature = new GameObject("CF_SettlementWorker");
        ZNetView view = creature.Add(new ZNetView());
        creature.Add(new Humanoid());

        view.Owner = false;
        Assert.False(ForemanFixtures.Stamp(creature, "foreman/thorstein"));

        view.Owner = true;
        view.Valid = false;
        Assert.False(ForemanFixtures.Stamp(creature, "foreman/thorstein"));

        view.Valid = true;
        Assert.True(ForemanFixtures.Stamp(creature, "foreman/thorstein"));
        Assert.Equal("foreman/thorstein", view.Zdo.GetString(ForemanWorkerBodies.KeyField, string.Empty));
    }

    // --- #372: the move onto Concerned NPC's body ------------------------------

    [Fact]
    public void ABodySavedByTheCopyThisReplacesLoadsUnchangedWithNothingWritten()
    {
        // What the shipped WorkerBody wrote, written here by hand under its own
        // three literals: an identity, a revision and vanilla's own inventory
        // package. No migration exists, so none can run.
        NpcBody source = Body();
        source.Inventory!.AddItem(Stack(Stone, 9));
        byte[] package = source.View!.Zdo.GetByteArray(ForemanWorkerBodies.InventoryField, null)!;
        ResetBodies();

        var saved = new ZDO();
        saved.Set("tcc.worker.key", "foreman/thorstein");
        saved.Set("tcc.worker.revision", 7);
        saved.Set("tcc.worker.inventory", package);
        saved.Written.Clear();

        NpcBody loaded = Body(key: string.Empty, zdo: saved);

        Assert.True(loaded.IsLoaded);
        Assert.Null(loaded.Fault);
        Assert.Equal("foreman/thorstein", loaded.StoredKey);
        Assert.Equal(7, loaded.Revision);
        Assert.Equal(9, EngineInventoryPort.CountIn(loaded.Inventory!, Stone));
        Assert.Empty(saved.Written);
        Assert.Same(loaded, ForemanWorkerBodies.FindLive("foreman/thorstein"));
    }

    [Fact]
    public void AnotherRolesBodyWithHisKeyIsNeverTakenForHis()
    {
        // The library's lists and events are shared by every product that loads
        // it, so the lookup and the event handlers take his prefab only.
        var other = NpcBodyContract.ForWorker("CT_TeamsterWorker", "tcc.worker.");
        Assert.True(NpcBody.TryRegister(other, NpcBodyKeeps.IdentityAndInventory, out string reason), reason);
        var creature = new GameObject("CT_TeamsterWorker");
        ZNetView view = creature.Add(new ZNetView());
        Humanoid humanoid = creature.Add(new Humanoid());
        humanoid.Bag = new Inventory("worker", null, 8, 4);
        view.Zdo.Set("tcc.worker.key", "foreman/thorstein");
        var stranger = creature.Add(new NpcBody());
        Start(stranger);

        Assert.True(stranger.IsLoaded);
        Assert.False(ForemanWorkerBodies.IsForeman(stranger));
        Assert.Null(ForemanWorkerBodies.FindLive("foreman/thorstein"));

        NpcBody his = Body();
        Assert.True(ForemanWorkerBodies.IsForeman(his));
        Assert.Same(his, ForemanWorkerBodies.FindLive("foreman/thorstein"));
        Assert.Null(ForemanWorkerBodies.FindLive("not a key"));
        Assert.Null(ForemanWorkerBodies.FindLive(string.Empty));
    }
}
