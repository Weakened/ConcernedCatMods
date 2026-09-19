using System;
using TheConcernedCat.ConcernedForeman.Domain.Construction;

namespace ConcernedForeman.Tests;

/// <summary>#380: he understands the current phase, and nothing in a later one
/// is walked to before the pieces under it are standing.</summary>
public sealed class ConstructionProgressTests
{
    private static ShelterPlan Plan() => ConstructionRig.Plan();

    [Fact]
    public void A_shelter_nobody_has_started_is_on_the_foundation()
    {
        ConstructionProgress progress = ConstructionProgress.Read(Plan(), new FakeSight());

        Assert.Equal(BuildPhase.Foundation, progress.CurrentPhase);
        Assert.Equal(17, progress.Remaining.Count);
        Assert.False(progress.IsComplete);
        Assert.True(progress.IsConclusive);
    }

    [Fact]
    public void A_roof_panel_is_never_built_before_the_walls_under_it()
    {
        ShelterPlan plan = Plan();
        var sight = new FakeSight();
        sight.Finish(plan, BuildPhase.Foundation);

        ConstructionProgress progress = ConstructionProgress.Read(plan, sight);

        Assert.Equal(BuildPhase.Walls, progress.CurrentPhase);

        foreach (CostedPiece piece in plan.Pieces)
        {
            PieceDisposition disposition = progress.DispositionOf(piece);
            switch (piece.Phase)
            {
                case BuildPhase.Foundation:
                    Assert.Equal(PieceDisposition.AlreadyBuilt, disposition);
                    break;
                case BuildPhase.Walls:
                    Assert.Equal(PieceDisposition.Build, disposition);
                    break;
                default:
                    // Not refused, not abandoned: offered again next round, when
                    // the walls it needs are standing.
                    Assert.Equal(PieceDisposition.NotYet, disposition);
                    break;
            }
        }
    }

    [Fact]
    public void A_phase_unlocks_only_when_every_piece_of_the_one_before_it_stands()
    {
        ShelterPlan plan = Plan();
        var sight = new FakeSight();
        sight.Finish(plan, BuildPhase.Foundation);

        // One wall still missing: the roof stays locked.
        foreach (CostedPiece piece in plan.Pieces)
        {
            if (piece.Phase == BuildPhase.Walls && piece.Placement.Piece.Prefab != "wood_door")
            {
                sight.Set(piece.Key, PieceSighting.Standing);
            }
        }

        ConstructionProgress progress = ConstructionProgress.Read(plan, sight);

        Assert.Equal(BuildPhase.Walls, progress.CurrentPhase);
        foreach (CostedPiece piece in plan.Pieces)
        {
            if (piece.Phase == BuildPhase.Roof)
            {
                Assert.Equal(PieceDisposition.NotYet, progress.DispositionOf(piece));
            }
        }
    }

    [Fact]
    public void A_piece_somebody_already_built_is_skipped_and_its_material_is_not_wanted()
    {
        ShelterPlan plan = Plan();
        var sight = new FakeSight();
        sight.Finish(plan, BuildPhase.Foundation);

        ConstructionProgress progress = ConstructionProgress.Read(plan, sight);

        // The player laid the floor themselves. The round plans for thirteen
        // pieces, not seventeen, and the manifest follows.
        Assert.Equal(13, progress.Remaining.Count);
        Assert.Equal(
            plan.Total.UnitsOf("Wood") - plan.TotalFor(BuildPhase.Foundation).UnitsOf("Wood"),
            progress.RemainingTotal.UnitsOf("Wood"));
    }

    [Fact]
    public void Ground_nobody_could_see_is_never_read_as_finished_and_never_as_empty()
    {
        ShelterPlan plan = Plan();
        var sight = new FakeSight { Default = PieceSighting.Unknown };

        ConstructionProgress progress = ConstructionProgress.Read(plan, sight);

        Assert.False(progress.IsConclusive);
        Assert.False(progress.IsComplete);
        Assert.Equal(17, progress.Remaining.Count);

        // And nothing is built on ground that was not read.
        foreach (CostedPiece piece in plan.Pieces)
        {
            Assert.Equal(PieceDisposition.NotYet, progress.DispositionOf(piece));
        }
    }

    [Fact]
    public void An_unknown_in_an_earlier_phase_never_unlocks_a_later_one()
    {
        ShelterPlan plan = Plan();
        var sight = new FakeSight();
        sight.Finish(plan, BuildPhase.Foundation);
        sight.Set(plan.Pieces[0].Key, PieceSighting.Unknown);

        ConstructionProgress progress = ConstructionProgress.Read(plan, sight);

        Assert.Equal(BuildPhase.Foundation, progress.CurrentPhase);
        Assert.False(progress.IsPhaseStanding(BuildPhase.Foundation));
    }

    [Fact]
    public void Something_in_the_way_is_named_rather_than_built_around()
    {
        ShelterPlan plan = Plan();
        var sight = new FakeSight();
        sight.Set(plan.Pieces[2].Key, PieceSighting.Blocked);

        ConstructionProgress progress = ConstructionProgress.Read(plan, sight);

        Assert.Single(progress.Blocked);
        Assert.Equal(PieceDisposition.Blocked, progress.DispositionOf(plan.Pieces[2]));

        // Blocked is not built: it still counts against the phase and against
        // what is left.
        Assert.False(progress.IsPhaseStanding(BuildPhase.Foundation));
        Assert.Contains(plan.Pieces[2].Key, Keys(progress));
    }

    [Fact]
    public void A_completion_condition_that_throws_defers_the_piece_rather_than_ending_the_build()
    {
        ShelterPlan plan = Plan();
        var sight = new FakeSight { Throws = new InvalidOperationException("the zone went away") };

        ConstructionProgress progress = ConstructionProgress.Read(plan, sight);

        Assert.False(progress.IsConclusive);
        Assert.Equal(17, progress.Remaining.Count);
    }

    [Fact]
    public void A_shelter_whose_every_piece_stands_is_finished()
    {
        ShelterPlan plan = Plan();
        var sight = new FakeSight { Default = PieceSighting.Standing };

        ConstructionProgress progress = ConstructionProgress.Read(plan, sight);

        Assert.True(progress.IsComplete);
        Assert.Empty(progress.Remaining);
        Assert.Equal(BuildPhase.Unspecified, progress.CurrentPhase);
        Assert.Equal(0, progress.RemainingTotal.TotalUnits);
    }

    [Fact]
    public void Progress_against_no_plan_is_not_a_finished_shelter()
    {
        ShelterPlan none = ShelterPlan.For(default, ConstructionRig.Recipes());
        ConstructionProgress progress =
            ConstructionProgress.Read(none, new FakeSight { Default = PieceSighting.Standing });

        Assert.False(progress.IsComplete);
        Assert.Empty(progress.Remaining);
    }

    [Fact]
    public void The_remaining_manifest_is_what_is_left_and_not_what_the_shelter_cost()
    {
        ShelterPlan plan = Plan();
        var sight = new FakeSight();
        sight.Finish(plan, BuildPhase.Foundation);
        sight.Finish(plan, BuildPhase.Walls);
        sight.Finish(plan, BuildPhase.Roof);

        ConstructionProgress progress = ConstructionProgress.Read(plan, sight);

        Assert.Equal(BuildPhase.Interior, progress.CurrentPhase);
        Assert.Equal(plan.TotalFor(BuildPhase.Interior).UnitsOf("Wood"),
            progress.RemainingTotal.UnitsOf("Wood"));
        Assert.NotEqual(plan.Total.UnitsOf("Wood"), progress.RemainingTotal.UnitsOf("Wood"));
    }

    private static System.Collections.Generic.List<string> Keys(ConstructionProgress progress)
    {
        var keys = new System.Collections.Generic.List<string>();
        foreach (CostedPiece piece in progress.Remaining)
        {
            keys.Add(piece.Key);
        }

        return keys;
    }
}
