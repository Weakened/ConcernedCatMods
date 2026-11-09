using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedLadders.Domain.Doors;

/// <summary>The cellar-door setting (CL-005 #486). Its own switch, off by default, separate from
/// <see cref="TrapdoorSettings"/> (lead default pending owner confirmation): turning trapdoors on must not also add a
/// second unfinished family whose placed pieces the game deletes when the switch goes off again. Bound by the plugin
/// (slice 2); with it off the piece adapter is never constructed, so nothing is cloned, localized or registered.</summary>
internal static class CellarDoorSettings
{
    internal const string Section = "CellarDoors";

    internal const string Key = "Enabled";

    internal const bool EnabledByDefault = false;

    internal const string Description =
        "UNFINISHED (CL-005): adds the nine sloped cellar doors to the hammer's Building tab, none of it yet tested in game. " +
        "Both leaves open and close together with the door's Use, and wait instead of moving into a player, a creature, " +
        "an item, a cart or another building piece. " +
        "Ladders do not work with them yet and treat them as ordinary pieces. " +
        "Leave this off. Turning it off again, or uninstalling, makes the game delete every placed cellar door the next " +
        "time a world loads, with no refund. In multiplayer every player and the host or server must have the same " +
        "value: a host or server with it off deletes the cellar doors other players place, during the session. " +
        "Restart after changing it.";
}

/// <summary>A native door family's sloped cellar door: the same donor, station, material, health, effects, wards and
/// door state as the family's trapdoor (<see cref="DoorFamily"/>, CL-004's pinned matrix, reused, not copied), with its
/// own stable prefab ID, name and cost.</summary>
internal sealed class CellarDoorFamily
{
    internal CellarDoorFamily(DoorFamily door, string english, DoorCost[] costs)
    {
        Door = door;
        English = english;
        Costs = costs;
    }

    /// <summary>The pinned native family this cellar door is cloned from.</summary>
    internal DoorFamily Door { get; }

    /// <summary>The English Title Case name.</summary>
    internal string English { get; }

    /// <summary>The cellar door's requirements: every one of the donor's, in its order, times
    /// <see cref="CellarDoors.CostMultiplier"/>. The same items as the donor, so native discovery, which follows the
    /// items and the station, matches the donor's exactly.</summary>
    internal IReadOnlyList<DoorCost> Costs { get; }

    /// <summary>The stable saved prefab ID. A save contract once shipped: never rename.</summary>
    internal string Prefab => CellarDoors.Prefix + Door.Family;

    /// <summary>The localization token (without <c>$</c>) for the name.</summary>
    internal string Token => "cl_cellardoor_" + Door.Family;
}

/// <summary>The nine cellar doors (CL-005 #486), one per CL-004 door family, in <see cref="DoorFamilies.All"/>'s order.
/// The census's exclusions (drawbridges, the shutter, the fixed windows, the curtains, the unbuildable and location
/// doors) are CL-004's decisions and carry over unchanged: no cellar door is built from a prefab that is not a family.
/// Game-free so the mapping is tested.</summary>
internal static class CellarDoors
{
    internal const string Prefix = "tcc_cellardoor_";

    /// <summary>Each requirement is the donor's times this (lead default pending owner confirmation): a cellar door
    /// has two leaves, each about a native door leaf, and its frame and cheeks come with them. Higher rather than equal,
    /// because a deconstruction refunds the recipe current at that moment: lowering a cost later only refunds less than
    /// was paid, while raising one later would refund more, and that would mint material.</summary>
    internal const int CostMultiplier = 2;

    internal const string DescriptionToken = "cl_cellardoor_description";

    private static readonly KeyValuePair<string, string>[] Names =
    {
        new KeyValuePair<string, string>("wood", "Wood Cellar Door"),
        new KeyValuePair<string, string>("wood_gate", "Wood Gate Cellar Door"),
        new KeyValuePair<string, string>("roundpole", "Roundpole Cellar Door"),
        new KeyValuePair<string, string>("iron_gate", "Iron Gate Cellar Door"),
        new KeyValuePair<string, string>("darkwood", "Darkwood Cellar Door"),
        new KeyValuePair<string, string>("timberwood", "Timberwood Cellar Door"),
        new KeyValuePair<string, string>("hexagonal", "Hexagonal Cellar Door"),
        new KeyValuePair<string, string>("ashwood", "Ashwood Cellar Door"),
        new KeyValuePair<string, string>("flametal", "Flametal Cellar Door"),
    };

    /// <summary>One cellar door per family. A family with no name here makes the type fail to load, so a tenth family
    /// cannot ship without a deliberate name and cost (a test pins the pairing).</summary>
    internal static readonly IReadOnlyList<CellarDoorFamily> All = Build();

    internal static CellarDoorFamily? ByPrefab(string prefab)
    {
        foreach (CellarDoorFamily cellar in All)
        {
            if (string.Equals(cellar.Prefab, prefab, StringComparison.Ordinal)) { return cellar; }
        }

        return null;
    }

    /// <summary>The English localization the cellar doors will add (tokens without <c>$</c>): one name per family and
    /// the shared description. Open/close hover text is the game's own Door text and is not duplicated.</summary>
    internal static Dictionary<string, string> English()
    {
        var english = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DescriptionToken] = "A sloped 2 m cellar entrance with two hinged leaves. Opens and closes like its door. Digs nothing.",
        };
        foreach (CellarDoorFamily cellar in All) { english[cellar.Token] = cellar.English; }
        return english;
    }

    private static IReadOnlyList<CellarDoorFamily> Build()
    {
        var all = new List<CellarDoorFamily>(DoorFamilies.All.Count);
        foreach (DoorFamily family in DoorFamilies.All)
        {
            string? english = null;
            foreach (KeyValuePair<string, string> name in Names)
            {
                if (string.Equals(name.Key, family.Family, StringComparison.Ordinal)) { english = name.Value; }
            }

            if (english == null) { throw new InvalidOperationException("No cellar-door name for door family " + family.Family); }
            var costs = new DoorCost[family.Costs.Count];
            for (int index = 0; index < costs.Length; ++index)
            {
                costs[index] = new DoorCost(family.Costs[index].Item, family.Costs[index].Amount * CostMultiplier);
            }

            all.Add(new CellarDoorFamily(family, english, costs));
        }

        return all;
    }
}
