using TheConcernedCat.ConcernedTeamster.Domain.Carts;
using TheConcernedCat.ConcernedTeamster.Domain.Config;
using TheConcernedCat.ConcernedTeamster.Domain.Profiles;
using TheConcernedCat.ConcernedTeamster.Domain.Risk;
using TheConcernedCat.ConcernedTeamster.Domain.Trips;
using TheConcernedCat.ConcernedTeamster.Domain.Ui;
using TheConcernedCat.ConcernedTeamster.Domain.Warnings;

namespace ConcernedTeamster.Tests;

/// <summary>CT-041 feature/default freeze: locks every default value a
/// fresh v0.9 install ships with, so an accidental future edit to a
/// default fails a test instead of silently shipping. <c>TeamsterSettings
/// .Bind</c> itself lives outside the Domain-only test project (it needs a
/// real BepInEx <c>ConfigFile</c>) and stays untested glue by design — this
/// suite locks the VALUES it reads instead, matching the "pure decision,
/// mechanical executor" split used everywhere else in this codebase. See
/// <c>docs/mods/concerned-teamster/FEATURE_FREEZE.md</c> for the full,
/// human-readable audit these assertions mirror.</summary>
public class DefaultFreezeSnapshotTests
{
    [Fact]
    public void GeneralDefaults_AreFrozen()
    {
        Assert.True(TeamsterDefaults.Enabled);
        Assert.Equal(ConfigProfile.Standard, TeamsterDefaults.DefaultProfile);
    }

    [Fact]
    public void DiagnosticsDefaults_AreFrozen()
    {
        Assert.False(TeamsterDefaults.DebugLogging);
    }

    [Fact]
    public void TelemetryDefaults_AreFrozen()
    {
        Assert.Equal(0.5f, TelemetrySamplerOptions.DefaultSampleIntervalSeconds);
        Assert.Equal(30f, TelemetrySamplerOptions.DefaultSearchRadiusMeters);
        Assert.Equal(2, TelemetrySamplerOptions.DefaultMaxCartsPerTick);
        Assert.Equal(8, TelemetrySamplerOptions.DefaultMaxTrackedCarts);
    }

    [Fact]
    public void WarningsDefaults_AreFrozen()
    {
        Assert.True(TeamsterDefaults.PanelWarningsEnabled);
        Assert.False(TeamsterDefaults.HudWarningHintsEnabled);
        Assert.Equal(18f, WarningOptions.DefaultSteepGradeCautionPercent);
    }

    [Fact]
    public void RiskDefaults_AreFrozen()
    {
        Assert.Equal(3, LookaheadOptions.DefaultPoints);
    }

    [Fact]
    public void BrakeDefault_IsFrozen_AndIsAVisibilityToggleNotAnEngagementState()
    {
        // True means "the button exists", never "the brake is engaged" —
        // engaging it is always a separate, explicit, per-use click that no
        // config value can default on. See TeamsterDefaults.BrakeEnabled's
        // own doc comment and RECOVERY.md for the non-persistence guarantee.
        Assert.True(TeamsterDefaults.BrakeEnabled);
    }

    [Fact]
    public void TripsDefaults_AreFrozen()
    {
        Assert.True(TeamsterDefaults.TripsEnabled);
        Assert.Equal(1f, TripRecorderOptions.DefaultRecordSpacingSeconds);
        Assert.Equal(600, TripRecorderOptions.DefaultMaxSamplesPerTrip);
        Assert.Equal(50, TripRecorderOptions.DefaultMaxTripsRetained);
    }

    [Fact]
    public void UiDefaults_AreFrozen()
    {
        Assert.Equal(1.0f, UiScaleOptions.DefaultScale);
        // PanelShortcut's default (BepInEx.Configuration.KeyboardShortcut
        // .Empty, "no accelerator bound") is not snapshotted here: the type
        // lives outside this Domain-only test project's reach, and "no
        // shortcut at all" is definitionally the safest possible default —
        // recorded in FEATURE_FREEZE.md's audit table instead.
    }

    [Fact]
    public void OnboardingDefault_IsFrozen()
    {
        Assert.False(TeamsterDefaults.OnboardingDismissed);
    }

    [Fact]
    public void SchemaVersionDefault_IsFrozen()
    {
        Assert.Equal(0, ConfigSchemaVersion.PreVersioning);
    }

    [Fact]
    public void FreshInstall_ActiveAndLastAppliedProfile_BothDefaultToStandard_SoTheProfileApplyNeverFiresOnFirstLoad()
    {
        // TeamsterSettings binds both ActiveProfile and LastAppliedProfile
        // to ConfigProfile.Standard. ProfileTransition.ShouldApply(active,
        // lastApplied) is active != lastApplied, so on a genuinely fresh
        // install it evaluates false — the raw Bind() defaults above ship
        // untouched, not a value written by the profile-apply path. This
        // is only safe because Standard's resolved values (below) already
        // equal those raw defaults; if they ever drift apart, a fresh
        // install and a "switch to Standard" install would disagree.
        Assert.False(ProfileTransition.ShouldApply(ConfigProfile.Standard, ConfigProfile.Standard));
    }

    [Fact]
    public void StandardProfile_ResolvedValues_MatchTheRawFreshInstallDefaults()
    {
        ProfileValues standard = ConfigProfileCatalog.Resolve(ConfigProfile.Standard);

        Assert.Equal(TeamsterDefaults.PanelWarningsEnabled, standard.PanelWarningsEnabled);
        Assert.Equal(TeamsterDefaults.HudWarningHintsEnabled, standard.HudWarningHintsEnabled);
        Assert.Equal(TeamsterDefaults.TripsEnabled, standard.TripsEnabled);
        Assert.Equal(LookaheadOptions.DefaultPoints, standard.RiskLookaheadPoints);

        // The brake is excluded from every profile's actual writes (Plugin
        // .ApplyProfileIfChanged never reads ProfileValues.BrakeEnabled) —
        // asserted false here anyway so the catalog's OWN stated intent
        // ("the brake stays off in every profile") never silently flips
        // true without a test noticing, even though nothing currently
        // wires it through.
        Assert.False(standard.BrakeEnabled);
    }
}
