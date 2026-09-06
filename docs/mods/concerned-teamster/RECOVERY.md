# Config/data migration, backup/recovery, and the support bundle (CT-039)

How Teamster keeps a player's history through version and format changes,
recovers from a corrupted sidecar without ever destroying data, and lets a
player hand over a sanitized diagnostic bundle when reporting a problem.

## Sidecar backup: bounded rotation, not overwrite

`Domain/Trips/SidecarFileStore.TryBackup(path, reason)` used to copy a
sidecar to a single fixed name per reason (`<path>.bak-refused`) with
`overwrite: true` — a second event with the same reason silently destroyed
the only evidence of the first (DEF-teamster-v0.4-001). It now rotates a
bounded set of generations per reason: `<path>.bak-<reason>-1` (newest)
through `<path>.bak-<reason>-3` (oldest,
`SidecarFileStore.MaxBackupGenerationsPerReason`), shifting older
generations up and evicting the oldest beyond the cap. Different reasons
rotate independently. Proven directly against a real filesystem in
`TripPersistenceTests` (rotation, cap enforcement, independence per reason).

## The backup-before-rewrite guarantee is now a tested decision, not review-only

Previously, "back up before a refused or migrating file is rewritten" lived
entirely in `TripRecordingService.Persist`'s hand-written control flow — an
`Adapters/`, BepInEx-bound class the Domain-only test project cannot reach,
so nothing failed automatically if that ordering ever regressed (the other
half of DEF-teamster-v0.4-001). `Domain/Trips/TripPersistPlan.Decide`
extracts the decision into a pure function: given a parsed sidecar, it
returns a `Plan` naming exactly which backup reason (if any) the caller
must apply before writing anything, plus the log lines to emit. `Persist`'s
job is now to execute that plan faithfully — a few lines mechanical enough
to trust by inspection — rather than to decide anything itself. Proven in
`TripPersistPlanTests`: a refused, malformed, or migrating file always
mandates a backup; a clean file never does.

**A related gap was closed while this was rebuilt.** The malformed-rows
case (some lines unreadable, valid trips kept) previously took **no**
backup at all — meaning the very next write permanently discarded whatever
those malformed lines had contained, with no way back. It is now backed up
identically to the refused and migrating cases.

## A failed persist attempt retries instead of silently dropping trips

Independent review of this leaf found one more instance of the exact
class of bug DEF-teamster-v0.4-001 named: every failure branch inside
`TripRecordingService.Persist` (backup failure, read failure, write
failure) logged a message like "trips held in memory" while actually just
returning — `TripRecorder.DrainFinishedTrips`/`DrainOnReset` had already
cleared their own queue before handing the drained trips to `Persist`, so
nothing was actually held anywhere, and a transient I/O failure (a
momentary antivirus lock, a full disk) silently lost real trip data
forever. `Domain/Trips/TripPersistPlan.CombineForRetry`/`BoundForRetry`
(pure, directly tested) now let `Persist` genuinely retain a failed
attempt's trips and prepend them ahead of the next cycle's newly-finished
ones, bounded by the sidecar's own retention cap so a persistently broken
disk cannot grow the retry queue unboundedly. The one exception is a lost
world context (`WorldContextAdapter.TryGetWorldUid` failing) — that is
deliberately still dropped with an honest log line, not retried, since a
world UID becoming available again could belong to a different world and
retrying into it would misattribute the trip.

**A second review round caught the same misattribution risk from a
different trigger.** `TripRecordingService` is a session-long singleton —
it survives a player exiting one world and loading another — but the
retry queue this fix introduced had no per-world tag at all. If a persist
attempt failed in World A and the player then loaded World B before the
next successful attempt, the queued World-A trips would have been merged
straight into World B's sidecar and cumulative road-quality history,
exactly the outcome the world-UID-unavailable case above already refuses
to risk, just reached by a different path (a UID that resolves
successfully, but to a different world, rather than failing outright).
`Domain/Trips/TripPersistPlan.ShouldDiscardPendingRetry` closes this: at
any moment a pending queue holds trips from exactly one world (whichever
was live when it was last retained), so one UID comparison is enough — no
per-trip tagging needed. A world change discards the stale queue with an
honest log line and a recorded `RecoveryEvent`, the same "tell the user
what happened" treatment every other recovery path in this leaf gets,
rather than silently contaminating the new world's data.

## Config schema migration

`Domain/Config/ConfigSchemaVersion`/`ConfigSchemaMigration` introduce the
first version marker Teamster's `.cfg` file has ever had.
`TeamsterSettings.SchemaVersion` defaults to `ConfigSchemaVersion
.PreVersioning` (0) — the value every pre-CT-039 install reads as on its
first post-upgrade load, since the key never existed in their file before —
so the migration in `Plugin.ApplyConfigSchemaMigrationIfNeeded` is a real
event every existing install actually goes through, not a hypothetical one
exercised only in test fixtures. `ConfigSchemaMigration.Decide` is pure and
directly tested (`ConfigSchemaMigrationTests`): a stored version below
current triggers a migration and a log line; equal is a no-op; a stored
version *above* current (a downgrade after a newer version wrote the file)
never guesses backward — it warns and leaves every value exactly as found.

**Why config migration gets no file backup, unlike sidecars.** The one
migration step that exists today only adds a new bookkeeping key — it
changes no other setting's value, and BepInEx's own `ConfigFile` already
never destroys keys it doesn't bind. There is nothing this step can lose.
A future migration that genuinely restructures a setting (a rename, a
removed key with a real replacement) should back up the `.cfg` file first,
the same way sidecar migrations do — this leaf did not need to, and adding
that machinery pre-emptively for a step that cannot lose data would be
build-it-because-we-might-need-it, not because CT-039 needed it.

## Recovery events, surfaced in plain language

Every time a sidecar backup fires, `TripRecordingService` records a
`Domain/Trips/RecoveryEvent` (bounded to the last 50 this session) — the
reason, the sidecar's file name only (never a full path), and a
plain-language message. These are not buried in the BepInEx log alone:
the **Support Bundle** panel's export includes a "Recovery events this
session" section, so a player who ran into a corrupted or migrating file
can see, in the panel they'd already open to report a problem, exactly
what happened without reading `LogOutput.log`.

## The Support Bundle: sanitized, player-triggered, nothing sent anywhere

The Cart Status panel's **Support** button opens a panel with one
**Export** action. Clicking it composes a text file (versions, effective
config, compatibility status, every sidecar's summary counts, this
session's recovery events, and recent Teamster-only log lines) and writes
it under
`BepInEx/config/ConcernedCatMods/ConcernedTeamster/SupportBundles/`. The
panel only shows the resulting path — nothing is transmitted by this
feature; the player decides what to do with the file.

**Sanitization is uniform, not selective.** Every single line the composer
emits — including the caller-supplied config summary and compatibility
lines — passes through `Domain/Support/SupportBundleSanitizer.Sanitize`
before it reaches the file, independent of how trustworthy that particular
line's source seems. This matters most for the recent-log-lines section:
Teamster's own log lines are written for a developer reading BepInEx's own
log, not for a file a player might hand to someone else, and this mod's
actual warning text embeds a full path
(`TripRecordingService.Persist`'s "Trip sidecar at
&lt;path&gt;: ..." line is the concrete, real example driving the test
suite, not a hypothetical). The sanitizer masks URLs, Windows/Unix paths,
`Users/&lt;name&gt;` fragments, save-file names, IPv4 addresses,
hex/token-shaped blobs, and any 7-or-more-digit run (which catches both a
raw world UID — the only world identifier Teamster ever reads at all, via
`ZNet.GetWorldUID()`, never a human-readable name — and the Steam64 portion
of a cart id). `SupportBundleTests` plants a realistic mix of exactly these
shapes, including this mod's own real warning-line format, and asserts
none of it survives.

**A path's own terminal segment is fully masked too, not retained.**
Concerned Cartographer's sibling sanitizer keeps a matched path's final
segment in its replacement (`"<path>/$1"`) — safe there only because that
product's composer never passes it truly arbitrary text (see its own
class doc comment). This sanitizer's whole purpose is scrubbing arbitrary
free-text log lines, so a leaf filename that is itself the sensitive
content (not a generic name) must not survive either; independent review
caught this exact gap before it shipped. Sidecar/bundle file names are
unaffected — they are surfaced separately as bare file names, which never
match the path regexes at all, with only their embedded digits masked.

**Recent log lines are the one place Teamster's approach differs from
Concerned Cartographer's crash reporter on purpose.** Cartographer's
automatic crash reports deliberately never include `LogOutput.log` content
at all (see that product's `PRIVACY.md`) — because that path sends data
without the player reviewing it first. A support bundle here is the
opposite: the player clicks Export, sees exactly what was written, and
decides whether and how to share it. Recent log context is the point of
the feature, not a risk to design around, so `Adapters/LogTailRecorder`
subscribes to Teamster's own `ManualLogSource.LogEvent` (never another
mod's or Valheim's own lines) and keeps the last 100 lines in memory for
exactly this panel to read back — sanitized the same as everything else.

## Known scope limits

- No in-game observation of an actual corrupted sidecar, an actual v1→v2
  migration on a real install, or an actual exported bundle opened and
  read has been run yet — the mechanism is exhaustively unit-tested
  against real files on a real filesystem (`TripPersistenceTests`) and
  against realistic planted content (`SupportBundleTests`), with the
  live-game observation pending. Tracked in `HUMAN_ATTENTION.md`.
- `Adapters/SupportBundleExporter.GatherSidecarSummaries` and
  `Adapters/LogTailRecorder` are BepInEx-bound and, like
  `TripRecordingService.Persist`, cannot be exercised by the Domain-only
  test project. Their logic is kept deliberately mechanical (a few lines
  each) for exactly the reason `TripPersistPlan` exists — the interesting,
  drift-prone decisions live in pure, tested functions instead.
