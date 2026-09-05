# Changelog

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
