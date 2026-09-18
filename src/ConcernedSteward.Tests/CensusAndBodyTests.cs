using System;
using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.ConcernedSteward.Domain;
using TheConcernedCat.ConcernedSteward.Runtime;
using UnityEngine;

namespace TheConcernedCat.ConcernedSteward.Tests;

/// <summary>How many Stewards this world thinks there are, and what he
/// carries.</summary>
[Collection("scene")]
public sealed class CensusTests
{
    private static string Key => StewardRole.Worker.Value;

    [Fact]
    public void No_saved_body_reads_as_missing_rather_than_as_a_failure()
    {
        using var scene = new Scene();

        StewardCensus census = StewardCensusTaker.Take(StewardRole.BodyPrefabName, Key);

        Assert.Equal(StewardCensusVerdict.Missing, census.Verdict);
        Assert.False(census.CanWork);
        Assert.Equal(0, census.Saved);
        Assert.Contains("no Steward", census.Describe());
    }

    [Fact]
    public void One_saved_body_whose_ground_is_not_loaded_is_unloaded_and_not_missing()
    {
        using var scene = new Scene();
        scene.SaveBody(Key);

        StewardCensus census = StewardCensusTaker.Take(StewardRole.BodyPrefabName, Key);

        // Missing would be the answer that lets a second Steward be recruited
        // for a settlement the player has simply walked away from.
        Assert.Equal(StewardCensusVerdict.Unloaded, census.Verdict);
        Assert.Equal(1, census.Saved);
        Assert.False(census.CanWork);
        Assert.Contains("not loaded", census.Describe());
    }

    [Fact]
    public void Two_saved_bodies_with_one_identity_stop_him_and_destroy_nothing()
    {
        using var scene = new Scene();
        scene.SaveBody(Key);
        scene.SaveBody(Key);

        StewardCensus census = StewardCensusTaker.Take(StewardRole.BodyPrefabName, Key);

        Assert.Equal(StewardCensusVerdict.Duplicated, census.Verdict);
        Assert.Equal(2, census.Saved);
        Assert.False(census.CanWork);
        Assert.Contains("Nothing has been destroyed", census.Describe());
    }

    [Fact]
    public void The_scans_own_duplicate_row_is_not_mistaken_for_a_second_body()
    {
        using var scene = new Scene();

        // Vanilla's iterative sector walk adds sector 0 again on its
        // terminating call -- confirmed present in 1.0.14. A body counted twice
        // would read as a duplicate identity and stop him for no reason.
        scene.SaveBody(Key);

        StewardCensus census = StewardCensusTaker.Take(StewardRole.BodyPrefabName, Key);

        Assert.Equal(1, census.Saved);
        Assert.NotEqual(StewardCensusVerdict.Duplicated, census.Verdict);
    }

    [Fact]
    public void A_body_with_no_identity_is_counted_and_left_completely_alone()
    {
        using var scene = new Scene();
        ZDO anonymous = scene.SaveBody(null);

        StewardCensus census = StewardCensusTaker.Take(StewardRole.BodyPrefabName, Key);

        Assert.Equal(StewardCensusVerdict.Missing, census.Verdict);
        Assert.Equal(1, census.Unidentified);
        Assert.Contains("left alone", census.Describe());

        // Neither adopted nor destroyed: nothing was written to it.
        Assert.Empty(anonymous.Written);
    }

    [Fact]
    public void Somebody_elses_worker_is_not_his()
    {
        using var scene = new Scene();
        scene.SaveBody("foreman/thorstein");

        StewardCensus census = StewardCensusTaker.Take(StewardRole.BodyPrefabName, Key);

        Assert.Equal(StewardCensusVerdict.Missing, census.Verdict);
        Assert.Equal(0, census.Unidentified);
    }

    [Fact]
    public void A_world_that_cannot_be_asked_is_unknown_and_never_reads_as_one_body()
    {
        using var scene = new Scene();
        scene.SaveBody(Key);
        ZDOMan.instance!.Throws = true;

        StewardCensus census = StewardCensusTaker.Take(StewardRole.BodyPrefabName, Key);

        Assert.Equal(StewardCensusVerdict.Unknown, census.Verdict);
        Assert.False(census.CanWork);
    }

    [Fact]
    public void No_world_at_all_is_unknown()
    {
        using var scene = new Scene();
        ZDOMan.instance = null;

        Assert.Equal(
            StewardCensusVerdict.Unknown,
            StewardCensusTaker.Take(StewardRole.BodyPrefabName, Key).Verdict);
    }
}

/// <summary>What he carries, and where it is written down.</summary>
[Collection("scene")]
public sealed class BodyTests
{
    [Fact]
    public void His_inventory_is_written_to_his_own_object_and_to_nothing_else()
    {
        using var scene = new Scene();
        (StewardBody body, ZDO zdo) = PlaceBody(scene);

        body.Inventory!.AddItem(Scene.Stack("$item_wood", 6));

        Assert.True(body.LastChangePersisted);
        Assert.All(
            zdo.Written,
            key => Assert.StartsWith("tcc.steward.", key));
        Assert.Contains(StewardBody.InventoryField, zdo.Written);
        Assert.Contains(StewardBody.RevisionField, zdo.Written);
    }

    [Fact]
    public void Every_change_is_persisted_inside_the_call_that_made_it()
    {
        using var scene = new Scene();
        (StewardBody body, ZDO zdo) = PlaceBody(scene);

        int before = zdo.Written.Count;
        body.Inventory!.AddItem(Scene.Stack("$item_wood", 6));
        int afterAdd = zdo.Written.Count;
        body.Inventory.RemoveItem(body.Inventory.GetAllItems()[0], 2);

        // Not batched, not deferred: a withdrawal, the walk and the feed have
        // to share one snapshot, so the write happens in the mutating call.
        Assert.True(afterAdd > before);
        Assert.True(zdo.Written.Count > afterAdd);
    }

    [Fact]
    public void A_change_that_cannot_be_written_makes_the_body_unusable_rather_than_silent()
    {
        using var scene = new Scene();
        (StewardBody body, ZDO zdo) = PlaceBody(scene);
        var pack = new StewardPackStore(() => body);
        Assert.True(pack.IsAvailable);

        zdo.FailWrites = true;
        body.Inventory!.AddItem(Scene.Stack("$item_wood", 6));

        Assert.False(body.LastChangePersisted);
        Assert.False(pack.IsAvailable);
    }

    [Fact]
    public void A_stored_inventory_comes_back_on_the_next_load()
    {
        using var scene = new Scene();
        (StewardBody body, ZDO zdo) = PlaceBody(scene);
        body.Inventory!.AddItem(Scene.Stack("$item_wood", 6));

        // A second body over the same stored object: what a reload looks like.
        StewardBody reloaded = Attach(new GameObject("steward-again"), zdo);

        Assert.True(reloaded.IsLoaded);
        Assert.Equal(6, reloaded.Inventory!.GetAllItems().Sum(item => item.m_stack));
    }

    [Fact]
    public void A_body_that_cannot_read_what_it_carries_is_inert_and_does_not_save_over_it()
    {
        using var scene = new Scene();
        var host = new GameObject("steward");
        ZNetView view = host.Add(new ZNetView());
        view.Zdo.m_uid = new ZDOID(7L, 99u);
        view.Zdo.Set(StewardBody.KeyField, StewardRole.Worker.Value);

        // A package whose bytes name nothing: the load throws.
        view.Zdo.Set(StewardBody.InventoryField, new byte[] { 1, 2, 3 });
        host.Add(new Humanoid());

        StewardBody body = host.Add(new StewardBody());

        // From here on, every write is one the body made.
        view.Zdo.Written.Clear();
        Start(body);

        Assert.False(body.IsLoaded);
        Assert.NotNull(body.Fault);

        // And nothing was written over the stored inventory: a body that could
        // not read what it carries must not save an empty pack over a full one.
        Assert.Empty(view.Zdo.Written);
        Assert.False(new StewardPackStore(() => body).IsAvailable);
    }

    [Fact]
    public void Dying_puts_everything_on_the_ground_and_reports_what_landed()
    {
        using var scene = new Scene();
        (StewardBody body, _) = PlaceBody(scene);
        body.Inventory!.AddItem(Scene.Stack("$item_wood", 6));

        var reported = new List<DroppedItem>();
        StewardBody.Died = (_, dropped, _) => reported.AddRange(dropped);
        body.Humanoid!.m_onDeath!();

        DroppedItem item = Assert.Single(reported);
        Assert.Equal("$item_wood", item.SharedName);
        Assert.Equal(6, item.Count);
        Assert.Single(body.Humanoid.Dropped);
        StewardBody.Died = null;
    }

    [Fact]
    public void One_item_that_will_not_drop_does_not_take_the_rest_of_the_pack_with_it()
    {
        using var scene = new Scene();
        (StewardBody body, _) = PlaceBody(scene);
        body.Inventory!.AddItem(Scene.Stack("$item_wood", 6, maxStack: 6));
        body.Inventory.AddItem(Scene.Stack("$item_stone", 4, maxStack: 4));

        // Vanilla's DropItem touches the animator and the visual equipment, and
        // this body is a clone: one null must not cost the rest.
        body.Humanoid!.DropThrows = item => item.m_shared.m_name == "$item_wood";

        var reported = new List<DroppedItem>();
        StewardBody.Died = (_, dropped, _) => reported.AddRange(dropped);
        body.Humanoid.m_onDeath!();

        Assert.Single(reported);
        Assert.Equal("$item_stone", reported[0].SharedName);
        StewardBody.Died = null;
    }

    private static (StewardBody body, ZDO zdo) PlaceBody(Scene scene)
    {
        var host = new GameObject("steward");
        ZNetView view = host.Add(new ZNetView());
        view.Zdo.m_uid = new ZDOID(7L, 42u);
        view.Zdo.Set(StewardBody.KeyField, StewardRole.Worker.Value);
        host.Add(new Humanoid());
        StewardBody body = host.Add(new StewardBody());
        Start(body);
        return (body, view.Zdo);
    }

    private static StewardBody Attach(GameObject host, ZDO stored)
    {
        ZNetView view = host.Add(new ZNetView { Zdo = stored });
        host.Add(new Humanoid());
        StewardBody body = host.Add(new StewardBody());
        Start(body);
        return body;
    }

    /// <summary>Unity calls <c>Start</c>; outside the player nothing does, so
    /// the test does. Reflection rather than making the method internal,
    /// because the shipped file should not gain a wider member to suit a
    /// test.</summary>
    private static void Start(StewardBody body) =>
        typeof(StewardBody)
            .GetMethod("Start", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(body, Array.Empty<object>());
}
