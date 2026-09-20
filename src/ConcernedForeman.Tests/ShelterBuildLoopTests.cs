using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Domain.Construction;
using TheConcernedCat.Settlement.Worker;

namespace ConcernedForeman.Tests;

/// <summary>#380's execution loop, with no game: a confirmed order really does
/// turn into seventeen placements, the chest is opened once a phase, he walks,
/// the work is visible, and every refusal names what happened and where the
/// material went.
///
/// <b>Every test asserts the conservation invariant too.</b> Not as a separate
/// suite bolted on the end: <see cref="Drive"/> checks it after every single tick,
/// so a loop that spent twice, spent for nothing, or built for nothing fails the
/// test that was about something else. That is deliberate - the invariant is the
/// one property of this loop that cannot be allowed to hold only in the cases
/// somebody thought to write down.</summary>
public sealed class ShelterBuildLoopTests
{
    /// <summary>A round takes two ticks per piece: one starts the working pose,
    /// the next places once <see cref="ShelterBuildLoop.WorkSeconds"/> has
    /// passed. Two seconds a tick keeps that to two ticks and stays well inside
    /// the draw-retry interval, so a happy path never reopens the chest.</summary>
    private const float Step = 2f;

    private static ShelterBuildLoop Drive(BuildWorld world, int ticks, float from = 100f)
    {
        ShelterBuildLoop loop = world.Loop();
        Run(loop, world, ticks, from);
        return loop;
    }

    private static void Run(ShelterBuildLoop loop, BuildWorld world, int ticks, float from = 100f)
    {
        int wood = world.Total("Wood");
        int hide = world.Total("DeerHide");
        for (int tick = 0; tick < ticks; tick++)
        {
            loop.Tick(from + (tick * Step));
            Assert.Equal(wood, world.Total("Wood"));
            Assert.Equal(hide, world.Total("DeerHide"));
        }
    }

    // ---- the whole shelter ------------------------------------------------

    [Fact]
    public void A_confirmed_order_is_planned_whole_and_every_piece_of_it_goes_up()
    {
        var world = new BuildWorld();

        ShelterBuildLoop loop = Drive(world, 80);

        Assert.Equal(BuildStep.Finished, loop.Step);
        Assert.Equal(world.Plan.Pieces.Count, world.Placer.Placed.Count);
        Assert.Equal(17, world.Plan.Pieces.Count);
        Assert.Equal(world.Plan.Pieces.Count, loop.Built);
        Assert.True(loop.Progress!.IsComplete);

        // Exactly the manifest, and nothing else, left the chest and is now in
        // the pieces.
        foreach (PieceCost line in world.Plan.Total.Lines)
        {
            Assert.Equal(line.Amount, world.Embodied[line.Item]);
            Assert.Equal(0, world.Carrying(line.Item));
        }
    }

    [Fact]
    public void Every_piece_is_placed_exactly_once()
    {
        var world = new BuildWorld();

        Drive(world, 80);

        var seen = new HashSet<string>();
        foreach (string key in world.Placer.Placed)
        {
            Assert.True(seen.Add(key), "piece " + key + " was placed twice");
        }
    }

    [Fact]
    public void The_pieces_go_up_in_phase_order()
    {
        var world = new BuildWorld();

        Drive(world, 80);

        BuildPhase last = BuildPhase.Foundation;
        foreach (string key in world.Placer.Placed)
        {
            Assert.True(world.Plan.TryFind(key, out CostedPiece piece));
            Assert.True(
                BuildPhases.PriorityOf(piece.Phase) <= BuildPhases.PriorityOf(last),
                piece.Phase + " went up after " + last);
            last = piece.Phase;
        }
    }

    // ---- provisioning ----------------------------------------------------

    [Fact]
    public void The_chest_is_opened_once_for_each_phase_and_never_once_for_each_piece()
    {
        var world = new BuildWorld();

        Drive(world, 80);

        // Four phases, four trips: #380's "coherent batches - foundation, walls,
        // roof, interior - rather than arbitrary single-piece trips".
        Assert.Equal(BuildPhases.InOrder.Length, world.Materials.Draws);
        Assert.True(
            world.Materials.Draws < world.Plan.Pieces.Count,
            "the chest was opened " + world.Materials.Draws + " times for " + world.Plan.Pieces.Count + " pieces");
    }

    [Fact]
    public void Nothing_is_placed_before_the_chest_has_been_opened()
    {
        var world = new BuildWorld();
        ShelterBuildLoop loop = world.Loop();

        // One tick is not enough to place anything: he has to be provisioned and
        // to have walked there first.
        loop.Tick(100f);

        Assert.Empty(world.Placer.Placed);
        Assert.True(world.Materials.Draws >= 1 || loop.Step == BuildStep.GoingToSupply);
    }

    [Fact]
    public void A_phase_the_player_already_built_is_not_provisioned_or_rebuilt()
    {
        var world = new BuildWorld();
        world.AlreadyBuilt(BuildPhase.Foundation);
        int woodBefore = world.InChest("Wood");

        ShelterBuildLoop loop = Drive(world, 80);

        Assert.Equal(BuildStep.Finished, loop.Step);
        foreach (string key in world.Placer.Placed)
        {
            Assert.True(world.Plan.TryFind(key, out CostedPiece piece));
            Assert.NotEqual(BuildPhase.Foundation, piece.Phase);
        }

        // Three phases left, so three trips, and the foundation's wood stayed in
        // the chest because the floor was already there.
        Assert.Equal(3, world.Materials.Draws);
        int spent = woodBefore - world.InChest("Wood");
        Assert.Equal(world.Plan.Total.UnitsOf("Wood") - world.Plan.TotalFor(BuildPhase.Foundation).UnitsOf("Wood"), spent);
    }

    [Fact]
    public void A_chest_that_is_short_waits_and_names_what_is_missing()
    {
        var world = new BuildWorld(wood: 4);

        ShelterBuildLoop loop = Drive(world, 20);

        Assert.Equal(BuildStep.Waiting, loop.Step);
        Assert.Contains("Wood", loop.Reason);
        Assert.Contains("not in any container", loop.Reason);

        // What he could afford still went up: waiting is not stopping.
        Assert.Equal(2, world.Placer.Placed.Count);
    }

    [Fact]
    public void No_marked_chest_is_a_named_refusal_and_nothing_moves()
    {
        var world = new BuildWorld();
        world.Materials.NoSupply = "no supply chest is marked for this settlement";

        ShelterBuildLoop loop = Drive(world, 10);

        Assert.Equal(BuildStep.Waiting, loop.Step);
        Assert.Contains("no supply chest is marked", loop.Reason);
        Assert.Contains("Nothing else will be used instead", loop.Reason);
        Assert.Equal(0, world.Materials.Draws);
        Assert.Empty(world.Placer.Placed);
    }

    [Fact]
    public void A_chest_that_will_not_open_is_reported_and_nothing_is_substituted()
    {
        var world = new BuildWorld();
        world.Materials.RefuseDraw = "the marked supply chest could not be opened (a ward refuses it)";

        ShelterBuildLoop loop = Drive(world, 10);

        Assert.Equal(BuildStep.Waiting, loop.Step);
        Assert.Contains("a ward refuses it", loop.Reason);
        Assert.Empty(world.Placer.Placed);
    }

    // ---- walking ---------------------------------------------------------

    [Fact]
    public void He_walks_to_the_chest_then_to_the_site_and_stands_within_reach_of_every_piece()
    {
        var world = new BuildWorld();
        world.Walk.Position = new SitePoint(80f, 0f, 80f);

        Drive(world, 80);

        Assert.Contains(world.Materials.At, world.Walk.Goals);
        Assert.Contains(world.Marker.At, world.Walk.Goals);

        // And he is aimed at every single piece before working on it, not just at
        // the site. The panels of a four-metre cottage are all within one step of
        // the marker, so this is the assertion that keeps "he moves to the
        // pieces" from being satisfied by standing still.
        foreach (CostedPiece piece in world.Plan.Pieces)
        {
            Assert.Contains(piece.Placement.At, world.Walk.Goals);
        }

        // The chest comes before the site: one trip out and one back, not three.
        Assert.True(
            world.Walk.Goals.IndexOf(world.Materials.At) < world.Walk.Goals.IndexOf(world.Marker.At),
            "he went to the site before he had the material");
    }

    [Fact]
    public void A_walk_that_cannot_be_planned_says_why_and_takes_nothing_out_of_a_chest()
    {
        var world = new BuildWorld();
        world.Walk.Position = new SitePoint(80f, 0f, 80f);
        world.Walk.Arrives = false;
        world.Walk.Walked = BuildWalkStatus.Deferred;
        world.Walk.Why = "there is no way for him to walk there";

        ShelterBuildLoop loop = Drive(world, 10);

        Assert.Equal(BuildStep.Waiting, loop.Step);
        Assert.Contains("no way for him to walk there", loop.Reason);
        Assert.Contains("Nothing has been taken out of a container", loop.Reason);
        Assert.Equal(0, world.Materials.Draws);
    }

    [Fact]
    public void A_worker_who_is_not_here_is_reported_and_nothing_moves()
    {
        var world = new BuildWorld();
        world.Walk.Present = false;

        ShelterBuildLoop loop = Drive(world, 10);

        Assert.Equal(BuildStep.Waiting, loop.Step);
        Assert.Contains("Thorstein is not here", loop.Reason);
        Assert.Equal(0, world.Materials.Draws);
        Assert.Empty(world.Placer.Placed);
    }

    // ---- the visible work ------------------------------------------------

    [Fact]
    public void The_working_pose_runs_once_around_every_placement_and_is_off_at_the_end()
    {
        var world = new BuildWorld();

        ShelterBuildLoop loop = Drive(world, 80);

        Assert.Equal(BuildStep.Finished, loop.Step);
        Assert.Equal(world.Plan.Pieces.Count, world.Pose.TimesStarted);
        Assert.False(world.Pose.On);
    }

    [Fact]
    public void A_piece_is_not_placed_in_the_same_round_the_work_starts()
    {
        var world = new BuildWorld();
        ShelterBuildLoop loop = world.Loop();

        // Enough rounds to be provisioned and standing at the first piece, with
        // time frozen so the work phase cannot elapse.
        for (int tick = 0; tick < 12; tick++)
        {
            loop.Tick(100f);
        }

        Assert.Equal(BuildStep.Working, loop.Step);
        Assert.True(world.Pose.On);
        Assert.Empty(world.Placer.Placed);
    }

    // ---- placement, authority and refusals -------------------------------

    [Fact]
    public void Every_placement_carries_the_players_authority_and_what_he_really_holds()
    {
        var world = new BuildWorld();

        Drive(world, 80);

        Assert.NotEmpty(world.Placer.Authority);
        Assert.DoesNotContain(false, world.Placer.Authority);
        for (int index = 0; index < world.Placer.Offered.Count; index++)
        {
            Assert.False(world.Placer.Offered[index].IsEmpty);
        }
    }

    [Fact]
    public void A_refused_placement_spends_nothing_and_names_the_check()
    {
        var world = new BuildWorld();
        world.Placer.Default = PiecePlaced.Refused;
        world.Placer.Reason = "a ward refuses building there";

        ShelterBuildLoop loop = Drive(world, 20);

        Assert.Empty(world.Placer.Placed);
        Assert.Contains("was not placed", loop.Reason);
        Assert.Contains("a ward refuses building there", loop.Reason);
        Assert.Contains("Nothing was taken out of a container for it", loop.Reason);
        Assert.Empty(world.Embodied);
    }

    [Fact]
    public void A_piece_that_passed_every_gate_and_did_not_appear_stops_the_order()
    {
        var world = new BuildWorld();
        world.Placer.Default = PiecePlaced.Failed;
        world.Placer.Reason = "the piece was not created";

        ShelterBuildLoop loop = Drive(world, 20);

        Assert.Equal(BuildStep.Stopped, loop.Step);
        Assert.Contains("passed every check and was not created", loop.Reason);
        Assert.Contains("Nothing was taken out of a container for it", loop.Reason);
        Assert.Empty(world.Placer.Placed);
    }

    [Fact]
    public void A_piece_that_went_up_and_could_not_be_paid_for_stops_and_says_so()
    {
        var world = new BuildWorld();
        world.Materials.RefuseSpend = "his inventory could not be written";
        int wood = world.Total("Wood");

        // The one case the invariant is DELIBERATELY not asserted tick by tick,
        // because this is the disclosed window: the piece went up and the cost
        // did not leave his hands, so the world holds one wall's worth more wood
        // than it did. Nothing of the player's is lost, the order stops, and the
        // sentence says so - which is what is asserted instead.
        ShelterBuildLoop loop = world.Loop();
        for (int tick = 0; tick < 20; tick++)
        {
            loop.Tick(100f + (tick * Step));
        }

        Assert.Equal(wood + world.Plan.Pieces[0].Recipe.Costs[0].Amount, world.Total("Wood"));

        Assert.Equal(BuildStep.Stopped, loop.Step);
        Assert.Contains("went up and the", loop.Reason);
        Assert.Contains("his inventory could not be written", loop.Reason);
        Assert.Contains("Nothing else is built until that is sorted out", loop.Reason);

        // One piece, one failure, and it did not keep going.
        Assert.Single(world.Placer.Placed);
    }

    // ---- the parked clearance decision -----------------------------------

    [Fact]
    public void An_obstructed_piece_is_a_named_refusal_and_nothing_is_ever_cleared()
    {
        var world = new BuildWorld();
        CostedPiece blocked = world.Plan.Pieces[0];
        world.Sight.Set(blocked.Key, PieceSighting.Blocked);

        ShelterBuildLoop loop = Drive(world, 40);

        Assert.Equal(BuildStep.Waiting, loop.Step);
        Assert.Contains(blocked.Placement.Piece.Prefab, loop.Reason);
        Assert.Contains(blocked.Placement.At.ToString(), loop.Reason);
        Assert.Contains("Nothing will be cleared, levelled or taken down", loop.Reason);

        // And the phase order held: nothing of a later phase went up over an
        // unfinished foundation.
        foreach (string key in world.Placer.Placed)
        {
            Assert.True(world.Plan.TryFind(key, out CostedPiece piece));
            Assert.Equal(BuildPhase.Foundation, piece.Phase);
        }
    }

    [Fact]
    public void Ground_that_could_not_be_read_is_left_for_another_round_and_not_assumed_empty()
    {
        var world = new BuildWorld();
        world.Sight.Default = PieceSighting.Unknown;

        ShelterBuildLoop loop = Drive(world, 20);

        Assert.Equal(BuildStep.Waiting, loop.Step);
        Assert.Contains("could not be read", loop.Reason);
        Assert.Empty(world.Placer.Placed);
        Assert.Equal(0, world.Materials.Draws);
    }

    // ---- withdrawal and cancellation -------------------------------------

    [Fact]
    public void The_sentence_about_a_withdrawal_survives_the_rounds_after_it()
    {
        var world = new BuildWorld();
        ShelterBuildLoop loop = world.Loop();
        Run(loop, world, 3);
        world.Authorised = false;
        loop.Tick(200f);

        // The round AFTER the withdrawal must not replace "40 Wood went back
        // where it came from" with "there is no confirmed build order". This
        // programme has already shipped a console that said one thing while the
        // panel said another; a status line that forgets where the material went
        // one frame later is the same defect with a clock attached.
        Run(loop, world, 5, from: 300f);

        Assert.Contains("went back where it came from", loop.Reason);
    }

    [Fact]
    public void Withdrawing_the_order_puts_the_unspent_material_back_and_says_where_it_went()
    {
        var world = new BuildWorld();
        ShelterBuildLoop loop = world.Loop();
        Run(loop, world, 3);
        Assert.True(world.Carrying("Wood") > 0, "he should be carrying the foundation's wood by now");
        int chestBefore = world.InChest("Wood");
        int carried = world.Carrying("Wood");

        world.Authorised = false;
        loop.Tick(200f);

        Assert.Equal(BuildStep.Stopped, loop.Step);
        Assert.Equal(1, world.Materials.PutBacks);
        Assert.Equal(chestBefore + carried, world.InChest("Wood"));
        Assert.Contains("withdrawn", loop.Reason);
        Assert.Contains("went back where it came from", loop.Reason);
    }

    [Fact]
    public void A_cancel_that_cannot_reach_the_chest_says_he_is_still_carrying_it()
    {
        var world = new BuildWorld();
        ShelterBuildLoop loop = world.Loop();
        Run(loop, world, 3);
        int carried = world.Carrying("Wood");
        Assert.True(carried > 0);

        world.Materials.RefusePutBack = "the chest is not in reach";
        loop.Cancel(200f, "a person stopped it.");

        Assert.Contains("the chest is not in reach", loop.Reason);
        Assert.Contains("still carrying", loop.Reason);
        Assert.Contains("nothing has been lost", loop.Reason);
        Assert.Equal(carried, world.Carrying("Wood"));
    }

    [Fact]
    public void A_cancel_before_anything_was_fetched_says_nothing_was_taken()
    {
        var world = new BuildWorld();
        ShelterBuildLoop loop = world.Loop();

        loop.Cancel(100f, "a person stopped it.");

        Assert.Contains("Nothing had been taken out of a container", loop.Reason);
    }

    [Fact]
    public void A_stopped_order_does_not_start_itself_again()
    {
        var world = new BuildWorld();
        ShelterBuildLoop loop = world.Loop();
        Run(loop, world, 3);
        loop.Cancel(200f, "a person stopped it.");
        int placed = world.Placer.Placed.Count;

        Run(loop, world, 20, from: 300f);

        Assert.Equal(BuildStep.Stopped, loop.Step);
        Assert.Equal(placed, world.Placer.Placed.Count);
    }

    // ---- resume ----------------------------------------------------------

    [Fact]
    public void A_fresh_loop_over_the_same_world_resumes_instead_of_duplicating()
    {
        var world = new BuildWorld();
        ShelterBuildLoop first = world.Loop();
        Run(first, world, 10);
        int placedBefore = world.Placer.Placed.Count;
        Assert.True(placedBefore > 0 && placedBefore < world.Plan.Pieces.Count);

        // A reload, as the loop sees one: nothing remembered, the same chest, the
        // same hands, and the same pieces standing at the site.
        ShelterBuildLoop second = world.Loop();
        Run(second, world, 80, from: 1000f);

        Assert.Equal(BuildStep.Finished, second.Step);
        Assert.Equal(world.Plan.Pieces.Count, world.Placer.Placed.Count);
        var seen = new HashSet<string>();
        foreach (string key in world.Placer.Placed)
        {
            Assert.True(seen.Add(key), "piece " + key + " was built twice across the reload");
        }
    }

    [Fact]
    public void A_piece_the_player_takes_down_is_built_again_because_the_world_is_re_read()
    {
        var world = new BuildWorld();
        ShelterBuildLoop loop = Drive(world, 80);
        Assert.Equal(BuildStep.Finished, loop.Step);

        // Somebody knocks the bed out and puts its material back in the chest.
        CostedPiece bed = world.Plan.Pieces[world.Plan.Pieces.Count - 1];
        world.Sight.Set(bed.Key, PieceSighting.Missing);
        foreach (PieceCost cost in bed.Recipe.Costs)
        {
            world.Embodied[cost.Item] -= cost.Amount;
            world.Chest[cost.Item] += cost.Amount;
        }

        Run(loop, world, 20, from: 1000f);

        Assert.Equal(BuildStep.Finished, loop.Step);
        Assert.Equal(PieceSighting.Standing, world.Sight.Look(bed.Placement));
    }
}
