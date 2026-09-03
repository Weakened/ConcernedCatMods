# Privacy — Concerned Cartographer

Concerned Cartographer records your roads, pins, and routes **locally**,
in sidecar files inside your mod-manager profile's BepInEx config
folder. Nothing about your gameplay is uploaded anywhere. There are no
gameplay analytics and no advertising telemetry of any kind, and there
never will be without an explicit, documented change to this policy.

The only network features are ones you invoke yourself: the in-game
atlas *sharing* between players on your server (explicit, reviewed,
documented in the package README), and — the subject of this document —
**optional crash reporting**.

## Crash reporting is optional and off by default

- Crash reporting is **opt-in**. Until you answer the one-time question,
  it is disabled and nothing is ever sent.
- The question is asked exactly once, on your first large-map open after
  installing the mod — never on the title screen, never per world, never
  per character, and never again after you answer (a future release may
  ask once more only if the categories of collected data materially
  change).
- Your choice is stored in the mod's own config file inside your
  mod-manager profile (`Privacy/SendCrashReports`) — never in Valheim
  world saves.
- You can change it at any time: **CC Atlas → Privacy → Send anonymous
  crash reports**, or by editing the config setting directly. Changes
  take effect immediately.
- If the consent dialog itself fails for any reason, reporting simply
  stays off; gameplay is never blocked.

## What a crash report contains (the complete list)

A report is generated only when Concerned Cartographer itself hits an
internal error (a subsystem fail-closed disable, a persistence/
migration/decoder failure, an invariant violation, or an unhandled
exception in the mod's own code). It contains exactly:

- Concerned Cartographer version and release identity
  (`ConcernedCartographer@<version>+<commit>`)
- Valheim, Unity, BepInEx, and Jötunn versions
- the name of the affected mod subsystem
- three booleans: multiplayer session, NoMap world, map open
- the exception type and its sanitized message
- a sanitized stack trace

There is no field for anything else: reports are built from an explicit
allowlist, and arbitrary exception data is dropped unless a future
policy revision explicitly allowlists it (nothing is allowlisted today).

## What is never collected

- Steam IDs or any Steam/player/character identity or names
- world names or world seeds
- server addresses or passwords
- IP addresses (scrubbed client-side where they appear in text, and the
  provider is configured not to store submitter IPs)
- map coordinates or any positions
- pin names, notes, or tags; route names; chat
- save files, screenshots, or your `LogOutput.log`
- machine usernames or filesystem paths (absolute paths and Valheim
  save-file names are scrubbed out of exception text before sending)
- credentials or tokens of any kind

Automated tests assert, for every category above, that data planted into
exception text and context can not appear in the outgoing report.

## Provider and purpose

Reports go to **Sentry** (sentry.io), an error-monitoring service, over
HTTPS, solely so the maintainer learns about real Concerned Cartographer
failures and can fix them before players have to report them by hand.
Reports are used for debugging only. The provider-side project is
configured to not store submitter IP addresses and to run its data
scrubbers (see `docs/mods/concerned-cartographer/CRASH_REPORTING.md`);
events age out under Sentry's standard retention (90 days or less).

The mod embeds only Sentry's public event-ingestion key (DSN) — a
submit-only address. No account tokens or secrets ship with the mod, and
crash reports are never sent by email.

Rate limiting is built in: each distinct failure is reported at most
once per session, with a hard per-session cap, a bounded queue, no
retries, and silent failure when offline.

## Opting out, support, and questions

- Opt out (or never opt in): choose **No thanks** in the dialog, or set
  **CC Atlas → Privacy → Send anonymous crash reports** to off.
- A crash-reporting alternative that shares nothing automatically:
  `cc_atlas support` writes a sanitized report (versions, settings, row
  counts, and sizes — never positions, names, notes, world identifiers,
  or file paths) you can attach to a GitHub issue yourself.
- The mod's own log lines follow the same discipline: no world UIDs,
  file paths, machine usernames, coordinates, player or pin/route
  names, or IPs — and exception text that reaches the log is scrubbed
  by the same sanitizer as crash reports — so sharing the Concerned
  Cartographer lines of `LogOutput.log` does not identify you or your
  worlds.
- Ordinary bugs and feature requests: the GitHub issue tracker —
  https://github.com/Weakened/ConcernedCatMods/issues
- Privacy or crash-reporting questions, or anything you should not post
  publicly: **support@theconcernedcat.com**
