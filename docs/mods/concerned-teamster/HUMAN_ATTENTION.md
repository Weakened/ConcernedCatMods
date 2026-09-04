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

## Resolved items

None yet.
