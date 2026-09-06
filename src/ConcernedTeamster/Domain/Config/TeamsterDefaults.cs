using TheConcernedCat.ConcernedTeamster.Domain.Profiles;

namespace TheConcernedCat.ConcernedTeamster.Domain.Config;

/// <summary>The v0.9 feature/default freeze (CT-041): every boolean config
/// default that has no other Domain-layer home (numeric defaults already
/// live in their own feature's Options class — see
/// <c>docs/mods/concerned-teamster/FEATURE_FREEZE.md</c> for the complete,
/// audited list). Frozen here specifically so a snapshot test can lock
/// these values directly — <c>TeamsterSettings.Bind</c> only reads them, it
/// does not decide them. Post-freeze, changing a value here needs its own
/// issue, not a silent edit.</summary>
public static class TeamsterDefaults
{
    /// <summary>Master switch. On by default: with it on and no other
    /// feature installed, Teamster only reads cart state — vanilla physics
    /// are untouched either way.</summary>
    public const bool Enabled = true;

    /// <summary>Off by default: development diagnostics, not something a
    /// beta user needs running.</summary>
    public const bool DebugLogging = false;

    /// <summary>On by default: read-only advisory text in the Cart Status
    /// panel, never a value that changes gameplay.</summary>
    public const bool PanelWarningsEnabled = true;

    /// <summary>Off by default: the panel is the primary warning surface;
    /// this is an opt-in HUD supplement for hauling with the panel closed.</summary>
    public const bool HudWarningHintsEnabled = false;

    /// <summary>On by default — but this is a feature-visibility toggle,
    /// not an engagement state: it controls whether the parking brake
    /// BUTTON exists at all. Engaging the brake itself always requires an
    /// explicit click each time, is always reversible, and is never
    /// written to a save (a reloaded world is always brake-free) — see
    /// <c>docs/mods/concerned-teamster/RECOVERY.md</c> and the brake
    /// adapter's own tests. No config default can make the brake
    /// auto-engage.</summary>
    public const bool BrakeEnabled = true;

    /// <summary>On by default: read-only recording of the player's own
    /// hauls into Teamster's own sidecar file; never touches a Valheim
    /// save.</summary>
    public const bool TripsEnabled = true;

    /// <summary>Off by default (unseen) until the player dismisses the
    /// first-run onboarding hint; never reset by the mod itself.</summary>
    public const bool OnboardingDismissed = false;

    /// <summary>Both <c>ActiveProfile</c> and <c>LastAppliedProfile</c> bind
    /// to this same value on a fresh install, so <c>ProfileTransition.
    /// ShouldApply</c> evaluates false and the raw defaults above ship
    /// untouched rather than being overwritten by a profile-apply pass —
    /// which only stays correct because <see cref="ConfigProfileCatalog"/>'s
    /// Standard resolution already equals these same values (see
    /// <c>DefaultFreezeSnapshotTests</c>).</summary>
    public const ConfigProfile DefaultProfile = ConfigProfile.Standard;
}
