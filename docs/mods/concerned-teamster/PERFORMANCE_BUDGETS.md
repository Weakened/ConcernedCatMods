# v1.0 performance, memory, network, and stability budgets (CT-048)

Formal, numbered budgets for every real per-tick or per-call Domain entry
point, each backed by an automated test that runs repeatably on any
machine. This is the single source of truth for these numbers — other
docs (`TEST_PLAN.md`, `V1_DEFINITION_OF_DONE.md`) point here rather than
repeating the figures.

Wall-clock budgets are generous multiples of the one-machine measured
value (the convention CT-040's scale tests already established), so a
different machine's JIT/GC noise does not cause a false failure while an
actual quadratic-blowup or unbounded-growth regression still fails loudly.
Allocation budgets that assert exact zero are exact zero on every machine
— allocation count does not vary with clock speed the way wall-clock does.

## How to read "long-run"

Teamster has no background thread and no fixed frame rate of its own; its
real-time cost rides whatever rate Valheim's `Update()` calls. Two
cadences matter here:

- **Due-tick cadence** (default 0.5 s, configurable 0.1–10 s,
  `TelemetrySamplerOptions.DefaultSampleIntervalSeconds`): the sampler,
  brake lifecycle, and network input guard all do their real work only on
  a due tick.
- **Frame cadence** (~60 fps while a panel is open): the route profiler's
  per-frame budget and every panel presenter's "am I due yet" check run
  here, gated internally to their own due interval except the profiler.

Each budget below states which cadence its iteration count maps to and
what real elapsed time that implies, so "long-run" is a checked number,
not an adjective.

## Budget table

| # | Capability | Cadence | Budget | Measured (this run) | Status | Evidence |
|---|---|---|---|---|---|---|
| 1 | Sampler not-due fast path | every frame | 0 B/call | 0 B / 200k calls | Met | `TelemetrySamplerTests.Tick_NotDueFastPath_AllocatesNothing` (pre-existing) |
| 2 | Sampler due tick, empty world | due tick (0.5 s) | 0 B/call | 0 B / 10k calls | Met | `TelemetrySamplerTests.Tick_DueTicksWithEmptyWorld_AllocateNothing` (pre-existing) |
| 3 | Sampler due tick, dense cluster, long session | due tick (0.5 s) → 2,000 ticks ≈ 16.7 min/window | allocation flat across two equal windows | window 1: 2,434,192 B; window 2: 2,438,168 B (+0.16%) | Met | `TelemetrySamplerTests.Tick_MaxScaleLongSession_AllocationPerTickDoesNotGrowOverTime` (pre-existing) |
| 4 | Route profiler `Advance`, worst-case profile (4,096 positions, CT-023's hard cap) | frame, while a build is in flight | < 200 ms total to fully consume; 0 B | **0 ms; 0 B** | Met | `PerformanceBudgetTests.RouteProfiler_Advance_ConsumingWorstCaseProfile_AllocatesNothingAndCompletesWithinBudget` (new) |
| 5 | Route profiler `Advance`, post-completion (panel keeps polling after the build finished) | every frame → 1M calls ≈ 4.6 h at 60 fps | 0 B/call | 0 B / 1,000,000 calls | Met | `PerformanceBudgetTests.RouteProfiler_Advance_AfterCompletion_AllocatesNothing` (new) |
| 6 | Brake lifecycle `EvaluateTick`, engaged steady state | due tick (0.5 s) → 200k ticks ≈ 27.8 h | 0 B/call | 0 B / 200,000 calls | Met | `PerformanceBudgetTests.BrakeLifecycle_EvaluateTick_LongRun_AllocatesNothing` (new) |
| 7 | Trip recorder feed→detach→drain, many finished trips | per haul (49 samples × 1.1 s spacing + debounce ≈ 58 s/cycle) → 100 cycles/window ≈ 1.6 h/window | allocation flat across two equal windows | window 1: 832,800 B; window 2: 832,800 B (identical) | Met | `PerformanceBudgetTests.TripRecorder_FeedDetachDrainOverManyTripCycles_AllocationStaysFlat` (new) |
| 8 | Route bottleneck presenter, worst-case trip (5,000 samples, `MaxMaxSamplesPerTrip`) | per keystroke in the hypothetical-mass field | < 50 ms | **1 ms** | Met | `PerformanceBudgetTests.RouteBottleneckPresenter_Present_WorstCaseTripSize_CompletesWithinBudget` (new) |
| 9 | Trip comparison presenter, two worst-case trips | on row select | < 100 ms | **10 ms** | Met | `PerformanceBudgetTests.TripComparisonPresenter_Present_WorstCaseTripSizes_CompletesWithinBudget` (new) |
| 10 | Cargo manifest, many distinct items (200 synthetic, stress input) | on cargo change | < 500 ms | < 1 ms | Met | `CargoManifestTests.Create_ManyDistinctEntries_StaysCorrectAndFast` (pre-existing) |
| 11 | Network input guard (`Mass`/`MassFactor`/`Speed`/`Grade`), every sanitize branch | rides sampler due-tick cadence → 50k×4 calls ≈ 6.9 h | 0 B/call | 0 B / 200,000 calls | Met | `PerformanceBudgetTests.NetworkInputGuard_AllGuards_LongRun_AllocateNothing` (new) |
| 12 | Sidecar compose+write at max retention (500 trips × 20 samples) | once per finished trip (early-return otherwise) | < 500 ms | 23 ms (CT-040, `RELEASE_DOSSIER.md`) | Met | `TripPersistenceTests.Scale_MaxTripsRetainedRoundTrip_StaysCorrectAndReasonablyFast` (tightened this leaf) |
| 13 | Sidecar read+parse at max retention | once per Trip History panel open/reload | < 500 ms | 27 ms (CT-040, `RELEASE_DOSSIER.md`) | Met | same test |
| 14 | Sidecar file size at max retention | — | < 1 MiB | 474,750 B (~464 KiB) (CT-040, `RELEASE_DOSSIER.md`) | Met | same test |

All 14 rows are Met. No budget miss occurred; nothing needed fixing or
re-budgeting.

## Network-derived processing: scope of what's actually live

Row 11 above is the *only* network-derived processing with a live call
site today — `NetworkInputGuard.Mass`/`MassFactor` runs inside
`CartSnapshot.Create`, on the same due-tick cadence and per-tick cart cap
as the sampler itself, so it never runs more often than row 1–3 already
bound. `NetworkInputGuard.Label` is not zero-allocation by design (it
copies into a length-capped buffer) but is bounded to at most 32
characters per call at the same cadence — a fixed, small cost, not
included as its own row.

**Correction after review:** an earlier draft of this section wrongly
claimed `CartAuthorityPolicy` had no live call site. It does:
`CartAuthorityPolicy.MayMutate(TeamsterFeature.ParkingBrake, facts.Authority)`
is called directly from both `BrakeLifecycle.EvaluateToggle` and
`BrakeLifecycle.EvaluateTick` (`Domain/Brake/BrakeLifecycle.cs`), which
`Adapters/BrakeService.cs` drives from `Adapters/CartTelemetryPump.cs`'s
real due-tick loop — the brake's *entire* mutation-authority check runs
through this policy, every due tick while engaged and on every toggle
press. (`BrakeFacts.IsLocalAuthority` is a different thing: it only
gates the HUD button's *visibility*, `Ui/CartStatusHudController.cs`.)
CT-026's own `HUMAN_ATTENTION.md` entry already says as much — "the
policy is the single source of truth the brake enforces through
(test-asserted)" — its pending item is live *multiplayer* validation of
this already-wired behavior (CT-027's scope), not absent wiring. No new
row is needed here: row 6 above already measures `EvaluateTick`'s full
cost, `MayMutate` included, at 0 B over 200k calls.

`CooperativeEffortClassifier`, `RemoteStalenessPolicy`, and
`OncePerKeyGate` genuinely have no live Adapters call site —
`CooperativeEffortClassifier`'s one production caller
(`RecoveryGuidancePanel`) always passes a null participant feed,
matching CT-028's own already-recorded "production supplies none."
Because nothing currently calls these three at runtime, there is no
real-world cadence to budget against for them — inventing one would be
a guess, not a measurement. If a future issue wires any of them in, that
issue is where its budget belongs.

## Panel / HUD per-frame cost

`CartStatusHudController.Update()` runs every rendered frame and calls
six sub-panel `HandleFrame` methods plus two hint updaters. Every one of
them self-gates to a due interval (0.25–1 Hz) except two intentional
exceptions, both already budgeted above: the route profiler's per-frame
`Advance` (rows 4–5, CT-023's own by-design per-frame budget) and
`OnboardingPresenter.Evaluate` (two boolean comparisons every frame, no
allocation possible, documented in its own source as too trivial to need
a dedicated timing test). No panel presenter does unbounded per-frame
work.

## Log volume: structural audit, not a live measurement

No unconditional per-frame or per-tick logging call exists anywhere in
the shipped source. `Domain/` contains zero logging calls at all (no
`BepInEx`/`ManualLogSource`/`UnityEngine` reference anywhere under that
tree). Every `Adapters/` logging call site is gated to fire at most once
per state change, once per session, or once per multi-second period, off
by default where relevant:

| Call site | Gate |
|---|---|
| `CartTelemetryPump.cs` `Update()` catch | once, on first unhandled exception, then permanently silenced |
| `CartTelemetryPump.cs` debug summary | `DebugLogging` off by default; 5 s period when on |
| `BrakeService.cs` | one line per engage/release state transition, never per tick |
| `CartographerCapability.cs` / `CompatibilityAdapter.cs` | idempotent one-shot per session |
| `TripRecordingService.cs` | one-shot flags, fire only on an actual I/O failure/backup event |
| `LocalizationFiles.cs` | startup-only, plus one line per distinct missing key |

This table is a source-cited manual audit taken at CT-048 time (re-auditable
by grepping `Adapters/` for `.Log(` and checking each call site's guard),
not a new CI-gated `validate_repo.py` rule — encoding "is this log call
inside a rate limit" as a static check would require control-flow
analysis the existing token-scan audits (CT-026/CT-028/CT-041) don't
attempt, and would risk false positives/negatives for a property already
manually verified here. The real, wall-clock BepInEx log file size after
an actual multi-hour in-game session is **pending** for the same reason
every other in-game observation in this sprint is: no TCT-* mod-manager
profile exists yet on this development machine (`PROFILE_REHEARSAL.md`).

## What remains pending (in-game only)

Per this issue's own scope ("structured manual capture for in-game"),
these require a real running game and stay pending, itemized here rather
than claimed:

| Observation | Why it can't be automated here |
|---|---|
| Real frame-time delta while hauling with panels open | Requires Unity's actual frame loop; the Domain tests above prove the logic-layer cost is bounded, not the real render-thread cost |
| Real BepInEx log file size after a multi-hour session | Requires a real running session; the structural audit above proves no hot-path spam source exists |
| Real terrain-probe cost inside `RouteProfiler.Advance` | The Domain test's fake probe is free; the real cost is a Unity `Heightmap` query in `Adapters/`, architecturally reused/bounded per `ARCHITECTURE.md` but not independently timed here |
| Real memory growth (working set) over a multi-hour session | The Domain tests bound managed-heap allocation rate per component; total process working set also includes Unity/BepInEx/other-mod memory outside Teamster's control |

Nothing above is claimed Met. They carry the same pending status CT-043
first established for every in-game-only observation, cross-referenced in
`HUMAN_ATTENTION.md`'s CT-048 entry.
