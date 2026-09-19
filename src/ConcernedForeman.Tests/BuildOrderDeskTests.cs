using TheConcernedCat.ConcernedForeman.Domain.Construction;
using TheConcernedCat.Settlement.Worker;

namespace ConcernedForeman.Tests;

/// <summary>The Build Orders menu, as the decisions behind it: the player marks
/// a place and a facing, sees what it costs from the real recipes, and confirms.
/// That confirmation is the whole of #280's authority, and nothing in this
/// product places a piece without it.</summary>
public sealed class BuildOrderDeskTests
{
    private static BuildOrderContext At(
        float x = 10f, float y = 2f, float z = -4f, float facing = 45f,
        bool mayWork = true, IPieceRecipes? recipes = null, string refusal = "") =>
        new BuildOrderContext(
            mayWork, new SitePoint(x, y, z), facing, recipes ?? ConstructionRig.Recipes(), refusal);

    [Fact]
    public void A_desk_with_no_order_authorises_nothing()
    {
        var desk = new BuildOrderDesk();

        Assert.Equal(BuildOrderStatus.None, desk.Status);
        Assert.False(desk.IsAuthorised);
        Assert.False(desk.PlanNow(At()).IsPlanned);
        Assert.Contains("Stand where the middle of the shelter should go", desk.Execute(null, At()));
    }

    [Fact]
    public void Marking_a_shelter_takes_the_place_and_the_facing_the_player_is_standing_in()
    {
        var desk = new BuildOrderDesk();

        string said = desk.Execute(new[] { "here" }, At(10f, 2f, -4f, 45f));

        Assert.Equal(BuildOrderStatus.Proposed, desk.Status);
        Assert.Equal(10f, desk.Marker.At.X, 3);
        Assert.Equal(45f, desk.Marker.Yaw, 3);
        Assert.Contains("not authorised", said);
    }

    [Fact]
    public void A_marked_shelter_authorises_nothing_until_it_is_confirmed()
    {
        var desk = new BuildOrderDesk();
        desk.Execute(new[] { "here" }, At());

        Assert.False(desk.IsAuthorised);
        Assert.False(desk.PlanNow(At()).IsPlanned);
        Assert.Empty(ShelterBlueprint.PlaceAt(desk.Marker));
    }

    [Fact]
    public void Confirming_is_the_authorisation_and_it_says_what_it_costs()
    {
        var desk = new BuildOrderDesk();
        desk.Execute(new[] { "here" }, At());

        string said = desk.Execute(new[] { "confirm" }, At());

        Assert.Equal(BuildOrderStatus.Confirmed, desk.Status);
        Assert.True(desk.IsAuthorised);
        Assert.Contains("Authorised", said);
        Assert.Contains("17 pieces", said);
        Assert.Contains("64 Wood", said);
        Assert.Contains("containers you have enabled, and nothing else", said);
        Assert.True(desk.PlanNow(At()).IsPlanned);
    }

    [Fact]
    public void An_order_that_cannot_be_priced_is_never_authorised()
    {
        FakeRecipes recipes = ConstructionRig.Recipes();
        recipes.Forget("bed");

        var desk = new BuildOrderDesk();
        desk.Execute(new[] { "here" }, At(recipes: recipes));

        string said = desk.Execute(new[] { "confirm" }, At(recipes: recipes));

        // Pricing first is not politeness: an order authorised before it is
        // priced is one that can open a chest for a cottage nobody can finish.
        Assert.Equal(BuildOrderStatus.Proposed, desk.Status);
        Assert.False(desk.IsAuthorised);
        Assert.Contains("not authorised", said);
        Assert.Contains("bed", said);
    }

    [Fact]
    public void Preview_prices_it_without_authorising_it()
    {
        var desk = new BuildOrderDesk();
        desk.Execute(new[] { "here" }, At());

        string said = desk.Execute(new[] { "preview" }, At());

        Assert.Contains("64 Wood", said);
        Assert.Contains("Confirm to authorise", said);
        Assert.False(desk.IsAuthorised);
    }

    [Fact]
    public void Turning_a_marked_shelter_turns_it_and_leaves_it_unauthorised()
    {
        var desk = new BuildOrderDesk();
        desk.Execute(new[] { "here" }, At(facing: 0f));

        desk.Execute(new[] { "turn", "90" }, At(facing: 0f));
        Assert.Equal(90f, desk.Marker.Yaw, 3);

        desk.Execute(new[] { "turn", "-135" }, At(facing: 0f));
        Assert.Equal(315f, desk.Marker.Yaw, 3);
        Assert.False(desk.IsAuthorised);
    }

    [Fact]
    public void Turn_without_a_number_asks_for_one_rather_than_guessing()
    {
        var desk = new BuildOrderDesk();
        desk.Execute(new[] { "here" }, At(facing: 30f));

        Assert.Contains("how many degrees", desk.Execute(new[] { "turn" }, At()));
        Assert.Contains("how many degrees", desk.Execute(new[] { "turn", "sideways" }, At()));
        Assert.Equal(30f, desk.Marker.Yaw, 3);
    }

    [Fact]
    public void An_authorised_order_is_not_moved_or_turned_out_from_under_the_work()
    {
        var desk = new BuildOrderDesk();
        desk.Execute(new[] { "here" }, At(10f, 2f, -4f, 45f));
        desk.Execute(new[] { "confirm" }, At());

        string moved = desk.Execute(new[] { "here" }, At(90f, 0f, 90f, 0f));
        string turned = desk.Execute(new[] { "turn", "90" }, At());

        // Moving it would move the authority with it, to a place the player
        // agreed nothing about.
        Assert.Equal(10f, desk.Marker.At.X, 3);
        Assert.Equal(45f, desk.Marker.Yaw, 3);
        Assert.Contains("Cancel it first", moved);
        Assert.Contains("Cancel it first", turned);
    }

    [Fact]
    public void Cancelling_withdraws_the_authority_and_keeps_the_place()
    {
        var desk = new BuildOrderDesk();
        desk.Execute(new[] { "here" }, At(10f, 2f, -4f));
        desk.Execute(new[] { "confirm" }, At());

        string said = desk.Execute(new[] { "cancel" }, At());

        Assert.Equal(BuildOrderStatus.Proposed, desk.Status);
        Assert.False(desk.IsAuthorised);
        Assert.Equal(10f, desk.Marker.At.X, 3);
        Assert.Contains("accounted for rather than dropped", said);
    }

    [Fact]
    public void An_order_may_be_marked_and_authorised_where_no_work_can_happen_and_says_so()
    {
        var desk = new BuildOrderDesk();
        desk.Execute(new[] { "here" }, At(mayWork: false, refusal: "somebody else is connected."));

        string said = desk.Execute(
            new[] { "confirm" }, At(mayWork: false, refusal: "somebody else is connected."));

        // The authority is the player's and it stands; the working is the
        // runtime's and it does not. Collapsing them would make a player
        // re-confirm an order every time a friend logs in.
        Assert.True(desk.IsAuthorised);
        Assert.Contains("Authorised", said);
        Assert.Contains("somebody else is connected", said);
    }

    [Fact]
    public void A_world_going_away_takes_the_order_with_it()
    {
        var desk = new BuildOrderDesk();
        desk.Execute(new[] { "here" }, At());
        desk.Execute(new[] { "confirm" }, At());

        desk.Forget();

        // A marker names a place in a world that has gone. Carrying it into the
        // next one would authorise building somewhere nobody agreed to.
        Assert.Equal(BuildOrderStatus.None, desk.Status);
        Assert.False(desk.IsAuthorised);
    }

    [Fact]
    public void Nothing_to_price_turn_confirm_or_cancel_is_said_rather_than_ignored()
    {
        var desk = new BuildOrderDesk();

        Assert.Contains("nothing marked to price", desk.Execute(new[] { "preview" }, At()));
        Assert.Contains("nothing marked to turn", desk.Execute(new[] { "turn", "90" }, At()));
        Assert.Contains("nothing marked to confirm", desk.Execute(new[] { "confirm" }, At()));
        Assert.Contains("no build order to cancel", desk.Execute(new[] { "cancel" }, At()));
    }

    [Fact]
    public void A_word_the_desk_does_not_know_lists_the_ones_it_does()
    {
        string said = new BuildOrderDesk().Execute(new[] { "demolish" }, At());

        Assert.Contains("status", said);
        Assert.Contains("here", said);
        Assert.Contains("confirm", said);
        Assert.Contains("cancel", said);
    }

    [Fact]
    public void Status_says_where_it_is_and_whether_anything_will_happen()
    {
        var desk = new BuildOrderDesk();
        desk.Execute(new[] { "here" }, At());
        Assert.Contains("nobody has authorised it", desk.Execute(new[] { "status" }, At()));

        desk.Execute(new[] { "confirm" }, At());
        Assert.Contains("authorised at", desk.Execute(new[] { "status" }, At()));
        Assert.Contains(
            "nobody is here to do it",
            desk.Execute(new[] { "status" }, At(mayWork: false, refusal: "nobody is here to do it.")));
    }

    [Fact]
    public void The_plan_is_priced_fresh_every_time_and_never_remembered()
    {
        FakeRecipes recipes = ConstructionRig.Recipes();
        var desk = new BuildOrderDesk();
        desk.Execute(new[] { "here" }, At(recipes: recipes));
        desk.Execute(new[] { "confirm" }, At(recipes: recipes));

        Assert.Equal(64, desk.PlanNow(At(recipes: recipes)).Total.UnitsOf("Wood"));

        // The world is the authority on a cost, so a world that answers
        // differently gives a different plan rather than a remembered one.
        recipes.Set("wood_wall", ("Wood", 4));
        Assert.Equal(78, desk.PlanNow(At(recipes: recipes)).Total.UnitsOf("Wood"));
    }
}
