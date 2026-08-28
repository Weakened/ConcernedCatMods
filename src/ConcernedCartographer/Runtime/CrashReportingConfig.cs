namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>Persistent, profile-level crash-reporting consent (#97).
/// Stored only in the BepInEx config file — never in Valheim world saves,
/// never per world or per character. Unknown means the player has not
/// answered the one-time dialog yet; nothing is ever sent while Unknown
/// or Disabled.</summary>
internal enum CrashConsentState
{
    Unknown,
    Enabled,
    Disabled,
}

/// <summary>The isolated home of every crash-reporting deployment
/// constant (#97), so exactly what ships is auditable in one place.</summary>
internal static class CrashReportingConfig
{
    /// <summary>The Sentry client DSN embedded at package time — the ONLY
    /// credential-like value that may ship, and it is a public
    /// event-ingestion key (it can submit events to the project; it
    /// cannot read data or manage the account). Sentry AUTH TOKENS are
    /// never embedded or used anywhere in this mod.
    ///
    /// Empty in the repository by policy: with no DSN (and no
    /// Privacy/SentryDsn override) crash reporting is fully inert
    /// regardless of consent. The maintainer inserts the real DSN here
    /// before packaging — see docs/mods/concerned-cartographer/CRASH_REPORTING.md.</summary>
    public const string EmbeddedSentryDsn = "";

    /// <summary>Version of the crash-reporting privacy policy the consent
    /// dialog describes. Bump ONLY when the categories or purpose of the
    /// collected data materially change — an answered player is then asked
    /// again exactly once. Routine mod updates never re-prompt.</summary>
    public const int ConsentPolicyVersion = 1;

    public const string PrivacyPolicyUrl =
        "https://github.com/Weakened/ConcernedCatMods/blob/main/PRIVACY.md";
}
