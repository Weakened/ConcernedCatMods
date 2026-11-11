using System;

namespace TheConcernedCat.ConcernedLadders.Domain.Excavation;

/// <summary>The Iron Shovel's identity, recipe and the rules its use follows (#487 slice 3). Game-free; the item
/// registration and the attack-input adapter read these, so the numbers live in one tested place.</summary>
internal static class IronShovel
{
    /// <summary>The new item's prefab id: a save contract from the first build that registers it. Never reuse or
    /// rename it.</summary>
    internal const string PrefabName = "tcc_iron_shovel";

    /// <summary>The vanilla tool cloned for the model, the equip slot, the hand animation, durability and repair. No
    /// game asset ships with the mod.</summary>
    internal const string DonorPrefab = "PickaxeIron";

    internal const string NameToken = "$cl_item_iron_shovel";
    internal const string DescriptionToken = "$cl_item_iron_shovel_description";

    /// <summary>Crafted and repaired at the Forge, level 1. A lead default pending owner confirmation.</summary>
    internal const string Station = "forge";
    internal const int StationLevel = 1;

    /// <summary>Refined Iron (never Iron Scrap) is a requirement, so the game's own known-material discovery unlocks
    /// the recipe only once the player has held Iron. A lead default pending owner confirmation.</summary>
    internal const string IronItem = "Iron";
    internal const int IronAmount = 10;
    internal const string WoodItem = "Wood";
    internal const int WoodAmount = 4;

    /// <summary>True for the shovel's own name token. The adapter identifies an equipped item by this alone.</summary>
    internal static bool IsShovelName(string? sharedName) => string.Equals(sharedName, NameToken, StringComparison.Ordinal);

    /// <summary>Whether this dig can be paid for, asked after the planner has a Ready plan and before any world check
    /// (so a tired player flashes no ward). In order: the shovel is the equipped weapon; the player is free to act
    /// (not mid-swing, dodging, staggered or knocked back); the shovel has the whole durability cost left (a tool
    /// that would break part-way never digs); the player has the stamina. Nothing is ever charged here.</summary>
    internal static ExcavationCommitRefusal CanPay(bool shovelEquipped, bool ready, bool usesDurability, float durability,
        float durabilityCost, bool haveStamina)
    {
        if (!shovelEquipped) return ExcavationCommitRefusal.NoShovel;
        if (!ready) return ExcavationCommitRefusal.NotReady;
        if (usesDurability && !(LadderRules.Finite(durabilityCost) && durability >= durabilityCost && durability > 0f))
        {
            return ExcavationCommitRefusal.ShovelWorn;
        }
        if (!haveStamina) return ExcavationCommitRefusal.NotEnoughStamina;
        return ExcavationCommitRefusal.None;
    }

    /// <summary>The shovel's durability after a committed dig: the cost taken once, never below zero.</summary>
    internal static float DurabilityAfter(float durability, float durabilityCost) =>
        LadderRules.Finite(durability) && LadderRules.Finite(durabilityCost) ? Math.Max(0f, durability - Math.Max(0f, durabilityCost)) : 0f;

    /// <summary>The English description, built from the same numbers the gate charges so it cannot drift (#551: the
    /// player sizes the hole, and the cost follows the ground it removes).</summary>
    internal static string Description() =>
        "Digs a level-floored basement hole in ground you can see, within " + F0(ExcavationLimits.Reach) + " m. " +
        "Scroll to choose its size (" + SizeList() + " m), and attack to dig it. " +
        "A hole lines up with the last holes you dug beside it, so rows stay straight. " +
        "Ground objects in the way - stones, branches, bushes, rocks, stumps - are picked or cleared. " +
        "Press secondary attack twice to fill your last hole back in. " +
        "Costs " + F0(DigCost.BaseStamina) + " stamina plus a little per cubic metre (at most " + F0(DigCost.MaximumStamina) + "), and durability by the " +
        "ground removed, charged once when a dig goes ahead. Filling a hole back in is free. Never removes anything built.";

    /// <summary>Key-hint labels for the game's own key-hint panel (#487 slice 4; the dig session since #551).</summary>
    internal const string DigHintToken = "$cl_hint_shovel_dig";
    internal const string SizeHintToken = "$cl_hint_shovel_size";
    internal const string DigHintText = "Dig";
    internal const string SizeHintText = "Fill in (press twice)";

    /// <summary>The sizes, as the description lists them: "1x1x1, 2x2x1, ... 4x4x4".</summary>
    private static string SizeList() => string.Join(", ", System.Array.ConvertAll(DigSizes.All, size => size.Name));

    private static string F0(float value) => value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Bounds shared by the shovel and the read-only console plan.</summary>
internal static class ExcavationLimits
{
    /// <summary>How far from the player's eyes the aimed ground may be. #487 used the game's own building reach (5 m);
    /// the owner's playtest found it too short ("I can't place longer holes than 3 m since it starts to say too far"), and
    /// with ready-made sizes (#574) the aim falls anywhere inside a hole up to 4 m across, so it is 8 m. Lead default.</summary>
    internal const float Reach = 8f;

    /// <summary>The aim ray's length, as the game's own placement ray.</summary>
    internal const float AimRayLength = 50f;
}
