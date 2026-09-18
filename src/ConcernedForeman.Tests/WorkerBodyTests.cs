using System;
using System.Collections.Generic;
using System.Reflection;
using TheConcernedCat.ConcernedForeman.Runtime.Custody;
using UnityEngine;
using static ConcernedForeman.Tests.ForemanFixtures;

namespace ConcernedForeman.Tests;

/// <summary>D9: the worker body's own persistence, against a vanilla inventory
/// and a vanilla network object modelled on 1.0.14.
///
/// This is the code that makes a relog, a zone unload and a despawn refusal
/// honest, and until review R2's M4 it was proved by reading alone.</summary>
public sealed class WorkerBodyTests : IDisposable
{
    private readonly List<string> _errors = new List<string>();

    public WorkerBodyTests()
    {
        ClearLiveBodies();
        ObjectDB.instance = new ObjectDB();
        WorkerBody.ErrorLog = _errors.Add;
        WorkerBody.Loaded = null;
        WorkerBody.Died = null;
    }

    public void Dispose()
    {
        WorkerBody.ErrorLog = null;
        WorkerBody.Loaded = null;
        WorkerBody.Died = null;
        ClearLiveBodies();
    }

    private static void ClearLiveBodies() =>
        ((List<WorkerBody>)typeof(WorkerBody)
            .GetField("s_live", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!).Clear();

    [Fact]
    public void AChangeIsWrittenToHisObjectInTheSameCallThatMadeIt()
    {
        WorkerBody body = Body();
        ZDO zdo = body.View!.Zdo;
        int revision = body.Revision;

        body.Inventory!.AddItem(Stack(Stone, 7));

        // Not queued, not at the next save: written inside AddItem, through the
        // inventory's own change callback, exactly as a Container does it.
        Assert.True(body.LastChangePersisted);
        Assert.True(body.Revision > revision);
        Assert.NotNull(zdo.GetByteArray(WorkerBody.InventoryField, null));
        Assert.Equal(body.Revision, zdo.GetInt(WorkerBody.RevisionField, -1));
    }

    [Fact]
    public void NothingIsWrittenWhileHisStoredInventoryIsBeingLoaded()
    {
        // The loading guard is what stops an empty inventory being written over
        // a carried one when a body wakes up.
        WorkerBody first = Body();
        first.Inventory!.AddItem(Stack(Stone, 4));
        ZDO zdo = first.View!.Zdo;
        int revisionOnDisk = zdo.GetInt(WorkerBody.RevisionField, -1);
        zdo.Written.Clear();

        WorkerBody second = Body(key: string.Empty, zdo: zdo);

        Assert.True(second.IsLoaded);
        Assert.Empty(zdo.Written);
        Assert.Equal(revisionOnDisk, zdo.GetInt(WorkerBody.RevisionField, -1));
        Assert.Equal(4, EngineInventoryPort.CountIn(second.Inventory!, Stone));
    }

    [Fact]
    public void WhatHeCarriesAndWhatHeWasHoldingComeBackWithHim()
    {
        WorkerBody first = Body();
        first.Inventory!.AddItem(Stack(Stone, 12));
        ItemDrop.ItemData axe = Axe();
        axe.m_equipped = true;
        first.Inventory.AddItem(axe);

        WorkerBody second = Body(key: string.Empty, zdo: first.View!.Zdo);

        Assert.Equal("foreman/thorstein", second.Key);
        Assert.Equal(12, EngineInventoryPort.CountIn(second.Inventory!, Stone));
        Assert.Equal(2, second.ItemCount);

        // An item saved as equipped comes back in his hands rather than loose.
        Assert.Single(second.Humanoid!.Equipped);
        Assert.Equal("$item_axe_bronze", second.Humanoid.Equipped[0].m_shared.m_name);
    }

    [Fact]
    public void ABodyThatCannotReadWhatItCarriesIsInertAndNeverSavesOverIt()
    {
        WorkerBody first = Body();
        first.Inventory!.AddItem(Stack(Stone, 5));
        ZDO zdo = first.View!.Zdo;

        // The stored package is unreadable: the body must not come up empty and
        // then write that emptiness over what it was carrying.
        zdo.Set(WorkerBody.InventoryField, new byte[] { 1, 2, 3 });
        zdo.Written.Clear();
        WorkerBody broken = Body(key: string.Empty, zdo: zdo);

        Assert.False(broken.IsLoaded);
        Assert.NotNull(broken.Fault);
        Assert.Empty(zdo.Written);
        Assert.DoesNotContain(broken, WorkerBody.Live);
        Assert.Contains(_errors, line => line.Contains("is inert"));

        // And it refuses to be a place: no work can move anything through it.
        Assert.False(new WorkerInventoryPort(broken, () => 100f).IsAvailable);
    }

    [Fact]
    public void OnlyHisOwnThreeFieldsAreEverWritten()
    {
        WorkerBody body = Body();
        body.Inventory!.AddItem(Stack(Stone, 3));
        body.Inventory.AddItem(Axe());

        foreach (string key in body.View!.Zdo.Written)
        {
            Assert.Contains(key, new[] { WorkerBody.KeyField, WorkerBody.InventoryField, WorkerBody.RevisionField });
        }
    }

    [Fact]
    public void AWriteThatFailsIsReportedRatherThanAssumed()
    {
        WorkerBody body = Body();
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
        WorkerBody body = Body();
        body.Inventory!.AddItem(Stack(Stone, 6));
        body.Inventory.AddItem(Axe());
        body.Inventory.AddItem(Stack(Wood, 4));

        body.Humanoid!.DropThrows = item => item.m_shared.m_name == "$item_axe_bronze";

        var dropped = new List<string>();
        Vector3? where = null;
        WorkerBody.Died = (who, items, place) =>
        {
            Assert.Same(body, who);
            where = place;
            foreach (DroppedItem item in items)
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
        WorkerBody body = Body();
        var raised = 0;
        WorkerBody.Died = (_, items, _) =>
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
        var body = creature.Add(new WorkerBody());
        Start(body);

        Assert.True(body.IsLoaded);
        Assert.Equal(string.Empty, body.Key);
        Assert.Null(WorkerBody.FindLive("foreman/thorstein"));
    }

    [Fact]
    public void AStampNeedsAnOwnedValidObject()
    {
        var creature = new GameObject("CF_SettlementWorker");
        ZNetView view = creature.Add(new ZNetView());
        creature.Add(new Humanoid());

        view.Owner = false;
        Assert.False(WorkerBody.TryStamp(creature, "foreman/thorstein"));

        view.Owner = true;
        view.Valid = false;
        Assert.False(WorkerBody.TryStamp(creature, "foreman/thorstein"));

        view.Valid = true;
        Assert.True(WorkerBody.TryStamp(creature, "foreman/thorstein"));
        Assert.Equal("foreman/thorstein", view.Zdo.GetString(WorkerBody.KeyField, string.Empty));
    }
}
