using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Body;
using TheConcernedCat.ConcernedNPC.Roles;
using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>A body's own persistence, against a stubbed inventory and network
/// object.
///
/// This is the code that makes a relog, a zone unload and a despawn honest, and
/// in two of the three products it replaces it had no coverage at all until a
/// second round of review asked for some.</summary>
public sealed class NpcBodyTests : IDisposable
{
    private readonly List<string> _errors = new List<string>();

    public NpcBodyTests()
    {
        BodyFixtures.ResetWorld();
        NpcBody.ErrorLog += _errors.Add;
    }

    public void Dispose() => BodyFixtures.ResetWorld();

    [Fact]
    public void A_change_is_written_to_its_own_object_in_the_same_call_that_made_it()
    {
        BodyFixtures.Register(BodyFixtures.ForemanContract());
        NpcBody body = BodyFixtures.SavedBody(BodyFixtures.ForemanPrefab, Stamped);
        ZDO zdo = body.View!.GetZDO();
        int revision = body.Revision;

        body.Inventory!.AddItem(BodyFixtures.Stack("$item_stone", 7));

        // Not queued and not at the next save: written inside the add, through
        // the inventory's own change callback, exactly as a container does it.
        Assert.True(body.LastChangePersisted);
        Assert.True(body.Revision > revision);
        Assert.NotNull(zdo.GetByteArray(BodyFixtures.WorkerInventoryField, null));
        Assert.Equal(body.Revision, zdo.GetInt(BodyFixtures.WorkerRevisionField, -1));
    }

    [Fact]
    public void Nothing_is_written_while_the_stored_inventory_is_being_loaded()
    {
        BodyFixtures.Register(BodyFixtures.ForemanContract());
        var package = new ZPackage();
        package.Items.Add(BodyFixtures.Stack("$item_stone", 3));
        byte[] stored = package.GetArray();

        NpcBody body = BodyFixtures.SavedBody(BodyFixtures.ForemanPrefab, zdo =>
        {
            Stamped(zdo);
            zdo.Set(BodyFixtures.WorkerInventoryField, stored);
            zdo.Set(BodyFixtures.WorkerRevisionField, 9);
        });

        // Loading raises the same change callback a real add does. If the
        // guard were missing, the revision would have moved during Start and an
        // empty inventory could be written over a full one on the very first
        // frame.
        Assert.Equal(9, body.Revision);
        Assert.Single(body.Inventory!.GetAllItems());
        Assert.Equal(3, body.Inventory.GetAllItems()[0].m_stack);
    }

    [Fact]
    public void A_body_that_cannot_read_what_it_carries_is_inert_and_never_writes()
    {
        BodyFixtures.Register(BodyFixtures.ForemanContract());
        var package = new ZPackage();
        byte[] stored = package.GetArray();

        var instance = new GameObject(BodyFixtures.ForemanPrefab + "(Clone)");
        ZNetView view = instance.Add(new ZNetView());
        Humanoid humanoid = instance.Add(new Humanoid());
        humanoid.Bag.LoadThrows = true;
        NpcBody body = instance.Add(new NpcBody());
        view.GetZDO().Set(BodyFixtures.WorkerKeyField, "foreman/thorstein");
        view.GetZDO().Set(BodyFixtures.WorkerInventoryField, stored);
        view.GetZDO().Written.Clear();

        BodyFixtures.Start(body);

        Assert.False(body.IsLoaded);
        Assert.NotNull(body.Fault);
        Assert.False(body.TryPersist());
        Assert.Empty(view.GetZDO().Written);
        Assert.NotEmpty(_errors);
    }

    [Fact]
    public void A_body_that_keeps_only_its_identity_never_writes_an_inventory()
    {
        // The cart puller. Unifying the code must not quietly give him
        // persistence he does not have today.
        BodyFixtures.Register(BodyFixtures.TeamsterContract(), NpcBodyKeeps.IdentityOnly);
        NpcBody body = BodyFixtures.SavedBody(BodyFixtures.TeamsterPrefab, zdo =>
            zdo.Set(BodyFixtures.WorkerKeyField, "teamster/gunnar"));
        ZDO zdo = body.View!.GetZDO();
        zdo.Written.Clear();

        body.Inventory!.AddItem(BodyFixtures.Stack("$item_wood", 5));

        Assert.Equal(BodyFixtures.Gunnar, body.Identity);
        Assert.Equal(NpcBodyKeeps.IdentityOnly, body.Keeps);
        Assert.Empty(zdo.Written);
        Assert.False(body.TryPersist());
    }

    [Fact]
    public void A_failed_write_is_reported_rather_than_swallowed()
    {
        BodyFixtures.Register(BodyFixtures.ForemanContract());
        NpcBody body = BodyFixtures.SavedBody(BodyFixtures.ForemanPrefab, Stamped);

        body.View!.GetZDO().FailWrites = true;
        body.Inventory!.AddItem(BodyFixtures.Stack("$item_stone", 1));

        // A transfer involving this body is now uncertain, never completed.
        Assert.False(body.LastChangePersisted);
        Assert.NotEmpty(_errors);
    }

    [Fact]
    public void An_equipped_item_comes_back_in_its_hands()
    {
        BodyFixtures.Register(BodyFixtures.ForemanContract());
        ItemDrop.ItemData axe = BodyFixtures.Stack("$item_axe", 1);
        axe.m_equipped = true;
        var package = new ZPackage();
        package.Items.Add(axe);
        byte[] stored = package.GetArray();

        NpcBody body = BodyFixtures.SavedBody(BodyFixtures.ForemanPrefab, zdo =>
        {
            Stamped(zdo);
            zdo.Set(BodyFixtures.WorkerInventoryField, stored);
        });

        Assert.Contains(axe, body.Humanoid!.Equipped);
    }

    [Fact]
    public void Death_puts_everything_on_the_ground_and_says_what_reached_it()
    {
        BodyFixtures.Register(BodyFixtures.ForemanContract());
        NpcBody body = BodyFixtures.SavedBody(BodyFixtures.ForemanPrefab, Stamped);
        body.Inventory!.AddItem(BodyFixtures.Stack("$item_stone", 4));
        body.Inventory.AddItem(BodyFixtures.Stack("$item_wood", 2));

        IReadOnlyList<NpcDroppedItem>? reported = null;
        NpcBody.Died += (_, dropped, _) => reported = dropped;

        body.Humanoid!.m_onDeath!.Invoke();

        Assert.NotNull(reported);
        Assert.Equal(2, reported!.Count);
        Assert.Equal(2, body.Humanoid.Dropped.Count);
    }

    [Fact]
    public void One_stack_that_cannot_be_dropped_does_not_take_the_rest_or_the_report_with_it()
    {
        BodyFixtures.Register(BodyFixtures.ForemanContract());
        NpcBody body = BodyFixtures.SavedBody(BodyFixtures.ForemanPrefab, Stamped);
        ItemDrop.ItemData bad = BodyFixtures.Stack("$item_stone", 4);
        body.Inventory!.AddItem(bad);
        body.Inventory.AddItem(BodyFixtures.Stack("$item_wood", 2));
        body.Humanoid!.DropThrows = item => ReferenceEquals(item, bad);

        IReadOnlyList<NpcDroppedItem>? reported = null;
        NpcBody.Died += (_, dropped, _) => reported = dropped;

        body.Humanoid.m_onDeath!.Invoke();

        Assert.NotNull(reported);
        Assert.Single(reported!);
        Assert.NotEmpty(_errors);
    }

    [Fact]
    public void A_report_that_throws_does_not_escape_into_the_engines_death_handler()
    {
        BodyFixtures.Register(BodyFixtures.ForemanContract());
        NpcBody body = BodyFixtures.SavedBody(BodyFixtures.ForemanPrefab, Stamped);
        NpcBody.Died += (_, _, _) => throw new InvalidOperationException("the ledger is broken");

        body.Humanoid!.m_onDeath!.Invoke();

        Assert.NotEmpty(_errors);
    }

    [Fact]
    public void Live_bodies_are_found_by_prefab_and_then_by_identity()
    {
        // Two roles, one shared key prefix, two prefabs, and the SAME identity
        // text in both. Searching by identity alone would return the wrong one.
        BodyFixtures.Register(BodyFixtures.ForemanContract());
        BodyFixtures.Register(BodyFixtures.TeamsterContract(), NpcBodyKeeps.IdentityOnly);

        NpcBody foreman = BodyFixtures.SavedBody(BodyFixtures.ForemanPrefab, zdo =>
            zdo.Set(BodyFixtures.WorkerKeyField, "foreman/thorstein"));
        NpcBody teamster = BodyFixtures.SavedBody(BodyFixtures.TeamsterPrefab, zdo =>
            zdo.Set(BodyFixtures.WorkerKeyField, "foreman/thorstein"));

        Assert.Same(
            foreman,
            NpcBody.FindLive(BodyFixtures.ForemanContract(), BodyFixtures.Thorstein));
        Assert.Same(
            teamster,
            NpcBody.FindLive(BodyFixtures.TeamsterContract(), BodyFixtures.Thorstein));
    }

    [Fact]
    public void A_world_going_away_drops_every_body_of_the_role_that_says_so()
    {
        BodyFixtures.Register(BodyFixtures.ForemanContract());
        BodyFixtures.SavedBody(BodyFixtures.ForemanPrefab, Stamped);
        Assert.Single(NpcBody.LiveFor(BodyFixtures.ForemanContract()));

        NpcBody.ForgetAll(BodyFixtures.ForemanContract());

        Assert.Empty(NpcBody.LiveFor(BodyFixtures.ForemanContract()));
    }

    [Fact]
    public void One_roles_teardown_leaves_another_roles_bodies_standing()
    {
        // ForgetAll used to clear every role's bodies, so whichever product
        // noticed the world go first blinded the others - and a runtime that
        // has forgotten a body still in the world is one that will permit a
        // second body for the same identity.
        BodyFixtures.Register(BodyFixtures.ForemanContract());
        BodyFixtures.Register(BodyFixtures.TeamsterContract(), NpcBodyKeeps.IdentityOnly);
        BodyFixtures.SavedBody(BodyFixtures.ForemanPrefab, Stamped);
        BodyFixtures.SavedBody(BodyFixtures.TeamsterPrefab, Stamped);

        NpcBody.ForgetAll(BodyFixtures.ForemanContract());

        Assert.Empty(NpcBody.LiveFor(BodyFixtures.ForemanContract()));
        Assert.Single(NpcBody.LiveFor(BodyFixtures.TeamsterContract()));
    }

    [Fact]
    public void A_contract_nobody_registered_reaches_no_bodies_and_forgets_none()
    {
        BodyFixtures.Register(BodyFixtures.ForemanContract());
        BodyFixtures.SavedBody(BodyFixtures.ForemanPrefab, Stamped);

        // The right prefab under the wrong key prefix is not that role's
        // contract, and is refused rather than quietly treated as it.
        NpcBodyContract wrongPrefix =
            NpcBodyContract.ForWorker(BodyFixtures.ForemanPrefab, BodyFixtures.StewardKeyPrefix);

        Assert.Empty(NpcBody.LiveFor(wrongPrefix));
        Assert.Empty(NpcBody.LiveFor(BodyFixtures.StewardContract()));

        NpcBody.ForgetAll(wrongPrefix);
        Assert.Single(NpcBody.LiveFor(BodyFixtures.ForemanContract()));
    }

    private static void Stamped(ZDO zdo) => zdo.Set(BodyFixtures.WorkerKeyField, "foreman/thorstein");
}
