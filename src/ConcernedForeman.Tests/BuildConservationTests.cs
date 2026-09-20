using TheConcernedCat.ConcernedForeman.Domain.Construction;
using TheConcernedCat.Settlement.Worker;

namespace ConcernedForeman.Tests;

/// <summary>Material is conserved exactly once, across interruption and reload.
///
/// <b>What the invariant is.</b> Every unit of an item in the world is in the
/// supply chest, in Thorstein's own inventory, or inside a piece that is
/// standing. <see cref="BuildWorld.Total"/> adds those three up, and the number
/// must be the same after any sequence of rounds, refusals, failures,
/// withdrawals and reloads. That single equality is what "no free material, no
/// double-spend, no silent loss" means operationally, and the three failures it
/// catches are distinguishable:
///
/// <list type="bullet">
/// <item>a <b>double-spend</b> takes the cost out of his hands twice, so the
/// total falls;</item>
/// <item>a <b>silent loss</b> takes it out for a piece that did not go up, so the
/// total falls;</item>
/// <item><b>free material</b> puts a piece up without taking anything, so the
/// total rises.</item>
/// </list>
///
/// <b>Which is why the count assertions are here too.</b> A total can be right by
/// accident, so the number of spends is pinned against the number of placements:
/// one spend per piece that went up, never a spend for a piece that did not, and
/// never two for one. Those two assertions are what the mutation runs in the
/// completion report were planted against.</summary>
public sealed class BuildConservationTests
{
    private const float Step = 2f;

    private static void Ticks(ShelterBuildLoop loop, BuildWorld world, int count, float from)
    {
        int wood = world.Total("Wood");
        int hide = world.Total("DeerHide");
        for (int tick = 0; tick < count; tick++)
        {
            loop.Tick(from + (tick * Step));
            Assert.Equal(wood, world.Total("Wood"));
            Assert.Equal(hide, world.Total("DeerHide"));
        }
    }

    // ---- one spend per piece, ever ---------------------------------------

    [Fact]
    public void Exactly_one_spend_happens_for_each_piece_that_goes_up()
    {
        var world = new BuildWorld();
        ShelterBuildLoop loop = world.Loop();

        Ticks(loop, world, 80, 100f);

        Assert.Equal(BuildStep.Finished, loop.Step);
        Assert.Equal(world.Plan.Pieces.Count, world.Placer.Placed.Count);

        // The whole conservation argument in one line: a spend for each piece
        // that went up and for nothing else. A second spend per piece would be a
        // double-spend; a spend without a placement would be a silent loss.
        Assert.Equal(world.Placer.Placed.Count, world.Materials.Spends);
    }

    [Fact]
    public void Nothing_is_spent_when_every_placement_is_refused()
    {
        var world = new BuildWorld();
        world.Placer.Default = PiecePlaced.Refused;
        ShelterBuildLoop loop = world.Loop();

        Ticks(loop, world, 30, 100f);

        Assert.Empty(world.Placer.Placed);
        Assert.Equal(0, world.Materials.Spends);
    }

    [Fact]
    public void Nothing_is_spent_when_a_placement_passes_the_gates_and_fails()
    {
        var world = new BuildWorld();
        world.Placer.Default = PiecePlaced.Failed;
        ShelterBuildLoop loop = world.Loop();

        Ticks(loop, world, 30, 100f);

        Assert.Empty(world.Placer.Placed);
        Assert.Equal(0, world.Materials.Spends);
        Assert.Equal(BuildStep.Stopped, loop.Step);
    }

    // ---- interruption ----------------------------------------------------

    [Fact]
    public void A_withdrawal_half_way_through_conserves_everything()
    {
        var world = new BuildWorld();
        int wood = world.Total("Wood");
        ShelterBuildLoop loop = world.Loop();
        Ticks(loop, world, 9, 100f);
        Assert.True(world.Placer.Placed.Count > 0 && world.Placer.Placed.Count < world.Plan.Pieces.Count);

        world.Authorised = false;
        loop.Tick(500f);

        Assert.Equal(wood, world.Total("Wood"));
        Assert.Equal(0, world.Carrying("Wood"));
        Assert.Equal(world.Placer.Placed.Count, world.Materials.Spends);
    }

    [Fact]
    public void A_cancel_that_cannot_put_anything_back_still_conserves_everything()
    {
        var world = new BuildWorld();
        int wood = world.Total("Wood");
        ShelterBuildLoop loop = world.Loop();
        Ticks(loop, world, 3, 100f);
        int carried = world.Carrying("Wood");
        Assert.True(carried > 0);

        world.Materials.RefusePutBack = "the chest is not in reach";
        loop.Cancel(500f, "a person stopped it.");

        // Nothing was lost - it is still on him, and the sentence says so.
        Assert.Equal(wood, world.Total("Wood"));
        Assert.Equal(carried, world.Carrying("Wood"));
        Assert.Contains("still carrying", loop.Reason);
    }

    [Fact]
    public void A_worker_who_vanishes_half_way_loses_nothing_from_the_accounting()
    {
        var world = new BuildWorld();
        int wood = world.Total("Wood");
        ShelterBuildLoop loop = world.Loop();
        Ticks(loop, world, 9, 100f);

        world.Walk.Present = false;
        Ticks(loop, world, 10, 500f);

        Assert.Equal(wood, world.Total("Wood"));
        Assert.Equal(world.Placer.Placed.Count, world.Materials.Spends);
    }

    [Fact]
    public void A_chest_that_runs_dry_mid_phase_conserves_everything()
    {
        // Enough for the foundation and three walls and no more.
        var world = new BuildWorld(wood: 14);
        int wood = world.Total("Wood");
        ShelterBuildLoop loop = world.Loop();

        Ticks(loop, world, 60, 100f);

        Assert.Equal(wood, world.Total("Wood"));
        Assert.Equal(world.Placer.Placed.Count, world.Materials.Spends);
        Assert.Equal(BuildStep.Waiting, loop.Step);
    }

    // ---- reload ----------------------------------------------------------

    [Fact]
    public void A_reload_half_way_conserves_everything_and_builds_nothing_twice()
    {
        var world = new BuildWorld();
        int wood = world.Total("Wood");
        int hide = world.Total("DeerHide");

        ShelterBuildLoop before = world.Loop();
        Ticks(before, world, 9, 100f);
        int placedBefore = world.Placer.Placed.Count;
        int spentBefore = world.Materials.Spends;
        Assert.True(placedBefore > 0 && placedBefore < world.Plan.Pieces.Count);

        // The reload. A brand new loop, no memory, the same world - which is
        // exactly what the game hands back: the pieces are in the world save and
        // what he carries is in his own network object.
        ShelterBuildLoop after = world.Loop();
        Ticks(after, world, 80, 1000f);

        Assert.Equal(wood, world.Total("Wood"));
        Assert.Equal(hide, world.Total("DeerHide"));
        Assert.Equal(BuildStep.Finished, after.Step);
        Assert.Equal(world.Plan.Pieces.Count, world.Placer.Placed.Count);
        Assert.True(world.Materials.Spends > spentBefore);
        Assert.Equal(world.Placer.Placed.Count, world.Materials.Spends);
    }

    [Fact]
    public void Material_carried_across_a_reload_is_used_and_not_fetched_twice()
    {
        var world = new BuildWorld();
        ShelterBuildLoop before = world.Loop();

        // One round is enough to have the foundation's wood in his hands.
        before.Tick(100f);
        before.Tick(102f);
        int carried = world.Carrying("Wood");
        Assert.True(carried > 0);
        int drawsBefore = world.Materials.Draws;

        ShelterBuildLoop after = world.Loop();
        after.Tick(1000f);
        after.Tick(1002f);

        // He was already carrying the phase's material, so the new loop does not
        // open the chest again for it.
        Assert.Equal(drawsBefore, world.Materials.Draws);
        Assert.True(world.Placer.Placed.Count > 0);
    }

    // ---- the whole adversarial sequence ----------------------------------

    [Fact]
    public void Everything_at_once_conserves_everything()
    {
        var world = new BuildWorld(wood: 60);
        int wood = world.Total("Wood");
        int hide = world.Total("DeerHide");

        // A refused wall, a failed roof panel, a vanished worker, a withdrawal, a
        // reload, and then the player fixes it all and it finishes.
        ShelterBuildLoop loop = world.Loop();
        world.Placer.Verdicts[world.Plan.Pieces[5].Key] = PiecePlaced.Refused;
        Ticks(loop, world, 12, 100f);

        world.Walk.Present = false;
        Ticks(loop, world, 4, 200f);
        world.Walk.Present = true;

        world.Authorised = false;
        loop.Tick(300f);
        Assert.Equal(wood, world.Total("Wood"));

        world.Authorised = true;
        world.Placer.Verdicts.Clear();
        ShelterBuildLoop resumed = world.Loop();
        Ticks(resumed, world, 120, 1000f);

        Assert.Equal(wood, world.Total("Wood"));
        Assert.Equal(hide, world.Total("DeerHide"));
        Assert.Equal(BuildStep.Finished, resumed.Step);
        Assert.Equal(world.Plan.Pieces.Count, world.Placer.Placed.Count);
        Assert.Equal(world.Placer.Placed.Count, world.Materials.Spends);

        // And the chest paid for exactly the manifest, no more and no less.
        Assert.Equal(wood - world.Plan.Total.UnitsOf("Wood"), world.InChest("Wood"));
        Assert.Equal(0, world.Carrying("Wood"));
    }
}
