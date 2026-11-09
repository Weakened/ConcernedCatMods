using System;

namespace TheConcernedCat.ConcernedLadders.Domain.Excavation;

/// <summary>What one committed dig costs (#487 slice 3). A balance choice and a <b>lead default pending owner
/// confirmation</b> (EXCAVATION.md section 7.3). The large mode lowers four times the floor area to twice the depth,
/// so it costs four times the durability and twice the stamina: still one stamina bar on a fed character, and far
/// less than the pickaxe strikes the same hole takes.</summary>
internal readonly struct ExcavationCost
{
    private ExcavationCost(float stamina, float durability)
    {
        Stamina = stamina;
        Durability = durability;
    }

    /// <summary>Stamina, before the world's stamina modifier (the game's own UseStamina applies it).</summary>
    internal float Stamina { get; }

    /// <summary>Durability, before the world's durability modifier.</summary>
    internal float Durability { get; }

    internal static ExcavationCost For(ExcavationMode mode) => mode switch
    {
        ExcavationMode.Small => new ExcavationCost(20f, 4f),
        ExcavationMode.Large => new ExcavationCost(40f, 16f),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}

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

    /// <summary>Which hole one press asks for: the primary attack digs the small one, the secondary attack the large
    /// one. Both pressed in the same input step is ambiguous and asks for nothing, as does neither. A press is the
    /// game's own edge (true for one input step), so holding the button never asks twice.</summary>
    internal static bool ModeFor(bool primaryPressed, bool secondaryPressed, out ExcavationMode mode)
    {
        mode = ExcavationMode.Small;
        if (primaryPressed == secondaryPressed) return false;
        mode = primaryPressed ? ExcavationMode.Small : ExcavationMode.Large;
        return true;
    }

    /// <summary>The durability a dig takes, after the world's durability modifier (the game multiplies every tool drain
    /// by it). A modifier that is not a finite non-negative number gives NaN, which <see cref="CanPay"/> refuses.</summary>
    internal static float DurabilityCost(ExcavationMode mode, float durabilityRate) =>
        LadderRules.Finite(durabilityRate) && durabilityRate >= 0f ? ExcavationCost.For(mode).Durability * durabilityRate : float.NaN;

    /// <summary>The stamina the player must have for a dig, after the world's stamina modifier (the game's UseStamina
    /// multiplies by it, so the check must too). NaN for a modifier that is not a finite non-negative number.</summary>
    internal static float StaminaNeeded(ExcavationMode mode, float staminaRate) =>
        LadderRules.Finite(staminaRate) && staminaRate >= 0f ? ExcavationCost.For(mode).Stamina * staminaRate : float.NaN;

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

    /// <summary>The English description, built from the same numbers the gate charges so it cannot drift.</summary>
    internal static string Description()
    {
        ExcavationCost small = ExcavationCost.For(ExcavationMode.Small);
        ExcavationCost large = ExcavationCost.For(ExcavationMode.Large);
        return "Digs a measured, level-floored basement hole where you look, within " + F0(ExcavationLimits.Reach) + " m. " +
               "Attack: " + ExcavationReport.ModeName(ExcavationMode.Small) + " (" + F0(small.Stamina) + " stamina, " + F0(small.Durability) + " durability). " +
               "Secondary attack: " + ExcavationReport.ModeName(ExcavationMode.Large) + " (" + F0(large.Stamina) + " stamina, " + F0(large.Durability) + " durability). " +
               "Charged once, only when a dig goes ahead. Never removes rocks, trees or anything built.";
    }

    private static string F0(float value) => value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Bounds shared by the shovel and the read-only console plan.</summary>
internal static class ExcavationLimits
{
    /// <summary>How far from the player the aimed terrain may be. Slice 3 keeps slice 2's harness bound; the full
    /// reach and targeting rule (under a floor, from a ladder) belongs to the preview slice.</summary>
    internal const float Reach = 10f;
}
