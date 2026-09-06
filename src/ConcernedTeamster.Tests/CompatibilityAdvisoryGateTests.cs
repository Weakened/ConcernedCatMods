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

    // ---- The shipped registry's one real entry (CT-037 research) ----------

    [Fact]
    public void ShippedRegistry_BetterCarts_IsRegisteredWithVerifiedGuidAndCoexistPolicy()
    {
        // GUID verified directly against the mod's published source
        // (github.com/TastyChickenLegs/BetterCarts, Plugin.cs: ModGUID =
        // "TastyChickenLegs.BetterCarts") -- see COMPATIBILITY.md for the
        // full research trail, including the candidate NOT registered
        // (We_Haul's Better_Cart) because its GUID could not be verified.
        KnownModProbe betterCarts = Assert.Single(
            CompatibilityKnownMods.Registry, p => p.DisplayName == "BetterCarts");

        Assert.Equal("TastyChickenLegs.BetterCarts", betterCarts.Guid);
        Assert.Equal(CompatibilityPolicy.Coexist, betterCarts.Policy);
        Assert.Equal(CompatibilityAffectedAspect.None, betterCarts.AffectedAspect);
        Assert.NotEmpty(betterCarts.Description);
    }

    [Fact]
    public void ShippedRegistry_BetterCarts_NeverGatesCartMassAdvice()
    {
        // Direct consequence of Coexist + AffectedAspect.None: even
        // detected, BetterCarts must never trip the precedence gate.
        IReadOnlyList<ModDetectionResult> results = CompatibilityRegistry.Evaluate(
            CompatibilityKnownMods.Registry, _ => (true, "1.0.6"));

        Assert.True(CompatibilityAdvisoryGate.CartMassAdviceReliable(results));
    }
}
