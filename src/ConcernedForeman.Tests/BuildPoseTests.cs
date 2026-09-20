using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Runtime.Construction;
using TheConcernedCat.ConcernedForeman.Runtime.Custody;
using UnityEngine;

namespace ConcernedForeman.Tests;

/// <summary>The visible work, and exactly which calls it makes.
///
/// <b>The surface is the point of this test, not just the behaviour.</b> An
/// animation on an NPC body is a networking question in this game, so what is
/// pinned here is that the pose writes <c>ZSyncAnimation.SetFloat</c> on
/// <b>Thorstein's own</b> component and equips a hammer he is already carrying
/// through <c>Humanoid.EquipItem</c> - and nothing else. There is no
/// <c>SetTrigger</c>: it sends an RPC, and the test project's stub does not even
/// declare one, so a future edit that reached for it would not compile here.
/// </summary>
public sealed class BuildPoseTests : IDisposable
{
    private static readonly int Forward = ZSyncAnimation.GetHash("forward_speed");
    private static readonly int Sideway = ZSyncAnimation.GetHash("sideway_speed");
    private static readonly int Turn = ZSyncAnimation.GetHash("turn_speed");

    private readonly List<string> _log = new List<string>();

    public BuildPoseTests()
    {
        Game.instance ??= new Game();
    }

    public void Dispose()
    {
        WorkerBody.Loaded = null;
        WorkerBody.Died = null;
        WorkerBody.ErrorLog = null;
    }

    private static WorkerBody Body(bool animation = true, bool hammer = true, bool owner = true)
    {
        WorkerBody body = ForemanFixtures.Body(owner: owner);
        if (animation)
        {
            body.gameObject.Add(new ZSyncAnimation());
        }

        if (hammer)
        {
            body.Inventory!.AddItem(ForemanFixtures.Hammer());
        }

        return body;
    }

    private BuildPose Pose(WorkerBody? body) => new BuildPose(() => body, _log.Add);

    [Fact]
    public void Working_stands_him_still_on_his_own_animation_component()
    {
        WorkerBody body = Body();
        ZSyncAnimation animation = body.GetComponent<ZSyncAnimation>()!;

        Pose(body).Working(true);

        Assert.Equal(0f, animation.Floats[Forward]);
        Assert.Equal(0f, animation.Floats[Sideway]);
        Assert.Equal(0f, animation.Floats[Turn]);
    }

    [Fact]
    public void Working_puts_the_hammer_he_is_carrying_in_his_hand()
    {
        WorkerBody body = Body();
        BuildPose pose = Pose(body);

        pose.Working(true);

        Assert.True(pose.HoldingTool);
        Assert.Single(body.Humanoid!.Equipped);
        Assert.Equal("$item_hammer", body.Humanoid!.Equipped[0].m_shared.m_name);
    }

    [Fact]
    public void Stopping_puts_the_hammer_away_again()
    {
        WorkerBody body = Body();
        BuildPose pose = Pose(body);
        pose.Working(true);

        pose.Working(false);

        Assert.False(pose.HoldingTool);
        Assert.Empty(body.Humanoid!.Equipped);
    }

    [Fact]
    public void Working_twice_does_not_equip_twice()
    {
        WorkerBody body = Body();
        BuildPose pose = Pose(body);

        pose.Working(true);
        pose.Working(true);

        Assert.Single(body.Humanoid!.Equipped);
    }

    [Fact]
    public void A_worker_with_no_hammer_still_stands_still_and_holds_nothing()
    {
        WorkerBody body = Body(hammer: false);
        BuildPose pose = Pose(body);

        pose.Working(true);

        Assert.False(pose.HoldingTool);
        Assert.Equal(0f, body.GetComponent<ZSyncAnimation>()!.Floats[Forward]);
        Assert.Empty(body.Humanoid!.Equipped);
    }

    [Fact]
    public void A_body_with_no_animation_component_is_not_a_failure()
    {
        WorkerBody body = Body(animation: false);
        BuildPose pose = Pose(body);

        pose.Working(true);

        // The hammer still comes out; only the stance is unavailable.
        Assert.True(pose.HoldingTool);
        Assert.Empty(_log);
    }

    [Fact]
    public void A_body_that_is_not_ours_is_left_alone()
    {
        // Ownership is taken away after the body has loaded, because a body that
        // was never owned cannot be stamped and so cannot be built at all. This
        // is the shape that matters anyway: a body this process loaded and then
        // lost ownership of, mid-order.
        WorkerBody body = Body();
        body.View!.Owner = false;
        BuildPose pose = Pose(body);

        pose.Working(true);

        Assert.False(pose.HoldingTool);
        Assert.Empty(body.Humanoid!.Equipped);
    }

    [Fact]
    public void No_body_at_all_is_not_a_failure()
    {
        BuildPose pose = Pose(null);

        pose.Working(true);
        pose.Working(false);

        Assert.False(pose.HoldingTool);
        Assert.Empty(_log);
    }

    [Fact]
    public void A_body_lookup_that_throws_is_reported_once_and_never_again()
    {
        int asked = 0;
        var pose = new BuildPose(
            () =>
            {
                asked++;
                throw new InvalidOperationException("the scene went away");
            },
            _log.Add);

        pose.Working(true);
        pose.Working(true);

        Assert.Equal(2, asked);
        Assert.Single(_log);
        Assert.Contains("failed soft", _log[0]);
    }
}
