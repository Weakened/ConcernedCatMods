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
    public void TheCostFollowsTheGroundRemovedAndTheOldSizesCostAboutWhatTheyDid()
    {
        // About 18 m3 for the old 2x2x2 m hole (9 vertices, 2 m each) and 100 m3 for the old 4x4x4 m one (25 vertices, 4 m).
        Assert.Equal(15.4f, DigCost.Stamina(18f), 3);
        Assert.Equal(2.88f, DigCost.Durability(18f), 3);
        Assert.Equal(40f, DigCost.Stamina(100f), 3);
        Assert.Equal(16f, DigCost.Durability(100f), 3);
        // Stamina is capped so any dig can be paid by a fed character; durability is not, so a big dig wears the shovel.
        Assert.Equal(DigCost.MaximumStamina, DigCost.Stamina(567f), 3);
        Assert.Equal(90.72f, DigCost.Durability(567f), 2);
        Assert.True(DigCost.Stamina(5f) < DigCost.Stamina(50f));
        // Nothing removed, or a volume that is not a number, is never a cost: the gate refuses it.
        Assert.True(float.IsNaN(DigCost.Stamina(0f)));
        Assert.True(float.IsNaN(DigCost.Durability(float.NaN)));
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
        Assert.Equal(16f, DigCost.DurabilityCost(100f, 1f), 3);
        Assert.Equal(8f, DigCost.DurabilityCost(100f, 0.5f), 3);
        Assert.Equal(0f, DigCost.DurabilityCost(100f, 0f), 3);
        Assert.True(float.IsNaN(DigCost.DurabilityCost(100f, -1f)));
        Assert.True(float.IsNaN(DigCost.DurabilityCost(100f, float.NaN)));
        Assert.Equal(60f, DigCost.StaminaNeeded(100f, 1.5f), 3);
        Assert.True(float.IsNaN(DigCost.StaminaNeeded(100f, float.PositiveInfinity)));
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
    public void TheDescriptionStatesTheFlowAndTheNumbersTheGateUses()
    {
        string text = IronShovel.Description();
        Assert.Contains("within 5 m", text);
        Assert.Contains("Attack sets one corner, then the opposite one (1 to 8 m a side)", text);
        Assert.Contains("ready-made 1, 2 or 4 m cube", text);
        Assert.Contains("are picked or cleared", text);
        Assert.Contains("scroll sets the depth (1 to 7 m)", text);
        Assert.Contains("press it twice to fill your last hole back in", text);
        Assert.Contains("Costs 10 stamina", text);
        Assert.Contains("at most 60", text);
        Assert.Contains("Filling a hole back in is free", text);
        Assert.Equal("Set corner / dig", IronShovel.DigHintText);
        Assert.Equal("Back / fill in (press twice)", IronShovel.SizeHintText);
    }
}
