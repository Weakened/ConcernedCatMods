# Concerned Teamster golden path

CT-046. The one canonical end-to-end journey every stable Teamster user
takes, from install to uninstall. Each step names the feature it
exercises, its automated evidence, and the exact `PRE_RELEASE_SMOKE_TEST.md`
section that carries its still-pending in-game confirmation. This
document proves nothing new by itself — it is the narrative spine tying
`V1_DEFINITION_OF_DONE.md`'s capability-by-capability matrix into one
walkthrough a human can actually follow in one sitting.

## 1. Install

Import the package into a fresh `TCT-Dev` profile (or, for comparison,
`TCT-Clean` with no Teamster at all — see step 2). Start modded.

- **Exercises:** capability probe (CT-002), startup banner, fail-closed
  design (a missing/changed game member disables cart features with one
  log line, everything else keeps working).
- **Automated evidence:** `GameMemberProbeTests` (every simulated-missing-
  member path), the startup banner composition (`EnvironmentBanner`).
- **Smoke section:** §1 (Fresh install and startup).

## 2. First cart

Build a cart, attach it, open the Cart Status panel (visible **Cart**
button, right screen edge, in-world only).

- **Exercises:** the cart capability adapter, telemetry sampler, grade
  math, terrain surface classification, panel construction and
  fail-closed session-disable.
- **Automated evidence:** `CartTelemetryTests`, `GradeMathTests`,
  `TerrainPaintTests`, `CartStatusPresenterTests` (every displayed
  string, stale/no-cart/off states).
- **Smoke section:** §2 (Vanilla truth baseline and Cart Status panel) —
  including the `TCT-Clean` vs. `TCT-Dev` comparison that proves
  Teamster's numbers are the game's own, not invented.

## 3. Status and manifest

Load the cart with mixed cargo (including worn/quality-scaled gear);
open the cargo manifest.

- **Exercises:** quality-scaled weight aggregation, sort/filter, the
  calibrated safe-load model's honest "uncalibrated" state.
- **Automated evidence:** `CargoManifestTests` (including the CT-040
  scale test at 200 synthetic entries), `CargoManifestTrackerTests`,
  `CargoManifestPresenterTests` (full sort/filter matrix),
  `LoadModelTests`.
- **Smoke section:** §3 (Cargo manifest and load planning).

## 4. A planned haul with warnings

Load progressively heavier cargo and climb a graded slope with the panel
open (and, optionally, the HUD hint enabled).

- **Exercises:** load/grade warning evaluation, anti-flicker hysteresis,
  the compatibility precedence gate (an altered-physics mod would
  substitute an "unavailable" notice here instead of a number).
- **Automated evidence:** `CartWarningTrackerTests` (oscillation
  single-transition-pair property), `CompatibilityAdvisoryGateTests`.
- **Smoke section:** §3 row 3.4, §9 (compatibility matrix, if a
  registered mod is installed).

## 5. A descent with risk and the brake

Descend a graded slope; observe descent-risk state; park on a grade and
engage/release the parking brake.

- **Exercises:** the descent-risk model (3D dominance, bounded
  lookahead), the brake lifecycle (explicit, reversible, authority-gated,
  never persisted to a save), stuck diagnostics and recovery guidance if
  the cart gets stuck along the way.
- **Automated evidence:** `RiskModelTests`, `BrakeLifecycleTests` (every
  engage refusal and release path), `StuckDetectorTests`,
  `RecoveryGuidancePresenterTests`.
- **Smoke section:** §4 (Descent safety and the parking brake).

## 6. A recorded trip with quality and bottleneck

Complete the haul (detach); open Trip History; view the road-quality
score and, if a Cartographer route exists, the route bottleneck report.

- **Exercises:** trip recording (per-world sidecar, atomic writes),
  8 m-segment road-quality scoring, trip comparison, and — when
  Concerned Cartographer 0.10.0+ is installed — optional route profiling
  and reporting.
- **Automated evidence:** `RoadQualityTests`, `TripHistoryUiTests`,
  `RouteBottleneckTests`, `RouteProfilerTests`, `RouteReportPresenterTests`,
  `CartographerGateTests`, `CartographerRouteReaderTests`.
- **Smoke section:** §5 (Trip recording and road quality), §6 (Optional
  Cartographer integration).

## 7. Recovery guidance

Stage a stuck scenario (obstruction, grounded chassis, or genuine
overload); follow the Guidance panel's numbered steps to free the cart.
Optionally export a support bundle from the Support panel.

- **Exercises:** the full stuck→diagnose→guide→free loop, and — for a
  corrupted or pre-migration sidecar — the recovery/backup/migration
  path.
- **Automated evidence:** `StuckDetectorTests` (confusion matrix),
  `RecoveryGuidancePresenterTests`, `TripPersistenceTests`,
  `TripPersistPlanTests`, `ConfigSchemaMigrationTests`, `SupportBundleTests`.
- **Smoke section:** §4 rows 4.5–4.8, §10 (Recovery, migration, and
  support bundle).

## 8. Uninstall

Remove the plugin DLL only; relaunch.

- **Exercises:** the uninstall-safe guarantee — vanilla behavior
  resumes exactly, no missing-object errors, and the player's own
  sidecar/support-bundle data (a separate, explicit choice to keep or
  delete) is untouched by removing the plugin itself.
- **Automated evidence:** the write-path audit in `validate_repo.py`
  (every sidecar write lives under `BepInEx/config/ConcernedCatMods/
  ConcernedTeamster/`, never a Valheim save), confirmed structurally —
  there is no code path that could touch a save file even if it tried.
- **Smoke section:** §13 (Upgrade, migration, and uninstall).

## What this path deliberately does not cover

Multiplayer authority handoff (its own topology-dependent path, §7 of
the smoke test, not a step every *solo* player takes) and the
compatibility-mod matrix (§9, conditional on which third-party mods are
installed) are real, tested, pending-verification surfaces — they are
cross-referenced in `V1_DEFINITION_OF_DONE.md` rather than folded into
this single-player narrative, so the golden path stays a path an actual
player would actually walk in one sitting rather than an exhaustive
enumeration.
