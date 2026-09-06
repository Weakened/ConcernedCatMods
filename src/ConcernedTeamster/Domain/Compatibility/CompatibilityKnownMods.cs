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

        // CT-038: GUID verified directly against the mod's published source
        // (github.com/mtnewton/valheim-mods, ItemStacksPlugin.cs — const
        // GUID = "net.mtnewton.itemstacks"). Its Harmony postfix on the
        // game's item-database init rewrites every item's shared weight
        // field via a configurable multiplier, defaulting to 0.1 (a 90%
        // reduction) with the feature itself on by default
        // (weightEnabledConfig defaults true) — see COMPATIBILITY.md for
        // the exact source citation. Cargo weight is exactly what this
        // touches, globally, so it is tagged the same as BetterCarts.
        new KnownModProbe(
            guid: "net.mtnewton.itemstacks",
            displayName: "ItemStacks",
            policy: CompatibilityPolicy.Adapt,
            affectedAspect: CompatibilityAffectedAspect.CartMassOrPhysics,
            description: TeamsterStrings.Get("compat.itemStacksDescription")),

        // CT-038: GUID verified directly against the mod's published source
        // (github.com/valheimPlus/ValheimPlus, ValheimPlus.cs —
        // [BepInPlugin("org.bepinex.plugins.valheim_plus", ...)]). Its cart
        // mass patch is gated behind the mod's own optional Wagon config
        // section, shipped disabled by default; disabled, the patch
        // reproduces vanilla's mass formula exactly (verified from the
        // patch's own disabled-branch logic, the section's default field
        // values, and the shipped default config template — see
        // COMPATIBILITY.md). Only an explicit, non-default config change by
        // the player alters cart mass, which this registry's presence-only
        // detection cannot observe — Warn/None rather than a gate trip, so
        // the very large fraction of installs that never touch this one
        // optional section do not lose accurate load advice for a change
        // they never made.
        new KnownModProbe(
            guid: "org.bepinex.plugins.valheim_plus",
            displayName: "ValheimPlus",
            policy: CompatibilityPolicy.Warn,
            affectedAspect: CompatibilityAffectedAspect.None,
            description: TeamsterStrings.Get("compat.valheimPlusDescription")),
    };
}
