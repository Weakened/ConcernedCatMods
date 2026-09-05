# Human attention ledger — Concerned Teamster

Questions that deserve owner awareness but do not block safe progress. Per the
CT-OPS-001 operating contract (#107), each entry records the safe reversible
default chosen so work could continue. Items marked "Must resolve before public
release: Yes" are repeated in the owner smoke checklist before the v0.9 public
beta and the v1.0 release.

Hard stops are **not** recorded here — they stop the conveyor. This ledger is
only for non-blocking uncertainty.

## Entry template

```markdown
### YYYY-MM-DD — Short title

- Version / issue: vX.Y / CT-0NN (#issue)
- Question: what was uncertain and why it matters.
- Safe reversible default selected: what the conveyor chose.
- Why work continued: why the default is safe and reversible.
- Risk / alternative: what the owner might prefer instead.
- Must resolve before public release: Yes/No
- Status: Open | Resolved YYYY-MM-DD — outcome.
```

## Open items

### 2026-09-04 — Generated placeholder package icon

- Version / issue: v0.1 / CT-001 (#109)
- Question: the Thunderstore package needs a 256x256 `icon.png` from day one
  (validation and packaging require it), but final storefront art is an
  owner-taste decision and Cartographer's icon was owner-provided artwork.
- Safe reversible default selected: a deterministic, license-clean cart glyph
  rendered by `tools/generate_teamster_icon.py` (pure stdlib, reproducible
  byte-for-byte), visually consistent with the Cartographer sprite language.
- Why work continued: the placeholder ships in no public release before v0.9;
  replacing `icon.png` is a one-file swap with no code impact, and CT-042
  (public docs/media audit) explicitly covers final storefront media.
- Risk / alternative: the owner may want commissioned/AI artwork matching the
  Cartographer icon's style before anything public; keeping the generated
  glyph is also viable.
- Must resolve before public release: Yes
- Status: Open

### 2026-09-04 — CT-002 startup probe log excerpt pending first in-game run

- Version / issue: v0.1 / CT-002 (#110)
- Question: the capability probe's startup log line (expected: "Cart
  telemetry capability ENABLED: 11 game members verified.") has not been
  observed in a live game session, because no TCT-Dev profile exists yet
  (profile automation is CT-043) and game launches are owner-interactive.
- Safe reversible default selected: ship the probe verified three other ways —
  members compiled against the publicized assemblies of the exact local build
  (0.221.12, see CART_INTERNALS.md), 14 unit tests over the probe mechanism
  including every simulated-missing-member path, and read-only adapter code
  that fails closed to null snapshots.
- Why work continued: the probe touches type metadata only; a wrong outcome
  cannot corrupt anything — worst case is a spurious WARN line or a disabled
  feature, both visible in the first real log.
- Risk / alternative: none beyond a cosmetic log surprise; the excerpt joins
  the v0.1 RC in-game campaign (CT-005) and the owner smoke checklist.
- Must resolve before public release: Yes
- Status: Open

### 2026-09-04 — CT-003 in-game telemetry spot check pending

- Version / issue: v0.1 / CT-003 (#111)
- Question: the displayed-vs-expected cargo-weight spot check and a live
  telemetry debug-summary log excerpt require a game session with a cart,
  which needs the TCT profiles (CT-043) and an interactive launch.
- Safe reversible default selected: ship the sampler verified off-game — 31
  new unit tests over scheduling, budget, rotation, store cap, eviction,
  reset, and zero-allocation fast paths; the cargo number itself is the
  game's own `GetTotalWeight()` relayed unmodified, with availability
  flagged when no container exists.
- Why work continued: telemetry is read-only and fails closed (capability
  gate, per-cart null results, no logging in the sample path); a wrong
  number would be a display defect, not a world-safety risk.
- Risk / alternative: none beyond a possible calibration surprise; the spot
  check joins the v0.1 RC in-game campaign (CT-005) and the vanilla-truth
  baseline of the test plan.
- Must resolve before public release: Yes
- Status: Open

### 2026-09-04 — CT-004 in-game grade/surface spot check pending

- Version / issue: v0.1 / CT-004 (#112)
- Question: the built-dirt-slope-vs-flat-ground grade spot check and a live
  surface classification screenshot need an interactive game session (TCT
  profiles arrive with CT-043).
- Safe reversible default selected: grade math and paint classification are
  fully fixture-tested off-game (flat, uniform slopes, crest, dip, noisy
  no-oscillation, channel table); the game-facing reads reuse the exact
  members Cartographer's shipped paint probe already exercises in
  production, plus `Heightmap.GetHeight`, all decompile-verified.
- Why work continued: read-only terrain getters cannot alter anything; a
  wrong grade would be a display defect caught by the RC campaign's
  marked-slope scenario.
- Risk / alternative: heading is anchored to the pull handle direction; if
  the vanilla prefab ever places the handle sideways the sign convention
  would need revisiting — the RC spot check covers exactly this.
- Must resolve before public release: Yes
- Status: Open

### 2026-09-04 — CT-005 panel visual check and v0.1 in-game campaign pending

- Version / issue: v0.1 / CT-005 (#113)
- Question: the Cart Status panel's visual placement (right-edge button at
  (-70, +170) from the right-center anchor, panel docked beside it), wood-
  panel readability, and the full v0.1 in-game campaign (vanilla truth
  baseline, cart/world lifecycle, uninstall safety, 30-minute perf session)
  need a real game session with TCT profiles (CT-043).
- Safe reversible default selected: ship the RC with every string and state
  proven headlessly (22 presenter tests), UI built on the exact GUIManager
  calls Cartographer ships in production, fail-closed session-disable on
  any UI exception, and the panel default-hidden until the player clicks
  the button. The RC is internal; nothing publishes.
- Why work continued: UI construction cannot touch world state; the worst
  visual outcome is an awkwardly placed button, a one-constant fix.
- Risk / alternative: button may overlap other HUD mods' elements;
  position constants are trivially adjustable and CT-033 owns UI polish.
  The full pending list is itemized in RELEASE_DOSSIER.md (v0.1 RC1).
- Must resolve before public release: Yes
- Status: Open

### 2026-09-04 — CT-006 in-game manifest-vs-container check pending

- Version / issue: v0.2 / CT-006 (#115)
- Question: the manifest-vs-container screenshot (same items, counts, and
  weights as the vanilla container UI shows) needs a live session; also
  whether quality-scaled weights (worn gear in a cart) display exactly as
  vanilla charges them.
- Safe reversible default selected: line weights come from the game's own
  `GetWeight()`/`GetNonStackedWeight()` (quality scaling included, verified
  by decompile), totals are the audited sum of known lines, and unknown or
  broken items become explicit markers — 14 unit tests over totals,
  ordering, immutability, fallbacks, and tracker call-count bounds.
- Why work continued: read-only container access cannot alter cargo; a
  display mismatch would be a defect caught by the v0.2 RC campaign
  (CT-010) with the manifest UI from CT-007.
- Risk / alternative: none beyond display accuracy; the check joins the
  v0.2 RC campaign and the owner smoke checklist.
- Must resolve before public release: Yes
- Status: Open

### 2026-09-04 — CT-007 manifest panel screenshot pending

- Version / issue: v0.2 / CT-007 (#116)
- Question: the manifest panel screenshot (sorting, filter typing, localized
  item names, full-cart responsiveness feel) needs a live session with a
  loaded cart.
- Safe reversible default selected: every behavior is proven headlessly (20
  presenter tests: full sort matrix, tie stability, case-insensitive
  filter incl. localized names, explicit states, localizer fallbacks), the
  UI re-renders only on data/sort/filter changes plus a 1 Hz tick, and the
  game localizer is reflective and falls back to raw tokens.
- Why work continued: read-only UI over tracker-bounded reads; worst case
  is a layout blemish, adjustable by constants (CT-033 owns polish).
- Risk / alternative: sort-arrow glyphs (▲▼) depend on the game font's
  glyph coverage; if missing they render as boxes — cosmetic, and the RC
  campaign will catch it.
- Must resolve before public release: Yes
- Status: Open

### 2026-09-04 — CT-008 calibration protocol runs pending

- Version / issue: v0.2 / CT-008 (#117)
- Question: the calibration data file ships with zero Measured rows — the
  protocol's set×ramp runs (5 cargo sets × 3 graded ramps, two reps) need
  interactive play in TCT-Clean/TCT-Dev profiles (CT-043). Also gravity is
  assumed (Unity default 9.81) in the derived joint-break bounds, each of
  which states the minimum gravity for which it still holds (≥4.4 m/s²).
- Safe reversible default selected: the shipped rows are labeled exactly
  what they are (Prior flat-pullability assumptions + DerivedConstant
  physics bounds from the verified break force); the dominance model
  answers "Unknown" everywhere else and never interpolates, so no player
  ever sees fake precision. Appending Measured rows sharpens verdicts with
  zero code changes (versioned data, not constants).
- Why work continued: an advisory model that says "uncalibrated" is safe;
  in-game runs are inherently owner/manual and the protocol document makes
  them reproducible.
- Risk / alternative: the owner may prefer different cargo sets or ramp
  targets; the protocol is a doc-only change.
- Must resolve before public release: Yes
- Status: Open

### 2026-09-04 — CT-009 in-game warning transcript pending

- Version / issue: v0.2 / CT-009 (#118)
- Question: a live transcript/screenshot of warning states (steep-climb
  caution rising and holding through grade dips, the panel warning row,
  the optional HUD hint while pulling) needs an interactive session on
  built test ramps.
- Safe reversible default selected: warnings are advisory text only, off
  the HUD by default, evaluated solely on new telemetry snapshots, with
  fixed hysteresis (exit −3%, 4 s fall hold) proven by 11 unit tests
  including the oscillation single-transition-pair property; Unknown
  calibration verdicts never warn, so no player is scared by uncalibrated
  guesses.
- Why work continued: a wrong warning threshold is a config-tunable
  display matter; nothing mutates carts and the RC campaign (CT-010)
  covers the visual check.
- Risk / alternative: the default 18% steep-caution threshold is a design
  prior until calibration rows sharpen it; documented in the config text.
- Must resolve before public release: Yes
- Status: Open

### 2026-09-04 — CT-011 descent calibration runs pending

- Version / issue: v0.3 / CT-011 (#121)
- Question: the descent data file ships with zero Measured rows — the
  protocol's descent runs (5 sets × 3 ramps × 3 entry speeds, two reps)
  need interactive play; the shipped rows are two stationary near-flat
  Held priors and two joint-break physics bounds (same gravity-floor
  notes as CT-008).
- Safe reversible default selected: the three-dimensional dominance model
  answers Unknown everywhere real descents live today, and the evaluator
  reports "not descending"/"no calibration data" states explicitly; the
  lookahead budget is fixed (≤ points+1 height reads per tick, config
  0–5, default 3). Nothing warns or mutates from these verdicts yet
  (CT-013/CT-014 own surfacing with their own hysteresis).
- Why work continued: an advisory model that says "uncalibrated" is safe;
  appending Measured rows sharpens verdicts with zero code changes.
- Risk / alternative: entry-speed banding (stand/walk/run) may need
  refinement after the first real runs; the protocol is a doc-only change.
- Must resolve before public release: Yes
- Status: Open

### 2026-09-04 — CT-012 in-game brake demonstration pending

- Version / issue: v0.3 / CT-012 (#123)
- Question: the slope hold/release demonstration (engage on a grade, cart
  holds; release restores vanilla rolling; wheels/joints behave while the
  root body is frozen; multiplayer authority hand-off releases) needs
  interactive sessions.
- Safe reversible default selected: engage is explicit-button-only behind
  five eligibility facts; every release path (player toggle, grab, walk-
  away, authority loss, capability loss, world exit, plugin shutdown,
  cart destruction) is unit-tested in the lifecycle matrix; the mutation
  is a single runtime `constraints` assignment that Valheim's save format
  cannot persist, so uninstall/reload is vanilla by construction.
- Why work continued: the worst in-game surprise is visual jitter of a
  frozen cart, reversible by the release button or any automatic path;
  no save/world state can be affected.
- Risk / alternative: freezing only the root body relies on wheel joints
  to hold the wheels — if wheels visibly dangle in-game, freezing all
  child bodies is a contained follow-up defect.
- Must resolve before public release: Yes
- Status: Open

### 2026-09-04 — CT-013 staged stuck scenarios pending

- Version / issue: v0.3 / CT-013 (#124)
- Question: staged in-game stuck scenarios (wheel against a rock on flat
  ground, chassis grounded on a terrain lip, genuine overload stall on a
  built ramp) and the panel screenshot with a live diagnosis need
  interactive sessions.
- Safe reversible default selected: the classifier is a fixed evidence
  table over already-shipped telemetry (no new game members), fires only
  after 2.5 s of pulled near-zero speed, does zero work for parked carts
  (pump gate + detector gate both tested), and answers "cause unclear"
  whenever evidence conflicts — 9 tests including the class confusion
  matrix.
- Why work continued: diagnostics are advisory text; a wrong class is a
  wording defect, not a safety issue, and the thresholds are documented
  constants.
- Risk / alternative: the mild-grade obstruction threshold (±8%) and the
  15% steep boundary are design priors pending real stuck scenarios.
- Must resolve before public release: Yes
- Status: Open

### 2026-09-04 — CT-014 guidance walkthrough pending

- Version / issue: v0.3 / CT-014 (#125)
- Question: the in-game guidance walkthrough (stuck cart → Guidance panel
  → follow the steps → cart freed) and the panel screenshot need staged
  interactive scenarios (shared with the CT-013 list).
- Safe reversible default selected: guidance is advisory text from a pure
  presenter with no adapter references (mutation audit in the PR); the
  quantitative unload step cites only proven load-model rows; 9 presenter
  tests cover every class, the unclear case, quantity math, and brake-step
  gating.
- Why work continued: text cannot mutate anything; a wording defect is
  the worst outcome.
- Risk / alternative: step wording may need play-tested tuning (CT-033
  readability pass also applies).
- Must resolve before public release: Yes
- Status: Open

### 2026-09-04 — CT-016 in-game trip recording check pending

- Version / issue: v0.4 / CT-016 (#128)
- Question: a real haul producing a sidecar file (attach, pull a route,
  detach, inspect the file; logout flush; world-switch isolation) needs an
  interactive session.
- Safe reversible default selected: the entire pipeline is proven off-game
  (13 persistence tests: recorder state machine incl. cap-splitting and
  debounce, codec round-trip with NaN markers, malformed-row skip,
  wrong-world and unknown-version refusals, prune renumbering, real-
  filesystem atomic-write crash simulation, backup-before-refusal); writes
  go only to Teamster's own config-path folder; Trips.Enabled=false turns
  the recorder off entirely.
- Why work continued: worst case is an empty or refused sidecar file —
  world saves cannot be touched by construction.
- Risk / alternative: sample cadence (1 s default) may want tuning once
  CT-017 scoring consumes real trips.
- Must resolve before public release: Yes
- Status: Open

### 2026-09-04 — CT-017 real-trip score sanity check pending

- Version / issue: v0.4 / CT-017 (#129)
- Question: whether scores from real hauls look sane (a smooth built road
  scoring less rough than raw meadows; a mud/water crossing showing a
  lower drag-proxy speed) needs recorded real trips.
- Safe reversible default selected: formulas are deterministic, documented
  in ARCHITECTURE.md with explicit limits (grade-jitter roughness, not
  height noise; mass-agnostic drag proxy), and proven on synthetic trips
  (9 tests incl. byte-identical incremental-vs-batch and the v1→v2
  migration recompute).
- Why work continued: scores are derived data in Teamster's own sidecar;
  wrong-looking scores are a calibration/interpretation issue for CT-019,
  not a safety issue.
- Risk / alternative: the 8 m cell size and 3% level band are design
  constants; real data may suggest different values (doc-only change).
- Must resolve before public release: Yes
- Status: Open

## Resolved items

None yet.
