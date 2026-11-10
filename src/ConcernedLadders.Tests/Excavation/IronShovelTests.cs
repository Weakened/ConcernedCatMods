using TheConcernedCat.ConcernedLadders.Domain.Excavation;

namespace TheConcernedCat.ConcernedLadders.Tests.Excavation;

/// <summary>#487 slice 3: the Iron Shovel's identity, costs, press rule and the can-pay gate. The runtime adapter calls
/// these and adds only game reads; recipe visibility, crafting, equip, repair and every dig are OWNER GO-AROUND
/// PENDING.</summary>
public sealed class IronShovelTests
{
    [Fact]
    public void TheIdentityIsASaveContract()
    {
        // Changing either breaks every shovel already crafted; the token is how the adapter recognises the item.
        Assert.Equal("tcc_iron_shovel", IronShovel.PrefabName);
        Assert.Equal("$cl_item_iron_shovel", IronShovel.NameToken);
        Assert.Equal("PickaxeIron", IronShovel.DonorPrefab);
        Assert.True(IronShovel.IsShovelName("$cl_item_iron_shovel"));
        Assert.False(IronShovel.IsShovelName("$item_pickaxe_iron"));
        Assert.False(IronShovel.IsShovelName("$CL_ITEM_IRON_SHOVEL"));
        Assert.False(IronShovel.IsShovelName(null));
    }

    [Fact]
    public void TheRecipeNeedsRefinedIronAtTheForge()
    {
        Assert.Equal("Iron", IronShovel.IronItem);
        Assert.NotEqual("IronScrap", IronShovel.IronItem);
        Assert.True(IronShovel.IronAmount > 0);
        Assert.Equal("forge", IronShovel.Station);
        Assert.Equal(1, IronShovel.StationLevel);
    }

    [Fact]
    public void TheLargeModeCostsMoreThanTheSmallOne()
    {
        ExcavationCost small = ExcavationCost.For(ExcavationMode.Small);
        ExcavationCost large = ExcavationCost.For(ExcavationMode.Large);
        Assert.Equal(20f, small.Stamina);
        Assert.Equal(4f, small.Durability);
        Assert.Equal(40f, large.Stamina);
        Assert.Equal(16f, large.Durability);
        Assert.Throws<ArgumentOutOfRangeException>(() => ExcavationCost.For((ExcavationMode)7));
    }

    private static ExcavationCommitRefusal Pay(bool equipped = true, bool ready = true, bool usesDurability = true,
        float durability = 100f, float cost = 4f, bool stamina = true) =>
        IronShovel.CanPay(equipped, ready, usesDurability, durability, cost, stamina);

    [Fact]
    public void AnEquippedReadyShovelWithEnoughOfEverythingPays()
    {
        Assert.Equal(ExcavationCommitRefusal.None, Pay());
        Assert.Equal(ExcavationCommitRefusal.None, Pay(durability: 4f, cost: 4f));
    }

    [Fact]
    public void EachCostRefusesInOrder()
    {
        Assert.Equal(ExcavationCommitRefusal.NoShovel, Pay(equipped: false, ready: false, durability: 0f, stamina: false));
        Assert.Equal(ExcavationCommitRefusal.NotReady, Pay(ready: false, durability: 0f, stamina: false));
        Assert.Equal(ExcavationCommitRefusal.ShovelWorn, Pay(durability: 3.99f, stamina: false));
        Assert.Equal(ExcavationCommitRefusal.NotEnoughStamina, Pay(stamina: false));
    }

    [Fact]
    public void ABrokenOrUnreadableShovelNeverDigs()
    {
        // A world with durability off (rate 0) makes the cost 0, but a broken tool still does not dig, as in the game.
        Assert.Equal(ExcavationCommitRefusal.ShovelWorn, Pay(durability: 0f, cost: 0f));
        Assert.Equal(ExcavationCommitRefusal.ShovelWorn, Pay(durability: float.NaN));
        Assert.Equal(ExcavationCommitRefusal.ShovelWorn, Pay(cost: float.NaN));
        Assert.Equal(ExcavationCommitRefusal.ShovelWorn, Pay(cost: float.PositiveInfinity, durability: float.PositiveInfinity));
        // A tool the game does not wear out ignores durability.
        Assert.Equal(ExcavationCommitRefusal.None, Pay(usesDurability: false, durability: 0f, cost: float.NaN));
    }

    [Fact]
    public void TheWorldModifiersScaleTheCostOrRefuse()
    {
        Assert.Equal(4f, IronShovel.DurabilityCost(ExcavationMode.Small, 1f));
        Assert.Equal(8f, IronShovel.DurabilityCost(ExcavationMode.Large, 0.5f));
        Assert.Equal(0f, IronShovel.DurabilityCost(ExcavationMode.Small, 0f));
        Assert.True(float.IsNaN(IronShovel.DurabilityCost(ExcavationMode.Small, -1f)));
        Assert.True(float.IsNaN(IronShovel.DurabilityCost(ExcavationMode.Small, float.NaN)));
        Assert.Equal(60f, IronShovel.StaminaNeeded(ExcavationMode.Large, 1.5f));
        Assert.True(float.IsNaN(IronShovel.StaminaNeeded(ExcavationMode.Small, float.PositiveInfinity)));
    }

    [Fact]
    public void TheChargeTakesTheCostOnceAndNeverGoesNegative()
    {
        Assert.Equal(96f, IronShovel.DurabilityAfter(100f, 4f));
        Assert.Equal(0f, IronShovel.DurabilityAfter(3f, 4f));
        Assert.Equal(100f, IronShovel.DurabilityAfter(100f, -4f));
        Assert.Equal(0f, IronShovel.DurabilityAfter(float.NaN, 4f));
        Assert.Equal(0f, IronShovel.DurabilityAfter(100f, float.NaN));
    }

    [Fact]
    public void TheDescriptionStatesTheNumbersTheGateCharges()
    {
        string text = IronShovel.Description();
        Assert.Contains("2x2x2 m (20 stamina, 4 durability)", text);
        Assert.Contains("4x4x4 m (40 stamina, 16 durability)", text);
        Assert.Contains("within 5 m", text);
        Assert.Contains("Attack: dig the size shown", text);
        Assert.Contains("Secondary attack: switch between", text);
        Assert.Contains("outline shows", text);
        Assert.Contains("Charged once", text);
        string cost = ExcavationReport.Cost(ExcavationMode.Large);
        Assert.Contains("40 stamina and 16 durability", cost);
    }

    [Fact]
    public void TheCostRefusalsSayWhatToDoAndThatNothingWasCharged()
    {
        ExcavationPlan plan = ExcavationPlanner.Plan(new VanillaTerrainModel((_, _) => 50f).Sample(ExcavationMode.Large, 0, 0), 0, 0, null);
        string worn = ExcavationReport.Commit(ExcavationCommitRefusal.ShovelWorn, ExcavationReadFault.None, plan, null);
        Assert.Contains("16 durability", worn);
        Assert.Contains("forge", worn);
        Assert.Contains("Nothing is changed or charged", worn);
        string tired = ExcavationReport.Commit(ExcavationCommitRefusal.NotEnoughStamina, ExcavationReadFault.None, plan, null);
        Assert.Contains("40 stamina", tired);
        Assert.Contains("Nothing is changed or charged", tired);
    }
}
