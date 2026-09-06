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
        // "TastyChickenLegs.BetterCarts"). Its cart-mass-assignment Harmony
        // patch (see COMPATIBILITY.md's research trail for the exact
        // file/method) reduces the cart's mass by a default 20% — a
        // default-on, physics-altering change, so Teamster's
        // vanilla-calibrated load advice cannot be trusted while this mod
        // is present. This entry is tagged CartMassOrPhysics precisely so
        // CompatibilityAdvisoryGate.CartMassAdviceReliable turns false and
        // every load-advice consumer substitutes its "unavailable" notice
        // instead of a silently-wrong vanilla number.
        new KnownModProbe(
            guid: "TastyChickenLegs.BetterCarts",
            displayName: "BetterCarts",
            policy: CompatibilityPolicy.Adapt,
            affectedAspect: CompatibilityAffectedAspect.CartMassOrPhysics,
            description: TeamsterStrings.Get("compat.betterCartsDescription")),
    };
}
