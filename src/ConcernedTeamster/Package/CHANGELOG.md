# Changelog

## 0.7.0 (Internal — unreleased)

**UX, Controller, Accessibility, Localization (CT-031..CT-035).** The
seventh internal release candidate: every panel is scalable, contrast-
checked, and controller-navigable; every user-facing string is externalized
with a documented translator path; and a new player is gently pointed at
the Cart button instead of left to discover it cold.

- **Controller navigation and accelerators (CT-031).** A deterministic focus
  order for every panel and a conflict checker for keyboard accelerators
  (internal duplicates and external collisions with a caller-supplied
  reserved set) — buttons-first is enforced by an audit: every feature has a
  visible button, none is accelerator-only. Live gamepad input wiring is
  staged pending verification against the real read surface.
- **Localization framework and full-catalog externalization (CT-032).**
  Every user-facing string — status, manifest, warnings, diagnostics,
  guidance, trips, routes, navigation — resolves through a 244-key English
  catalog with override loading from a translator `.tsv`, English fallback,
  a once-only missing-key report, and placeholder validation. A CI-gating
  audit test fails the build if a hardcoded English literal, a dead catalog
  key, or a re-hardcoded catalog sentence is ever introduced.
- **UI scale, contrast, and non-color cues (CT-033).** A configurable scale
  factor (0.8–1.3) applied to each panel's whole transform, so nothing can
  clip relative to its own container at any scale. Every panel text color
  is audited against the WCAG AA contrast target; a sensitivity check found
  a real, narrow risk under the wood-panel background's uncertainty, fixed
  by adding an outline everywhere one was missing. Every warning, diagnosis,
  and comparison state was already text-distinguishable by design since
  CT-009/CT-018 — now an audited, tested invariant.
- **Onboarding, config profiles, and discoverability (CT-034).** A single
  non-modal, self-dismissing hint introduces new players to the Cart button
  the first time they are near a cart. Three documented settings presets
  (Minimal / Standard / EverythingObservational) apply idempotently on an
  explicit config change — Standard matches the mod's original defaults
  exactly, and the parking brake stays opt-in in every preset. Every panel's
  Close button now sits at the same position and size.
- **Safety posture unchanged.** No cart physics, inventory, stamina, or
  network behavior touched; nothing written to worlds or saves. All four
  leaves are UI/config-layer only.

## 0.6.0 (Internal — unreleased)

**Multiplayer Trust and Authority (CT-026..CT-030).** The sixth internal release candidate: a written, enforced policy for who may read, act, and observe each feature in multiplayer — with the parking brake the only mutating feature, gated to live local authority, and every network-derived input treated as hostile. Client-side throughout; Teamster sends nothing and takes no ownership.

- **Authority policy (CT-026).** Every shipped feature is classified observation or mutation. Only the parking brake mutates, and only when this client owns the cart under vanilla rules right now; any authority ambiguity fails closed. The brake enforces its right to act through the policy itself, so the documented matrix and the code cannot drift. Owner-fresh observations (mass, grade, pull state) are labeled remote when viewed without local authority.
- **Topology validation (CT-027).** The authority scenarios — player-hosted and dedicated-server, including cart authority handoff mid-haul — are proven at the logic layer (topology is not an input to the decision); the live in-game rows are listed for the owner smoke test.
- **Cooperative diagnostics (CT-028).** When crews haul together, Teamster describes who is helping, hindering, or idle and why the cart still will not move — pairing the crew context with the physical stuck verdict without ever changing it, and without adding a single newton of force (audited).
- **Input hardening + privacy (CT-029).** Every network-derived number is bounded to a finite, in-range value (garbage in, safe value out — never a NaN or negative cart mass); a fuzz sweep proves it. A committed privacy inventory confirms nothing Teamster produces ever leaves the machine, and player names are never logged.
- **Safety posture unchanged.** No force, no teleport, no ownership takeover, no world-save mutation; validator audits fail the build on any of these. Removing the mod leaves worlds and carts exactly as vanilla made them.

## 0.5.0 (Internal — unreleased)

**Optional Cartographer Integration (CT-021..CT-025).** The fifth internal release candidate: when Concerned Cartographer is installed, its drawn routes can be profiled for cart safety — with zero hard dependency in either direction and zero writes into Cartographer's atlas.

- **Runtime capability detection (CT-021).** Teamster detects Concerned Cartographer by plugin GUID and version at runtime (floor 0.10.0) and verifies a 12-member read contract before any integration feature can appear. Absence, an older version, or any contract mismatch hides the integration with one INFO line — nothing errors, and Teamster runs fully standalone. There is no compile-time reference between the mods, enforced by automated audits.
- **Route picker (CT-022).** A **Routes** button (inside the Cart Status panel, only when Cartographer is detected) lists the current world's routes — name, ground length, point count. Archived routes are hidden; routes without usable geometry are listed with the reason. Selection is held by the route's stable id: renames follow it, while deletion, archiving, geometry loss, or an unreadable catalog invalidate it with an explicit message — never a stale ghost.
- **Route profiling (CT-023).** The selected route is terrain-sampled in bounded per-frame chunks (cancellable, capped): total distance, sampled vs **unsampled** meters (unloaded terrain is reported, never guessed), surface composition, grade histogram, worst climb/descent, and the safe-load bottleneck — the steepest sampled section answered verbatim by the calibrated load model for your cart's current mass. Profiles cache by geometry and recompute only when the route actually changes.
- **Route report (CT-024).** A **Report** button renders the profile as advice: numbered problem sections (steep grades, unsampled stretches with locations), and load recommendations quoted directly from the calibration model — sections the model cannot answer get facts, not invented advice.
- **Read-only by construction (CT-025).** The whole integration reflects into Cartographer through reads only; a validator audit fails the build if any mutating or invoking reflection ever appears in the integration path. Cartographer's atlas, pins, routes, and files are never touched.

## 0.4.0 (Internal — unreleased)

**Road Quality and Trip Profiles (CT-016..CT-020).** The fourth internal release candidate: recorded trips grade the roads themselves — roughness, drag, grade, and bottlenecks — so haulers improve routes with evidence instead of vibes.

- **Per-world trip recording (CT-016).** Pulled-cart trips (position, grade, speed, load) are recorded as bounded sample sequences and persisted to Teamster's own sidecar file — never a Valheim save. Atomic temp-file writes survive a kill at any moment; a versioned header carries the owning world's UID, so world A's trips can never load into world B (filename and header both); malformed rows are skipped and reported; a foreign or future file is backed up first and then replaced with a fresh sidecar for this world — recording never silently destroys it. Trip count is capped with oldest-trip pruning and a visible retention setting.
- **Road-quality scoring (CT-017).** Recorded trips score the world in fixed 8 m segments: roughness (mean absolute grade change), mean and worst grade, and a drag proxy (mean speed on near-level ground). Every stat is additive, so scores are deterministic regardless of trip order; segments aggregate all recorded history and survive raw-trip pruning by design. Format v2 sidecars persist the scores; v1 files are backed up and migrated by recomputation.
- **Trip history and comparison UI (CT-018).** The Trips panel lists recorded trips with distance, duration, average speed, grade extremes, and cargo mass — sortable, deterministic, with explicit empty states — and compares any two trips (A/B, marked in text, never color alone) on shared distance-normalized quintiles so different-length routes align by fraction of the way. Individual trip records can be deleted.
- **Route bottlenecks (CT-019).** For a recorded trip, the panel locates the worst-grade point, the roughest crossed segment, and — for a chosen hypothetical cargo mass — the point where the calibrated load model binds, each positioned by distance along the route and explained by naming its constraint. Unknown calibration coverage is reported honestly instead of pretending a clear verdict.
- **Safety posture unchanged.** Recording observes only what the local player already pulls; no cart physics, inventory, stamina, or network behavior is touched; Teamster writes only its own sidecar directory under the BepInEx config path — Valheim world saves are never modified.

## 0.3.0 (Internal — unreleased)

**Descent Safety and Recovery Guidance (CT-011..CT-015).** The third internal release candidate: know whether the hill down will stay controlled, hold a parked cart on purpose, and get told why a stuck cart is stuck — with vanilla physics untouched by default and every mutating convenience explicit and reversible.

- **Descent risk model (CT-011).** A calibrated three-dimensional model (downgrade, mass, entry speed) rates the descent where you are and the worst descent within a bounded lookahead window ahead of your cart. Verdicts come only from recorded rows and physics bounds; uncalibrated descents say "unknown" instead of pretending.
- **Parking brake (CT-012).** The first and only behavior mutation, under the strictest rules: a visible button freezes a parked cart you control; release is explicit or automatic (grab the handle, walk away, lose authority, leave the world, quit, or any capability loss). Nothing is ever written to saves — a reloaded world is always brake-free, and uninstalling restores pure vanilla.
- **Stuck diagnostics (CT-013).** When a pulled cart stops moving, the panel says why — overloaded (with the calibration row as evidence), marginal load, steep uncalibrated climb, or a physical obstruction/grounded chassis — and honestly says "cause unclear" when signatures conflict. Parked carts cost nothing.
- **Recovery guidance (CT-014).** A Guidance panel turns the diagnosis into numbered vanilla-legal steps — including exactly how much weight to unload, traced to proven calibration rows — never a button that moves the cart for you.
- **Safety posture.** Read-only observation everywhere except the explicitly-invoked brake; no inventory, stamina, or network behavior touched; no world or save writes.

## 0.2.0 (Internal — unreleased)

**Cargo and Load Planning (CT-006..CT-010).** The second internal release candidate: know what your cart carries and whether the hill ahead is provably safe — still read-only, still vanilla physics.

- **Cargo Manifest panel (CT-006/CT-007).** A Manifest button on the Cart Status panel opens a sortable, filterable list of the cart's cargo: item, count, unit weight, line weight — weights taken from the game's own quality-scaled accounting. Sort by any column (stable, deterministic), filter by name (case-insensitive, localized names). Broken modded items appear as explicit unreadable markers instead of silently skewing totals; empty carts and filtered-to-nothing states say so.
- **Calibrated load model (CT-008).** A written calibration protocol plus versioned data with full provenance. Verdicts come only from dominance over recorded rows — proven climbs, proven failures, physics bounds from the verified joint break force — and everything else answers "uncalibrated", never fake precision. No measured runs ship yet; the data file says exactly that.
- **Live load/grade warnings (CT-009).** While climbing, the panel (and an optional off-by-default HUD hint) warns with actionable text and non-color cues: proven-impossible climbs are DANGER with the evidence quoted, marginal climbs are CAUTION, steep uncalibrated climbs get a terrain-fact caution. Anti-flicker hysteresis is fixed in code; uncalibrated verdicts never warn.
- **Safety posture unchanged.** Read-only observation; no cart physics, inventory, stamina, or network behavior modified; nothing written to worlds or saves.

## 0.1.0 (Internal — unreleased)

**Cart Truth (CT-001..CT-005).** The first internal release candidate of Concerned Teamster: read-only cart telemetry with a discoverable Cart Status panel, and vanilla cart physics untouched.

- **Cart Status panel (CT-005).** A visible "Cart" button at the right screen edge (in-world only) opens a draggable panel showing total mass, the base + cargo breakdown, terrain grade with climbing/descending state, ground surface, attachment/pull state, and data freshness. Stale data is visibly marked STALE; "no cart nearby" and unavailable values say so instead of showing wrong numbers. Optional rebindable keyboard accelerator (empty by default) — the button is always the primary path.
- **Verified cart adapter (CT-002).** All game access sits behind a startup capability probe that verifies every member against the running game (18 members) and disables cart features with one actionable warning if a game update changes internals — the mod always keeps loading.
- **Bounded telemetry (CT-003).** A read-only sampler (configurable interval/radius/budgets with hard caps) tracks nearby carts: mass from the game's own formula, cargo weight, velocity, attachment and local pull state. State fully resets on logout/world switch; destroyed carts age out within seconds.
- **Terrain grade and surface (CT-004).** Deterministic grade math (smoothed, oscillation-free) along the pull-handle heading, and surface classification from the game's terrain paint (untouched/dirt/cultivated/paved) that reports "unknown" rather than guessing.
- **Safety posture.** No cart physics, mass, inventory, stamina, or network behavior is modified. Nothing is written to worlds or saves. Disabling or removing the mod leaves everything vanilla.

Product bootstrap (CT-001): independent plugin (`com.theconcernedcat.valheim.concernedteamster`), configuration, pure-domain test project, package metadata, and repository validation.
