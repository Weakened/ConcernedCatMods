using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Domain.Construction;

namespace ConcernedForeman.Tests;

/// <summary>#380: material requirements come from the real vanilla piece
/// recipes. No invented costs, no defaults, and no order accepted that could not
/// be priced.</summary>
public sealed class ShelterManifestTests
{
    [Fact]
    public void The_manifest_is_the_sum_of_the_recipes_the_game_gave_and_nothing_else()
    {
        FakeRecipes recipes = ConstructionRig.Recipes();
        ShelterPlan plan = ConstructionRig.Plan(recipes: recipes);

        Assert.True(plan.IsPlanned);

        // Four floors and four roof panels at 4, seven walls at 2, one door at
        // 10, one bed at 8: 32 + 14 + 10 + 8.
        Assert.Equal(64, plan.Total.UnitsOf("Wood"));
        Assert.Equal(1, plan.Total.Kinds);

        // And it is the sum of the phases, with nothing left over.
        int phases = 0;
        foreach (BuildPhase phase in BuildPhases.InOrder)
        {
            phases += plan.TotalFor(phase).UnitsOf("Wood");
        }

        Assert.Equal(plan.Total.UnitsOf("Wood"), phases);
    }

    [Fact]
    public void Each_distinct_prefab_is_priced_once_rather_than_once_per_piece()
    {
        FakeRecipes recipes = ConstructionRig.Recipes();
        ConstructionRig.Plan(recipes: recipes);

        // Seventeen pieces, four prefabs. Asking the game seventeen times would
        // work and would be seventeen prefab-table walks per order.
        Assert.Equal(ShelterBlueprint.Prefabs.Count, recipes.Asked.Count);
        Assert.Equal(new HashSet<string>(ShelterBlueprint.Prefabs), new HashSet<string>(recipes.Asked));
    }

    [Fact]
    public void A_piece_the_game_will_not_price_refuses_the_whole_order_and_names_it()
    {
        FakeRecipes recipes = ConstructionRig.Recipes();
        recipes.Forget("wood_door");

        ShelterPlan plan = ConstructionRig.Plan(recipes: recipes);

        Assert.False(plan.IsPlanned);
        Assert.Contains("wood_door", plan.Refusal);
        Assert.Empty(plan.Pieces);

        // The refusal has to say that nothing was taken, because the whole
        // reason to price up front is that a chest is not opened for a cottage
        // that cannot be finished.
        Assert.Contains("chest", plan.Refusal);
    }

    [Fact]
    public void A_recipe_line_that_is_not_an_amount_is_not_a_recipe()
    {
        Assert.False(PieceRecipe.Known("wood_wall", new[] { new PieceCost("Wood", 0) }).IsKnown);
        Assert.False(PieceRecipe.Known("wood_wall", new[] { new PieceCost("Wood", -2) }).IsKnown);
        Assert.False(PieceRecipe.Known("wood_wall", new[] { new PieceCost(string.Empty, 2) }).IsKnown);
        Assert.False(PieceRecipe.Known(string.Empty, new[] { new PieceCost("Wood", 2) }).IsKnown);
    }

    [Fact]
    public void A_piece_that_really_costs_nothing_is_not_the_same_as_one_nobody_could_read()
    {
        PieceRecipe free = PieceRecipe.Known("wood_wall", null);
        Assert.True(free.IsKnown);
        Assert.Empty(free.Costs);

        PieceRecipe unknown = PieceRecipe.Unknown("wood_wall", "no piece component");
        Assert.False(unknown.IsKnown);
        Assert.Equal("no piece component", unknown.Refusal);

        // Folding them together is how a missing prefab becomes a free cottage.
        Assert.NotEqual(free.IsKnown, unknown.IsKnown);
    }

    [Fact]
    public void An_unconfirmed_order_is_never_priced()
    {
        ShelterPlan plan = ShelterPlan.For(
            BuildOrderMarker.Proposed(
                BuildOrderKind.Shelter, new TheConcernedCat.Settlement.Worker.SitePoint(0f, 0f, 0f), 0f),
            ConstructionRig.Recipes());

        Assert.False(plan.IsPlanned);
        Assert.Contains("confirmed", plan.Refusal);
    }

    [Fact]
    public void An_order_with_no_way_to_read_a_cost_is_refused_rather_than_guessed()
    {
        ShelterPlan plan = ShelterPlan.For(ConstructionRig.Confirmed(), recipes: null);

        Assert.False(plan.IsPlanned);
        Assert.Equal(0, plan.Total.TotalUnits);
    }

    [Fact]
    public void The_missing_list_is_per_item_and_never_in_units()
    {
        MaterialTally wanted = ConstructionRig.Tally(("Wood", 40), ("Stone", 8), ("Resin", 4));
        MaterialTally have = ConstructionRig.Tally(("Wood", 40), ("Stone", 2));

        MaterialTally missing = wanted.Missing(have);

        Assert.Equal(0, missing.UnitsOf("Wood"));
        Assert.Equal(6, missing.UnitsOf("Stone"));
        Assert.Equal(4, missing.UnitsOf("Resin"));

        // "You are ten short" is not a list. The sentence names both.
        string sentence = ConstructionSentences.ShortOfMaterial(missing);
        Assert.Contains("6 Stone", sentence);
        Assert.Contains("4 Resin", sentence);
        Assert.DoesNotContain("Wood", sentence);
    }

    [Fact]
    public void The_missing_list_reads_the_same_way_twice()
    {
        MaterialTally wanted = ConstructionRig.Tally(("Wood", 40), ("Stone", 8), ("Resin", 4));
        MaterialTally missing = wanted.Missing(new MaterialTally());

        Assert.Equal(missing.Describe(), missing.Describe());
        Assert.Equal("40 Wood, 8 Stone and 4 Resin", missing.Describe());
    }

    [Fact]
    public void A_tally_never_subtracts_through_a_negative_requirement()
    {
        MaterialTally tally = ConstructionRig.Tally(("Wood", 10));
        tally.Add("Wood", -4);
        tally.Add("Wood", 0);
        tally.Add(null, 5);

        Assert.Equal(10, tally.UnitsOf("Wood"));
        Assert.Equal(1, tally.Kinds);
    }

    [Fact]
    public void A_copy_of_a_tally_cannot_be_edited_through_the_original()
    {
        MaterialTally original = ConstructionRig.Tally(("Wood", 10));
        MaterialTally copy = original.Copy();
        original.Add("Wood", 5);

        Assert.Equal(10, copy.UnitsOf("Wood"));
        Assert.Equal(15, original.UnitsOf("Wood"));
    }

    [Fact]
    public void Nothing_is_a_readable_answer_for_an_empty_tally()
    {
        Assert.Equal("nothing", new MaterialTally().Describe());
        Assert.True(new MaterialTally().IsEmpty);
    }
}
