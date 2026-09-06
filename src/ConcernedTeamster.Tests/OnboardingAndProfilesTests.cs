using TheConcernedCat.ConcernedTeamster.Domain.Onboarding;
using TheConcernedCat.ConcernedTeamster.Domain.Profiles;

namespace ConcernedTeamster.Tests;

/// <summary>CT-034: the onboarding pointer shows once and dismisses forever
/// (a two-input pure decision, so the whole contract is provable without a
/// game session), applying a config profile twice equals applying it once,
/// and every profile keeps the parking brake opt-in.</summary>
public class OnboardingAndProfilesTests
{
    // ---- Onboarding state machine ------------------------------------------

    [Fact]
    public void Evaluate_NeverDismissed_NearCart_IsVisible()
    {
        Assert.Equal(OnboardingVisibility.Visible,
            OnboardingPresenter.Evaluate(dismissedForever: false, isNearCart: true));
    }

    [Fact]
    public void Evaluate_NeverDismissed_NotNearCart_IsHidden()
    {
        // First launch away from any cart must stay silent, not pop up blind.
        Assert.Equal(OnboardingVisibility.Hidden,
            OnboardingPresenter.Evaluate(dismissedForever: false, isNearCart: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Evaluate_Dismissed_IsAlwaysHiddenRegardlessOfProximity(bool isNearCart)
    {
        // Dismissal is permanent: walking back up to a cart must never
        // re-trigger it.
        Assert.Equal(OnboardingVisibility.Hidden,
            OnboardingPresenter.Evaluate(dismissedForever: true, isNearCart));
    }

    // ---- Config profiles ----------------------------------------------------

    [Theory]
    [InlineData(ConfigProfile.Minimal)]
    [InlineData(ConfigProfile.Standard)]
    [InlineData(ConfigProfile.EverythingObservational)]
    public void Resolve_ApplyingTwiceEqualsApplyingOnce(ConfigProfile profile)
    {
        ProfileValues first = ConfigProfileCatalog.Resolve(profile);
        ProfileValues second = ConfigProfileCatalog.Resolve(profile);

        Assert.True(first.ValueEquals(second));
    }

    [Theory]
    [InlineData(ConfigProfile.Minimal)]
    [InlineData(ConfigProfile.Standard)]
    [InlineData(ConfigProfile.EverythingObservational)]
    public void Resolve_BrakeStaysOptInInEveryProfile(ConfigProfile profile)
    {
        Assert.False(ConfigProfileCatalog.Resolve(profile).BrakeEnabled);
    }

    [Fact]
    public void Resolve_StandardMatchesShippedDefaults()
    {
        // Standard must equal Teamster's actual pre-CT-034 shipped defaults
        // (TeamsterSettings.Bind) — picking it can never surprise an existing
        // installation.
        ProfileValues standard = ConfigProfileCatalog.Resolve(ConfigProfile.Standard);

        Assert.True(standard.PanelWarningsEnabled);
        Assert.False(standard.HudWarningHintsEnabled);
        Assert.True(standard.TripsEnabled);
        Assert.False(standard.BrakeEnabled);
        Assert.Equal(
            TheConcernedCat.ConcernedTeamster.Domain.Risk.LookaheadOptions.DefaultPoints,
            standard.RiskLookaheadPoints);
    }

    [Fact]
    public void Resolve_UnrecognizedProfileValue_FailsClosedToStandard()
    {
        // Simulates an "old-version fixture": a raw stored profile value
        // that no longer names a current profile (a future removed preset,
        // or corrupted data). Must never throw, and must fail closed to the
        // same values as Standard rather than guessing.
        var unrecognized = (ConfigProfile)999;

        ProfileValues resolved = ConfigProfileCatalog.Resolve(unrecognized);
        ProfileValues standard = ConfigProfileCatalog.Resolve(ConfigProfile.Standard);

        Assert.True(resolved.ValueEquals(standard));
    }

    // ---- Profile transition (apply-if-changed) ------------------------------

    [Theory]
    [InlineData(ConfigProfile.Minimal, ConfigProfile.Minimal, false)]
    [InlineData(ConfigProfile.Standard, ConfigProfile.Standard, false)]
    [InlineData(ConfigProfile.Minimal, ConfigProfile.Standard, true)]
    [InlineData(ConfigProfile.EverythingObservational, ConfigProfile.Minimal, true)]
    public void ShouldApply_OnlyWhenActiveDiffersFromLastApplied(
        ConfigProfile active, ConfigProfile lastApplied, bool expected)
    {
        Assert.Equal(expected, ProfileTransition.ShouldApply(active, lastApplied));
    }

    [Fact]
    public void ShouldApply_SameActiveTwiceInARow_IsIdempotent()
    {
        // Models "apply, then run again with nothing changed": the first
        // call transitions and (conceptually) updates lastApplied to match
        // active; the second call against that updated lastApplied must not
        // re-apply.
        const ConfigProfile active = ConfigProfile.EverythingObservational;
        var lastApplied = ConfigProfile.Standard;

        bool firstShouldApply = ProfileTransition.ShouldApply(active, lastApplied);
        lastApplied = active; // what the caller does when firstShouldApply is true
        bool secondShouldApply = ProfileTransition.ShouldApply(active, lastApplied);

        Assert.True(firstShouldApply);
        Assert.False(secondShouldApply);
    }
}
