using TheConcernedCat.ConcernedNPC.Camp;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>Building a camp in a test, one piece at a time, the way a role
/// would.</summary>
internal sealed class CampFixture
{
    private int _next;

    internal CampFixture(CampSurveyOptions? options = null)
    {
        Epoch = Identities.AWorld();
        Registry = new CampRegistry(options);
        Registry.BeginWorldLoad(Epoch);
    }

    internal NpcWorldEpoch Epoch { get; }

    internal CampRegistry Registry { get; }

    internal CampAnchor Anchor { get; set; } = new CampAnchor(CampAnchorKind.PlayerBed, new NpcPoint(0, 0, 0));

    /// <summary>A player-built piece at a point. Returns its key so a test can
    /// destroy exactly that one.</summary>
    internal string Build(float x, float z, CampPieceClass pieceClass = CampPieceClass.Built)
    {
        string key = "piece-" + _next++;
        Registry.Note(new CampPiece(key, new NpcPoint(x, 0, z), pieceClass, Epoch));
        return key;
    }

    internal CampSnapshot Survey() => Registry.Survey(Anchor);
}

public class CampSurveyTests
{
    [Fact]
    public void A_house_and_its_workbench_are_one_camp()
    {
        var camp = new CampFixture();
        camp.Build(0, 0);
        camp.Build(4, 0);
        camp.Build(8, 2);
        camp.Build(12, 2);

        CampSnapshot surveyed = camp.Survey();

        Assert.Equal(4, surveyed.Members);
        Assert.True(surveyed.IsEstablished);
        Assert.True(surveyed.Contains(new NpcPoint(6, 0, 1)));
    }

    [Fact]
    public void One_stray_distant_piece_does_not_stretch_camp_across_the_map()
    {
        // The named failure of this leaf. A torch on a hill half a kilometre
        // away is a piece the player built; it is not their camp, and a camp
        // that reaches it is one an NPC will walk out of the world to patrol.
        var camp = new CampFixture();
        camp.Build(0, 0);
        camp.Build(5, 0);
        camp.Build(10, 0);

        CampSnapshot before = camp.Survey();

        camp.Build(500, 500);
        CampSnapshot after = camp.Survey();

        Assert.Equal(before.Members, after.Members);
        Assert.Equal(before.ExtentMetres, after.ExtentMetres, 3);
        Assert.False(after.Contains(new NpcPoint(500, 0, 500)));
        Assert.False(after.Contains(new NpcPoint(250, 0, 250)));

        // And it cost nothing to ignore: the stray sits in a cell nothing
        // reaches, so the survey never opened it. This is the performance half
        // of the same property, and it is what makes the boundary half true
        // rather than merely observed.
        Assert.Equal(before.Cost.PiecesConsidered, after.Cost.PiecesConsidered);
    }

    [Fact]
    public void A_chain_of_pieces_joins_two_clusters_and_removing_one_link_separates_them_again()
    {
        // Connectivity is the rule, so it has to work in both directions:
        // bridging must join, and un-bridging must separate. Without the second
        // half, a camp would only ever grow. A tight seed radius so the far
        // cluster can only be reached through the bridge, never by being near
        // the anchor.
        var camp = new CampFixture(new CampSurveyOptions(10f, 8f, 4f, 2048));
        camp.Build(0, 0);
        camp.Build(6, 0);
        camp.Build(60, 0);
        camp.Build(66, 0);

        Assert.Equal(2, camp.Survey().Members);

        string firstLink = camp.Build(12, 0);
        for (int x = 18; x <= 54; x += 6)
        {
            camp.Build(x, 0);
        }

        Assert.Equal(12, camp.Survey().Members);

        Assert.True(camp.Registry.Forget(firstLink));
        Assert.Equal(2, camp.Survey().Members);
    }

    [Fact]
    public void Terrain_edits_of_every_kind_are_excluded_and_cost_nothing_afterwards()
    {
        // Levelling, a trench, a path, a road, paint: each of them is a piece
        // the player made, and counting any of them stretches camp along every
        // route they ever walked.
        var camp = new CampFixture();
        camp.Build(0, 0);

        Assert.Equal(CampPieceOutcome.Excluded, camp.Registry.Note(
            new CampPiece("flattened", new NpcPoint(4, 0, 0), CampPieceClass.TerrainEdit, camp.Epoch)));
        Assert.Equal(CampPieceOutcome.Excluded, camp.Registry.Note(
            new CampPiece("a-road", new NpcPoint(8, 0, 0), CampPieceClass.TerrainEdit, camp.Epoch)));
        Assert.Equal(CampPieceOutcome.Excluded, camp.Registry.Note(
            new CampPiece("an-oak", new NpcPoint(6, 0, 0), CampPieceClass.Natural, camp.Epoch)));
        Assert.Equal(CampPieceOutcome.Excluded, camp.Registry.Note(
            new CampPiece("who-knows", new NpcPoint(6, 0, 2), CampPieceClass.Unknown, camp.Epoch)));

        CampSnapshot surveyed = camp.Survey();

        Assert.Equal(1, surveyed.Members);
        Assert.Equal(1, camp.Registry.Known);

        // Not stored, so not skipped either: the exclusion is free every later
        // survey rather than a filter run over a growing list. Proved against
        // the same camp with nothing excluded in it, because "free" means "the
        // same cost", not a number anybody should have to know.
        var clean = new CampFixture();
        clean.Build(0, 0);

        Assert.Equal(clean.Survey().Cost.PiecesConsidered, surveyed.Cost.PiecesConsidered);
        Assert.Equal(clean.Survey().Cost.NeighbourChecks, surveyed.Cost.NeighbourChecks);
    }

    [Fact]
    public void A_road_running_out_of_camp_never_widens_it()
    {
        // The exclusion, stated as the situation that produces it rather than
        // as a property of one call.
        var camp = new CampFixture();
        camp.Build(0, 0);
        camp.Build(5, 0);

        for (int step = 1; step <= 40; step++)
        {
            camp.Registry.Note(new CampPiece(
                "road-" + step, new NpcPoint(5 + (step * 4), 0, 0), CampPieceClass.TerrainEdit, camp.Epoch));
        }

        CampSnapshot surveyed = camp.Survey();

        Assert.Equal(2, surveyed.Members);
        Assert.True(surveyed.ExtentMetres < 20f);
    }

    [Fact]
    public void Camp_updates_when_a_piece_is_built_and_again_when_it_is_destroyed()
    {
        var camp = new CampFixture();
        camp.Build(0, 0);
        camp.Build(5, 0);

        CampSnapshot first = camp.Survey();
        Assert.Equal(2, first.Members);

        string shed = camp.Build(5, 6);
        CampSnapshot grown = camp.Survey();
        Assert.Equal(3, grown.Members);
        Assert.True(grown.Revision > first.Revision);

        Assert.True(camp.Registry.Forget(shed));
        CampSnapshot shrunk = camp.Survey();
        Assert.Equal(2, shrunk.Members);
        Assert.True(shrunk.Revision > grown.Revision);
    }

    [Fact]
    public void A_survey_over_an_unchanged_camp_recomputes_nothing()
    {
        // "No per-frame hull recomputation", as a measurement rather than a
        // promise: a role may call this on a timer, or every frame, and pay
        // only when camp changed.
        var camp = new CampFixture();
        camp.Build(0, 0);
        camp.Build(5, 0);

        CampSnapshot first = camp.Survey();
        Assert.True(first.Cost.Recomputed);
        Assert.Equal(1, camp.Registry.Recomputations);

        CampSnapshot again = camp.Survey();
        Assert.Same(first, again);
        Assert.Equal(1, camp.Registry.Recomputations);

        // Re-noting a piece that has not moved is not a change either.
        camp.Registry.Note(new CampPiece("piece-0", new NpcPoint(0, 0, 0), CampPieceClass.Built, camp.Epoch));
        Assert.False(camp.Registry.IsStale);
        Assert.Same(first, camp.Survey());
        Assert.Equal(1, camp.Registry.Recomputations);
    }

    [Fact]
    public void An_anchor_that_moves_a_long_way_re_surveys_and_a_nudge_does_not()
    {
        var camp = new CampFixture();
        camp.Build(0, 0);
        camp.Build(5, 0);
        CampSnapshot first = camp.Survey();

        camp.Anchor = new CampAnchor(CampAnchorKind.PlayerBed, new NpcPoint(0.4f, 0, 0.4f));
        Assert.Same(first, camp.Survey());
        Assert.Equal(1, camp.Registry.Recomputations);

        camp.Anchor = new CampAnchor(CampAnchorKind.PlayerBed, new NpcPoint(200, 0, 200));
        CampSnapshot moved = camp.Survey();
        Assert.True(moved.Cost.Recomputed);
        Assert.Equal(0, moved.Members);
    }

    [Fact]
    public void A_survey_costs_what_camp_costs_and_not_what_the_world_costs()
    {
        // The stated cost: work is proportional to camp plus its fringe, and
        // independent of how much else has been built elsewhere. Two worlds,
        // the same camp, three hundred distant pieces of difference.
        var small = new CampFixture();
        var large = new CampFixture();
        foreach (CampFixture camp in new[] { small, large })
        {
            camp.Build(0, 0);
            camp.Build(5, 0);
            camp.Build(10, 0);
            camp.Build(10, 5);
        }

        for (int index = 0; index < 300; index++)
        {
            large.Registry.Note(new CampPiece(
                "far-" + index, new NpcPoint(2000 + (index * 40), 0, 2000), CampPieceClass.Built, large.Epoch));
        }

        CampSurveyCost cheap = small.Survey().Cost;
        CampSurveyCost rich = large.Survey().Cost;

        Assert.Equal(cheap.PiecesConsidered, rich.PiecesConsidered);
        Assert.Equal(cheap.NeighbourChecks, rich.NeighbourChecks);
        Assert.Equal(cheap.CellsVisited, rich.CellsVisited);
        Assert.Equal(304, large.Registry.Known);
    }

    [Fact]
    public void A_camp_larger_than_the_budget_is_truncated_rather_than_slow_or_refused()
    {
        var camp = new CampFixture(new CampSurveyOptions(24f, 8f, 4f, 16));
        for (int index = 0; index < 120; index++)
        {
            camp.Build(index * 4, 0);
        }

        CampSnapshot surveyed = camp.Survey();

        Assert.True(surveyed.Cost.Truncated);
        Assert.True(surveyed.Members <= 16 + 8);
        Assert.True(surveyed.IsEstablished);
    }

    [Fact]
    public void A_world_load_forgets_every_piece_because_every_name_now_means_something_else()
    {
        var camp = new CampFixture();
        camp.Build(0, 0);
        camp.Build(5, 0);
        Assert.Equal(2, camp.Survey().Members);

        NpcWorldEpoch next = Identities.AWorld();
        Assert.Equal(2, camp.Registry.BeginWorldLoad(next));
        Assert.Equal(0, camp.Registry.Known);

        // And the same load again is a no-op, so two parts of a role may both
        // report it without either needing to know about the other.
        Assert.Equal(0, camp.Registry.BeginWorldLoad(next));
    }

    [Fact]
    public void A_piece_from_another_world_load_is_refused()
    {
        var camp = new CampFixture();
        CampPieceOutcome outcome = camp.Registry.Note(
            new CampPiece("elsewhere", new NpcPoint(1, 0, 1), CampPieceClass.Built, Identities.AWorld()));

        Assert.Equal(CampPieceOutcome.StaleEpoch, outcome);
        Assert.Equal(0, camp.Registry.Known);
    }

    [Fact]
    public void A_piece_with_no_name_or_no_real_position_is_refused()
    {
        var camp = new CampFixture();

        Assert.Equal(CampPieceOutcome.Malformed, camp.Registry.Note(
            new CampPiece(string.Empty, new NpcPoint(1, 0, 1), CampPieceClass.Built, camp.Epoch)));
        Assert.Equal(CampPieceOutcome.Malformed, camp.Registry.Note(
            new CampPiece("nowhere", new NpcPoint(float.NaN, 0, 1), CampPieceClass.Built, camp.Epoch)));
        Assert.Equal(0, camp.Registry.Known);
    }

    [Fact]
    public void Re_noting_a_piece_that_moved_is_taken_and_re_noting_one_that_did_not_is_not()
    {
        var camp = new CampFixture();
        camp.Build(0, 0);

        Assert.Equal(CampPieceOutcome.AlreadyKnown, camp.Registry.Note(
            new CampPiece("piece-0", new NpcPoint(0, 0, 0), CampPieceClass.Built, camp.Epoch)));
        Assert.Equal(CampPieceOutcome.Moved, camp.Registry.Note(
            new CampPiece("piece-0", new NpcPoint(3, 0, 0), CampPieceClass.Built, camp.Epoch)));

        Assert.Equal(1, camp.Registry.Known);
        Assert.Equal(1, camp.Survey().Members);
    }

    [Fact]
    public void Forgetting_something_never_known_is_an_ordinary_no()
    {
        var camp = new CampFixture();
        Assert.False(camp.Registry.Forget("a-path-we-never-stored"));
        Assert.False(camp.Registry.Forget(null));
    }

    [Fact]
    public void With_no_anchor_there_is_no_camp_however_much_has_been_built()
    {
        var camp = new CampFixture { Anchor = CampAnchor.None };
        camp.Build(0, 0);
        camp.Build(5, 0);

        CampSnapshot surveyed = camp.Survey();

        Assert.Equal(0, surveyed.Members);
        Assert.False(surveyed.IsEstablished);
        Assert.False(surveyed.Contains(new NpcPoint(0, 0, 0)));
        Assert.False(surveyed.TryNearestInteriorPoint(new NpcPoint(0, 0, 0), out _));
    }
}
