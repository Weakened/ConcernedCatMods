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

### 2026-09-04 — CT-018 history/comparison screenshot pending

- Version / issue: v0.4 / CT-018 (#130)
- Question: screenshots of the Trip History panel (sorting, A/B selection,
  two-step deletion) and a real side-by-side comparison need recorded real
  trips in an interactive session.
- Safe reversible default selected: presenters are fully headless-tested
  (14 tests: summary aggregates, 6-case sort matrix, text selection
  markers, empty/missing-selection states, normalized-distance alignment
  with a spike-position proof, invariant row formatting, deletion
  keeps-exactly-the-rest with segments untouched); the panel summarizes
  once per load so cost stays bounded at the 500-trip cap.
- Why work continued: read-only UI over Teamster's own sidecar; deletion
  is two-step confirmed, removes one raw trip, and is atomic.
- Risk / alternative: layout niceties (column alignment, row density) wait
  for CT-033.
- Must resolve before public release: Yes
- Status: Open

### 2026-09-04 — CT-019 in-game bottleneck view pending

- Version / issue: v0.4 / CT-019 (#131)
- Question: the bottleneck block on real recorded routes (does the located
  meter/percent point match where the haul actually struggled?) needs
  real trips and an interactive session.
- Safe reversible default selected: analysis is pure domain math over
  already-recorded data (7 tests: planted worst-grade and planted rough
  segment found and located exactly, load-binding verdicts equal direct
  LoadModel queries, uncalibrated coverage counted honestly, all
  no-data/invalid-mass paths explicit); recomputing for a hypothetical
  mass reads no game state.
- Why work continued: advisory text over sidecar data; a mislocated
  bottleneck is a formula/interpretation defect, catchable in the RC
  campaign.
- Risk / alternative: with mostly-Prior calibration, the load line will
  usually say "uncalibrated" until protocol runs land — by design.
- Must resolve before public release: Yes
- Status: Open

### 2026-09-05 — CT-021 integration reads Cartographer internals reflectively

- Version / issue: v0.5 / CT-021 (#134)
- Question: Teamster's route integration needs a read surface on Concerned
  Cartographer, but Cartographer is in public beta and changes to it happen
  only through its own issues — so CT-021 could not add a public API to
  Cartographer and instead binds reflectively to internal members
  (`Plugin._runtime` → `CartographerRuntime._routeStore` → `RouteStore.Living`
  → route/id/point properties).
- Safe reversible default selected: a written 12-member contract
  (CARTOGRAPHER_CONTRACT.md) with floor 0.10.0 (the first publicly
  distributed Cartographer; verified — the five runtime/domain contract
  files are byte-identical between released 0.10.0 and the current tree,
  and Plugin.cs differs only in its version constant and a comment, with
  the `_runtime` declaration unchanged), a full runtime member probe that
  hides the integration with one INFO line on any mismatch, and two
  validator gates: a bidirectional compile-time-independence audit and a
  source-level drift tripwire that fails the build if Cartographer renames a
  contract member.
- Why work continued: fail-closed by construction — the worst outcome of a
  broken assumption is a hidden feature plus one log line, never an error or
  atlas mutation; the monorepo tripwire converts silent breakage into a
  loud validation failure at the moment of the rename.
- Risk / alternative: the owner may prefer a small public, versioned
  integration surface inside Cartographer (a Cartographer-side issue, e.g.
  post-beta); the contract document plans that migration and only the
  adapter seam would change.
- Must resolve before public release: No
- Status: Open

### 2026-09-05 — CT-022 in-game route picker checks pending

- Version / issue: v0.5 / CT-022 (#135)
- Question: the picker screenshot with Cartographer installed (routes
  listed, one selected, ineligible route showing its reason) and the
  absent-case check (no Routes button anywhere without Cartographer) need
  interactive sessions with both TCT profiles and a Cartographer install.
- Safe reversible default selected: every listing, eligibility, selection,
  and invalidation behavior is proven headlessly (13 presenter tests over
  fake catalogs including mid-session deletion/archive/rename/geometry-loss
  and unreadable-catalog paths); the panel is built on the same GUIManager
  calls every shipped Teamster panel uses, is created only when the
  capability probe reported Available, fails closed on any UI exception,
  and holds selection in Teamster only (zero writes — the surface exposes
  no mutating call).
- Why work continued: a read-only list panel cannot touch world or atlas
  state; the worst visual outcome is layout polish owned by CT-033, and
  the absent case is structural (the panel class is never instantiated).
- Risk / alternative: with more routes than fit one page the panel shows
  "+N more" instead of scrolling — acceptable for v0.5, revisit with the
  UX sprint if real worlds overflow it.
- Must resolve before public release: Yes
- Status: Open

### 2026-09-05 — CT-023 in-game route profile check pending

- Version / issue: v0.5 / CT-023 (#136)
- Question: profiling a real drawn route in-game (numbers look sane against
  the visible terrain; unloaded far stretches show as UNSAMPLED meters;
  the load line matches the cart being pulled) needs an interactive
  session with Cartographer installed and a drawn route.
- Safe reversible default selected: the profiler is pure domain math proven
  by 21 tests (budget/cancel bookkeeping exact, sampled+unsampled meters
  partition the total by construction, gap and throwing-probe honesty,
  surface attribution, fingerprint cache invalidation, bottleneck verdicts
  asserted equal to direct LoadModel queries); the terrain probe reuses
  exactly the height/paint members the startup capability probe already
  verifies, read-only; per-frame work is capped at 24 samples.
- Why work continued: read-only terrain sampling over an advisory panel —
  the worst outcome is a wrong-looking number, catchable in the v0.5 RC
  campaign (CT-025) with a staged route over known ramps.
- Risk / alternative: 4 m sampling can miss sub-4 m spikes between
  positions (documented spacing constant); the RC campaign's ramp check
  covers whether the default needs tightening.
- Must resolve before public release: Yes
- Status: Open

### 2026-09-05 — CT-024 in-game route report demonstration pending

- Version / issue: v0.5 / CT-024 (#137)
- Question: the report panel demonstration on a real route (numbered
  problem sections matching the terrain, gap entries where terrain was
  unloaded, load lines matching the pulled cart) needs an interactive
  session with Cartographer installed.
- Safe reversible default selected: every rendering path is proven
  headlessly (10 presenter tests: all-clear, steep climb/descent, gap
  ranking with locations, no-model/no-mass states, verbatim LoadModel
  Explanation tracing); the panel is plain read-only text fed by the
  picker; the validator now enforces the read-only integration contract
  (mutating/invoking reflection tokens fail validation) so the
  no-atlas-mutation promise is automated, not aspirational.
- Why work continued: advisory text over already-computed profile data;
  the worst outcome is wording polish, owned by CT-033.
- Risk / alternative: the 15% problem threshold is a design constant
  (aligned with CT-013's steep boundary); real hauling feedback in the
  beta may argue for a config knob.
- Must resolve before public release: Yes
- Status: Open

### 2026-09-05 — CT-025 in-game coexistence matrix pending

- Version / issue: v0.5 / CT-025 (#138)
- Question: the v0.5 gate's coexistence matrix (both mods loaded with
  integration on; Teamster alone with it hidden; Cartographer alone
  unaffected; a version-mismatch floor simulation) and the "no new log
  exceptions during real coexistence runs" row require an interactive
  session in the TCT-Compat profile with Concerned Cartographer installed.
- Safe reversible default selected: the four detection paths are unit-proven
  off-game by the CT-021 gate tests (present / absent / version-too-low /
  probe-failure), the Cartographer suite stays 568/568 green with Teamster
  in the solution, and three validator audits (cross-product independence,
  contract drift tripwire, integration read-only) enforce no coupling and
  no atlas mutation. The RC is internal; nothing publishes.
- Why work continued: the integration is reflection-reads-only and
  fail-closed by construction, so a coexistence surprise can at worst
  hide a feature plus one log line — never corrupt a world or mutate
  Cartographer state; the seal is internal.
- Risk / alternative: none beyond confirming the log lines and button
  visibility in a real dual-mod launch; the rows join the owner smoke
  checklist alongside the CT-021..024 pending items.
- Must resolve before public release: Yes
- Status: Open

### 2026-09-05 — CT-026 authority policy proven off-game; live MP validation is CT-027

- Version / issue: v0.6 / CT-026 (#140)
- Question: the authority policy (who reads / acts / observes) is enforced
  and unit-proven over fake authority states, but real player-hosted and
  dedicated-server behavior — authority actually moving between clients, an
  unmodded peer genuinely unaffected — is observed only in a live multiplayer
  session, which is interactive and owner-run.
- Safe reversible default selected: the policy is the single source of truth
  the brake enforces through (test-asserted), resolution is fail-closed
  (`Unknown` denies mutation), and two validator audits prove the backing
  invariants (no outbound-network/ownership calls; every feature documented).
  CT-027 owns the in-game player-hosted and dedicated-server validation; this
  leaf deliberately stops at the enforced, audited policy.
- Why work continued: the brake was already authority-gated and fail-closed
  since CT-012; CT-026 formalizes and audits it without changing runtime
  behavior, so there is no new in-game risk to gate on here.
- Risk / alternative: none beyond confirming the matrix rows in a real
  multiplayer session, which is exactly CT-027's scope.
- Must resolve before public release: Yes (via CT-027's live validation)
- Status: Open

### 2026-09-05 — CT-027 live multiplayer scenario runs pending

- Version / issue: v0.6 / CT-027 (#141)
- Question: the authority scenario matrix (player-hosted two-client, dedicated
  server via TCT-Dedicated, cart authority handoff mid-haul, observation
  labeling, unmodded-peer coexistence) is proven at the logic layer but its
  in-game observation on real servers — including that the brake button
  disappears and panels re-label on a live handoff, and that an unmodded peer
  genuinely sees vanilla — needs interactive multiplayer sessions with a
  dedicated server and a second client.
- Safe reversible default selected: the policy logic is topology-independent
  (each client decides from its own authority) and is proven off-game by 7
  `MultiplayerScenarioTests` driving the handoff/flap/observer sequences, plus
  the CT-026 validator audit proving Teamster sends nothing and takes no
  ownership. The scenario matrix in TEST_PLAN.md marks every in-game row
  pending-manual with its proving test named.
- Why work continued: Teamster runs no server component and only reads
  authority; a real-topology surprise can at worst hide a feature or mislabel
  an observation, never corrupt a world or mutate a cart without authority.
- Risk / alternative: none beyond confirming the labeled rows on a real
  server; the rows join the owner smoke checklist.
- Must resolve before public release: Yes
- Status: Open

### 2026-09-05 — CT-028 coop classifier proven; live participant feed + staged scenario pending

- Version / issue: v0.6 / CT-028 (#142)
- Question: the cooperative-effort classifier (helping/hindering/idle/unclear)
  and its recovery-guidance integration are proven over synthetic multi-actor
  traces, but two things need a live session: (a) the adapter that reduces
  real nearby players into `CoopParticipant` observations from verified
  read-only surfaces (attachment is already replicated; contact and motion
  alignment need the exact multiplayer read surface validated in game before
  wiring), and (b) the staged co-op scenario (two players, one pushing the
  wrong way) confirming the classification matches what players see.
- Safe reversible default selected: ship the decision logic + guidance
  integration proven by 20 tests (full single-actor matrix, tally, name-safe
  summary, combined-effort explanation that never overrides the physical
  verdict), gated so the guidance shows crew context only when participants
  are supplied — production supplies none until the read surface is validated,
  so nothing can display a wrong co-op claim yet. Zero-force and
  privacy are validator/design enforced (no force APIs; names already
  visible; nothing transmitted).
- Why work continued: the classifier is advisory text over observations and
  applies no force by construction (audited); an unwired feed simply shows no
  crew line — it cannot mislead or mutate.
- Risk / alternative: the motion-alignment reduction and the 0.15 meaningful
  threshold are design priors; the staged scenario may argue for tuning
  (data/constant change). The contact/motion read surface must be verified in
  game (not invented) before the adapter feed lands — tracked here.
- Must resolve before public release: Yes
- Status: Open

### 2026-09-05 — CT-029 input hardening proven off-game; live lifecycle runs pending

- Version / issue: v0.6 / CT-029 (#143)
- Question: the network-input bounds/validity guards, single-shot logging,
  and staleness policy are proven by an adversarial matrix and a seeded fuzz
  sweep, but the live lifecycle rows (a teammate joining/leaving/disconnecting
  mid-haul, a world switch during observation) producing no exceptions and no
  stale mutating state need a real multiplayer session.
- Safe reversible default selected: ship the hardening proven by 44 tests
  (every hostile float → finite bounded output, a 10k-iteration seeded fuzz
  sweep that never throws or escapes bounds, gate single-shot + bounded +
  reset, staleness thresholds incl. fail-closed unknown age, and the snapshot
  producing a finite non-negative mass from any input), plus the committed
  PRIVACY_INVENTORY.md. Lifecycle resets (world switch clearing state) already
  ship from earlier sprints; the guards fail closed by construction.
- Why work continued: the guards only bound/drop values and the staleness
  policy only labels — no path mutates or transmits, so a live-lifecycle
  surprise can at worst show a stale-marked or bounded number, never corrupt
  or leak anything.
- Risk / alternative: the bound caps and the 5 s stale threshold are design
  priors; real multiplayer play may argue for tuning (constant change). The
  live join/leave/disconnect scenarios join the owner smoke checklist.
- Must resolve before public release: Yes
- Status: Open

### 2026-09-05 — CT-030 v0.6 RC sealed; live two-topology campaign pending

- Version / issue: v0.6 / CT-030 (#144)
- Question: the v0.6 RC integrates the multiplayer sprint, but the full live
  campaign on both topologies (player-hosted two-client + dedicated server:
  authority handoff, coop diagnostics, hardening spot checks, unmodded-peer
  coexistence) needs interactive sessions the owner runs.
- Safe reversible default selected: seal the internal RC with every
  automatable gate green — 458 Teamster + 568 Cartographer tests, five
  interop audits (independence, contract, read-only, authority/no-network,
  no-force/no-teleport), version-synced package with recorded hashes — and
  carry the live rows as pending (they aggregate the CT-021..CT-029 pending
  entries). Nothing publishes; publication is owner-only.
- Why work continued: the sprint's logic is unit-proven and fail-closed by
  construction; the live rows confirm behavior a running server produces,
  which cannot be automated on this machine.
- Risk / alternative: none beyond confirming the pending rows in game; the
  RC is internal.
- Must resolve before public release: Yes (the v0.9 beta gate consumes these)
- Status: Open

### 2026-09-05 — CT-031 navigation/binding logic proven; live controller wiring pending

- Version / issue: v0.7 / CT-031 (#146)
- Question: the focus-order navigation model and the accelerator
  conflict checker are proven off-game, but the live controller walkthrough
  (a gamepad actually moving focus through every panel with visible
  indication, accelerators firing and their conflicts warning in game) needs
  an interactive session, and the gamepad input read surface must be verified
  against the current game/BepInEx build before the adapter wiring lands.
- Safe reversible default selected: ship the deterministic focus catalog +
  ring and the external/internal conflict checker proven by 18 tests
  (traversal wrap, reachability, buttons-first over every panel, chord
  normalization, conflict matrices); the reserved-chord set is caller-
  supplied so no mod key is invented. Buttons-first already holds (every
  feature has a visible button today), so no accelerator-only path exists
  even before controller focus is wired.
- Why work continued: navigation and conflict detection are pure decisions
  over data; the worst outcome of the unwired state is that controller focus
  is not yet driven in game — no safety or world impact — and buttons-first
  keeps every feature operable by mouse meanwhile.
- Risk / alternative: the focus orders and the vanilla reserved-key list are
  design priors; the in-game walkthrough may argue for reordering or adding
  reserved binds (data change). Verified against the real gamepad surface
  before wiring, per "research, don't invent".
- Must resolve before public release: Yes
- Status: Open

### 2026-09-06 — CT-033 scale/contrast proven off-game; wood-panel background and canvas bounds are approximations

- Version / issue: v0.7 / CT-033 (#148)
- Question: the UI scale factor (0.8–1.3) is proven proportional and clamped
  in pure domain code, and every panel text color is contrast-audited, but
  three things are genuinely unverifiable without a live game session: (a)
  whether a scaled panel visually clips against a *neighboring* panel's
  fixed screen anchor (each panel's own offset from the screen edge is
  independent pixel math, not itself scaled); (b) whether the tallest panel
  (Trip History, 760 units, 988 at MaxScale) genuinely fits inside Jötunn's
  actual `CustomGUIFront` canvas — 1080 is a commonly-cited Valheim UI
  reference height chosen as a conservative planning assumption, not one
  read from a verified source; (c) the contrast audit's background color
  (`PanelPalette.ApproximateWoodBackground`) is a documented visual estimate
  of Jötunn's wood-panel texture, not a measured pixel value.
- Safe reversible default selected: uniform transform scaling is a Unity
  geometric guarantee (a panel's own children cannot newly clip relative to
  each other at any scale in the clamped range), proven in code via
  `AccessibilityTests`; `MaxScale` was set to 1.3 (not a rounder 1.5)
  specifically so the tallest panel stays under the 1080 reference-height
  assumption with margin, also test-proven; every panel text call that
  lacked an outline now has one (`outline: true`, with its own regression
  test scanning for a reintroduced `outline: false`), which is
  contrast-robust independent of the exact background tone — a sensitivity
  check against a lighter plausible estimate in `ACCESSIBILITY.md` shows
  this matters (unoutlined Header would drop to 4.15:1, below the 4.5:1 AA
  target, under that estimate). The scale value itself is read fresh from
  config on every panel (re)build, never cached, so a config edit is not
  stuck behind a full game restart.
- Why work continued: worst case is a cosmetic one — a larger scale setting
  looking slightly crowded next to another open panel or exceeding the
  actual canvas bounds by some margin, or text reading marginally less
  crisp — never a crash, never hidden content (Truncate/Overflow wrap modes
  are already in place), never a world-safety issue.
- Risk / alternative: the owner may prefer scaling font size and per-element
  offsets individually instead of the whole transform, for crisper text at
  the cost of touching every dimension in all six panels; the chosen
  approach trades a small crispness risk for structurally provable
  no-self-clipping and a much smaller, lower-risk diff. If the 1080
  reference-height assumption turns out wrong once verified in game,
  lowering `MaxScale` further is a one-constant change. Replacing the
  approximate background with a screenshot color-pick is likewise a
  one-constant change with no code impact.
- Must resolve before public release: Yes
- Status: Open

### 2026-09-06 — CT-034 onboarding/profiles logic proven off-game; hint placement and profile switch pending

- Version / issue: v0.7 / CT-034 (#149)
- Question: the onboarding show/dismiss decision and the config-profile
  resolution/idempotence/brake-opt-in logic are all pure and fully proven,
  but three things need a live session: (a) whether the onboarding hint
  button's text wraps and reads clearly at its chosen position/size (above
  the Cart button, ~280×44) rather than clipping or overlapping the button
  it points at; (b) whether switching `General.Profile` in the .cfg file and
  relaunching visibly changes the expected settings (warnings/HUD
  hint/trips/risk lookahead) while the brake stays whatever the player left
  it at; (c) that a player's own manual edit to an individual setting
  genuinely survives an unrelated restart (no `Profile` change) rather than
  being silently overwritten.
- Safe reversible default selected: the onboarding hint is a single
  non-modal button with no background dim and no other input blocked
  (construction guarantee — nothing else intercepts clicks while it shows);
  worst case is a layout blemish. Profile application only ever writes
  observational settings plus never touches `Brake.Enabled`
  (`Resolve_BrakeStaysOptInInEveryProfile`, all three profiles), and only
  fires when `ActiveProfile != LastAppliedProfile`
  (`ShouldApply_OnlyWhenActiveDiffersFromLastApplied`,
  `ShouldApply_SameActiveTwiceInARow_IsIdempotent`), so a manual edit is safe
  until the player deliberately changes the profile again.
- Why work continued: onboarding text/positioning is cosmetic (Truncate/Wrap
  modes are already set); profile application only writes already-existing,
  already-safe observational settings (nothing new mutates), so a wrong
  value is a display/config surprise, never a safety issue — and the brake
  exclusion is enforced by a test that runs over all three profiles, not by
  hand-checking each one.
- Risk / alternative: the hint's exact wording/position may need play-tested
  tuning (a text/constant change, no logic change); the owner may prefer a
  profile *picker panel* in the UI instead of a raw config-file enum — the
  current design keeps this leaf's scope to what CT-034 actually asked for
  (documented presets + explicit config switch), with a UI picker a
  reasonable CT-042/CT-043 polish candidate if desired later.
- Must resolve before public release: Yes
- Status: Open

### 2026-09-06 — CT-037 precedence policy wired end-to-end and exercised against a real mass-altering mod; in-game matrix still pending

- Version / issue: v0.8 / CT-037 (#153)
- Question: `PROJECT.md`'s "Better Carts" reference could not be pinned to
  one exact, GUID-verified Thunderstore package. Two real candidates were
  researched: BetterCarts (TastyChickenLegs, 46K downloads) — GUID verified
  directly from its published source — and Better Cart (We_Haul, 2.2K
  downloads) — a much closer conceptual match (it explicitly customizes
  min/max cart mass), but its GUID could not be verified: no linked source
  repository, and Thunderstore's decompiled-source viewer for it did not
  yield readable content through available tooling. Verifying it further
  would mean downloading and inspecting the mod's compiled binary, which
  this leaf treats as beyond an autonomous research pass without the
  owner's awareness first. **Correction during review:** the first research
  pass on BetterCarts read only `Plugin.cs` and the README and concluded no
  physics impact; that was wrong — its `Patches/CartPatches.cs` installs a
  Harmony prefix on `Vagon.SetMass` that reduces cart mass by a default 20%
  (`Patches/CartConfigs.cs`: `cartMassReduction = 0.2f`, applied whenever
  `allowPlayerHelp` is false, itself the default), found only once an
  independent review re-inspected the mod's `Patches/` folder directly.
- Safe reversible default selected: register the GUID-verified candidate
  with its corrected classification (BetterCarts,
  `Adapt`/`AffectedAspect.CartMassOrPhysics`) rather than guess at We_Haul's
  GUID. The precedence mechanism
  (`CompatibilityAdvisoryGate.CartMassAdviceReliable`, read through
  `CompatibilityAdapter.CartMassAdviceReliable`) is generic over
  `CompatibilityAffectedAspect.CartMassOrPhysics` and is now wired into
  every LoadModel-/RiskModel-derived consumer: cart warnings, stuck
  diagnosis (`StuckDetector` → `CartDiagnosis.LoadAdviceUnavailable`,
  inherited automatically by recovery guidance), the route profile/report/
  trip-history bottleneck lines, and the descent-risk debug log. The
  parking brake was audited and needs no gate — `BrakeFacts` carries no
  mass field and the brake stack never calls `LoadModel`/`RiskModel`.
  Registering a future mass-altering mod (a corrected We_Haul GUID, or
  CT-038's broader research) requires only a new registry entry, no logic
  change.
- Why work continued: the mechanism and every consumer are exhaustively
  unit-tested against both fake probes and the real shipped `BetterCarts`
  entry (`CompatibilityAdvisoryGateTests`, `StuckDetectorTests`,
  `RecoveryGuidancePresenterTests`, `RouteProfilerTests`,
  `RouteReportPresenterTests`, `RouteBottleneckTests`); correcting the
  classification in place (rather than shipping the wrong one and filing a
  separate defect) matches this repo's "don't silently weaken acceptance
  criteria" rule.
- Risk / alternative: the owner may already know We_Haul's Better_Cart's
  exact GUID (or may prefer a different mod entirely as "the" physics-
  altering target) — supplying it turns this into a one-line registry
  addition.
- Must resolve before public release: Yes — the full in-game coexistence
  matrix (telemetry, manifest, load model, risk model, brake, diagnostics —
  this issue's own acceptance criteria) with BetterCarts actually installed
  alongside Teamster (`TCT-Compat` profile) must be run and captured before
  any public release claims compatibility awareness. Everything short of
  that live observation is now in place and unit-proven.
- Status: Open

### 2026-09-06 — CT-038 registered two more real mass-relevant mods; presence-only detection can't see another mod's live config

- Version / issue: v0.8 / CT-038 (#154)
- Question: research the current Thunderstore Valheim landscape (not just
  re-litigate "Better Carts") for maintained mods touching carts, cart
  physics, item weights, or container behavior, and register the
  significant combinations. A broad sweep (cart-specific, item-weight-
  specific, container-specific, and all-in-one-overhaul searches) surfaced:
  **ItemStacks** (mtnewton, 306,955 downloads — GUID `net.mtnewton.itemstacks`
  verified from `ItemStacksPlugin.cs`) reduces every item's weight by a
  default 90%, on by default; **ValheimPlus** (186,034 downloads — GUID
  `org.bepinex.plugins.valheim_plus` verified from `ValheimPlus.cs`) has an
  optional Wagon section that can change cart mass, shipped disabled, and
  disabled its patch reproduces vanilla's mass formula exactly (confirmed
  from the patch's own disabled-branch logic, its config section's
  defaults, and the shipped default config template — three independent
  points, not one). A renewed attempt at We_Haul's "Better Cart" GUID
  (1,793 downloads) again failed — still no linked source repository.
  Several player-carry-capacity mods (SkilledCarryWeight,
  FascinatingCarryWeight, "Skills Give More Carry Weight", PlecakDlaCweli,
  InfinityInventory) and container-grid-sizing mods (CustomContainerSizes,
  Bigger Chests) were found and ruled out as outside this compatibility
  surface — documented with reasoning, not silently dropped.
- Safe reversible default selected: register ItemStacks
  `Adapt`/`AffectedAspect.CartMassOrPhysics` (same treatment as BetterCarts,
  for the same reason — its default behavior demonstrably alters cargo
  weight). For ValheimPlus, register `CompatibilityPolicy.Warn`/
  `AffectedAspect.None` rather than `Adapt`/`CartMassOrPhysics` — its
  cart-mass effect is not a shipped default but an explicit, separate
  per-player config choice (`[Wagon] enabled=true`) that this framework's
  presence-only detector cannot observe. Tagging it `CartMassOrPhysics`
  would suppress accurate load advice for every install that never touches
  that one optional section out of ValheimPlus's dozens of features — a
  real cost given the mod's popularity, not a hypothetical one. `Warn`
  still tells the (likely smaller) subset who *did* enable it to check
  their own config, via the Compat panel description.
- Why work continued: both registrations are backed by direct source
  verification (GUID plus the exact behavior, not a README description
  taken on faith — the exact gap that caused CT-037's own defect); the
  We_Haul non-finding and the ruled-out categories are documented with
  reasoning rather than silently omitted, matching this repo's evidence
  standard.
- Risk / alternative: **presence-only detection is a real, now-demonstrated
  architectural limit.** ValheimPlus is the first registered mod whose
  mass-relevance depends on another mod's own live configuration rather
  than its mere installation — `Adapters/CompatibilityAdapter.Lookup` reads
  only `Chainloader.PluginInfos` (is the mod present, what version), never
  another mod's live configuration. A player who has both Teamster and a
  Wagon-section-enabled ValheimPlus will see a `Warn`-level note, not a
  suppressed reading — correctly cautious, but not as strong a guarantee as
  the `Adapt` entries provide. Extending `CompatibilityAdapter` to probe a
  specific known mod's specific config value is a real, buildable
  follow-up in principle, but harder than mirroring the presence probe: the
  Wagon setting lives in ValheimPlus's own `ServerSyncConfig<T>` POCO
  scheme (verified from source), not a standard BepInEx `ConfigEntry<T>`
  binding, so it would need per-mod, format-specific reading logic rather
  than one generic mechanism. Out of scope for a research/registration
  leaf either way.
- Must resolve before public release: Yes — the full in-game coexistence
  matrix (per this issue's acceptance criteria) with ItemStacks and
  ValheimPlus actually installed alongside Teamster (`TCT-Compat` profile,
  ValheimPlus tested in both its default-disabled and an explicitly-enabled
  Wagon-section configuration) must be run and captured before any public
  release claims compatibility awareness for these entries. The We_Haul GUID
  gap remains open across two research passes now — resolving it needs
  either the owner supplying the real GUID or a future leaf explicitly
  authorized to inspect the compiled binary.
- Status: Open

### 2026-09-06 — CT-039 recovery/migration/support-bundle mechanism proven off-game; two small pre-existing gaps noticed in passing

- Version / issue: v0.8 / CT-039 (#155), folding in DEF-teamster-v0.4-001
  (#189)
- Question: DEF-teamster-v0.4-001 flagged two weaknesses in the trip
  sidecar backup path — fixed-name backups clobber on a repeat event, and
  the backup-before-rewrite ordering lived only in untestable Adapters-
  layer control flow. CT-039's own scope (config/data migration, backup/
  recovery, sanitized support bundle) directly overlaps this subsystem, so
  the defect was folded into this leaf rather than filed and fixed
  separately.
- Safe reversible default selected: bounded backup rotation (3 generations
  per reason, `SidecarFileStore.MaxBackupGenerationsPerReason`) replacing
  the fixed-name overwrite; the ordering guarantee extracted into a pure,
  tested `TripPersistPlan.Decide` function that `TripRecordingService
  .Persist` now merely executes; the previously-unbacked-up
  malformed-rows case now backed up too (a real, related gap found while
  rebuilding this path, not originally listed in the defect). Config
  schema versioning introduced via the one migration every existing
  install actually goes through (see `RECOVERY.md` for why no file backup
  was added for that specific step). Support bundle sanitization mirrors
  Concerned Cartographer's proven regex technique, reimplemented
  independently (no compile-time coupling permitted between products).
- Why work continued: every new mechanism is exhaustively unit-tested
  against a real filesystem or realistic planted content
  (`TripPersistenceTests`, `TripPersistPlanTests`,
  `ConfigSchemaMigrationTests`, `SupportBundleTests`); nothing here
  changes vanilla behavior or touches a Valheim save.
- Risk / alternative: two small, pre-existing gaps were noticed while
  touching adjacent code and are recorded here rather than silently fixed
  or silently ignored:
  1. `NavigationCatalog` (CT-031) never gained an entry for the
     Compatibility panel's own Close button when CT-036 shipped it
     (only the main panel's Compat *button* is cataloged now, added
     alongside this leaf's own Support button). Fixing the main-panel
     button entries was in scope for this leaf since it touches the same
     list; fully building out `CompatibilityPanel`'s own sub-navigation
     entry was not (a separate panel, a CT-036 leftover) and is left for
     a future accessibility pass.
  2. The Cart Status panel grew from `PanelHeight = 410f` to `450f` to fit
     a third button row (Support, beside Compat) without crowding the row
     -text block above it. The panel-height/MaxScale margin assumption
     `AccessibilityTests.MaxScale_TallestPanelStaysUnderConservativeReferenceCanvasHeight`
     checks is pinned to `TripHistoryPanel` (760f), not this panel, so
     this change does not affect that guard — but the new row's actual
     in-game clearance at `UiScaleOptions.MaxScale` has not been visually
     confirmed, only computed.
- Must resolve before public release: No for both — neither blocks a
  release claim on its own; both are visual/accessibility polish items
  suitable for CT-041's public-beta hardening pass or a dedicated small
  leaf. The recovery/migration/support-bundle mechanism itself has no
  "must resolve" gap beyond the in-game observation already noted in the
  test-plan evidence table.
- Status: Open

### 2026-09-06 — CT-040 v0.8 RC sealed; live corruption/compatibility/scale campaign pending

- Version / issue: v0.8 / CT-040 (#156)
- Question: the v0.8 RC integrates the compatibility, recovery, and scale
  sprint, but the full live campaign — killing the process mid-write and
  confirming rotated-backup recovery, migrating a real pre-v1 sidecar,
  exporting and reading a real support bundle, installing BetterCarts /
  ItemStacks / ValheimPlus and confirming the Compat panel and load-advice
  gate for each, and a long real hauling session in a dense cart cluster —
  needs interactive sessions the owner runs.
- Safe reversible default selected: seal the internal RC with every
  automatable gate green — 609 Teamster + 568 Cartographer tests, a
  filtered rerun of the CT-039 recovery/migration/support-bundle suite
  (47/47) and the CT-036..038 compatibility suite (18/18) as their own
  campaigns, the same five interop audits, three new scale tests proving
  max-retention/many-entry/dense-cluster-long-session behavior with real
  measured numbers recorded in `RELEASE_DOSSIER.md`, and a version-synced
  package with recorded hashes built from a clean (non-dirty) commit —
  confirmed by reading the shipped DLL's own InformationalVersion back
  after the build rather than assuming it. Nothing publishes; publication
  is owner-only.
- Why work continued: every new/rerun mechanism is unit-tested (real
  filesystem fixtures for recovery, a fake cart world for the sampler
  scale tests); the live rows confirm behavior only a running game can
  produce.
- Risk / alternative: none beyond confirming the pending rows in game; the
  RC is internal. The one genuinely new scale finding — the telemetry
  sampler's practically-reachable tracked-cart ceiling (27) falling below
  its configured cap (32) in a dense cluster — is a documented design
  tradeoff (freshness over coverage), not a defect; see
  `RELEASE_DOSSIER.md`'s v0.8 scale measurement table for the derivation.
  This leaf's own independent review additionally found that the recorded
  DLL/ZIP hashes do not reproduce across separate rebuilds/environments
  (filed as DEF-teamster-v0.8-001, #214, P3, deferred) — a release-
  engineering traceability gap present since the v0.1 RC, not a CT-040
  regression, and with no safety/correctness/gameplay impact; the dossier
  entry was corrected to record hashes as an honest build fingerprint
  rather than a reproducibility claim.
- Must resolve before public release: Yes (the v0.9 beta gate consumes
  these, same as every prior RC's live rows)
- Status: Open

### 2026-09-06 — CT-041 feature/default freeze and feedback path proven off-game; Report a Bug button's real browser-open is pending

- Version / issue: v0.9 / CT-041 (#158)
- Question: the freeze document, default snapshot test, and privacy audit
  are all provable without a running game — but the Report a Bug button
  calls `UnityEngine.Application.OpenURL`, a real-OS-interaction API whose
  actual behavior (does the correct browser/URL actually open, does it
  work under whatever sandboxing a BepInEx-injected process might have)
  cannot be verified without a live session.
- Safe reversible default selected: freeze the feature/default surface in
  `FEATURE_FREEZE.md`, lock every Domain-testable default with
  `DefaultFreezeSnapshotTests`, extract seven previously-inline config
  literals into `TeamsterDefaults` (Domain-layer, so the lock is real
  rather than trusting an Adapters-layer literal by inspection alone),
  add a CI-gated privacy audit (`check_teamster_no_internet_egress`) that
  scans every shipped `.cs` file for internet-egress APIs and explicitly
  documents `Application.OpenURL` as the one intentional, data-free
  exception, and route the new Report a Bug button and the README's
  Support section at the same frozen `FeedbackLinks.IssuesUrl` constant
  so they cannot drift apart.
- Why work continued: `Application.OpenURL` is a long-established, widely
  used Unity API for exactly this purpose (many BepInEx mods link a wiki
  or Discord this way); worst case if it silently no-ops in some odd
  environment is that the button does nothing, which is safe (no data
  sent, no crash) — never worse than the button not existing.
- Risk / alternative: none beyond confirming the click actually opens a
  browser in a real session. An alternative (display the raw URL as
  static, non-interactive text instead of a button) was considered and
  rejected: it satisfies "visible" but not the issue's explicit ask for a
  "button", and offers strictly less discoverability for the same privacy
  profile (OpenURL sends no data either way).
- Must resolve before public release: Yes (the v0.9 beta gate consumes
  this, same as every prior RC's live rows) — add the confirmed row to
  the owner smoke checklist once clicked in a real session.
- Status: Open

### 2026-09-06 — CT-042 public docs and security audit complete; real-gameplay media and the icon-art decision remain owner tasks

- Version / issue: v0.9 / CT-042 (#159)
- Question: CT-042's scope asks for "current screenshots (and GIF where
  useful) produced from real gameplay" with the explicit constraint that
  "media reflects the current build (no mockups)". No screenshot or GIF
  of Concerned Teamster exists anywhere in this repo yet, and producing
  one honestly requires an actual interactive Valheim session (launching
  the game, loading a disposable world, positioning a cart, opening the
  real panels) — not something safely automatable from this environment,
  and exactly the kind of task `Prepare-TCC-Screenshot-Profile.ps1`
  already exists, untracked, at the repo root to support.
- Safe reversible default selected: complete every part of CT-042 that
  does not require a live game session — the package README now has an
  accurate feature list (cross-checked against `FEATURE_FREEZE.md`), a
  privacy paragraph distilled from `PRIVACY_INVENTORY.md`, an AI-
  assistance disclosure paragraph (repository-policy wording from
  `AI_DEVELOPMENT.md`, adapted for Teamster), and its existing
  compatibility section re-verified against `COMPATIBILITY.md`'s CT-038
  evidence (no drift found). `NOTICE.md` and the root `README.md` had
  Concerned Teamster added by name (both previously named only
  ConcernedCatMods and Concerned Cartographer — a real, if minor,
  pre-existing gap this leaf closed rather than left for later). A
  committed security self-audit (`SECURITY_AUDIT.md`) checked dependency
  pins, secrets, the package copy-list, and every `Adapters/` file's
  fail-closed behavior; findings below. No screenshot/GIF was added, and
  none was faked — the gap is recorded here instead.
- Why work continued: every other CT-042 deliverable (README accuracy,
  CHANGELOG completeness — already complete from v0.1, LICENSE
  correctness — already correct, Thunderstore categories — already
  correct, the security audit) is fully provable from the committed
  source tree without a running game; only the media bullet needs one.
- Risk / alternative: none beyond the pending media capture and the
  `icon.png` art decision (carried from the CT-002 entry, which named
  CT-042 as the leaf that would revisit it — dimensions are already
  validator-compliant at 256×256; whether to commission different art is
  a taste decision for the owner, not something this leaf can or should
  decide unilaterally).
- Security audit findings: (1) `SupportBundleExporter.Export`'s doc
  comment overstated "never throws" — fixed inline, a 1-line comment
  correction, no behavior change (the narrow theoretical exception was
  already caught one layer up by `SupportBundlePanel`'s existing
  try/catch). (2) `CartTelemetryPump.Update()`, the per-frame telemetry
  driver, has no top-level fail-closed guard unlike every `Ui/*Panel.cs`
  file — filed as DEF-teamster-v0.9-002 (#217, P2), deferred to CT-044
  since fixing it is a core-adapter code change deserving its own
  focused review, not something to bundle into a documentation leaf.
  **Fixed in CT-044** — see that entry below.
- Must resolve before public release: Yes for the media capture (the
  v0.9 beta gate expects real screenshots); the icon-art question is the
  owner's call and does not block the internal RC seal either way.
- Status: Open

### 2026-09-06 — CT-043 profile family scripted at the file level; profile creation and third-party installs stay owner GUI actions

- Version / issue: v0.9 / CT-043 (#160)
- Question: CT-043 asks for "idempotent profile automation" for
  TCT-Clean/Dev/Compat/Dedicated. Investigating the actual mod manager on
  this machine (Thunderstore Mod Manager) found that only `TCC-Dev`
  (Cartographer's) and a few other TCC-* profiles exist — **no TCT-*
  profile has ever been created**, and creating one is registered through
  the mod manager's own GUI/database (each profile's folder holds a
  `mods.yml` package manifest the manager itself maintains), not a
  documented, stable file format this repo's tooling should try to
  reverse-engineer or drive headlessly.
- Safe reversible default selected: script everything that is safely
  repo/script-scoped — `deploy.ps1` gained a `-Profile Dev|Compat|
  Dedicated` parameter (backward-compatible default `Dev`, Cartographer's
  own path untouched) reading three `Environment.props` keys
  (`TEAMSTER_DEPLOYPATH`/`_COMPAT_DEPLOYPATH`/`_DEDICATED_DEPLOYPATH`),
  and a new `rehearse-teamster-lifecycle.ps1` proves fresh-install/
  upgrade/uninstall/idempotence entirely inside a scratch directory using
  real, actually-built packages (including a real historical v0.7.0
  build via a temporary, always-cleaned-up `git worktree`) — see
  `PROFILE_REHEARSAL.md` for the full design and what it does and does
  not prove. Creating each profile once (New Profile → Install BepInEx)
  and, for TCT-Compat, installing the three real compatibility mods from
  Thunderstore's own browser, are documented as one-time owner steps in
  the same doc — never attempted by this leaf.
- Why work continued: nothing in this leaf touches a real mod-manager
  profile, downloads third-party content, or installs anything outside
  the repo's own `artifacts/` scratch space and system temp; the file-
  level rehearsal genuinely proves what it claims (real builds, real
  hashes, real worktree checkout of a real historical tag), and every
  claim it cannot make (BepInEx actually invoking a migration on a live
  launch, the mod manager actually recognizing a profile) is named
  explicitly rather than implied.
- Also discovered and fixed in passing: `concerned-teamster/v0.5.0`
  through `v0.8.0` were never tagged (only v0.1.0–v0.4.0 were, despite
  `docs/DEVELOPMENT.md`'s versioning policy requiring a namespaced tag
  per release) — backfilled all four as annotated tags pointing at each
  version's exact dossier-cited source commit (verified against each
  commit's own `csproj` `<Version>` before tagging), since the chain-
  integrity check this leaf's rehearsal performs needed the full tag set
  to be meaningful. Tags are additive and were not force-pushed or moved.
- Incidental note: verifying `deploy.ps1`'s Cartographer code path was
  genuinely untouched by this change required actually running it once
  with `-SkipBuild`, which redeployed the current main-branch
  Cartographer build to the real TCC-Dev profile — a normal, expected,
  fully-reversible action for that profile's own stated purpose (and the
  DLL was fresh, rebuilt as a side effect of this session's own earlier
  `build.ps1` runs), not a new risk, but noted here for transparency
  since it touched real state outside the repo.
- Risk / alternative: the owner may use a different mod manager than
  Thunderstore Mod Manager on another machine, in which case the
  `Environment.props` path convention still applies (any tool that
  produces a `BepInEx/plugins/` folder works) but the "New Profile"
  clicking steps in `PROFILE_REHEARSAL.md` would need adapting to that
  tool's own UI.
- Must resolve before public release: Yes — the four profiles need to
  exist for real and the owner-smoke-checklist rows in
  `PROFILE_REHEARSAL.md` need an actual in-game pass before the v0.9 beta
  gate closes.
- Status: Open

### 2026-09-06 — CT-044 defect burndown complete; the compiled smoke checklist has never been run

- Version / issue: v0.9 / CT-044 (#161)
- Question: CT-044's two jobs are (a) burn down every open in-scope
  defect to zero P0/P1/P2, and (b) compile every pending manual claim
  scattered across this ledger (CT-001 through CT-043, ~30 open entries)
  into one ordered, owner-runnable checklist. Neither job can itself
  produce new in-game evidence — compiling a checklist is not running it.
- Safe reversible default selected: fixed the one open P2
  (DEF-teamster-v0.9-002, #217 — `CartTelemetryPump.Update()` now wraps
  its body in the same try/catch-and-disable pattern every `Ui/*Panel.cs`
  file already uses, renamed to `UpdateCore()`), leaving both P3s
  (DEF-teamster-v0.8-001 build non-determinism, DEF-teamster-v0.9-001 the
  flaky allocation test) deferred exactly as their own filed rationale
  already states — CT-044's own scope explicitly asks for P0–P2 fixed and
  P3s *documented*, not fixed. Compiled `PRE_RELEASE_SMOKE_TEST.md`: every
  section traces to a specific still-open ledger entry (cross-reference
  table at the top of that file), organized by feature area rather than
  issue-number order so related checks share one session/setup instead of
  the owner repeating world/cart setup ~30 times. Dry-ran every
  automatable piece of the checklist's preamble (package audit via
  `validate_repo.py --require-binary`, all three `deploy.ps1 -Profile`
  paths, the full `rehearse-teamster-lifecycle.ps1` fresh-install/upgrade/
  uninstall proof) — all green, cited with fresh evidence in this leaf's
  PR — but the actual profile-creation clicks and every in-game row stay
  exactly as pending as they were before this leaf, now just organized
  into one document instead of scattered across thirty.
- Why work continued: the fix touches only the one adapter file the
  filed defect named, mechanically mirroring an already-proven pattern
  (no new decision logic, nothing to extract to Domain); the checklist
  compilation adds no new claim beyond what was already recorded open
  elsewhere in this ledger — it reorganizes, it does not invent.
- Risk / alternative: the owner may prefer a different session grouping
  than the one chosen here (by feature area); reordering rows is a
  doc-only change with no dependency on anything else in this leaf.
- Must resolve before public release: Yes — running the compiled
  checklist for real is exactly the v0.9 beta gate's remaining human work.
- Status: Open

### 2026-09-06 — CT-045 v0.9 public beta candidate sealed; this issue carries a human-preview gate the conveyor is honoring

- Version / issue: v0.9 / CT-045 (#162)
- Question: CT-045 is labeled `gate:human-preview` ("Must stop for
  integrated human preview and approval") — the first Teamster issue in
  this whole conveyor to carry that label. Every prior sprint-closing
  seal (CT-030, CT-035, CT-040) sealed an explicitly internal-only RC and
  the conveyor continued immediately into the next sprint with no pause;
  v0.9 is different in kind, not just degree — it is the first RC ever
  sealed as an actual publication candidate, and the label makes explicit
  what the issue's own text already implied ("conveyor proceeds to v1.0
  work without publishing" — a hand-off, not a mid-sprint checkpoint).
- Safe reversible default selected: complete CT-045's own work in full —
  version-sync, package, hash, the v0.9 dossier entry, and a new
  `OWNER_PACKET_v0.9.md` (RC identity, proven-vs-pending summary, exact
  owner-only publish steps adapted from Cartographer's own
  `V1_RELEASE_PREP.md`, and rollback/reapproval guidance) — merge the PR
  and close both this issue and sprint controller #157 with evidence,
  exactly like every prior leaf. Then **stop** rather than beginning
  CT-046 automatically, per the label. Nothing about sealing or
  documenting is itself a publish action, so this work is safe to
  complete without a human already having reviewed it; the label gates
  what happens *after*, not the sealing work itself.
- Why work continued (through the seal, not past it): every automatable
  gate is provably green (622 Teamster + 568 Cartographer tests, three
  zero-violation interop audits, a real fresh-install verification of
  the sealed ZIP, a full lifecycle rehearsal against real artifacts);
  none of that requires a human to have looked at it first, and stopping
  mid-seal would leave the repository in a half-versioned state that
  benefits no one. Continuing into v1.0 territory (CT-046) without the
  owner having reviewed a candidate that could become the project's
  first public release is a different, and real, kind of risk this
  session has not faced at any earlier gate.
- Also discovered and fixed in passing: `scripts/publish.ps1` was
  hardcoded to Concerned Cartographer only (no `-Product` parameter,
  Cartographer's path and package name baked into both the confirmation
  prompt and the `tcli publish` config-path argument) — the owner packet
  cannot honestly tell the owner to run this script for Teamster if it
  literally cannot target Teamster. Fixed by adding the same
  `-Product ConcernedCartographer|ConcernedTeamster` parameter
  `package.ps1`/`deploy.ps1` already use, verified by invoking it with
  each product and confirming it fails at the pre-existing
  `TCLI_AUTH_TOKEN` check (the correct, safe failure point in this
  environment) before ever reaching `package.ps1`, the interactive
  confirmation, or `tcli publish` itself — this leaf never sets that
  token and never types the confirmation string, so no publish action
  was possible at any point.
- Risk / alternative: none beyond the obvious — this packet's entire
  point is that the owner's review is the risk-reducing step, not
  something to route around. If the owner prefers the conveyor continue
  into v1.0 work on internal RCs regardless of the beta's publication
  status, that is their call to make explicitly (e.g. by removing the
  label from a future equivalent issue, or by directly instructing the
  next session), not something this session should infer from silence.
- Must resolve before public release: N/A — this entry describes the
  seal itself. The pending item is the owner packet's own sign-off
  checklist, in `OWNER_PACKET_v0.9.md`.
- Status: Open

### 2026-09-06 — CT-046 golden path and v1.0 DoD matrix audited; no new defect found

- Version / issue: v1.0 / CT-046 (#164)
- Question: CT-046's scope is audit-only ("this issue only proves or
  files") — every product promise through v0.9 needed to be mapped to
  its evidence source, with any gap becoming a defect or a documented
  limitation.
- Safe reversible default selected: `GOLDEN_PATH.md` (an 8-step
  install-to-uninstall narrative) and `V1_DEFINITION_OF_DONE.md` (a
  capability-by-capability matrix, mirroring Cartographer's own
  precedent doc) were compiled by cross-referencing every already-shipped
  feature against its actual test files and `HUMAN_ATTENTION.md`'s
  already-open pending items — not by re-deciding anything.
- Why work continued: this leaf changes no code and fixes nothing; it is
  read-and-document work with no risk of its own beyond the audit being
  inaccurate, which independent review checks for like any other claim.
- Risk / alternative: the audit's own honest conclusion is that no new
  defect exists beyond the two already-filed, already-deferred P3s
  (#214, #216) — every other gap (We_Haul's GUID, the presence-only
  detection limit, the NavigationCatalog cosmetic gap, media capture,
  profile creation) was already known and already documented before this
  leaf, just not previously collected into one matrix. The owner may
  read this differently and find a gap this pass missed; the matrix is
  structured so a missed row would be conspicuous (every capability area
  from `FEATURE_FREEZE.md` is represented) rather than silently absent.
- Must resolve before public release: No — this is a documentation leaf;
  the underlying pending items it cross-references keep whatever
  resolution requirement their own original entry already states.
- Status: Open

### 2026-09-06 — CT-047 full regression reruns clean from a fresh clone; in-game layer still needs the profiles CT-043 already flagged

- Version / issue: v1.0 / CT-047 (#165)
- Question: does the committed tree at the current `main` tip still
  build, pass its full test suite, and package cleanly when checked out
  from nothing but a bare clone — not just in this conveyor's own
  long-lived working tree, which could have accumulated stale generated
  files or cache state a fresh checkout would expose?
- Safe reversible default selected: a genuinely separate `git clone` of
  `main` into a scratch directory, with only the gitignored
  `Environment.props` copied in, then the full build+test+package cycle
  run there from scratch. `REGRESSION_REPORT_v1.0.md` records the
  result: 622 Teamster tests, 568 Cartographer tests, the compatibility
  and multiplayer campaign filters, and `package.ps1`'s validator/tcli
  step all PASS, with zero new defects. The scratch clone was deleted
  once evidence was captured; nothing about the real working tree was
  touched.
- Why work continued: this leaf changes no product code, only adds a
  report and updates the DoD matrix's two regression rows to cite it;
  the only risk is the report being inaccurate, which independent review
  checks like any other claim.
- Risk / alternative: per CT-047's own acceptance criteria, "reruns
  green or pending-listed with reasons" — the automated logic layer for
  every campaign (standard, compatibility, multiplayer, dedicated)
  reruns clean, but the in-game layer stays pending for the exact reason
  CT-043 already documented: no TCT-* mod-manager profile exists on this
  development machine, and creating one is a one-time owner GUI action
  this repo's tooling deliberately does not perform. This report invents
  no new claim about that gap; it only re-confirms the automated layer
  underneath it is sound.
- Must resolve before public release: No for this report itself
  (documentation-only); the in-game layer it itemizes as pending remains
  bound by whichever original entry (CT-008, CT-011, CT-027, CT-030,
  etc.) already states its own release-blocking status.
- Status: Open

### 2026-09-06 — CT-048 formal performance/memory/network/stability budgets proven at the domain layer; real in-game observation stays pending

- Version / issue: v1.0 / CT-048 (#166)
- Question: does Teamster's real per-tick/per-call hot path — the
  sampler, route profiler, brake lifecycle, trip recorder, the two
  O(n)-over-samples trip presenters, the network input guard, and
  sidecar IO — meet a concrete, numbered budget rather than just "feels
  fine," and does a simulated long session show any allocation growing
  without bound?
- Safe reversible default selected: `PERFORMANCE_BUDGETS.md` defines 14
  numbered budgets and backs every one with a repeatable automated test
  (six new in `PerformanceBudgetTests.cs`, one existing sidecar-IO test
  tightened from a loose sanity bound to a formal budget). All 14 are
  Met; no code changed to satisfy a budget — every real hot path was
  already fast/allocation-free, the tests simply prove it with a number
  instead of an impression. `CooperativeEffortClassifier`,
  `RemoteStalenessPolicy`, and `OncePerKeyGate` are explicitly excluded
  from a budget: none has a live Adapters call site yet (CT-028's entry
  above already records `CooperativeEffortClassifier`'s idle production
  caller), so there is no real cadence to measure against — a budget for
  uncalled code would be a guess. (An earlier draft of this entry also
  excluded `CartAuthorityPolicy` on the same grounds; that was wrong —
  it runs live inside `BrakeLifecycle.EvaluateTick`/`EvaluateToggle` on
  the brake's real due-tick/toggle path, exactly as CT-026's entry above
  already states, and its cost is already covered by the brake-lifecycle
  budget row.)
- Why work continued: this leaf adds tests and a doc; it changes no
  production code path, so the only risk is a wrong measurement, which
  independent review re-runs rather than trusts.
- Risk / alternative: per this issue's own scope ("structured manual
  capture for in-game"), four observations stay genuinely pending —
  real frame-time delta, real BepInEx log file size over a real
  multi-hour session, the real Unity terrain-probe cost inside the route
  profiler (the domain test's fake probe is free), and total process
  working set beyond Teamster's own managed-heap allocations. All four
  require a real running game; none is claimed here. The log-volume
  claim itself (no unconditional per-frame logging call exists) is a
  structural, source-cited manual audit, not a new CI-gated validator
  rule — encoding "is this log call inside a rate limit" as a static
  check would need control-flow analysis the existing token-scan audits
  don't attempt, and risks false positives/negatives for a property this
  leaf already verified by hand.
- Must resolve before public release: No for the budgets themselves (all
  Met); the four pending in-game observations remain bound by whatever
  release-blocking status the owner smoke checklist ultimately assigns
  them, same as every other in-game-only item in this sprint.
- Status: Open

### 2026-09-06 — CT-049 final sign-off across six areas; six stale doc claims found and fixed, one compatibility version bump flagged unresolved

- Version / issue: v1.0 / CT-049 (#167)
- Question: after CT-046's audit and CT-047/CT-048's reruns, do docs,
  localization, controller navigation, accessibility, migration, and the
  compatibility statement still say exactly what the current source and
  a fresh Thunderstore check show — or has anything drifted since each
  area's own original leaf?
- Safe reversible default selected: `V1_SIGNOFF.md` records six explicit
  sign-off lines, each backed by a fresh test rerun (111 cases across
  nine test classes, all green) plus a direct source/Thunderstore
  recheck rather than trusting a prior leaf's citation. Six stale doc
  claims were found and fixed in place: three wrong localization key
  counts (274/274+/280+, actual 277 — recounted twice after a first
  quick regex undercounted at 256), one misleading version-scoped
  heading in the package README, and the compatibility matrix's version
  pins (rechecked live against Thunderstore, not re-typed from memory).
  No production code changed; every fix is a documentation correction
  to match already-correct code and already-passing tests.
- Why work continued: this leaf reruns and audits; it does not introduce
  new runtime behavior, so the only risk is a wrong measurement or a
  missed drift, which independent review re-checks rather than trusts.
- Risk / alternative: BetterCarts was found to have released a real new
  version (1.0.6→1.1.0) since CT-037 decompiled its mass-reduction
  patch; this leaf did not re-decompile the new build to reconfirm the
  patch still does the same thing, and says so plainly in
  `COMPATIBILITY.md`'s new "Version recheck" section rather than
  silently re-stamping the old finding onto the new version number. The
  registry's GUID-based, presence-only detection is unaffected either
  way. Separately, ValheimPlus is now marked Deprecated on Thunderstore
  — flagged for owner awareness; its shipped behavior and Warn/None
  classification are unchanged. The already-open `CompatibilityPanel`
  NavigationCatalog gap (CT-031-era) was confirmed still open, not
  silently resolved and not expanded into this leaf's scope.
- Must resolve before public release: No for the sign-off itself (all
  six areas pass); BetterCarts' un-re-decompiled version bump is a small,
  non-blocking re-verification gap the owner may want closed before
  relying on the exact "20%" figure, though the Adapt classification
  itself does not depend on that exact number.
- Status: Open

### 2026-09-06 — CT-050 v1.0.0 stable candidate sealed; this is the v1.0 sprint's own final leaf, and the conveyor stops here for owner review

- Version / issue: v1.0 / CT-050 (#168)
- Question: with every v1.0 leaf (CT-046–049) closed, is Teamster ready
  to seal as a stable (not beta) release candidate, and what exactly
  remains for the owner to do before publication?
- Safe reversible default selected: version-synced 1.0.0 everywhere via
  the established two-commit pattern (version-sync commit, then real
  ZIP/DLL hashes from a genuinely separate `git clone` of that exact
  commit — never a dirty-tree build); ran `rehearse-teamster-lifecycle.ps1`
  against that same clean clone for fresh-profile install verification
  (all 5 sections green, DLL hash cross-confirmed by two independent
  tools); wrote `OWNER_PACKET_v1.0.md` following Concerned Cartographer's
  full `V1_RELEASE_PREP.md` sequence (this is a real stable release, not
  a scoped-down beta like v0.9.0's packet was); extended
  `PRE_RELEASE_SMOKE_TEST.md` through v1.0 (new §14, updated §9.1, new
  13.4 row); wrote `CONVEYOR_COMPLETION_REPORT.md` summarizing the whole
  v0.1→v1.0 journey. Backfilled the missing `concerned-teamster/v0.9.0`
  tag (the same gap CT-043 fixed for v0.1–v0.8, simply missed since it
  postdated that backfill pass) — a past beta version, not the final
  stable tag. Did **not** create `concerned-teamster/v1.0.0`, did not
  invoke `tcli publish`, and does not claim the smoke checklist passed —
  all explicitly reserved for the owner, per this issue's own scope and
  its `gate:human-preview` label.
- Why work continued: every prior v1.0 leaf closed clean with zero new
  defects; this leaf changes no runtime behavior beyond version strings,
  so the only risk is a wrong hash or a miscited fact in the owner
  packet, which independent review re-verifies against source rather
  than trusting the draft.
- Risk / alternative: the entire live campaign — the now-39-row
  `PRE_RELEASE_SMOKE_TEST.md` — has never been run, exactly as it was at
  every prior seal; this is the real, substantive gate, not a formality,
  and nothing in this leaf claims otherwise. One small, non-blocking
  re-verification gap carries over from CT-049 (BetterCarts 1.1.0's
  mass-reduction patch not re-decompiled).
- Must resolve before public release: N/A for this seal itself (it is
  the packaging of the request for the owner's own resolution); every
  item in "What is proven vs. what is pending" in `OWNER_PACKET_v1.0.md`
  carries its own already-stated resolution requirement.
- Status: Open — **this is the last Teamster entry the autonomous
  conveyor is expected to add before the owner reviews this seal.** Per
  the `gate:human-preview` label and this project's own precedent (the
  identical stop after CT-045's v0.9.0 seal), no further Teamster work
  resumes automatically after this leaf closes.

## Resolved items

### 2026-09-05 — CT-032 localization framework delivered; full-UI externalization is progressive

- Version / issue: v0.7 / CT-032 (#147)
- Question: CT-032's scope is "externalize every user-facing string across all
  UI/warnings/guidance" plus a repo-wide hardcoded-string audit. A faithful
  one-shot migration is ~343 literal sites across ten presenters (many
  piecewise-composed), with 32 test files asserting exact output — a large,
  review-hostile change that risks behavior/test drift if rushed, against the
  contract's "small enough for independent review".
- Safe reversible default selected: land the durable localization FRAMEWORK
  as the reviewable slice — catalog + English defaults + override/template +
  English fallback + once-only missing-key report + placeholder validation
  (9 framework tests), the file adapter, the translator guide (LOCALIZATION.md)
  — and migrate the newest surfaces (route picker + route report) through it
  output-preservingly (existing tests stay green, keys are live not dead).
  The remaining panels (status, manifest, warnings, guidance, trips) are
  externalized progressively in follow-up work, at which point the repo-wide
  hardcoded-string audit gate is added. English is the shipped language, so
  no user-facing regression exists in the interim.
- Why work continued: the framework is additive and behavior-preserving; the
  worst interim state is that some panels are not yet translatable, never a
  wrong or missing string (English fallback everywhere).
- Risk / alternative: the owner may prefer one large externalization PR over
  progressive per-surface migration; either reaches the same end. #147 stays
  OPEN until the full externalization + audit land — this leaf advanced it,
  it did not complete it.
- Must resolve before public release: Yes (the v0.9 beta expects full
  localization coverage + the audit gate)
- Status: Resolved 2026-09-05 — the progressive migration completed across
  PRs #202/#203/#204 (status+manifest, warnings+diagnostics+guidance+load,
  trips+routes+navigation; every PR output-preserving with an independent
  MERGE-verdict review), and the repo-wide hardcoded-string audit gate landed
  with the closing PR (negative-tested: planted literals, dead keys, and
  re-hardcoded sentences all fail it; log diagnostics are structurally
  exempt). Catalog: 241 keys, English complete. The in-game visual checks
  remain tracked by the per-surface entries above and the owner smoke
  checklist, unchanged.

### 2026-09-06 — CT-036 compatibility framework proven off-game with fake mods; empty registry pending real entries

- Version / issue: v0.8 / CT-036 (#152)
- Question: the registry/policy mechanism (detection, each policy outcome,
  silence for unregistered/not-found mods, the shared log/panel
  composition) is fully proven with fake test mods, but the shipped
  registry is intentionally empty — no real mod has been researched yet —
  so there is nothing for an in-game session to actually observe beyond the
  empty-state message and the new Compat button's presence/placement.
- Safe reversible default selected: ship the framework with zero registered
  mods rather than guess at a real one's GUID (this repository's "research,
  don't invent" rule for third-party mods); the Compat panel and log line
  both honestly say "No known compatibility concerns detected." until
  CT-037/038 add researched entries.
- Why work continued: an empty registry cannot mislead — there is no policy
  to misapply yet — and the mechanism itself is exhaustively unit-tested;
  the only pending item is cosmetic (button placement/panel visibility),
  not correctness.
- Risk / alternative: none beyond the pending visual check below; once
  CT-037 adds a real entry, that leaf's own HUMAN_ATTENTION entry will carry
  the actual in-game policy-observation pending items (e.g. "Better Carts
  installed → Compat panel shows its policy line").
- Must resolve before public release: No (the registry itself has nothing
  to verify yet; CT-037/038's entries will carry the real pending items)
- Status: Resolved 2026-09-06 — CT-037 populated the registry with one
  real, GUID-verified entry (BetterCarts by TastyChickenLegs); this entry's
  own concern (an empty registry with nothing to observe) no longer applies.
  CT-037's own entry (Open items, above) carries the current pending items.

### 2026-09-06 — DEF-teamster-v1.0-001: sealed artifact never preserved durably, caught by the owner before any smoke test ran

- Version / issue: v1.0 / DEF-teamster-v1.0-001 (#227) — an owner-directed
  fix, not an autonomously-selected leaf; the conveyor itself had already
  stopped per CT-050's `gate:human-preview` entry above.
- Question: the owner found that the ZIP sitting in this repo's own
  `artifacts/thunderstore/` folder — the natural place to look for "the
  sealed v1.0.0 ZIP" — did not match `RELEASE_DOSSIER.md`'s recorded
  hashes, and correctly held off running any smoke test against it
  until this was resolved.
- Safe reversible default selected: root-caused directly rather than
  guessed — the hashed artifact had only ever existed inside a scratch
  `git clone` that was deleted after CT-050's seal, and a leftover from
  an earlier, different verification build was sitting in the
  persistent folder instead. Rebuilt from `main`'s tip
  (`0d204eb9737707044b6b6cb834ed19fad2f710a9`) in a fresh clone; this
  time copied the exact resulting ZIP into `artifacts/thunderstore/`
  and re-hashed it in place, twice, before treating any value as final
  — catching, mid-process, that the lifecycle rehearsal script's own
  internal `package.ps1` call had silently overwritten the first build
  with a second one at the same path (expected per DEF-teamster-v0.8-001's
  ZIP non-determinism, but only safe to rely on once nothing further
  would rebuild it). `RELEASE_DOSSIER.md` and `OWNER_PACKET_v1.0.md`
  updated with the new values and an explicit reseal note; full account
  and a general process fix for future seals filed as #227.
- Why work continued: this is a direct, explicit request from the owner
  in response to a real problem they found — not autonomous continuation
  past the seal's own stop point. No source file changed; only the
  documented hash/commit citations and the persisted artifact.
- Risk / alternative: none beyond what CT-050's own entry already
  states — the in-game smoke checklist remains the real, unrun gate.
  Confirmed 629/629 Teamster + 568/568 Cartographer tests and the
  full 5-section lifecycle rehearsal all still green against this
  rebuilt artifact, and the persisted ZIP's hash re-confirmed by an
  independent review directly against the file on disk.
- Must resolve before public release: N/A — this is a documentation/
  artifact-preservation correction, not a defect in shipped behavior.
- Status: Resolved 2026-09-06 — fixed and merged via PR #228
  (`28dde7e73565992313509ead9db97821a9be6174`); issue #227 closed with
  evidence.
