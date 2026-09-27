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
### Paths containing spaces (#388, a deliberate change to this audit surface)

The path patterns originally forbade whitespace inside a segment, so a
mod-manager path stopped matching at its first space:
`...\Thunderstore Mod Manager\DataFolder\Valheim\profiles\<profile>\BepInEx\...`
had its head replaced and everything from `Mod` onwards — the profile name
and the whole folder layout — travelled verbatim. The user name sits before
that point and was always scrubbed, which is why this was a leak rather
than a breach, and why `SupportReportPrivacyTests` passed over it: its
Thunderstore-shaped plant asserted only as far as the user name.

A space is now admitted inside a segment, and the discriminator between
"this folder name has more words in it" and "the path ended and a sentence
began" is **count, not case**: at most two space-joined tokens per segment.
Two covers every real multi-word folder in the paths this scrubber sees —
`Thunderstore Mod Manager`, `Documents and Settings`, `Program Files (x86)`,
`Application Support`, `Eren cansunar` — and refuses the longer runs prose
produces.

**This rule tested case first, and #410's review took that apart in both
directions at once.** Against privacy: a token beginning lower case refused
the run, so a **user name with a space in it**
(`C:\Users\Eren cansunar\AppData\…`) kept the surname, the folder layout and
the profile name, and so did `Documents and Settings` and lower-case
non-Latin folders (`Meine änderungen`, `Мои моды`). Against diagnostics:
a capitalised run was admitted without limit, so
`wrote C:\a\b OK See BepInEx/LogOutput.log` collapsed to `wrote <path>/b`
and took the pointer to the log file with it.

The stated reason for preferring `\p{Ll}` to `[a-z]` was also simply wrong,
and is recorded here because it is the kind of error that survives review by
sounding careful: a **negated** ASCII class is *broader*, not narrower —
`[^a-z…]` admits `ä` and `моды`'s first letter where `[^\p{Ll}…]` refuses
them. What had excluded non-Latin names was the **positive**
`[A-Z0-9_\-(\[]` of the version before that. Counting tokens fixes both
directions and needs no case class at all, so the question does not arise.

Where the run is allowed to reach is guarded differently in the two halves
of a path, and each guard closes a failure an independent review
demonstrated against the first version of this change:

- **Directory chain:** the cap has a second guard for free — the segment a
  run extends must still end at a separator. Dots and commas are safe here
  — `My Mods V1.2\Valheim\x.cfg` is one path. That separator is not
  sufficient on its own, which is what the unbounded version got wrong: any
  later separator in the prose anchored the run, and the chain ate the
  sentence.
- **Final component:** three guards.
  1. A path ending in a **file name** takes no run at all (a
     variable-length negative lookbehind for `.ext`, which .NET supports
     and most engines do not). What it recognises is a dot plus one to
     eight **alphanumerics**, which is less than "a file name":
     `notes.configuration` and `b.cfg~` are not seen as extensions, so a
     run may still follow them. Without it at all,
     `wrote ...\b.cfg OK` lost the `OK`, `plugin.dll 0.9.0` lost the
     version, `b.cfg Cannot Be Read` and the German-locale
     `b.cfg Zugriffsverweigerung` lost the reason (.NET localizes its
     exception messages and German capitalizes nouns), and
     `Erens New World.db` lost the `.db` marker that `SaveFileNames`
     needs. A path ending in a folder is the only shape where a run buys
     any privacy, and now the only shape that gets one.
  2. No dot, comma or semicolon inside a final-run token, so it cannot
     reach across `, retrying` or into `World.db`.
  3. The run is taken only where the path visibly ends: end of text, end
     of **line** (`exception.ToString()` is multi-line the moment there is
     a stack trace, and `$` is not line-aware here), a quote, a comma or a
     semicolon. `:` is deliberately **not** an end marker — a run that
     reached the next path's drive letter stopped at its colon, consumed
     the `D` of `D:\...`, and left that entire second path unscrubbed,
     user name included. That was a new leak strictly worse than the
     pattern being replaced.
- **Neither run may hold `<` or `>`.** `Sanitize` replaces in sequence, so
  by the time `UnixPath` runs the text already contains this scrubber's own
  `<path>/` markers; a run that could hold them crossed one, swallowed the
  file name the Windows pass had just kept, and produced
  `<path><path>/x.cfg`. `UnixPath` additionally refuses to start at a `/`
  that directly follows `>`, which is that marker and never a separator in
  the original text.

**Stated limits**, written down rather than left to be found:

1. A path ending at a **folder** followed by at most two capitalised words
   and then a line end: the run fires and those words go. Erring this way
   keeps the folder name from surviving; the cost is a short reason.
2. A folder name of **four or more words** exceeds the cap, so the run is
   refused and the rest of the path survives. That is what buying limit 1's
   direction and the user-name fix cost.
3. An **extension of more than eight characters**, or one ending in a
   non-alphanumeric, is not recognised as an extension, so a run may still
   follow it and take the sentence.
4. UNC paths (`\\server\share\...`) are matched by neither pattern: no
   drive letter for `WindowsPath`, no `/` for `UnixPath`, and no `Users`
   segment for `UsersFragment` unless one happens to be there. Relative
   paths (`..\..\Users\me\x.cfg`) are likewise unmatched; a `~`-rooted path
   **is** matched once it has two separators. **Pre-existing and unchanged
   by #388 or #410**, tracked as #408 — which covers this scrubber and
   Teamster's independently-written one together.

This reaches `LogOutput.log` through `SafeLogText` and the support report
through `SupportReportComposer`, not the crash report alone.
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
