using TheConcernedCat.ConcernedForeman.Domain.Construction;

namespace ConcernedForeman.Tests;

/// <summary>#280 as #380 consumes it: every check is made explicitly, every
/// check that cannot be made is a refusal, and cost comes out of the
/// reservation.</summary>
public sealed class PlacementAuthorityTests
{
    private static CostedPiece AWall()
    {
        ShelterPlan plan = ConstructionRig.Plan();
        foreach (CostedPiece piece in plan.Pieces)
        {
            if (piece.Phase == BuildPhase.Walls && piece.Placement.Piece.Prefab == "wood_wall")
            {
                return piece;
            }
        }

        throw new Xunit.Sdk.XunitException("the blueprint has no wall");
    }

    private static PlacementVerdict May(
        FakeProbe probe, MaterialTally? reserved = null, bool authorised = true)
    {
        CostedPiece wall = AWall();
        return PlacementGate.May(
            wall.Placement,
            wall.Recipe,
            probe,
            reserved ?? ConstructionRig.Tally(("Wood", 2)),
            authorised);
    }

    [Fact]
    public void A_placement_that_passes_every_check_is_allowed()
    {
        Assert.True(May(new FakeProbe()).MayPlace);
    }

    [Fact]
    public void Nothing_is_placed_without_a_confirmed_build_order()
    {
        PlacementVerdict verdict = May(new FakeProbe(), authorised: false);

        Assert.Equal(PlacementRefusal.NotAuthorised, verdict.Refusal);

        // And the authority is asked before the world is, so a piece nobody
        // authorised never even asks whether it could have been built.
        var probe = new FakeProbe();
        PlacementGate.May(
            AWall().Placement, AWall().Recipe, probe, ConstructionRig.Tally(("Wood", 2)), false);
        Assert.Empty(probe.Asked);
    }

    [Fact]
    public void A_piece_inside_somebody_elses_ward_is_refused_with_the_ward_named()
    {
        PlacementVerdict verdict = May(new FakeProbe { Ward = ProbeAnswer.No });

        Assert.Equal(PlacementRefusal.Ward, verdict.Refusal);
        Assert.Contains("ward", verdict.Check);
    }

    [Fact]
    public void A_piece_needing_a_station_out_of_range_is_refused_with_the_station_named()
    {
        PlacementVerdict verdict = May(new FakeProbe { Station = ProbeAnswer.No });

        Assert.Equal(PlacementRefusal.Station, verdict.Refusal);
        Assert.Contains("station", verdict.Check);
    }

    [Theory]
    [InlineData("host", "NotHost")]
    [InlineData("exists", "NoSuchPiece")]
    [InlineData("loaded", "NotLoaded")]
    [InlineData("ward", "Ward")]
    [InlineData("station", "Station")]
    [InlineData("ground", "Ground")]
    [InlineData("space", "Space")]
    [InlineData("nobuild", "NoBuildZone")]
    public void Every_check_can_refuse_on_its_own_and_says_which_one_it_was(
        string check, string expectedName)
    {
        // The expected refusal is named as a string because xUnit needs a public
        // test signature and the refusal enum is internal, like everything else
        // in this product.
        var expected = (PlacementRefusal)System.Enum.Parse(typeof(PlacementRefusal), expectedName);
        var probe = new FakeProbe();
        switch (check)
        {
            case "host": probe.Host = ProbeAnswer.No; break;
            case "exists": probe.Exists = ProbeAnswer.No; break;
            case "loaded": probe.Loaded = ProbeAnswer.No; break;
            case "ward": probe.Ward = ProbeAnswer.No; break;
            case "station": probe.Station = ProbeAnswer.No; break;
            case "ground": probe.Ground = ProbeAnswer.No; break;
            case "space": probe.Space = ProbeAnswer.No; break;
            case "nobuild": probe.NoBuild = ProbeAnswer.No; break;
        }

        Assert.Equal(expected, May(probe).Refusal);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("exists")]
    [InlineData("loaded")]
    [InlineData("ward")]
    [InlineData("station")]
    [InlineData("ground")]
    [InlineData("space")]
    [InlineData("nobuild")]
    public void A_check_that_could_not_be_made_refuses_rather_than_assuming_a_yes(string check)
    {
        var probe = new FakeProbe();
        switch (check)
        {
            case "host": probe.Host = ProbeAnswer.CouldNotTell; break;
            case "exists": probe.Exists = ProbeAnswer.CouldNotTell; break;
            case "loaded": probe.Loaded = ProbeAnswer.CouldNotTell; break;
            case "ward": probe.Ward = ProbeAnswer.CouldNotTell; break;
            case "station": probe.Station = ProbeAnswer.CouldNotTell; break;
            case "ground": probe.Ground = ProbeAnswer.CouldNotTell; break;
            case "space": probe.Space = ProbeAnswer.CouldNotTell; break;
            case "nobuild": probe.NoBuild = ProbeAnswer.CouldNotTell; break;
        }

        // This is CF-SET-003's go/no-go in one assertion: a check that cannot be
        // faithfully made becomes a refusal, never an assumption.
        Assert.Equal(PlacementRefusal.Unchecked, May(probe).Refusal);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("ward")]
    [InlineData("space")]
    public void A_check_that_throws_is_a_check_that_was_not_made(string check)
    {
        PlacementVerdict verdict = May(new FakeProbe { ThrowsFrom = check });

        Assert.Equal(PlacementRefusal.Unchecked, verdict.Refusal);
        Assert.Contains("nothing is placed", verdict.Check);
    }

    [Fact]
    public void There_being_no_way_to_ask_the_world_refuses_everything()
    {
        CostedPiece wall = AWall();
        PlacementVerdict verdict = PlacementGate.May(
            wall.Placement, wall.Recipe, probe: null, ConstructionRig.Tally(("Wood", 2)), true);

        Assert.Equal(PlacementRefusal.Unchecked, verdict.Refusal);
    }

    [Fact]
    public void Cost_comes_out_of_the_reservation_and_a_shortfall_refuses_with_the_amount()
    {
        PlacementVerdict verdict = May(new FakeProbe(), ConstructionRig.Tally(("Wood", 1)));

        Assert.Equal(PlacementRefusal.Cost, verdict.Refusal);
        Assert.Contains("1 Wood", verdict.Check);

        // Nothing reserved at all is the same refusal, not a quiet success.
        Assert.Equal(PlacementRefusal.Cost, May(new FakeProbe(), new MaterialTally()).Refusal);

        CostedPiece wall = AWall();
        Assert.Equal(
            PlacementRefusal.Cost,
            PlacementGate.May(wall.Placement, wall.Recipe, new FakeProbe(), null, true).Refusal);
    }

    [Fact]
    public void A_surplus_in_hand_is_not_a_refusal()
    {
        // He may legitimately be carrying the whole cottage's wood while placing
        // one wall of it.
        Assert.True(May(new FakeProbe(), ConstructionRig.Tally(("Wood", 64))).MayPlace);
    }

    [Fact]
    public void A_piece_whose_cost_was_never_established_spends_nothing()
    {
        CostedPiece wall = AWall();
        PlacementVerdict verdict = PlacementGate.May(
            wall.Placement,
            PieceRecipe.Unknown("wood_wall", "no piece component"),
            new FakeProbe(),
            ConstructionRig.Tally(("Wood", 64)),
            true);

        Assert.Equal(PlacementRefusal.Cost, verdict.Refusal);
        Assert.Contains("never established", verdict.Check);
    }

    [Fact]
    public void The_checks_are_asked_in_the_written_order()
    {
        // The refusal a player reads is the first one that fired, so the order
        // is a decision about what they are told, not an implementation detail.
        var probe = new FakeProbe();
        May(probe);

        Assert.Equal(
            new[]
            {
                "host", "exists", "loaded", "ward", "nobuild", "constraints", "station",
                "ground", "space",
            },
            probe.Asked);
    }

    [Fact]
    public void The_cost_is_asked_last_so_a_warded_site_does_not_report_a_shortage()
    {
        PlacementVerdict verdict = May(
            new FakeProbe { Ward = ProbeAnswer.No }, ConstructionRig.Tally(("Wood", 0)));

        Assert.Equal(PlacementRefusal.Ward, verdict.Refusal);
    }

    [Fact]
    public void A_constraint_this_runtime_does_not_judge_refuses_and_names_it()
    {
        // The containment that used to live in a data file - "the four wood
        // pieces we happen to use declare none of these" - is now a check. A
        // fifth piece that declares one gets a refusal, not a wall built where
        // the game would have refused it.
        PlacementVerdict verdict = May(new FakeProbe
        {
            Constraints = ProbeAnswer.CouldNotTell,
            Constraint = "that it may only be built on cultivated ground",
        });

        Assert.Equal(PlacementRefusal.Constraint, verdict.Refusal);
        Assert.Contains("cultivated ground", verdict.Check);
        Assert.Contains("does not judge", verdict.Check);
    }

    [Fact]
    public void A_constraint_that_is_judged_and_fails_reads_as_a_refusal_rather_than_a_gap()
    {
        PlacementVerdict verdict = May(new FakeProbe
        {
            Constraints = ProbeAnswer.No,
            Constraint = "it may not be built inside a dungeon, and that place is inside one",
        });

        Assert.Equal(PlacementRefusal.Constraint, verdict.Refusal);
        Assert.Contains("may not be built there", verdict.Check);
        Assert.DoesNotContain("does not judge", verdict.Check);
    }

    [Fact]
    public void A_constraint_refusal_that_names_nothing_still_says_so()
    {
        PlacementVerdict verdict = May(new FakeProbe { Constraints = ProbeAnswer.CouldNotTell });

        Assert.Equal(PlacementRefusal.Constraint, verdict.Refusal);
        Assert.Contains("did not name", verdict.Check);
    }

    [Fact]
    public void A_blueprint_piece_that_is_not_a_piece_is_refused_before_anything_is_asked()
    {
        var probe = new FakeProbe();
        PlacementVerdict verdict = PlacementGate.May(
            default, PieceRecipe.Known("wood_wall", null), probe, new MaterialTally(), true);

        Assert.Equal(PlacementRefusal.NoSuchPiece, verdict.Refusal);
        Assert.Empty(probe.Asked);
    }
}
