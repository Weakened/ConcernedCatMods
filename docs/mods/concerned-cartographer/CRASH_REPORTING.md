# Crash reporting — maintainer guide (#97)

Player-facing policy: `PRIVACY.md` (repo root). This document covers the
implementation contract and the Sentry-side setup the maintainer must do.

## Architecture

- `Domain/Reporting/ICrashReporter` — the provider abstraction
  (`Initialize` / `CaptureException` / `CaptureFatalSubsystemFailure` /
  `Flush` / `Dispose`). The runtime talks only to this; no provider
  calls exist in feature classes.
- `NullCrashReporter` — used whenever no DSN is configured or the
  provider is unusable. `SentryCrashReporter` — implemented directly
  against Sentry's envelope-ingestion HTTP endpoint (no SDK is bundled;
  the package still ships exactly one DLL) with an injectable transport
  seam so tests assert on the exact outgoing bytes.
- Capture sources: the mod's own `ManualLogSource` Error/Fatal events
  (every fail-closed disable, persistence/migration/decoder failure,
  invariant violation) and Unity `logMessageReceived` exceptions whose
  stack contains `TheConcernedCat.ConcernedCartographer`. Nothing else —
  warnings and other mods' failures are never captured.
- Privacy: `CrashReportEvent` is allowlist-only; `CrashReportSanitizer`
  scrubs URLs, coordinates, absolute paths/usernames, Valheim save-file
  names, IPs, secret-shaped blobs, and long numeric IDs; exception Data
  is dropped unless allowlisted (`CrashReportEvent.AllowedDataKeys` is
  empty — additions require a PRIVACY.md revision AND a
  `ConsentPolicyVersion` bump so players are re-asked).
- Reliability: consent gate before any queueing, bounded queue (8),
  one delivery attempt per event, session dedupe + cap (10), background
  sender thread, bounded flush at shutdown.
- Consent: `Privacy/SendCrashReports` tri-state
  (Unknown/Enabled/Disabled, profile-level config only). The one-time
  dialog appears on the first large-map open; the permanent surface is
  CC Atlas → Privacy.

## The DSN (and exactly what is embedded)

`Runtime/CrashReportingConfig.EmbeddedSentryDsn` carries the live
project DSN, owner-provided and embedded 2026-08-28:

```text
https://eec0ed91ddb82ee984103b4180573feb@o4511990602989568.ingest.us.sentry.io/4511990681436160
```

That is the ONLY credential-like value in the repository or the mod. A
Sentry DSN is a *public event-ingestion key*: it can submit events to
this one project and nothing else — no reads, no account access. Client
apps routinely ship it. **Sentry auth tokens must never appear anywhere
in this repository or the mod.** Ingestion was verified live at embed
time (envelope POST → HTTP 200).

Notes:

- Consent still rules: the mod sends nothing while consent is Unknown
  or Disabled, DSN or not.
- Abuse of a public DSN (third-party spam into the project) is handled
  with Sentry's rate limits / inbound filters; if needed, rotate the key
  in Sentry, replace this constant, and cut a new RC.
- `Privacy/SentryDsn` in a profile config overrides the embedded value
  for local testing without a source change.
- The Sentry NuGet SDK is deliberately NOT used (`SentrySdk.Init`,
  `AutoSessionTracking`, `Debug` do not exist here): the package ships
  one DLL, and Release-Health session tracking would be session
  telemetry beyond the consented crash-reports-only policy — adding it
  would require a PRIVACY.md revision and a ConsentPolicyVersion bump.

## Required Sentry project settings (server-side scrubbing)

All under Project → Settings → Security & Privacy — these are REQUIRED
because PRIVACY.md promises them:

- **Prevent Storing of IP Addresses**: ON (org- or project-level).
- **Data Scrubber**: ON, including "Use Default Scrubbers".
- Additional sensitive fields: `steamid`, `world`, `seed`, `server`,
  `password`, `token`.
- Do not enable session replay, profiling, or any performance/analytics
  product for this project — errors only.

## Alerts to configure (Sentry → Alerts)

1. **New issue**: notify on "a new issue is created" (first sighting of
   any failure) → maintainer email/notification channel.
2. **Regression**: notify when "a resolved issue becomes unresolved"
   (a fixed failure returns in a newer release).
3. **High frequency**: metric alert when event count for the project
   exceeds ~50/hour (a bad release burning many players).
4. **Persistence/migration**: issue alert filtered on tag
   `cc.subsystem` containing `persist`, `atlas`, `migration`, or
   `sidecar` — data-safety failures get priority routing.

Release correlation: every event carries
`release: ConcernedCartographer@<semver>+<commit>` — enable "resolve in
the next release" workflows and compare releases when triaging.

## Support routing (canonical)

- Ordinary bugs/features → GitHub issues (first stop).
- Security vulnerabilities, privacy/crash-reporting questions, or
  sensitive logs → **support@theconcernedcat.com**.
- Crash reports go only to the backend above — never by email.
