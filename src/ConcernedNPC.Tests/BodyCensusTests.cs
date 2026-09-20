using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Body;
using TheConcernedCat.ConcernedNPC.Roles;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>The census: what a world holds of one identity's bodies, and what
/// that permits.
///
/// The contamination test is the one that matters most. Two shipped roles share
/// a key prefix, so a census that read the key before it filtered by prefab
/// would find two bodies for one identity, report it duplicated, and refuse to
/// work for the rest of the session - in a world where nothing is wrong.
/// </summary>
public sealed class BodyCensusTests : IDisposable
{
    public BodyCensusTests()
    {
        BodyFixtures.ResetWorld();
        ZDOMan.instance = new ZDOMan();
    }

    public void Dispose() => BodyFixtures.ResetWorld();

    [Fact]
    public void Nothing_may_be_built_until_the_walk_has_finished()
    {
        NpcWorldBodies census = Census(BodyFixtures.ForemanContract());

        // Nobody ran it. Reporting "nobody here" would build a second body for
        // an identity that already has one.
        Assert.False(census.IsComplete);
        Assert.Equal(NpcBodyPresence.Searching, census.TallyFor(BodyFixtures.Thorstein, 0, false).Presence);
        Assert.False(census.TallyFor(BodyFixtures.Thorstein, 0, false).MaySpawn);
    }

    [Fact]
    public void With_no_world_the_walk_never_finishes_rather_than_reporting_an_empty_one()
    {
        ZDOMan.instance = null!;
        NpcWorldBodies census = Census(BodyFixtures.ForemanContract());

        Assert.False(census.RunToCompletion());
        Assert.Equal(NpcBodyPresence.Searching, census.TallyFor(BodyFixtures.Thorstein, 0, false).Presence);
    }

    [Fact]
    public void A_finished_walk_over_an_empty_world_is_the_one_state_that_permits_a_body()
    {
        NpcWorldBodies census = Census(BodyFixtures.ForemanContract());

        Assert.True(census.RunToCompletion());
        NpcBodyTally tally = census.TallyFor(BodyFixtures.Thorstein, 0, false);

        Assert.Equal(NpcBodyPresence.Missing, tally.Presence);
        Assert.True(tally.MaySpawn);
    }

    [Fact]
    public void The_repeated_first_sector_does_not_turn_one_body_into_two()
    {
        // The game's iterative walk adds the first sector again on its
        // terminating call. Counted twice, one body reads as a duplicated
        // identity and the runtime stops for good.
        Save(BodyFixtures.ForemanPrefab, BodyFixtures.WorkerKeyField, "foreman/thorstein");
        NpcWorldBodies census = Census(BodyFixtures.ForemanContract());

        census.RunToCompletion();

        Assert.Equal(1, census.SavedBodiesFor(BodyFixtures.Thorstein));
        Assert.Equal(NpcBodyPresence.NotLoaded, census.TallyFor(BodyFixtures.Thorstein, 0, false).Presence);
    }

    [Fact]
    public void An_incremental_walk_reaches_the_same_answer_as_a_single_pass()
    {
        ZDOMan.instance.SectorsPerCall = 1;
        for (int index = 0; index < 4; index++)
        {
            Save(BodyFixtures.ForemanPrefab, BodyFixtures.WorkerKeyField, "foreman/worker-" + index);
        }

        NpcWorldBodies census = Census(BodyFixtures.ForemanContract());

        int steps = 0;
        while (!census.Advance())
        {
            steps++;
            Assert.True(steps < 20, "the walk did not terminate");
        }

        Assert.True(steps >= 4, "the walk finished in one call, so nothing incremental was exercised");
        Assert.Equal(1, census.SavedBodiesFor(new NpcIdentity("foreman", "worker-2")));
    }

    [Fact]
    public void Two_roles_that_share_a_key_prefix_never_see_each_others_bodies()
    {
        // Both bodies carry the SAME key name and the SAME identity text. Only
        // the prefab tells them apart, which is why the prefab is the first
        // filter and the key is the second.
        Save(BodyFixtures.ForemanPrefab, BodyFixtures.WorkerKeyField, "foreman/thorstein");
        Save(BodyFixtures.TeamsterPrefab, BodyFixtures.WorkerKeyField, "foreman/thorstein");

        NpcWorldBodies foreman = Census(BodyFixtures.ForemanContract());
        NpcWorldBodies teamster = Census(BodyFixtures.TeamsterContract());
        foreman.RunToCompletion();
        teamster.RunToCompletion();

        Assert.Equal(1, foreman.SavedBodiesFor(BodyFixtures.Thorstein));
        Assert.Equal(1, teamster.SavedBodiesFor(BodyFixtures.Thorstein));
        Assert.False(foreman.TallyFor(BodyFixtures.Thorstein, 0, false).IsDuplicated);
        Assert.False(teamster.TallyFor(BodyFixtures.Thorstein, 0, false).IsDuplicated);
    }

    [Fact]
    public void A_body_with_no_identity_is_counted_and_never_adopted()
    {
        Save(BodyFixtures.ForemanPrefab, BodyFixtures.WorkerKeyField, string.Empty);
        NpcWorldBodies census = Census(BodyFixtures.ForemanContract());

        census.RunToCompletion();

        Assert.Equal(1, census.Unidentified);
        Assert.Equal(0, census.SavedBodiesFor(BodyFixtures.Thorstein));

        // Counted, so a player can be told there is a stranger in their world -
        // and still Missing, because an anonymous body is nobody's.
        NpcBodyTally tally = census.TallyFor(BodyFixtures.Thorstein, 0, false);
        Assert.Equal(1, tally.Unidentified);
        Assert.True(tally.MaySpawn);
    }

    [Fact]
    public void A_key_this_build_cannot_parse_is_grouped_under_what_it_actually_says()
    {
        Save(BodyFixtures.ForemanPrefab, BodyFixtures.WorkerKeyField, "not a key");
        NpcWorldBodies census = Census(BodyFixtures.ForemanContract());

        census.RunToCompletion();

        // Not silently adopted into a well-formed identity, and not thrown in
        // with the anonymous either.
        Assert.Equal(0, census.Unidentified);
        Assert.Equal(0, census.SavedBodiesFor(BodyFixtures.Thorstein));
    }

    [Fact]
    public void Two_bodies_with_one_identity_are_reported_and_neither_is_touched()
    {
        Save(BodyFixtures.ForemanPrefab, BodyFixtures.WorkerKeyField, "foreman/thorstein");
        Save(BodyFixtures.ForemanPrefab, BodyFixtures.WorkerKeyField, "foreman/thorstein");
        NpcWorldBodies census = Census(BodyFixtures.ForemanContract());

        census.RunToCompletion();
        NpcBodyTally tally = census.TallyFor(BodyFixtures.Thorstein, 0, false);

        Assert.Equal(NpcBodyPresence.Duplicated, tally.Presence);
        Assert.False(tally.MaySpawn);
        Assert.Equal(2, ZDOMan.instance.Saved[BodyFixtures.ForemanPrefab].Count);
    }

    [Fact]
    public void A_loaded_body_is_reported_present_before_the_walk_finishes()
    {
        // Deliberate. A body standing in front of the player is not in doubt,
        // and waiting for a scan to bind it would leave it idle for no reason.
        // The scan may still turn the answer into Duplicated later, which costs
        // nothing, because the irreversible act needs Missing and an unfinished
        // walk can never produce one.
        NpcWorldBodies census = Census(BodyFixtures.ForemanContract());

        Assert.Equal(NpcBodyPresence.Present, census.TallyFor(BodyFixtures.Thorstein, 1, false).Presence);
        Assert.Equal(NpcBodyPresence.Faulted, census.TallyFor(BodyFixtures.Thorstein, 1, true).Presence);
        Assert.False(census.TallyFor(BodyFixtures.Thorstein, 1, false).MaySpawn);
    }

    [Fact]
    public void A_retired_body_is_forgotten_so_a_replacement_can_be_built()
    {
        ZDO record = Save(BodyFixtures.ForemanPrefab, BodyFixtures.WorkerKeyField, "foreman/thorstein");
        NpcWorldBodies census = Census(BodyFixtures.ForemanContract());
        census.RunToCompletion();
        Assert.False(census.TallyFor(BodyFixtures.Thorstein, 0, false).MaySpawn);

        census.Forget(record.m_uid);

        Assert.True(census.TallyFor(BodyFixtures.Thorstein, 0, false).MaySpawn);
    }

    [Fact]
    public void Pruning_drops_every_body_the_caller_says_is_gone()
    {
        ZDO first = Save(BodyFixtures.ForemanPrefab, BodyFixtures.WorkerKeyField, "foreman/thorstein");
        Save(BodyFixtures.ForemanPrefab, BodyFixtures.WorkerKeyField, "foreman/thorstein");
        NpcWorldBodies census = Census(BodyFixtures.ForemanContract());
        census.RunToCompletion();
        Assert.Equal(2, census.SavedBodiesFor(BodyFixtures.Thorstein));

        census.Prune(id => id.Equals(first.m_uid));

        Assert.Equal(1, census.SavedBodiesFor(BodyFixtures.Thorstein));
    }

    [Fact]
    public void A_world_load_forgets_the_previous_worlds_object_ids()
    {
        Save(BodyFixtures.ForemanPrefab, BodyFixtures.WorkerKeyField, "foreman/thorstein");
        NpcWorldBodies census = Census(BodyFixtures.ForemanContract());
        census.RunToCompletion();

        census.Restart();

        // Every network id in the previous world was renumbered, so carrying
        // one across would name some other object.
        Assert.False(census.IsComplete);
        Assert.Equal(0, census.SavedBodiesFor(BodyFixtures.Thorstein));
    }

    [Fact]
    public void What_a_saved_body_carries_can_be_read_without_loading_it()
    {
        var package = new ZPackage();
        package.Items.Add(BodyFixtures.Stack("$item_stone", 12));
        byte[] stored = package.GetArray();

        ZDO record = Save(BodyFixtures.ForemanPrefab, BodyFixtures.WorkerKeyField, "foreman/thorstein");
        record.Set(BodyFixtures.WorkerInventoryField, stored);
        NpcWorldBodies census = Census(BodyFixtures.ForemanContract());

        Inventory? carried = census.TryReadStoredInventory(record);

        Assert.NotNull(carried);
        Assert.Single(carried!.GetAllItems());
        Assert.Equal(12, carried.GetAllItems()[0].m_stack);
    }

    [Fact]
    public void A_role_whose_body_keeps_no_inventory_is_never_asked_what_it_carries()
    {
        ZDO record = Save(BodyFixtures.TeamsterPrefab, BodyFixtures.WorkerKeyField, "teamster/gunnar");
        NpcWorldBodies census = Census(BodyFixtures.TeamsterContract(), NpcBodyKeeps.IdentityOnly);

        Assert.Null(census.TryReadStoredInventory(record));
    }

    [Fact]
    public void A_damaged_world_index_leaves_the_walk_unfinished_rather_than_throwing_into_the_caller()
    {
        ZDOMan.instance.Throws = true;
        NpcWorldBodies census = Census(BodyFixtures.ForemanContract());

        Assert.Throws<InvalidOperationException>(() => census.Advance());
        Assert.False(census.IsComplete);
    }

    private static NpcWorldBodies Census(
        NpcBodyContract contract, NpcBodyKeeps keeps = NpcBodyKeeps.IdentityAndInventory)
    {
        Assert.True(NpcBodySetup.TryCompose(contract, keeps, out NpcBodySetup setup, out string reason), reason);
        return new NpcWorldBodies(setup);
    }

    private static ZDO Save(string prefab, string keyField, string identity)
    {
        if (!ZDOMan.instance.Saved.TryGetValue(prefab, out List<ZDO>? all))
        {
            all = new List<ZDO>();
            ZDOMan.instance.Saved.Add(prefab, all);
        }

        var record = new ZDO();
        if (identity.Length != 0)
        {
            record.Set(keyField, identity);
        }

        all.Add(record);
        return record;
    }
}
