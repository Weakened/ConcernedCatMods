using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;

namespace ConcernedTeamster.Tests;

/// <summary>What Gunnar's collection does with material, step by step (#381).
///
/// The sequence itself is the shared runtime's job driver's and is tested where
/// it lives. What is tested here is the half that is this product's and that the
/// driver deliberately knows nothing about: whether the material adds up after
/// every step, what a retry does, and what a destroyed cart does.</summary>
public sealed class GunnarCollectionAccountTests
{
    [Fact]
    public void Every_step_he_reports_is_conserved_after_that_step()
    {
        var account = new CollectionAccount();

        account.Record("r1#0", "Stone", 2, StopResult.Took());
        Assert.True(account.Ledger.IsConserved);
        account.Record("r1#1", "Stone", 2, StopResult.Took(CargoPlace.InCart));
        Assert.True(account.Ledger.IsConserved);
        account.Record("r1#2", "Wood", 3, StopResult.Did(StopOutcome.Gone));
        Assert.True(account.Ledger.IsConserved);

        Assert.Equal(2, account.Serviced);
        Assert.Equal(1, account.Skipped);
        Assert.Equal(4, account.Ledger.Acquired);
        Assert.Equal(2, account.Ledger.At("Stone", CargoPlace.Carried));
        Assert.Equal(2, account.Ledger.At("Stone", CargoPlace.InCart));
    }

    [Fact]
    public void A_step_reported_twice_is_recorded_once()
    {
        var account = new CollectionAccount();

        Assert.Equal(CargoOutcome.Recorded, account.Record("r1#0", "Stone", 2, StopResult.Took()));
        Assert.Equal(CargoOutcome.AlreadyRecorded, account.Record("r1#0", "Stone", 2, StopResult.Took()));

        Assert.Equal(2, account.Ledger.Acquired);
        Assert.Equal(1, account.Serviced);
    }

    [Fact]
    public void A_step_that_could_not_say_what_it_did_records_nothing()
    {
        var account = new CollectionAccount();

        account.Record("r1#0", "Stone", 2, StopResult.Did(StopOutcome.Unspecified));
        account.Record("r1#1", "Stone", 2, StopResult.Did(StopOutcome.Interrupted));

        Assert.Equal(0, account.Ledger.Acquired);
        Assert.Equal(0, account.Serviced);
        Assert.Equal(0, account.Skipped);
    }

    [Fact]
    public void A_stop_he_could_not_walk_to_is_counted_and_never_reached_another_way()
    {
        var account = new CollectionAccount();

        account.Record("r1#0", "Stone", 2, StopResult.Did(StopOutcome.Unreachable));

        Assert.Equal(1, account.Skipped);
        Assert.Equal(0, account.Ledger.Acquired);
    }

    [Fact]
    public void One_trip_carries_what_is_on_his_back_and_what_is_in_the_cart()
    {
        var account = new CollectionAccount();
        account.Record("a", "Wood", 3, StopResult.Took());
        account.Record("b", "Stone", 4, StopResult.Took(CargoPlace.InCart));
        account.Record("c", "Stone", 1, StopResult.Took());

        IReadOnlyList<KeyValuePair<string, int>> load = account.Load();

        Assert.Equal(2, load.Count);
        Assert.Equal("Stone", load[0].Key);
        Assert.Equal(5, load[0].Value);
        Assert.Equal("Wood", load[1].Key);
        Assert.Equal(3, load[1].Value);
    }

    [Fact]
    public void A_deposit_empties_his_back_before_it_empties_the_cart()
    {
        var account = new CollectionAccount();
        account.Record("a", "Stone", 2, StopResult.Took());
        account.Record("b", "Stone", 5, StopResult.Took(CargoPlace.InCart));

        account.RecordDeposit("r1", new DepositResult(
            DepositOutcome.Deposited, new[] { new KeyValuePair<string, int>("Stone", 4) }));

        Assert.Equal(0, account.Ledger.At("Stone", CargoPlace.Carried));
        Assert.Equal(3, account.Ledger.At("Stone", CargoPlace.InCart));
        Assert.Equal(4, account.Ledger.At("Stone", CargoPlace.Delivered));
        Assert.True(account.Ledger.IsConserved);
    }

    [Fact]
    public void A_refused_or_unmeasurable_deposit_moves_nothing()
    {
        var account = new CollectionAccount();
        account.Record("a", "Stone", 3, StopResult.Took());

        // Both results carry a measured list, and that is the point: a refusal
        // can still report what it saw on the way, and an uncertain transfer
        // reports both counts as its evidence. A test that passed an empty list
        // would pass with the guard deleted, which is how it was written first
        // and what a planted defect found.
        var measured = new[] { new KeyValuePair<string, int>("Stone", 3) };
        account.RecordDeposit("r1", new DepositResult(DepositOutcome.Refused, measured, "the chest is warded"));
        account.RecordDeposit("r2", new DepositResult(DepositOutcome.Uncertain, measured, "nobody could tell"));

        Assert.Equal(3, account.Ledger.At("Stone", CargoPlace.Carried));
        Assert.Equal(0, account.Ledger.At("Stone", CargoPlace.Delivered));
        Assert.Equal(0, account.Ledger.At("Stone", CargoPlace.Lost));
        Assert.True(account.Ledger.IsConserved);
    }

    [Fact]
    public void A_deposit_that_measured_more_than_is_there_moves_what_it_can_and_invents_nothing()
    {
        var account = new CollectionAccount();
        account.Record("a", "Stone", 2, StopResult.Took());

        account.RecordDeposit("r1", new DepositResult(
            DepositOutcome.Deposited, new[] { new KeyValuePair<string, int>("Stone", 9) }));

        Assert.Equal(2, account.Ledger.Acquired);
        Assert.Equal(2, account.Ledger.TotalEverywhere);
        Assert.Equal(2, account.Ledger.At("Stone", CargoPlace.Delivered));
        Assert.True(account.Ledger.IsConserved);
    }

    [Fact]
    public void A_destroyed_cart_stops_the_job_with_the_accounting_intact_and_no_replacement_cargo()
    {
        var account = new CollectionAccount();
        account.Record("a", "Stone", 6, StopResult.Took(CargoPlace.InCart));
        account.Record("b", "Wood", 4, StopResult.Took());

        Assert.Equal(CargoOutcome.Recorded, account.CartDestroyed("boom"));

        Assert.Equal(0, account.Ledger.At("Stone", CargoPlace.InCart));
        Assert.Equal(6, account.Ledger.At("Stone", CargoPlace.SpilledFromCart));
        Assert.Equal(4, account.Ledger.At("Wood", CargoPlace.Carried));
        Assert.Equal(10, account.Ledger.Acquired);
        Assert.Equal(10, account.Ledger.TotalEverywhere);
        Assert.True(account.Ledger.IsConserved);
    }
}

/// <summary>Whether a manual chest-to-chest haul may be started at all
/// (#381).</summary>
public sealed class GunnarHaulRequestTests
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

    private static HaulRequest Check(
        ContainerUse source = ContainerUse.Both,
        ContainerUse destination = ContainerUse.Both,
        string sourceKey = "chest-a",
        string destinationKey = "chest-b",
        List<ManifestLine>? available = null,
        IReadOnlyCollection<string>? filter = null,
        float carry = 100f,
        CartCapacity cart = default) =>
        HaulRequestGate.Check(
            source, destination, sourceKey, destinationKey,
            available ?? Holds(("Stone", 40, 2f)), filter, Room(carry), cart);

    [Fact]
    public void A_source_that_only_permits_depositing_refuses_the_take_and_says_which_way_round()
    {
        HaulRequest request = Check(source: ContainerUse.Deposit);

        Assert.Equal(ManifestRefusal.SourceNotEnabledForTaking, request.Refusal);
        Assert.Contains("not for taking them out", request.Detail);
    }

    [Fact]
    public void A_destination_that_only_permits_taking_refuses_the_deposit_and_says_which_way_round()
    {
        HaulRequest request = Check(destination: ContainerUse.Take);

        Assert.Equal(ManifestRefusal.DestinationNotEnabledForDepositing, request.Refusal);
        Assert.Contains("not for putting them in", request.Detail);
    }

    [Fact]
    public void A_container_nobody_enabled_is_off_and_off_is_the_default()
    {
        Assert.Equal(ContainerUse.Off, default(ContainerUse));
        Assert.Equal(ManifestRefusal.SourceNotEnabledForTaking, Check(source: ContainerUse.Off).Refusal);
        Assert.Contains("not enabled for helpers at all", Check(source: ContainerUse.Off).Detail);
    }

    [Fact]
    public void There_is_no_such_thing_as_the_nearest_chest()
    {
        Assert.Equal(ManifestRefusal.NoSource, Check(sourceKey: "").Refusal);
        Assert.Contains("nearest", Check(sourceKey: "").Detail);
        Assert.Equal(ManifestRefusal.NoDestination, Check(destinationKey: "").Refusal);
        Assert.Equal(ManifestRefusal.SameContainer, Check(destinationKey: "chest-a").Refusal);
    }

    [Fact]
    public void A_filter_takes_only_what_was_asked_for()
    {
        HaulRequest request = Check(
            available: Holds(("Stone", 10, 2f), ("Wood", 10, 1f), ("Coins", 99, 0.1f)),
            filter: new[] { "Wood" });

        Assert.False(request.IsRefused);
        Assert.Single(request.Wanted);
        Assert.Equal("Wood", request.Wanted[0].ItemPrefab);
        Assert.Equal(10, request.Units);
    }

    [Fact]
    public void A_filter_that_matches_nothing_refuses_rather_than_taking_everything()
    {
        Assert.Equal(
            ManifestRefusal.NothingMatchesTheFilter,
            Check(available: Holds(("Stone", 10, 2f)), filter: new[] { "Silver" }).Refusal);
    }

    [Fact]
    public void No_filter_at_all_means_everything_the_source_holds()
    {
        HaulRequest request = Check(available: Holds(("Stone", 4, 2f), ("Wood", 4, 1f)), filter: null);

        Assert.Equal(2, request.Wanted.Count);
        Assert.Equal(8, request.Units);
    }

    [Fact]
    public void The_order_is_the_same_every_time_the_same_manifest_is_asked_for()
    {
        HaulRequest first = Check(available: Holds(("Wood", 5, 1f), ("Stone", 5, 2f), ("Flint", 5, 1f)));
        HaulRequest second = Check(available: Holds(("Flint", 5, 1f), ("Stone", 5, 2f), ("Wood", 5, 1f)));

        Assert.Equal(Items(first), Items(second));
        Assert.Equal(new[] { "Stone", "Flint", "Wood" }, Items(first));
    }

    [Fact]
    public void No_room_on_his_back_and_no_cart_is_refused_rather_than_planned_as_nothing()
    {
        Assert.Equal(ManifestRefusal.NoRoomAnywhere, Check(carry: 0f, cart: CartCapacity.None).Refusal);
        Assert.False(Check(carry: 0f, cart: new CartCapacity(true, 1, 50)).IsRefused);
    }

    [Fact]
    public void A_line_the_source_could_not_be_read_for_is_skipped_rather_than_guessed_at()
    {
        HaulRequest request = Check(available: Holds(("Stone", 5, 2f), ("Mystery", 5, 0f), ("Ghost", 0, 1f)));

        Assert.Single(request.Wanted);
        Assert.Equal("Stone", request.Wanted[0].ItemPrefab);
    }

    [Fact]
    public void Nothing_here_decides_how_many_trips_it_takes()
    {
        // The guard on the boundary the lead drew: this product says what to
        // move and what it weighs; the shared runtime's tour partitioner says
        // how many trips that is. A second answer here would be one to keep in
        // step forever.
        foreach (System.Reflection.MemberInfo member in typeof(HaulRequest).GetMembers())
        {
            Assert.DoesNotContain("tour", member.Name, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("trip", member.Name, System.StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string[] Items(HaulRequest request)
    {
        var items = new List<string>();
        foreach (ManifestLine line in request.Wanted)
        {
            items.Add(line.ItemPrefab);
        }

        return items.ToArray();
    }
}
