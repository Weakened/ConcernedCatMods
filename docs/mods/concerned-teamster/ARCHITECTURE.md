# Concerned Teamster architecture

## Context

Concerned Teamster is a BepInEx/Jötunn client mod that observes Valheim carts and
explains them. The architecture enforces three boundaries:

1. **Deterministic domain core** — pure .NET math and models (grade, load,
   risk, road quality) with no Unity, BepInEx, Jötunn, or Valheim references,
   compiled into the plugin and exercised directly by `ConcernedTeamster.Tests`
   on any machine.
2. **Narrow capability-checked adapters** — the only code that touches Valheim
   internals. Every adapter verifies the members it needs at startup via
   reflection-safe checks and disables its capability with one actionable log
   line when the game changes.
3. **Presentation** — buttons, panels, and warnings built on the same proven
   UI stack Cartographer uses. Presenters consume domain snapshots; no UI code
   reads game objects directly.

Concerned Cartographer and Concerned Teamster never reference each other at
compile time. The v0.5 integration is a runtime capability probe.

## Solution layout

```text
src/ConcernedTeamster/                  ConcernedTeamster.csproj (net48)
  Plugin.cs                             BepInEx entry point, wiring, config
  Domain/                               pure deterministic core (no game types)
  Adapters/                             Valheim-facing capability adapters
  Ui/                                   panels, buttons, presenters
  Persistence/                          per-world sidecar IO (from v0.4)
  Package/                              thunderstore.toml, README, CHANGELOG, icon
src/ConcernedTeamster.Tests/            ConcernedTeamster.Tests.csproj
```

`Domain/**` sources are compiled into the shipped DLL and source-linked into the
test project, following the pattern proven in Cartographer (one shipped DLL, CI
tests without game assemblies).

## Components

### Plugin

BepInEx `BaseUnityPlugin` with GUID `com.theconcernedcat.valheim.concernedteamster`.
Owns configuration binding, adapter capability probing at startup, component
wiring, and the environment banner log line. Failure of any adapter capability
must not prevent plugin load; it disables the dependent features.

### CartAdapter

The single seam over Valheim's cart implementation (the `Vagon` component and
its container/attachment members — the exact surface is verified against the
current game build in CT-002 and recorded there; nothing outside `Adapters/`
may name Valheim cart types). Exposes a stable snapshot record: cart identity,
base and total mass, cargo weight, attachment state, local-player pull state,
velocity, and wheel/ground contact where obtainable.

### TerrainAdapter

Reads ground height and surface normals near the cart through supported
Valheim/Jötunn surfaces (verified in CT-004). Produces sample points for the
domain grade math. No terrain writes, ever.

### Telemetry pipeline

A bounded sampler (configurable interval, minimum spacing, hard per-frame
budget) that turns adapter snapshots into domain `CartTelemetry` values.
Allocation-conscious: reuses buffers, no per-frame LINQ, no logging in the
sample path.

### Domain core

- **GradeMath** — deterministic slope/grade computation from terrain samples,
  with unit tests over synthetic terrain fixtures.
- **LoadModel** — cargo mass aggregation and safe-load estimation for the
  current grade (calibrated empirically in CT-008; calibration constants are
  data, not code guesses).
- **RiskModel** (v0.3) — descent/runaway risk from grade, mass, and speed.
- **RoadQuality** (v0.4, CT-017) — deterministic per-segment scores over
  recorded trips. The world is cut into fixed 8 m grid cells (stable keys);
  every persisted stat is an additive accumulator (sums/counts/max), so
  incremental updates equal batch recomputation by construction and
  identical inputs produce byte-identical sidecar output (segments are
  written cell-sorted). Formulas:
  - `Roughness = Σ|Δgrade between consecutive in-segment samples| / pairs`
    — grade jitter as the bumpiness proxy. **Limit:** height is not
    recorded, so this measures slope noise, not literal height noise;
    cross-cell deltas are deliberately dropped.
  - `MeanGrade = Σgrade / gradeCount`, `MaxAbsGrade = max|grade|` over
    samples with a finite grade.
  - `DragProxySpeed = Σ(speed on |grade| < 3%) / levelCount` — mean
    near-level speed; lower means something slows carts there. **Limit:**
    mass-agnostic in this version; interpret with the trip's load in view
    (CT-019).
  Segments aggregate all recorded history; pruning old raw trips does not
  subtract their contribution. Scoring cost per trip is O(samples)
  dictionary work — touched segments ≤ samples, measured in tests.

- **RouteProfiler** (v0.5, CT-023) — incremental, budgeted terrain profiling
  along a Cartographer route: positions at fixed spacing (capped at 4096 by
  coarsening), each `Advance(budget)` probes at most the budget of positions
  through a caller-supplied sampler, and cancellation abandons partial data.
  The profile partitions every meter into sampled or unsampled — unloaded
  terrain is reported, never guessed — and grades/surfaces come only from
  fully sampled segments. The load bottleneck is the steepest sampled
  section treated as a climb (routes are hauled both ways), answered
  verbatim by `LoadModel` (equality is test-asserted). Profiles cache by
  route id + geometry fingerprint, so any vertex edit invalidates and a
  rename does not; the cache clears on world exit.

All domain types are immutable snapshots or pure functions; every model states
its inputs, outputs, and calibration source.

### Ui

Cart Status panel first (CT-005), then manifest, warnings, trip history, and
recovery guidance panels in later sprints. Panels open from visible buttons;
shortcuts are optional accelerators. Presenters read domain snapshots through
an interface so tests can drive them headlessly.

The route picker (CT-022) follows the same shape: a "Routes" button and panel
that exist only when the Cartographer capability probe reported Available.
Eligibility rule: a route is selectable when it is not archived and has at
least two points; archived routes are hidden, too-short routes are listed
with an explicit "(no usable geometry)" reason. Selection is Teamster-held
state keyed by the route's stable id — renames follow the id, while deletion,
archiving, geometry loss, unreadable catalogs, and world exit invalidate it
with an explicit status (fail closed, never a stale ghost). Refreshes are
ChangeStamp-driven at 1 Hz while the panel is open; nothing is ever written
toward Cartographer.

The route report (CT-024) renders the profile as advice from a visible
Report button: numbered problem sections (steep grades at or above 15%, and
the three longest unsampled spans with their locations — the summary's
UNSAMPLED total is always exact, so gaps can rank but never hide), plus load
recommendations that quote LoadModel answers verbatim
(section advice and the bottleneck line both come from Query /
RecommendedMaxMass; descents are advised as the return climb). Sections
without a model answer get facts, not advice. The whole CT-021..CT-024
integration path is audited read-only by tools/validate_repo.py: any
mutating or invoking reflection token in those files fails validation.

### Persistence (from v0.4)

Per-world sidecar files under the BepInEx config path, named by world UID with
a `teamster` infix so they can never collide with Cartographer sidecars.
Atomic writes (temp file + rename), versioned headers, malformed-row skipping,
and backup-before-migration — the same rules Cartographer's persistence proved.
No writes to Valheim save files.

### Integration adapters (v0.5+)

`CartographerCapability` (CT-021) probes for Concerned Cartographer at runtime
by GUID and version; when present and compatible, Teamster can read route
geometry for profiling. Absence, version mismatch, or probe failure all
degrade to "feature hidden" with one INFO line — never an error dialog, never
a crash. The mechanics mirror the CT-002 cart probe: a pure domain contract
(`Domain/Cartographer/CartographerContract`) names every reflective member,
`CartographerGate` decides availability through `GameMemberProbe`, and
`CartographerRouteReader` copies living routes into immutable snapshots,
re-walking the chain from the plugin instance on every call because
Cartographer replaces its route store on world enter. The full member table,
version floor (0.10.0), semantics, and enforcement (validator cross-product
audit plus contract drift tripwire) live in `CARTOGRAPHER_CONTRACT.md`.

### Multiplayer posture (v0.6)

Client-side observational until the v0.6 trust/authority design. Teamster never
takes authority over a cart it does not own under vanilla rules, never grants
force, and treats network data as untrusted input (bounds-checked, fail-closed,
privacy-reviewed) — the posture Cartographer's sync hardening established.

## Lifecycle

### Plugin load

Bind config, probe adapter capabilities, log one environment banner, register
UI buttons. No world state touched.

### World enter

Reset telemetry state; locate no carts eagerly (carts are discovered when the
player interacts with or approaches them within the sampler's bounded search).

### Runtime

Sampler ticks on its configured interval; panels update from the latest
snapshot; warnings evaluate on new snapshots only.

### World exit / shutdown

Stop sampling, flush any dirty sidecar data (v0.4+), drop references to game
objects. Re-entering a world must never show stale data from another world.

## Performance constraints

- No whole-world scans, no per-frame allocations in steady state, no logging
  in the sample path.
- Sampling interval and search radius are configurable with safe defaults and
  hard upper bounds.
- Panel updates are event/interval driven, not per-frame rebuilds.
- Budgets are validated per sprint RC (and formally in CT-048).

## Failure policy

- Missing/changed Valheim member → capability disabled, one WARN line naming
  the feature and the fix expectation, mod keeps running.
- Malformed sidecar row → skip row, keep valid rows, log once per file.
- Any uncertainty in mutating features (parking brake) → refuse the mutation
  and say why (fail closed).

## Future extension seams

- Additional cart-like vehicles behind `CartAdapter` if the game adds them.
- Server-side trust extensions behind the v0.6 policy layer.
- Deeper Cartographer exchange behind `CartographerCapability`.
