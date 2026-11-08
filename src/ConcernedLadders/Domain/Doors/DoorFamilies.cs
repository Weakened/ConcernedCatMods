using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedLadders.Domain.Doors;

/// <summary>How a native prefab carrying a <c>Door</c> component (or a hammer piece in the Doors usage tag, or named like a door) is treated by
/// the door-family pieces (CL-004 #485, CL-005 #486). Every classified prefab has exactly one class, so no family can
/// disappear silently: an exclusion is a recorded decision with its reason.</summary>
internal enum DoorDonorClass
{
    /// <summary>A player-buildable native door with open/close state: it gets a trapdoor (and later a cellar door).</summary>
    Family,

    /// <summary>Has a Door component but is not in the hammer's piece table in the installed game (dungeon and
    /// location doors, or a recipe the hammer does not offer).</summary>
    NotBuildable,

    /// <summary>Buildable and a Door, but its native behaviour is not a hinged leaf (a hoisted drawbridge with a crush
    /// area), so cloning its door semantics into a floor hatch would misrepresent it.</summary>
    SpecialSemantics,

    /// <summary>Buildable and a Door, but a window covering whose material family another donor already carries.</summary>
    WindowCovering,

    /// <summary>A hammer piece with no Door component (in the hammer's Doors usage tag, or named like a door): there is
    /// no open/closed state to reuse.</summary>
    NoDoorState,
}

/// <summary>The native <c>WearNTear.MaterialType</c> values, by the installed game's order (1.0.21). Mirrored so the
/// pinned matrix is readable and testable without the game assembly.</summary>
internal enum DonorMaterial
{
    Wood = 0,
    Stone = 1,
    Iron = 2,
    HardWood = 3,
    Marble = 4,
    Ashstone = 5,
    Ancient = 6,
    Ice = 7,
    Timberwood = 8,
}

/// <summary>One native resource requirement, exactly as the donor's <c>Piece.m_resources</c> entry reads in the
/// installed game. Every buildable donor recovers its resources (<c>m_recover</c> true).</summary>
internal readonly struct DoorCost
{
    internal DoorCost(string item, int amount)
    {
        Item = item;
        Amount = amount;
    }

    /// <summary>The ItemDrop prefab name (not a localized token).</summary>
    internal string Item { get; }

    internal int Amount { get; }
}

/// <summary>A classified native door prefab. <see cref="Reason"/> is empty only for <see cref="DoorDonorClass.Family"/>.</summary>
internal sealed class DoorDonorDecision
{
    internal DoorDonorDecision(string donor, DoorDonorClass kind, string reason)
    {
        Donor = donor;
        Kind = kind;
        Reason = reason;
    }

    internal string Donor { get; }

    internal DoorDonorClass Kind { get; }

    internal string Reason { get; }
}

/// <summary>A native player-buildable door and the trapdoor that matches it. The donor values are pinned from the
/// installed game (Valheim 1.0.21, bundle <c>c4210710</c>, read-only; see DOOR_DONOR_MATRIX.md) so the piece
/// adapter can check its clone against them and report a donor that changed, instead of guessing.</summary>
internal sealed class DoorFamily
{
    internal DoorFamily(string donor, string donorToken, string donorEnglish, string family, string englishTrapdoor,
        string station, DoorCost[] costs, float health, DonorMaterial material, bool burnable, float swingSeconds,
        string openEffect, string closeEffect)
    {
        Donor = donor;
        DonorToken = donorToken;
        DonorEnglish = donorEnglish;
        Family = family;
        EnglishTrapdoor = englishTrapdoor;
        Station = station;
        Costs = costs;
        Health = health;
        Material = material;
        Burnable = burnable;
        SwingSeconds = swingSeconds;
        OpenEffect = openEffect;
        CloseEffect = closeEffect;
    }

    /// <summary>The native prefab name, e.g. <c>wood_door</c>.</summary>
    internal string Donor { get; }

    /// <summary>The donor's own <c>Piece.m_name</c> / <c>Door.m_name</c> token.</summary>
    internal string DonorToken { get; }

    /// <summary>The donor's English display name in the installed localization.</summary>
    internal string DonorEnglish { get; }

    /// <summary>The short family key every door-family piece of this donor shares (trapdoor now, cellar door next).</summary>
    internal string Family { get; }

    /// <summary>The trapdoor's English Title Case name.</summary>
    internal string EnglishTrapdoor { get; }

    /// <summary>The donor's crafting station prefab (<c>piece_workbench</c>, <c>forge</c>, <c>blackforge</c>).</summary>
    internal string Station { get; }

    /// <summary>The donor's requirements, in its own order. The trapdoor costs the same (lead default pending owner
    /// confirmation: one 2 m door leaf for one 2 m hatch leaf), so native discovery, which follows the requirements
    /// and the station, matches the donor exactly.</summary>
    internal IReadOnlyList<DoorCost> Costs { get; }

    /// <summary>The donor's <c>WearNTear.m_health</c>.</summary>
    internal float Health { get; }

    internal DonorMaterial Material { get; }

    internal bool Burnable { get; }

    /// <summary>The donor animator's opening clip length, seconds (0.5 light, 1.0 medium, 2.0 heavy). The hatch's
    /// lid swings over the same time, so the hidden native animator that gates <c>Door.CanInteract</c> and the
    /// visible lid finish together.</summary>
    internal float SwingSeconds { get; }

    internal string OpenEffect { get; }

    internal string CloseEffect { get; }

    /// <summary>The stable saved prefab ID. A save contract once shipped: never rename.</summary>
    internal string TrapdoorPrefab => DoorFamilies.TrapdoorPrefix + Family;

    /// <summary>The localization token (without <c>$</c>) for the trapdoor's name.</summary>
    internal string TrapdoorToken => "cl_trapdoor_" + Family;
}

/// <summary>The installed-game door census and the families built from it. Game-free so the mapping is tested.</summary>
internal static class DoorFamilies
{
    /// <summary>The game version the matrix was read from (<c>Version.CurrentVersion</c>).</summary>
    internal const string GameVersion = "1.0.21";

    internal const string TrapdoorPrefix = "tcc_trapdoor_";

    private const string Workbench = "piece_workbench";
    private const string Forge = "forge";
    private const string BlackForge = "blackforge";

    /// <summary>Opening clip lengths of the three native door animators the families use.</summary>
    internal const float LightSwing = 0.5f;   // door_animator
    internal const float MediumSwing = 1.0f;  // darkwood_gate_animator
    internal const float HeavySwing = 2.0f;   // Flametal_gate_animator

    /// <summary>Nine families: every native player-buildable door/gate with hinged open/close state.</summary>
    internal static readonly IReadOnlyList<DoorFamily> All = new[]
    {
        new DoorFamily("wood_door", "$piece_wooddoor", "Wood Door", "wood", "Wood Trapdoor",
            Workbench, new[] { new DoorCost("Wood", 4) }, 400f, DonorMaterial.Wood, true, LightSwing,
            "sfx_door_open", "sfx_door_close"),
        new DoorFamily("wood_gate", "$piece_woodgate", "Wood Gate", "wood_gate", "Wood Gate Trapdoor",
            Workbench, new[] { new DoorCost("Wood", 12) }, 400f, DonorMaterial.Wood, true, LightSwing,
            "sfx_door_open", "sfx_door_close"),
        new DoorFamily("wood_fence_gate", "$piece_woodfencegate", "Roundpole Gate", "roundpole", "Roundpole Trapdoor",
            Workbench, new[] { new DoorCost("Wood", 4) }, 400f, DonorMaterial.Wood, true, LightSwing,
            "sfx_door_open", "sfx_door_close"),
        new DoorFamily("iron_grate", "$piece_irongate", "Iron Gate", "iron_gate", "Iron Gate Trapdoor",
            Forge, new[] { new DoorCost("Iron", 4) }, 2000f, DonorMaterial.Iron, false, LightSwing,
            "sfx_metalgate_open", "sfx_metalgate_close"),
        new DoorFamily("darkwood_gate", "$piece_darkwoodgate", "Darkwood Gate", "darkwood", "Darkwood Trapdoor",
            Forge, new[] { new DoorCost("Wood", 16), new DoorCost("Iron", 4), new DoorCost("Tar", 2) }, 1000f,
            DonorMaterial.HardWood, true, MediumSwing, "sfx_darkwood_door_open", "sfx_darkwood_door_close"),
        new DoorFamily("stave_gate", "$piece_stavegate", "Timberwood Gate", "timberwood", "Timberwood Trapdoor",
            Forge, new[] { new DoorCost("Frostwood", 24), new DoorCost("Gold", 4) }, 1000f,
            DonorMaterial.HardWood, true, MediumSwing, "sfx_darkwood_door_open", "sfx_darkwood_door_close"),
        new DoorFamily("piece_hexagonal_door", "$piece_hexagonalgate", "Hexagonal Gate", "hexagonal", "Hexagonal Trapdoor",
            BlackForge, new[] { new DoorCost("YggdrasilWood", 8), new DoorCost("Copper", 8) }, 1500f,
            DonorMaterial.HardWood, true, MediumSwing, "sfx_darkwood_door_open", "sfx_darkwood_door_close"),
        new DoorFamily("ashwood_door", "$piece_ashwood_door", "Ashwood Door", "ashwood", "Ashwood Trapdoor",
            Workbench, new[] { new DoorCost("FlametalNew", 1), new DoorCost("Blackwood", 5) }, 1000f,
            DonorMaterial.Wood, true, MediumSwing, "sfx_darkwood_door_open", "sfx_darkwood_door_close"),
        new DoorFamily("flametal_gate", "$piece_flametalgate", "Flametal Gate", "flametal", "Flametal Trapdoor",
            BlackForge, new[] { new DoorCost("FlametalNew", 16) }, 3000f, DonorMaterial.Iron, false, HeavySwing,
            "sfx_flametalgate_door_open", "sfx_flametalgate_door_close"),
    };

    /// <summary>Every prefab the census found with a Door component (23), every hammer piece in the Doors usage tag
    /// (<c>Piece.m_usage</c> flag 0x40000) without one (3), and the three hammer cloth curtains named <c>hanging_door</c>,
    /// which are in neither. Each appears exactly once.</summary>
    internal static readonly IReadOnlyList<DoorDonorDecision> Census = BuildCensus();

    /// <summary>The nine candidates the published Jötunn 1.0.12 piece list named; each must be a family.</summary>
    internal static readonly IReadOnlyList<string> JotunnCandidates = new[]
    {
        "wood_door", "wood_gate", "wood_fence_gate", "iron_grate", "darkwood_gate", "piece_hexagonal_door",
        "ashwood_door", "flametal_gate", "stave_gate",
    };

    internal static DoorFamily? ByDonor(string donor)
    {
        foreach (DoorFamily family in All)
        {
            if (string.Equals(family.Donor, donor, StringComparison.Ordinal)) { return family; }
        }

        return null;
    }

    internal static DoorFamily? ByTrapdoorPrefab(string prefab)
    {
        foreach (DoorFamily family in All)
        {
            if (string.Equals(family.TrapdoorPrefab, prefab, StringComparison.Ordinal)) { return family; }
        }

        return null;
    }

    internal static DoorDonorDecision? Classify(string donor)
    {
        foreach (DoorDonorDecision decision in Census)
        {
            if (string.Equals(decision.Donor, donor, StringComparison.Ordinal)) { return decision; }
        }

        return null;
    }

    private static IReadOnlyList<DoorDonorDecision> BuildCensus()
    {
        var census = new List<DoorDonorDecision>();
        foreach (DoorFamily family in All) { census.Add(new DoorDonorDecision(family.Donor, DoorDonorClass.Family, string.Empty)); }

        // Every non-family decision below is a lead default pending owner confirmation (2026-11-08).
        census.Add(new DoorDonorDecision("piece_drawbridge", DoorDonorClass.SpecialSemantics,
            "Timberwood Drawbridge: a 4 m x 8 m hoisted bridge (drawbridge2_animator, 1.5/2.2 s) whose open state enables a crush area (DrawBridgeAOE) and rope/weight presentation; its Frostwood material is carried by the Timberwood family."));
        census.Add(new DoorDonorDecision("piece_drawbridge_log", DoorDonorClass.SpecialSemantics,
            "Rustic Drawbridge: the same hoisted-bridge semantics and crush area as piece_drawbridge; its round-log material is carried by the Roundpole family."));
        census.Add(new DoorDonorDecision("wood_window", DoorDonorClass.WindowCovering,
            "Wood Shutter: a 1 m window shutter (window_animator), not a passage; its wood material is carried by the Wood family."));

        foreach (string window in new[] { "Piece_grausten_window_2x2", "Piece_grausten_window_4x2" })
        {
            census.Add(new DoorDonorDecision(window, DoorDonorClass.NoDoorState,
                "A fixed grausten window in the hammer's Doors usage tag with no Door component: it does not open."));
        }

        census.Add(new DoorDonorDecision("crystal_wall_1x1", DoorDonorClass.NoDoorState,
            "A fixed crystal wall in the hammer's Doors usage tag with no Door component: it does not open."));

        foreach (string cloth in new[] { "piece_cloth_hanging_door", "piece_cloth_hanging_door_blue", "piece_cloth_hanging_door_blue2" })
        {
            census.Add(new DoorDonorDecision(cloth, DoorDonorClass.NoDoorState,
                "A hanging cloth curtain, named like a door but in neither the Doors usage tag nor carrying a Door component: no open/closed state, and cloth cannot carry a walking player."));
        }

        census.Add(new DoorDonorDecision("piece_dvergr_wood_door", DoorDonorClass.NotBuildable,
            "Has a Piece recipe (8 YggdrasilWood, 8 Copper, black forge) but is not in the 1.0.21 hammer piece table."));
        foreach (string location in new[]
        {
            "dvergrtown_secretdoor", "dvergrtown_slidingdoor", "Morkborg_gate", "dungeon_sunkencrypt_irongate",
            "dungeon_forestcrypt_door", "MountainKit_wood_gate", "dungeon_queen_door", "Morkhalla_Drawbridge",
            "Morkhalla_jotun_gate", "sunken_crypt_gate",
        })
        {
            census.Add(new DoorDonorDecision(location, DoorDonorClass.NotBuildable,
                "A location or dungeon door, not in the hammer piece table."));
        }

        return census;
    }
}
