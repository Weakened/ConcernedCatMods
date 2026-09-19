using System;
using TheConcernedCat.ConcernedNPC.Body;
using TheConcernedCat.ConcernedNPC.Roles;
using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>The zero-migration tests: a pre-refactor world's bytes, named as
/// literals, still read.
///
/// <b>Why these are golden fixtures rather than round trips.</b> A test that
/// writes with this code and reads with this code passes however the names are
/// spelled, which is exactly the failure being guarded against. Every literal
/// below was copied from the shipped products' own sources, and a change to the
/// composition that does not also change these lines fails - which is the
/// correct cost, because the change would cost a player every worker they have
/// and everything inside them.</summary>
public sealed class BodyGoldenFixtureTests : IDisposable
{
    public BodyGoldenFixtureTests() => BodyFixtures.ResetWorld();

    public void Dispose() => BodyFixtures.ResetWorld();

    [Fact]
    public void The_settlement_workers_three_field_names_are_exactly_what_shipped()
    {
        Assert.True(NpcBodyFields.TryFor(BodyFixtures.ForemanContract(), out NpcBodyFields fields, out string why), why);

        Assert.Equal(BodyFixtures.WorkerKeyField, fields.Key);
        Assert.Equal(BodyFixtures.WorkerInventoryField, fields.Inventory);
        Assert.Equal(BodyFixtures.WorkerRevisionField, fields.Revision);
    }

    [Fact]
    public void The_stewards_three_field_names_are_exactly_what_shipped()
    {
        Assert.True(NpcBodyFields.TryFor(BodyFixtures.StewardContract(), out NpcBodyFields fields, out string why), why);

        Assert.Equal(BodyFixtures.StewardKeyField, fields.Key);
        Assert.Equal(BodyFixtures.StewardInventoryField, fields.Inventory);
        Assert.Equal(BodyFixtures.StewardRevisionField, fields.Revision);
    }

    [Fact]
    public void Two_roles_that_share_a_key_prefix_still_share_it_and_nothing_else()
    {
        NpcBodyFields.TryFor(BodyFixtures.ForemanContract(), out NpcBodyFields foreman, out _);
        NpcBodyFields.TryFor(BodyFixtures.TeamsterContract(), out NpcBodyFields teamster, out _);

        // Same three names, because the two products really do share a prefix.
        // Unifying them was never the danger; unifying them with the third
        // product's was.
        Assert.Equal(foreman.Key, teamster.Key);
        Assert.NotEqual(BodyFixtures.ForemanPrefab, BodyFixtures.TeamsterPrefab);

        NpcBodyFields.TryFor(BodyFixtures.StewardContract(), out NpcBodyFields steward, out _);
        Assert.NotEqual(foreman.Key, steward.Key);
    }

    [Fact]
    public void A_presentation_contract_has_no_keys_at_all()
    {
        Assert.False(
            NpcBodyFields.TryFor(NpcBodyContract.ForPresentation(), out NpcBodyFields fields, out string why));
        Assert.True(fields.IsEmpty);
        Assert.Contains("stores nothing", why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pre_refactor_saved_body_loads_with_no_migration_code()
    {
        // A world saved by the shipped build: the three literal keys, on an
        // object of the shipped prefab, with an axe in it.
        BodyFixtures.Register(BodyFixtures.ForemanContract());
        var package = new ZPackage();
        package.Items.Add(BodyFixtures.Stack("$item_axe_bronze", 1));
        byte[] storedInventory = package.GetArray();

        NpcBody body = BodyFixtures.SavedBody(BodyFixtures.ForemanPrefab, zdo =>
        {
            zdo.Set(BodyFixtures.WorkerKeyField, "foreman/thorstein");
            zdo.Set(BodyFixtures.WorkerInventoryField, storedInventory);
            zdo.Set(BodyFixtures.WorkerRevisionField, 41);
        });

        Assert.Null(body.Fault);
        Assert.True(body.IsLoaded);
        Assert.Equal(BodyFixtures.Thorstein, body.Identity);
        Assert.Equal(41, body.Revision);
        Assert.Equal(1, body.ItemCount);
    }

    [Fact]
    public void A_pre_refactor_stewards_body_loads_from_its_own_prefix()
    {
        BodyFixtures.Register(BodyFixtures.StewardContract());

        NpcBody body = BodyFixtures.SavedBody(BodyFixtures.StewardPrefab, zdo =>
        {
            zdo.Set(BodyFixtures.StewardKeyField, "steward/sunniva");
            zdo.Set(BodyFixtures.StewardRevisionField, 3);
        });

        Assert.Equal(BodyFixtures.Sunniva, body.Identity);
        Assert.Equal(3, body.Revision);
    }

    [Fact]
    public void One_roles_body_never_reads_another_roles_keys()
    {
        // Both roles registered, both prefabs known, and the Steward's body
        // carries only the Steward's key. A build that composed one key set for
        // everybody would read the settlement worker's key here and find
        // nothing - which is an anonymous body, and an anonymous body is one
        // the census refuses to adopt for the rest of the session.
        BodyFixtures.Register(BodyFixtures.ForemanContract());
        BodyFixtures.Register(BodyFixtures.StewardContract());

        NpcBody steward = BodyFixtures.SavedBody(BodyFixtures.StewardPrefab, zdo =>
        {
            zdo.Set(BodyFixtures.StewardKeyField, "steward/sunniva");
            zdo.Set(BodyFixtures.WorkerKeyField, "foreman/thorstein");
        });

        Assert.Equal(BodyFixtures.Sunniva, steward.Identity);
    }

    [Fact]
    public void A_body_of_a_prefab_nobody_registered_is_inert_and_writes_nothing()
    {
        var instance = new GameObject("SomeoneElsesWorker(Clone)");
        instance.Add(new ZNetView());
        instance.Add(new Humanoid());
        NpcBody body = instance.Add(new NpcBody());

        BodyFixtures.Start(body);

        Assert.False(body.IsLoaded);
        Assert.NotNull(body.Fault);
        Assert.Empty(instance.GetComponent<ZNetView>()!.GetZDO().Written);
    }

    [Fact]
    public void Stamping_a_new_body_writes_its_own_two_keys_and_no_others()
    {
        NpcBodySetup.TryCompose(
            BodyFixtures.ForemanContract(), NpcBodyKeeps.IdentityAndInventory, out NpcBodySetup setup, out _);

        var instance = new GameObject(BodyFixtures.ForemanPrefab + "(Clone)");
        ZNetView view = instance.Add(new ZNetView());

        Assert.True(NpcBody.TryStamp(instance, setup, BodyFixtures.Thorstein));

        Assert.Equal(
            new[] { BodyFixtures.WorkerKeyField, BodyFixtures.WorkerRevisionField },
            view.GetZDO().Written);
        Assert.Equal("foreman/thorstein", view.GetZDO().GetString(BodyFixtures.WorkerKeyField, string.Empty));
    }

    [Fact]
    public void The_identity_text_on_disk_is_the_product_slash_role_form()
    {
        // The wire form has not changed and must not: it is what every existing
        // body carries and what a journal row names.
        Assert.Equal("foreman/thorstein", BodyFixtures.Thorstein.Value);
        Assert.True(NpcIdentity.TryParse("foreman/thorstein", out NpcIdentity parsed));
        Assert.Equal(BodyFixtures.Thorstein, parsed);
    }
}
