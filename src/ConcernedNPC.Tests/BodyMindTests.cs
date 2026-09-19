using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Body;
using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>The mind: the silencing, the fault latch, and the motor.
///
/// Every assertion here corresponds to a specific behaviour in the installed
/// game that overriding the update does not reach, or to one of the four traps
/// the three shipped minds were each written around. None of it had a test in
/// any of them.</summary>
public sealed class BodyMindTests : IDisposable
{
    private readonly List<string> _errors = new List<string>();

    public BodyMindTests()
    {
        BodyFixtures.ResetWorld();
        NpcBodyMind.ErrorLog = _errors.Add;
    }

    public void Dispose() => BodyFixtures.ResetWorld();

    [Fact]
    public void Waking_silences_everything_the_update_does_not_reach()
    {
        NpcBodyMind mind = Mind();

        mind.Awake();

        // The repeating idle sound is armed in the base's own Awake and fires
        // on the engine's schedule, never through the update.
        Assert.Contains("DoIdleSound", mind.Cancelled);
        Assert.Equal(0f, mind.m_idleSoundChance);

        // One flag closes the whole alert path at its source, including the RPC
        // behind it, which is private and non-virtual.
        Assert.False(mind.m_canBeAlerted);

        // Three broadcasts to every player on the server.
        Assert.Equal(string.Empty, mind.m_spawnMessage);
        Assert.Equal(string.Empty, mind.m_deathMessage);
        Assert.Equal(string.Empty, mind.m_alertedMessage);

        // Not a threat and not a target-seeker.
        Assert.Equal(0f, mind.m_viewRange);
        Assert.Equal(0f, mind.m_hearRange);

        // The cart puller's mind did NOT do this one. It gains it here.
        Assert.False(mind.Hunting);

        Assert.Equal(Pathfinding.AgentType.Humanoid, mind.m_pathAgentType);
    }

    [Fact]
    public void A_tick_on_a_body_this_peer_does_not_own_does_nothing_at_all()
    {
        NpcBodyMind mind = Mind();
        int ticks = 0;
        mind.WorkTick = (_, _) => ticks++;
        mind.Owned = false;

        Assert.False(mind.UpdateAI(0.05f));
        Assert.Equal(0, ticks);
    }

    [Fact]
    public void Anything_that_escapes_the_work_tick_latches_the_body_inert_and_is_said_once()
    {
        // The shared driver walks every creature with no try/catch, at a fixed
        // rate. One exception escaping this method aborts every creature after
        // it for that tick, and the rest of the physics step with it.
        NpcBodyMind mind = Mind();
        mind.WorkTick = (_, _) => throw new InvalidOperationException("the job is broken");

        Assert.False(mind.UpdateAI(0.05f));
        Assert.True(mind.IsFaulted);
        Assert.Equal(1, mind.Stops);

        Assert.False(mind.UpdateAI(0.05f));
        Assert.False(mind.UpdateAI(0.05f));

        Assert.Single(_errors);
    }

    [Fact]
    public void A_fault_while_the_error_log_itself_throws_still_does_not_escape()
    {
        NpcBodyMind mind = Mind();
        NpcBodyMind.ErrorLog = _ => throw new InvalidOperationException("the log is broken too");
        mind.WorkTick = (_, _) => throw new InvalidOperationException("the job is broken");

        Assert.False(mind.UpdateAI(0.05f));
        Assert.True(mind.IsFaulted);
    }

    [Fact]
    public void The_cheap_path_question_is_the_field_read_and_the_expensive_one_is_asked_deliberately()
    {
        NpcBodyMind mind = Mind();

        // Reading it costs nothing and runs no search.
        Assert.False(mind.HasFoundPath);
        Assert.Equal(0, mind.PathSearches);

        Assert.True(mind.TryFindPath(new Vector3(5f, 0f, 5f)));
        Assert.Equal(1, mind.PathSearches);
        Assert.True(mind.HasFoundPath);
    }

    [Fact]
    public void Walking_needs_the_pathfinder_and_says_nothing_about_having_arrived()
    {
        NpcBodyMind mind = Mind();

        Pathfinding.instance = null!;
        mind.WalkTo(new Vector3(3f, 0f, 0f), 2f);
        Assert.Equal(0, mind.Moves);
        Assert.False(mind.MotorCommanded);

        Pathfinding.instance = new Pathfinding();
        mind.WalkTo(new Vector3(3f, 0f, 0f), 2f);

        Assert.Equal(1, mind.Moves);
        Assert.True(mind.MotorCommanded);
        Assert.True(mind.m_character.Walking);
        Assert.False(mind.m_character.Running);
    }

    [Fact]
    public void Steering_to_where_the_body_already_stands_halts_instead_of_shoving_it()
    {
        NpcBodyMind mind = Mind();
        mind.WalkTo(new Vector3(3f, 0f, 0f), 2f);
        Assert.True(mind.MotorCommanded);

        mind.SteerToward(mind.transform.position);

        Assert.False(mind.MotorCommanded);
        Assert.Equal(1, mind.Stops);
    }

    [Fact]
    public void Steering_toward_a_point_walks_a_unit_direction()
    {
        NpcBodyMind mind = Mind();

        mind.SteerToward(new Vector3(0f, 9f, 10f));

        Assert.True(mind.MotorCommanded);
        Assert.Equal(1f, mind.LastDirection.z, 3);

        // The vertical is dropped: a body walks on the ground.
        Assert.Equal(0f, mind.LastDirection.y);
    }

    [Fact]
    public void Facing_a_direction_turns_without_moving()
    {
        NpcBodyMind mind = Mind();

        mind.Face(new Vector3(1f, 4f, 0f));

        Assert.Equal(0, mind.Moves);
        Assert.Equal(1f, mind.LastLook.x, 3);
        Assert.Equal(0f, mind.LastLook.y);
    }

    [Fact]
    public void Halting_is_idempotent_and_safe_on_a_body_that_was_never_moving()
    {
        NpcBodyMind mind = Mind();

        mind.Halt();
        mind.Halt();

        Assert.False(mind.MotorCommanded);
        Assert.Equal(2, mind.Stops);
    }

    [Fact]
    public void A_mind_reads_its_identity_from_its_own_roles_key()
    {
        BodyFixtures.Register(BodyFixtures.StewardContract());
        NpcBodyMind mind = Mind(BodyFixtures.StewardPrefab);
        mind.m_nview!.GetZDO().Set(BodyFixtures.StewardKeyField, "steward/sunniva");

        Assert.Equal(BodyFixtures.Sunniva, mind.Identity);
    }

    [Fact]
    public void A_mind_on_a_prefab_nobody_registered_has_no_identity_and_keeps_asking()
    {
        NpcBodyMind mind = Mind("SomeoneElsesWorker");

        Assert.True(mind.Identity.IsEmpty);

        // Not remembered as "nobody": the role may register later in the same
        // process, and a cached empty answer would outlive the reason for it.
        BodyFixtures.Register(BodyFixtures.ForemanContract());
        NpcBodyMind second = Mind(BodyFixtures.ForemanPrefab);
        second.m_nview!.GetZDO().Set(BodyFixtures.WorkerKeyField, "foreman/thorstein");
        Assert.Equal(BodyFixtures.Thorstein, second.Identity);
    }

    [Fact]
    public void A_freshly_built_body_is_told_who_it_is_rather_than_going_back_to_the_object()
    {
        NpcBodyMind mind = Mind("SomeoneElsesWorker");

        mind.RememberIdentity(BodyFixtures.Gunnar);

        Assert.Equal(BodyFixtures.Gunnar, mind.Identity);
    }

    [Fact]
    public void Enabled_bodies_are_listed_once_and_dropped_when_they_go()
    {
        NpcBodyMind mind = Mind();

        mind.OnEnable();
        mind.OnEnable();
        Assert.Single(NpcBodyMind.Live);

        mind.OnDisable();
        Assert.Empty(NpcBodyMind.Live);
    }

    private static NpcBodyMind Mind(string prefabName = "Dverger")
    {
        var instance = new GameObject(prefabName + "(Clone)");
        ZNetView view = instance.Add(new ZNetView());
        Character character = instance.Add(new Character());
        NpcBodyMind mind = instance.Add(new NpcBodyMind());
        mind.m_nview = view;
        mind.m_character = character;
        return mind;
    }
}
