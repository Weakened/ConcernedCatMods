using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>#419: the three map mods a player asked Concerned Cartographer
/// to live beside, recognised by the BepInEx plugin GUID each one declares.
/// Every GUID here was read from the mod itself (its source or its
/// [BepInPlugin] attribute), never guessed; the evidence is in
/// docs/mods/concerned-cartographer/GREATESTMAP_COMPATIBILITY.md. Matching is
/// exact (case-insensitive), so a similarly named mod is never mistaken for
/// one of these. Detection is read-only: nothing here calls, patches or
/// reflects into another mod.</summary>
internal static class NeighborMapMods
{
    /// <summary>DeathMonger-TheGreatestMap (Thunderstore), 4.5.2 checked.</summary>
    public const string TheGreatestMapGuid = "DeathMonger.TheGreatestMap";

    /// <summary>Neobotics-HUDCompass (Thunderstore / Nexus 2674), 1.2.1 checked.</summary>
    public const string HudCompassGuid = "neobotics.valheim_mod.hudcompass";

    /// <summary>Ithilias' Round Minimap (Hexium; source on GitHub), 1.4.1 checked.</summary>
    public const string RoundMinimapGuid = "ithilias.roundminimap";

    public static NeighborMapModPresence Detect(IEnumerable<string>? loadedPluginGuids)
    {
        bool greatestMap = false;
        bool hudCompass = false;
        bool roundMinimap = false;
        if (loadedPluginGuids is not null)
        {
            foreach (string guid in loadedPluginGuids)
            {
                if (guid is null)
                {
                    continue;
                }

                string trimmed = guid.Trim();
                greatestMap |= string.Equals(trimmed, TheGreatestMapGuid, StringComparison.OrdinalIgnoreCase);
                hudCompass |= string.Equals(trimmed, HudCompassGuid, StringComparison.OrdinalIgnoreCase);
                roundMinimap |= string.Equals(trimmed, RoundMinimapGuid, StringComparison.OrdinalIgnoreCase);
            }
        }

        return new NeighborMapModPresence(greatestMap, hudCompass, roundMinimap);
    }
}

/// <summary>What the detected neighbours mean for Cartographer's own
/// behaviour. Each property is a consequence of something the neighbour was
/// verified to do, named for that behaviour rather than for the mod, so a
/// future neighbour with the same behaviour only has to set the flag.</summary>
internal sealed class NeighborMapModPresence
{
    public NeighborMapModPresence(bool theGreatestMap, bool hudCompass, bool roundMinimap)
    {
        TheGreatestMap = theGreatestMap;
        HudCompass = hudCompass;
        RoundMinimap = roundMinimap;
    }

    public bool TheGreatestMap { get; }
    public bool HudCompass { get; }
    public bool RoundMinimap { get; }

    /// <summary>The neighbour takes ownership of pins placed through the
    /// vanilla naming flow (TheGreatestMap's "Share Placed Markers" adopts
    /// every hand-placed marker). Cartographer then stays out of that flow:
    /// no palette claiming and no adopt prompts, exactly as with a pin
    /// manager, so one pin never has two owners.</summary>
    public bool OwnsVanillaPinFlow => TheGreatestMap;

    /// <summary>The neighbour removes pins it does not own straight through
    /// Minimap.RemovePin(PinData), without the player pointing at them
    /// (TheGreatestMap's tgm_clearlocal and tgm_import). Such a removal is
    /// not evidence that the player deleted a Cartographer marker.</summary>
    public bool RemovesForeignPinsWithoutPointing => TheGreatestMap;

    /// <summary>The neighbour reshapes the small minimap's image geometry
    /// (Round Minimap's disc and rotation), which the texture overlay layer
    /// under that image does not follow.</summary>
    public bool ReshapesSmallMap => RoundMinimap;

    public bool Any => TheGreatestMap || HudCompass || RoundMinimap;
}
