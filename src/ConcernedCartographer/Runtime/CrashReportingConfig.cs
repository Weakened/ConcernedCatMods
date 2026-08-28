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
    /// <summary>The Sentry client DSN — the ONLY credential-like value
    /// that ships, and it is a public event-ingestion key: it can submit
    /// events to the project and nothing else (no reads, no account
    /// access). Sentry AUTH TOKENS are never embedded or used anywhere in
    /// this mod. Owner-provided and embedded 2026-08-28 (#97); ingestion
    /// verified live (HTTP 200). Rotation: replace this value and cut a
    /// new RC — see docs/mods/concerned-cartographer/CRASH_REPORTING.md.
    /// Reporting still sends nothing without explicit player consent.</summary>
    public const string EmbeddedSentryDsn =
        "https://eec0ed91ddb82ee984103b4180573feb@o4511990602989568.ingest.us.sentry.io/4511990681436160";

    /// <summary>Version of the crash-reporting privacy policy the consent
    /// dialog describes. Bump ONLY when the categories or purpose of the
    /// collected data materially change — an answered player is then asked
    /// again exactly once. Routine mod updates never re-prompt.</summary>
    public const int ConsentPolicyVersion = 1;

    public const string PrivacyPolicyUrl =
        "https://github.com/Weakened/ConcernedCatMods/blob/main/PRIVACY.md";
}
