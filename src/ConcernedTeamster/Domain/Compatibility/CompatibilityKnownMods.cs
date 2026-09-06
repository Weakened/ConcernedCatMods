using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;

namespace TheConcernedCat.ConcernedTeamster.Domain.Compatibility;

/// <summary>The shipped compatibility registry. Every entry's GUID is
/// verified against the mod's actual published source before being added
/// here — inventing one would violate this repository's "research, don't
/// invent" rule for third-party mods. See
/// docs/mods/concerned-teamster/COMPATIBILITY.md for the exact research
/// trail behind each entry, including mods considered and not registered.</summary>
public static class CompatibilityKnownMods
{
    public static readonly IReadOnlyList<KnownModProbe> Registry = new[]
    {
        // CT-037: GUID verified directly against the mod's published source
        // (github.com/TastyChickenLegs/BetterCarts, Plugin.cs — ModGUID =
        // "TastyChickenLegs.BetterCarts"). Its shipped feature set (quick
        // attach/detach, multiplayer push assist, damage removal, network
        // sync) does not touch cart mass, weight, or pull physics, so it
        // coexists cleanly — Teamster's readings stay accurate.
        new KnownModProbe(
            guid: "TastyChickenLegs.BetterCarts",
            displayName: "BetterCarts",
            policy: CompatibilityPolicy.Coexist,
            affectedAspect: CompatibilityAffectedAspect.None,
            description: TeamsterStrings.Get("compat.betterCartsDescription")),
    };
}
