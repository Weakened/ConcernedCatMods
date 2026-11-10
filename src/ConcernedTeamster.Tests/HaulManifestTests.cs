using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;

namespace ConcernedTeamster.Tests;

/// <summary>The manifest of a manual chest-to-chest haul (#381 item 4): on-foot
/// capacity, the number of tours, the loading order and the cart figure, worked
/// out before anything moves - and the cargo filter it is built from.</summary>
public sealed class HaulManifestTests
{
    // A vanilla player's 300 kg plus Megingjord's 150 (D17), and a full inventory's 32 slots.
    private static readonly HaulCapacity Belted = new HaulCapacity(450f, 32);

    private static HaulCargo Stone(int inSource, int? asked = null) => new HaulCargo("Stone", 2f, 50, asked, inSource);

    private static HaulCargo Wood(int inSource, int? asked = null) => new HaulCargo("Wood", 2f, 50, asked, inSource);

    private static HaulManifest Plan(HaulCapacity capacity, params HaulCargo[] cargo) =>
        Planned(capacity, CartCapacity.None, cargo);

    /// <summary>The runtime's path: <c>HaulRequestGate</c> admits the haul between
    /// two marked chests and sets the loading order, then the planner splits it.
    /// </summary>
    internal static HaulManifest Planned(
        HaulCapacity capacity, CartCapacity cart, HaulCargo[]? cargo,
        ContainerUse source = ContainerUse.Take, ContainerUse destination = ContainerUse.Deposit)
    {
        var named = new List<string>();
        foreach (HaulCargo line in cargo ?? new HaulCargo[0])
        {
            named.Add(line.Item);
        }

        HaulRequest admitted = HaulRequestGate.Check(
            source, destination, "source-chest", "destination-chest", HaulManifestPlanner.Offered(cargo), named,
            CarryBudget.From(new CarryFacts(capacity.FreeKilograms, 0f, 0f, beltEquipped: false), CollectionLimits.Default),
            cart);
        return HaulManifestPlanner.Plan(admitted, cargo, capacity, cart);
    }

    [Fact]
    public void AllTheStoneInAChestIsPlannedInLoadsOfWhatHeCanCarry()
    {
        HaulManifest manifest = Plan(Belted, Stone(300));

        Assert.True(manifest.IsPlanned);
        Assert.Equal(2, manifest.Tours.Count);
        Assert.Equal(225, manifest.Tours[0].Units);
        Assert.Equal(75, manifest.Tours[1].Units);
        Assert.Equal(300, manifest.UnitsPlanned);
        Assert.Null(manifest.CartTours);
    }

    [Fact]
    public void ACountIsHonouredAndNeverExceedsWhatTheSourceHolds()
    {
        Assert.Equal(40, Plan(Belted, Stone(300, 40)).UnitsPlanned);

        HaulManifest shortOf = Plan(Belted, Stone(30, 40));
        Assert.Equal(30, shortOf.UnitsPlanned);
        Assert.Equal(10, shortOf.Cargo[0].UnitsShort);
        Assert.Contains("10 fewer", shortOf.Describe());
    }

    [Fact]
    public void FreeSlotsLimitATourAsWellAsWeight()
    {
        HaulManifest manifest = Plan(new HaulCapacity(450f, 2), Stone(300));

        Assert.Equal(3, manifest.Tours.Count);
        Assert.All(manifest.Tours, tour => Assert.True(tour.Slots <= 2));
        Assert.Equal(100, manifest.Tours[0].Units);
    }

    [Fact]
    public void TheLoadingOrderIsTheRequestGatesHeaviestFirstThenByName()
    {
        // 300 kg is 150 two-kilogram units a tour; the gate puts equal weights in name order.
        HaulManifest manifest = Plan(new HaulCapacity(300f, 32), Wood(500, 100), Stone(500, 100));

        Assert.Equal(2, manifest.Tours.Count);
        Assert.Equal(new[] { "Stone", "Wood" }, Items(manifest.Tours[0]));
        Assert.Equal(100, manifest.Tours[0].Loads[0].Units);
        Assert.Equal(50, manifest.Tours[0].Loads[1].Units);
        Assert.Equal(new[] { "Wood" }, Items(manifest.Tours[1]));
        Assert.Equal(50, manifest.Tours[1].Loads[0].Units);

        // Heavier first whatever order the player named them in.
        var ore = new HaulCargo("CopperOre", 10f, 30, 10, 10);
        HaulManifest heavy = Plan(new HaulCapacity(300f, 32), Stone(20, 20), ore);
        Assert.Equal(new[] { "CopperOre", "Stone" }, Items(heavy.Tours[0]));
        Assert.Equal("CopperOre", heavy.Cargo[0].Item);
    }

    [Fact]
    public void ThePlannerRunsOnlyWhatTheRequestGateAdmitted()
    {
        HaulCargo[] stone = { Stone(40) };
        Assert.Equal(HaulPlanRefusal.NotAdmitted,
            Planned(Belted, CartCapacity.None, stone, source: ContainerUse.Deposit).Refusal);
        Assert.Contains("not for taking them out",
            Planned(Belted, CartCapacity.None, stone, source: ContainerUse.Deposit).Describe());
        Assert.Equal(HaulPlanRefusal.NotAdmitted,
            Planned(Belted, CartCapacity.None, stone, destination: ContainerUse.Off).Refusal);
        Assert.Equal(HaulPlanRefusal.NotAdmitted,
            HaulManifestPlanner.Plan(null, stone, Belted, CartCapacity.None).Refusal);

        // An admitted request for other cargo than the cargo given is never planned.
        HaulRequest forWood = HaulRequestGate.Check(
            ContainerUse.Take, ContainerUse.Deposit, "a", "b", HaulManifestPlanner.Offered(new[] { Wood(40) }),
            new[] { "Wood" }, CarryBudget.From(new CarryFacts(450f, 0f, 0f, false), CollectionLimits.Default),
            CartCapacity.None);
        Assert.Equal(HaulPlanRefusal.NotAdmitted, HaulManifestPlanner.Plan(forWood, stone, Belted, CartCapacity.None).Refusal);
    }

    [Fact]
    public void EveryTourStaysWithinBothLimitsAndEveryUnitIsPlannedExactlyOnce()
    {
        float[] weights = { 0.1f, 0.5f, 1f, 2f, 7.5f, 12f };
        int[] stacks = { 1, 10, 20, 50, 100 };
        int[] slots = { 1, 3, 8, 32 };
        foreach (float weight in weights)
        {
            foreach (int stack in stacks)
            {
                foreach (int free in slots)
                {
                    var heavy = new HaulCargo("CopperOre", weight, stack, null, 173);
                    var light = new HaulCargo("Resin", 0.3f, 50, 61, 90);
                    var capacity = new HaulCapacity(150f, free);
                    HaulManifest manifest = Plan(capacity, heavy, light);
                    if (!manifest.IsPlanned)
                    {
                        Assert.Contains(manifest.Refusal, new[] { HaulPlanRefusal.NoCarryRoom, HaulPlanRefusal.TooManyTours });
                        continue;
                    }

                    Assert.Equal(173, manifest.PlannedOf("CopperOre"));
                    Assert.Equal(61, manifest.PlannedOf("Resin"));
                    Assert.All(manifest.Tours, tour =>
                    {
                        Assert.True(tour.Kilograms <= 150.0001, "a tour over the carry limit");
                        Assert.True(tour.Slots <= free, "a tour over the free slots");
                        Assert.True(tour.Units > 0, "an empty tour");
                    });
                }
            }
        }
    }

    [Fact]
    public void ACartIsFilledBySlotsAloneAndOnlyWhenOneIsGiven()
    {
        HaulManifest manifest = Planned(Belted, new CartCapacity(true, 18, 50), new[] { Stone(2000) });

        // 2000 stone on foot is nine loads of 225; 18 slots of 50 is 900 a cart round.
        Assert.Equal(9, manifest.Tours.Count);
        Assert.Equal(3, manifest.CartTours);
        Assert.Contains("a cart of that room would need 3 tour(s)", manifest.Describe());

        Assert.Null(Planned(Belted, CartCapacity.None, new[] { Stone(2000) }).CartTours);
        Assert.Null(Planned(Belted, new CartCapacity(false, 18, 50), new[] { Stone(2000) }).CartTours);
    }

    [Fact]
    public void NothingNamedNothingInTheSourceOrNoRoomPlansNoTour()
    {
        Assert.Equal(HaulPlanRefusal.NoCargo, Plan(Belted).Refusal);
        Assert.Equal(HaulPlanRefusal.NoCargo, HaulManifestPlanner.Plan(null, null, Belted, CartCapacity.None).Refusal);
        // Nothing of it in the source, or no room at all: the request gate's answer.
        Assert.Equal(HaulPlanRefusal.NotAdmitted, Plan(Belted, Stone(0)).Refusal);
        Assert.Contains("nothing that matches", Plan(Belted, Stone(0)).Describe());
        Assert.Equal(HaulPlanRefusal.NotAdmitted, Plan(default, Stone(10)).Refusal);
        Assert.Equal(HaulPlanRefusal.NoCarryRoom, Plan(new HaulCapacity(1.5f, 32), Stone(10)).Refusal);
        Assert.Equal(HaulPlanRefusal.NoCarryRoom, Plan(new HaulCapacity(450f, 0), Stone(10)).Refusal);
        Assert.Empty(Plan(default, Stone(10)).Tours);
    }

    [Fact]
    public void ALineThatCanNeverFitRefusesTheWholeHaulRatherThanLeavingItBehind()
    {
        var anvil = new HaulCargo("Anvil", 500f, 1, 1, 1);
        HaulManifest manifest = Plan(Belted, Stone(10), anvil);

        Assert.Equal(HaulPlanRefusal.NoCarryRoom, manifest.Refusal);
        Assert.Equal("Anvil", manifest.Detail);
        Assert.Empty(manifest.Tours);
    }

    [Fact]
    public void UnreadableOrMalformedCargoIsRefused()
    {
        Assert.Equal(HaulPlanRefusal.CargoUnreadable, Plan(Belted, new HaulCargo("Stone", 0f, 50, null, 10)).Refusal);
        Assert.Equal(HaulPlanRefusal.CargoUnreadable, Plan(Belted, new HaulCargo("Stone", float.NaN, 50, null, 10)).Refusal);
        Assert.Equal(HaulPlanRefusal.CargoUnreadable, Plan(Belted, new HaulCargo("Stone", 2f, 0, null, 10)).Refusal);
        Assert.Equal(HaulPlanRefusal.CargoNotNamed, Plan(Belted, new HaulCargo("Sto ne", 2f, 50, null, 10)).Refusal);
        Assert.Equal(HaulPlanRefusal.CargoNotNamed, Plan(Belted, new HaulCargo("Stone", 2f, 50, 0, 10)).Refusal);
        Assert.Equal(HaulPlanRefusal.DuplicateCargo, Plan(Belted, Stone(10), Stone(10)).Refusal);
        Assert.Equal(HaulPlanRefusal.TooManyCargo, Plan(Belted, Stone(1), Wood(1),
            new HaulCargo("Resin", 0.3f, 50, null, 1), new HaulCargo("Flint", 1f, 50, null, 1),
            new HaulCargo("Coal", 1f, 50, null, 1)).Refusal);
    }

    [Fact]
    public void AHaulTooBigForOneOrderIsRefusedNotWalkedForEver()
    {
        HaulManifest manifest = Plan(new HaulCapacity(2f, 32), Stone(HaulManifestPlanner.MostTours + 1));

        Assert.Equal(HaulPlanRefusal.TooManyTours, manifest.Refusal);
        Assert.True(Plan(new HaulCapacity(2f, 32), Stone(HaulManifestPlanner.MostTours)).IsPlanned);
    }

    [Fact]
    public void EveryRefusalHasASentence()
    {
        foreach (HaulPlanRefusal refusal in System.Enum.GetValues(typeof(HaulPlanRefusal)))
        {
            Assert.False(string.IsNullOrWhiteSpace(HaulManifestPlanner.Describe(refusal)));
        }
    }

    [Theory]
    [InlineData("Stone", "Stone", null)]
    [InlineData("Stone:40", "Stone", 40)]
    [InlineData("CopperOre:1", "CopperOre", 1)]
    public void TheFilterReadsExactNamesAndOptionalCounts(string word, string item, int? count)
    {
        Assert.Equal(HaulPlanRefusal.None, HaulCargoFilter.TryParse(new[] { "haul", word }, 1, out var lines, out _));
        Assert.Single(lines);
        Assert.Equal(item, lines[0].Key);
        Assert.Equal(count, lines[0].Value);
    }

    [Theory]
    [InlineData("Stone:")]
    [InlineData("Stone:0")]
    [InlineData("Stone:-3")]
    [InlineData("Stone:+3")]
    [InlineData("Stone:1.5")]
    [InlineData("St*ne")]
    [InlineData("*")]
    [InlineData(":5")]
    [InlineData("")]
    public void TheFilterRefusesAnythingThatIsNotAnExactNameAndCount(string word)
    {
        Assert.Equal(HaulPlanRefusal.CargoNotNamed, HaulCargoFilter.TryParse(new[] { "haul", word }, 1, out _, out _));
    }

    [Fact]
    public void TheFilterKeepsTheOrderAndRefusesRepeatsTooManyAndNothing()
    {
        Assert.Equal(HaulPlanRefusal.None,
            HaulCargoFilter.TryParse(new[] { "haul", "plan", "Wood:5", "Stone" }, 2, out var lines, out _));
        Assert.Equal(new[] { "Wood", "Stone" }, new[] { lines[0].Key, lines[1].Key });

        Assert.Equal(HaulPlanRefusal.DuplicateCargo,
            HaulCargoFilter.TryParse(new[] { "haul", "Stone", "Stone:3" }, 1, out _, out _));
        Assert.Equal(HaulPlanRefusal.TooManyCargo,
            HaulCargoFilter.TryParse(new[] { "haul", "A", "B", "C", "D", "E" }, 1, out _, out _));
        Assert.Equal(HaulPlanRefusal.NoCargo, HaulCargoFilter.TryParse(new[] { "haul" }, 1, out _, out _));
        Assert.Equal(HaulPlanRefusal.NoCargo, HaulCargoFilter.TryParse(null, 1, out _, out _));
    }

    private static string[] Items(HaulTourPlan tour)
    {
        var items = new List<string>();
        foreach (HaulLoad load in tour.Loads)
        {
            items.Add(load.Item);
        }

        return items.ToArray();
    }
}
