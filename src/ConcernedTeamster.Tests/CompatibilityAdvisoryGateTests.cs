using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Compatibility;

namespace ConcernedTeamster.Tests;

/// <summary>CT-037: the precedence policy — cart-mass-derived advice
/// (warnings, stuck diagnostics, recovery guidance, route bottlenecks) is
/// reliable exactly until a registered mod affecting cart mass/physics is
/// actually detected, and the gate is generic over the aspect, never a
/// specific mod's GUID or name.</summary>
public class CompatibilityAdvisoryGateTests
{
    private static readonly KnownModProbe HarmlessProbe = new(
        "com.example.harmless", "Harmless Mod", CompatibilityPolicy.Coexist,
        CompatibilityAffectedAspect.None, "no impact");

    private static readonly KnownModProbe MassAlteringProbe = new(
        "com.example.massmod", "Mass-Altering Mod", CompatibilityPolicy.Warn,
        CompatibilityAffectedAspect.CartMassOrPhysics, "changes cart mass limits");

    [Fact]
    public void CartMassAdviceReliable_NoResults_IsTrue()
    {
        Assert.True(CompatibilityAdvisoryGate.CartMassAdviceReliable(System.Array.Empty<ModDetectionResult>()));
    }

    [Fact]
    public void CartMassAdviceReliable_OnlyHarmlessModsDetected_IsTrue()
    {
        var results = new List<ModDetectionResult>
        {
            new(HarmlessProbe, found: true, detectedVersion: "1.0.0"),
        };

        Assert.True(CompatibilityAdvisoryGate.CartMassAdviceReliable(results));
    }

    [Fact]
    public void CartMassAdviceReliable_MassAlteringModRegisteredButNotFound_IsTrue()
    {
        // Registered but not actually installed — must not gate anything,
        // matching "silence is the default" for anything not truly present.
        var results = new List<ModDetectionResult>
        {
            new(MassAlteringProbe, found: false, detectedVersion: null),
        };

        Assert.True(CompatibilityAdvisoryGate.CartMassAdviceReliable(results));
    }

    [Fact]
    public void CartMassAdviceReliable_MassAlteringModDetected_IsFalse()
    {
        var results = new List<ModDetectionResult>
        {
            new(HarmlessProbe, found: true, detectedVersion: "1.0.0"),
            new(MassAlteringProbe, found: true, detectedVersion: "2.0.0"),
        };

        Assert.False(CompatibilityAdvisoryGate.CartMassAdviceReliable(results));
    }

    [Fact]
    public void CartMassAdviceReliable_IsGenericOverAspect_NotTiedToAnyParticularMod()
    {
        // A different GUID/name entirely, same aspect — the gate must react
        // identically, proving it dispatches on the aspect, never a
        // hardcoded mod identity.
        var otherMassAlteringProbe = new KnownModProbe(
            "com.different.author.totally.unrelated.mod", "Some Other Mod", CompatibilityPolicy.Adapt,
            CompatibilityAffectedAspect.CartMassOrPhysics, "also changes physics");

        var results = new List<ModDetectionResult> { new(otherMassAlteringProbe, found: true, "9.9.9") };

        Assert.False(CompatibilityAdvisoryGate.CartMassAdviceReliable(results));
    }

    // ---- The shipped registry's real entries (CT-037/038 research) --------

    [Fact]
    public void ShippedRegistry_BetterCarts_IsRegisteredWithVerifiedGuidAndAdaptPolicy()
    {
        // GUID verified directly against the mod's published source
        // (github.com/TastyChickenLegs/BetterCarts, Plugin.cs: ModGUID =
        // "TastyChickenLegs.BetterCarts") -- see COMPATIBILITY.md for the
        // full research trail, including the candidate NOT registered
        // (We_Haul's Better_Cart) because its GUID could not be verified.
        // Policy is Adapt/CartMassOrPhysics because
        // Patches/CartPatches.cs's Vagon.SetMass prefix reduces cart mass
        // by a default 20% (Patches/CartConfigs.cs: cartMassReduction =
        // 0.2f, applied whenever allowPlayerHelp is false, itself the
        // default) -- a default-on physics change, not a cosmetic one.
        KnownModProbe betterCarts = Assert.Single(
            CompatibilityKnownMods.Registry, p => p.DisplayName == "BetterCarts");

        Assert.Equal("TastyChickenLegs.BetterCarts", betterCarts.Guid);
        Assert.Equal(CompatibilityPolicy.Adapt, betterCarts.Policy);
        Assert.Equal(CompatibilityAffectedAspect.CartMassOrPhysics, betterCarts.AffectedAspect);
        Assert.NotEmpty(betterCarts.Description);
    }

    [Fact]
    public void ShippedRegistry_BetterCarts_AlwaysGatesCartMassAdviceWhenDetected()
    {
        // Direct consequence of Adapt + AffectedAspect.CartMassOrPhysics:
        // once detected, BetterCarts must trip the precedence gate so
        // Teamster never shows vanilla-calibrated load advice as truth
        // under its default mass reduction. Scoped lookup: only BetterCarts
        // is "found", isolating this from the other shipped entries.
        IReadOnlyList<ModDetectionResult> results = CompatibilityRegistry.Evaluate(
            CompatibilityKnownMods.Registry,
            guid => (guid == "TastyChickenLegs.BetterCarts", "1.0.6"));

        Assert.False(CompatibilityAdvisoryGate.CartMassAdviceReliable(results));
    }

    [Fact]
    public void ShippedRegistry_ItemStacks_IsRegisteredWithVerifiedGuidAndAdaptPolicy()
    {
        // GUID verified directly against the mod's published source
        // (github.com/mtnewton/valheim-mods, ItemStacksPlugin.cs: const
        // GUID = "net.mtnewton.itemstacks"). Policy is Adapt/
        // CartMassOrPhysics because its Harmony postfix rewrites every
        // item's shared weight field via a multiplier defaulting to 0.1 (a
        // 90% reduction), itself enabled by default -- see
        // COMPATIBILITY.md for the exact source citation.
        KnownModProbe itemStacks = Assert.Single(
            CompatibilityKnownMods.Registry, p => p.DisplayName == "ItemStacks");

        Assert.Equal("net.mtnewton.itemstacks", itemStacks.Guid);
        Assert.Equal(CompatibilityPolicy.Adapt, itemStacks.Policy);
        Assert.Equal(CompatibilityAffectedAspect.CartMassOrPhysics, itemStacks.AffectedAspect);
        Assert.NotEmpty(itemStacks.Description);
    }

    [Fact]
    public void ShippedRegistry_ItemStacks_AlwaysGatesCartMassAdviceWhenDetected()
    {
        IReadOnlyList<ModDetectionResult> results = CompatibilityRegistry.Evaluate(
            CompatibilityKnownMods.Registry,
            guid => (guid == "net.mtnewton.itemstacks", "1.2.0"));

        Assert.False(CompatibilityAdvisoryGate.CartMassAdviceReliable(results));
    }

    [Fact]
    public void ShippedRegistry_ValheimPlus_IsRegisteredWithVerifiedGuidAndWarnPolicy()
    {
        // GUID verified directly against the mod's published source
        // (github.com/valheimPlus/ValheimPlus, ValheimPlus.cs:
        // [BepInPlugin("org.bepinex.plugins.valheim_plus", ...)]). Policy is
        // Warn/None -- not Adapt/CartMassOrPhysics -- because its cart-mass
        // patch is gated behind the mod's own optional Wagon config
        // section, shipped disabled by default; disabled, the patch
        // reproduces vanilla's mass formula exactly (verified from the
        // patch's disabled-branch logic, the section's default field
        // values, and the shipped default config template). Only an
        // explicit, non-default config change alters cart mass, which this
        // registry's presence-only detection cannot observe -- see
        // COMPATIBILITY.md for the full reasoning.
        KnownModProbe valheimPlus = Assert.Single(
            CompatibilityKnownMods.Registry, p => p.DisplayName == "ValheimPlus");

        Assert.Equal("org.bepinex.plugins.valheim_plus", valheimPlus.Guid);
        Assert.Equal(CompatibilityPolicy.Warn, valheimPlus.Policy);
        Assert.Equal(CompatibilityAffectedAspect.None, valheimPlus.AffectedAspect);
        Assert.NotEmpty(valheimPlus.Description);
    }

    [Fact]
    public void ShippedRegistry_ValheimPlus_NeverGatesCartMassAdviceEvenWhenDetected()
    {
        // Direct consequence of AffectedAspect.None: even detected,
        // ValheimPlus must never trip the precedence gate on presence
        // alone -- the large majority of installs never touch its optional
        // Wagon section, and gating on mere presence would silently deny
        // them accurate load advice for a change they never made.
        IReadOnlyList<ModDetectionResult> results = CompatibilityRegistry.Evaluate(
            CompatibilityKnownMods.Registry,
            guid => (guid == "org.bepinex.plugins.valheim_plus", "0.9.9.11"));

        Assert.True(CompatibilityAdvisoryGate.CartMassAdviceReliable(results));
    }
}
