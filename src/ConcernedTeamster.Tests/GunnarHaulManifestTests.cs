using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;

namespace ConcernedTeamster.Tests;

/// <summary>The manual haul: chest to chest, with an explicit source, an
/// explicit destination and a requested cargo filter, worked out before he takes
/// a step (#381).</summary>
public sealed class GunnarHaulManifestTests
{
    private static readonly CollectionLimits Limits = CollectionLimits.Default;

    private static CarryBudget Room(float kilograms) =>
        CarryBudget.From(new CarryFacts(kilograms, 0f, 0f, beltEquipped: true), Limits);

    private static List<ManifestLine> Holds(params (string Item, int Units, float Kilograms)[] lines)
    {
        var held = new List<ManifestLine>();
        foreach ((string item, int units, float kilograms) in lines)
        {
            held.Add(new ManifestLine(item, units, kilograms));
        }

        return held;
    }

    private static HaulManifestPlan Plan(
        ContainerUse source = ContainerUse.Both,
        ContainerUse destination = ContainerUse.Both,
        List<ManifestLine>? available = null,
        IReadOnlyCollection<string>? filter = null,
        float carry = 100f,
        CartCapacity cart = default,
        CollectionLimits? limits = null) =>
        HaulManifestPlanner.Plan(
            source, destination, "chest-a", "chest-b",
            available ?? Holds(("Stone", 40, 2f)), filter, Room(carry), cart, limits ?? Limits);

    [Fact]
    public void A_source_that_only_permits_depositing_refuses_the_take_and_says_which_way_round()
    {
        HaulManifestPlan plan = Plan(source: ContainerUse.Deposit);

        Assert.Equal(ManifestRefusal.SourceNotEnabledForTaking, plan.Refusal);
        Assert.Contains("not for taking them out", plan.Detail);
    }

    [Fact]
    public void A_destination_that_only_permits_taking_refuses_the_deposit_and_says_which_way_round()
    {
        HaulManifestPlan plan = Plan(destination: ContainerUse.Take);

        Assert.Equal(ManifestRefusal.DestinationNotEnabledForDepositing, plan.Refusal);
        Assert.Contains("not for putting them in", plan.Detail);
    }

    [Fact]
    public void A_container_nobody_enabled_is_off_and_off_is_the_default()
    {
        Assert.Equal(ContainerUse.Off, default(ContainerUse));
        Assert.Equal(ManifestRefusal.SourceNotEnabledForTaking, Plan(source: ContainerUse.Off).Refusal);
        Assert.Contains("not enabled for helpers at all", Plan(source: ContainerUse.Off).Detail);
    }

    [Fact]
    public void There_is_no_such_thing_as_the_nearest_chest()
    {
        HaulManifestPlan plan = HaulManifestPlanner.Plan(
            ContainerUse.Both, ContainerUse.Both, string.Empty, "chest-b",
            Holds(("Stone", 10, 2f)), null, Room(100f), CartCapacity.None, Limits);

        Assert.Equal(ManifestRefusal.NoSource, plan.Refusal);
        Assert.Contains("nearest", plan.Detail);
    }

    [Fact]
    public void A_haul_from_a_chest_to_itself_is_refused()
    {
        HaulManifestPlan plan = HaulManifestPlanner.Plan(
            ContainerUse.Both, ContainerUse.Both, "chest-a", "chest-a",
            Holds(("Stone", 10, 2f)), null, Room(100f), CartCapacity.None, Limits);

        Assert.Equal(ManifestRefusal.SameContainer, plan.Refusal);
    }

    [Fact]
    public void The_whole_job_is_worked_out_into_tours_before_a_step_is_taken()
    {
        // Forty stones at two kilograms, and room for twenty kilograms a trip:
        // ten a trip, four trips, all decided up front.
        HaulManifestPlan plan = Plan(carry: 20f);

        Assert.False(plan.IsRefused);
        Assert.Equal(4, plan.Tours.Count);
        Assert.Equal(0, plan.LeftForAnotherRound);
        Assert.True(plan.CoversTheWholeJob);
        foreach (HaulTour tour in plan.Tours)
        {
            Assert.Equal(10, tour.Units);
            Assert.False(tour.UsesCart);
        }
    }

    [Fact]
    public void The_tours_are_numbered_so_a_player_can_be_told_which_one_he_is_on()
    {
        HaulManifestPlan plan = Plan(carry: 20f);

        Assert.Equal(new[] { 1, 2, 3, 4 }, Numbers(plan));
    }

    [Fact]
    public void A_filter_takes_only_what_was_asked_for()
    {
        HaulManifestPlan plan = Plan(
            available: Holds(("Stone", 10, 2f), ("Wood", 10, 1f), ("Coins", 99, 0.1f)),
            filter: new[] { "Wood" });

        Assert.Single(plan.Wanted);
        Assert.Equal("Wood", plan.Wanted[0].ItemPrefab);
        Assert.Single(plan.Tours);
        Assert.Equal(10, plan.Tours[0].Units);
    }

    [Fact]
    public void A_filter_that_matches_nothing_refuses_rather_than_taking_everything()
    {
        HaulManifestPlan plan = Plan(
            available: Holds(("Stone", 10, 2f)), filter: new[] { "Silver" });

        Assert.Equal(ManifestRefusal.NothingMatchesTheFilter, plan.Refusal);
    }

    [Fact]
    public void No_filter_at_all_means_everything_the_source_holds()
    {
        HaulManifestPlan plan = Plan(available: Holds(("Stone", 4, 2f), ("Wood", 4, 1f)), filter: null);

        Assert.Equal(2, plan.Wanted.Count);
    }

    [Fact]
    public void The_cart_is_only_hitched_for_a_tour_that_needs_it()
    {
        // Two kilograms of room on his back and a cart with plenty: the first
        // tour uses the cart, and a tour that fits on his back says so.
        HaulManifestPlan withCart = Plan(
            available: Holds(("Stone", 10, 2f)),
            carry: 2f,
            cart: new CartCapacity(assigned: true, freeSlots: 1, slotStackSize: 50));
        HaulManifestPlan onFoot = Plan(available: Holds(("Stone", 1, 2f)), carry: 100f);

        Assert.Single(withCart.Tours);
        Assert.True(withCart.Tours[0].UsesCart);
        Assert.False(onFoot.Tours[0].UsesCart);
    }

    [Fact]
    public void The_loading_order_is_the_same_every_time_the_same_manifest_is_planned()
    {
        HaulManifestPlan first = Plan(available: Holds(("Wood", 5, 1f), ("Stone", 5, 2f), ("Flint", 5, 1f)));
        HaulManifestPlan second = Plan(available: Holds(("Flint", 5, 1f), ("Stone", 5, 2f), ("Wood", 5, 1f)));

        Assert.Equal(Items(first), Items(second));
        // Heaviest first, then by name.
        Assert.Equal(new[] { "Stone", "Flint", "Wood" }, Items(first));
    }

    [Fact]
    public void A_manifest_bigger_than_the_tour_ceiling_is_partly_planned_and_never_refused()
    {
        // The same rule as a batch: a job the player can see is doable never
        // becomes a permanent refusal because of a cap nobody chose.
        HaulManifestPlan plan = Plan(
            available: Holds(("Stone", 100, 2f)), carry: 2f, limits: Limits.With(mostToursPerManifest: 3));

        Assert.False(plan.IsRefused);
        Assert.Equal(3, plan.Tours.Count);
        Assert.Equal(97, plan.LeftForAnotherRound);
        Assert.False(plan.CoversTheWholeJob);
    }

    [Fact]
    public void Every_unit_is_either_in_a_tour_or_left_for_another_round()
    {
        for (int allowance = 1; allowance <= 8; allowance++)
        {
            HaulManifestPlan plan = Plan(
                available: Holds(("Stone", 37, 2f), ("Wood", 11, 1f)),
                carry: 9f,
                limits: Limits.With(mostToursPerManifest: allowance));

            int planned = 0;
            foreach (HaulTour tour in plan.Tours)
            {
                planned += tour.Units;
            }

            Assert.Equal(37 + 11, planned + plan.LeftForAnotherRound);
        }
    }

    [Fact]
    public void No_room_on_his_back_and_no_cart_is_refused_rather_than_planned_as_nothing()
    {
        HaulManifestPlan plan = Plan(carry: 0f, cart: CartCapacity.None);

        Assert.Equal(ManifestRefusal.NoRoomAnywhere, plan.Refusal);
    }

    [Fact]
    public void A_line_the_source_could_not_be_read_for_is_skipped_rather_than_guessed_at()
    {
        HaulManifestPlan plan = Plan(available: Holds(("Stone", 5, 2f), ("Mystery", 5, 0f), ("Ghost", 0, 1f)));

        Assert.Single(plan.Wanted);
        Assert.Equal("Stone", plan.Wanted[0].ItemPrefab);
    }

    private static int[] Numbers(HaulManifestPlan plan)
    {
        var numbers = new int[plan.Tours.Count];
        for (int index = 0; index < plan.Tours.Count; index++)
        {
            numbers[index] = plan.Tours[index].Number;
        }

        return numbers;
    }

    private static string[] Items(HaulManifestPlan plan)
    {
        var items = new List<string>();
        foreach (ManifestLine line in plan.Tours[0].Load)
        {
            items.Add(line.ItemPrefab);
        }

        return items.ToArray();
    }
}
