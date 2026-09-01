# Concerned Cartographer v1.0 — Release Dossier

Prepared by the autonomous conveyor (Tankard Olafsson) per OPS-001 rev 2.
The single remaining gate is the human smoke test
(`PRE_RELEASE_SMOKE_TEST.md`); nothing has been published.

## 1–5. Release candidate identity

- **Version:** 1.0.0 (unchanged — 1.0.0 has never been publicly tagged or published)
- **RC commit:** `a053369f503c13d51e0eeed650bd01fba9e1998e` (**RC8**, on
  the CC-098 line — the RC8 release-blocker pass after **RC7
  `f493178c`/`9b43e25` FAILED its human smoke** (owner failure prompt of
  2026-08-31). RC8 delivers all twelve directives:
  **(1) strict road source authority** — only successful explicit
  local-player Pathen ⇒ Dirt / Paved ⇒ Paved construction creates road
  atlas data; passive traversal/chunk-recovery creation is refused at
  the pipeline choke point, the chunk-recovery adapter is retired, the
  surveyor is diagnostics-only, existing passive strokes migrate away
  once with a `.pre-authority.bak` backup (construction strokes and
  identities preserved), Level/Raise/Cultivate/Reset create no roads
  and erase covered ink of both kinds, later explicit Pathen/Paved
  wins, with restart/reopen regression tests
  (`RoadSourceAuthorityTests`);
  **(2) single road presentation** — while the vector layer is healthy
  it is the ONLY large-map road ink (texture suppressed; minimap keeps
  texture; texture returns on disable/failure, and an over-budget bake
  now fails soft to the complete texture view);
  **(3) real cc:* marker sprites** — 12 distinct generated icons
  (road/junction, harbor, resource, danger, farm, mine, fishing, camp,
  travel, trader, dungeon, objective) embedded in the DLL with stable
  IDs, vanilla fallback types for uninstall safety, unknown-ID
  preserve/fallback intact (`tools/generate_icon_sprites.py` is the
  reproducible source);
  **(4) survey works out of the box** — useful bounded starter rules
  (gatherables/ores/dungeons/runestones), untouched pre-RC8 starter
  files upgrade in place, live scanner/rules/last-scan/pending status
  plus Scan now in the panel, accepted observations pin immediately;
  **(5) routes** — draggable panels, pointer-over-CC-UI never adds
  points, Free Draw hold-to-draw/release-ends-stroke (each stroke its
  own route, no empty routes), geometric dash/dot cadence at all zooms,
  selected-route/style/status clarity;
  **(6) quick pin** targeted sync (renders immediately, no duplicates,
  ledger regression test);
  **(7) UI layout** — toolbar height derived from the vanilla hints
  layout (no magic offset, live re-check), Settings' dedicated middle
  status block, Atlas Drawer explicit no-overlap grid with an accounted
  vertical budget, Pin Workbench sheds the inert Size/Color controls
  (values still round-trip);
  **(8) align live** appends explicit open-map/stand-on-your-road
  guidance. RC6 content (crash reporting #97 with the live DSN
  embedded) and the RC7 full-UI surface / DEF-v1.0-006 vector layer are
  included as amended by the directives above. **Retires RC7
  `f493178c`/`9b43e25` (ZIP `8E76CD0A…`, FAILED human smoke) and
  supersedes RC6 `17b0524350` (ZIP `EE0F3A6E…`), RC5 `7881cbcd` (ZIP
  `1849C62E…`), RC4 `35f20e1a` (ZIP `8B4B41AD…`), RC3 `86050cd2` (ZIP
  `710183B3…`), RC `7ed20fef` (ZIP `B47E7C9D…`, FAILED the second human
  smoke pass), and RC `9eb65291` (ZIP `9F1F4128…`). Do not test, tag,
  or upload ANY of those ZIPs.** Already-passed evidence that remains
  valid: startup environment (Valheim 0.221.12 / Unity 6000.0.61f1 /
  BepInEx 5.4.23.3 / Jötunn 2.29.2.0, clean 1.0.0 banner, no CC
  errors), the adoption input-trap fix (DEF-v1.0-001), the workbench
  two-column layout discipline (DEF-v1.0-003), and overlay projection
  alignment (DEF-v1.0-002, residuals ≤ 1 texel). Everything the RC8
  directives touched is re-verified by smoke section **R5** (then R3/R4
  as amended).)
- **ZIP:** `artifacts\thunderstore\TheConcernedCat-ConcernedCartographer-1.0.0.zip`
  (built in the CC-098 session worktree at the RC commit; an identical
  immutable copy is at
  `artifacts\rc8\TheConcernedCat-ConcernedCartographer-1.0.0-RC8.zip` —
  verify the hash below before importing)
- **ZIP SHA-256:** `AF267AC26F23E7083F70339405311E9D6F10D93AB7E4E8A0EFAE63C26D1CEDB0` (272,222 bytes)
- **Plugin DLL SHA-256:** `E9904771A52864892250A60D8459BD1DE27DE8C354BBEDD996D6B64DB33FB597` (388,096 bytes; the DLL inside the ZIP is hash-identical to the Release build output; informational version `1.0.0+a053369f…` verified in the DLL; the 12 `CC.Icons.cc-*.png` sprite resources verified embedded)
- **Assembly metadata (verified in the DLL):** Company "The Concerned Cat",
  Product "Concerned Cartographer", Copyright © 2026 Eren Cansunar,
  RepositoryUrl embedded, informational version `1.0.0+<RC commit>`.
- **Package audit:** ZIP root contains exactly `manifest.json`, `README.md`,
  `CHANGELOG.md`, `LICENSE`, `icon.png` (256×256),
  `plugins/TheConcernedCat.ConcernedCartographer.dll`. No PDBs, game DLLs,
  saves, logs, or secrets. Dependencies pinned:
  denikson-BepInExPack_Valheim 5.4.2333, ValheimModding-Jotunn 2.29.2.

## 6. Sprints and issues

Every sprint v0.3→v1.0 shipped through its internal gate; all 42 child
issues and 8 controllers (#8, #27–#81) are closed with evidence comments.
Shipped versions on main with tags: 0.3.0, 0.4.0, 0.5.0, 0.6.0, 0.7.0,
0.8.0, 0.9.0. The RC6 line is on main; the CC-098 completion line (RC7,
now retired, plus the RC8 release-blocker pass) is on
`feat/cc-098-v1-completion` awaiting its post-smoke merge and tag
(section 19).

## 7. Defects

Full-conveyor totals: 8 defects filed and fixed across v0.1–v0.2
(#82–#86 plus three pre-OPS fixes); **zero** open P0/P1/P2 at the RC.
Notable finds: chunk-recovery MethodAccessException (P1, silent
fail-closed — caught by log review), terraforming-inks-roads (P2), ink
contrast (P3).

Post-RC audit: SEC-1.0-001 (#87) — owner-requested adversarial audit of
the sync receive path found and fixed 7 hardening gaps, the worst a
decompression bomb (size cap was checked only after unbounded
decompression). All fixed in the RC identified above: bounded gzip,
revision sanity cap, non-finite float rejection, string-length caps,
deletion names in the sync preview, author display sanitization, and
declared-length verification.

Owner-directed opt-in crash reporting (2026-08-28, #97), implemented in
this RC: `Domain/Reporting` provider abstraction (Null/Sentry), the
sanitizer + allowlist-only event with the forbidden-field redaction test
matrix (23 tests asserting on the complete outgoing envelope), tri-state
profile-level consent (Unknown default; one-time dialog on first
large-map open; permanent Atlas → Privacy surface; policy-version-gated
re-consent), capture of the mod's own Error/Fatal events + CC unhandled
exceptions only, once-per-subsystem notices, bounded queue / no retries /
background sender, the live ingestion DSN embedded at the owner's
direction with ingestion pre-verified (remaining owner actions —
Sentry-side scrubbing settings and alerts — in HUMAN_ATTENTION +
CRASH_REPORTING.md), PRIVACY.md, SECURITY.md
telemetry clause updated, support@theconcernedcat.com routing everywhere
(no personal email anywhere in mod/package/docs; crash reports never by
email). **Publish/tag remains blocked until the redaction tests pass in
the gate (they do) AND the human consent flow passes smoke block R3.L.**

Owner-directed v1 completion pass (2026-08-31, CC-098 / #98–#102),
implemented in RC7:

- **DEF-v1.0-006 (#98, P1)**: owner screenshots showed the live player
  visibly offset from road ink on the large map; the prior
  `cc_roads align` PASS only proved same-coordinate projection
  compatibility. RC7 ships the two-part fix the issue specifies: the
  high-precision large-map road renderer (batched vector geometry in
  map-content space, RoadAtlas stays source of truth, texture overlay
  kept for minimap/fallback, rebuild only on data/zoom-step change,
  kind-split, fail-soft, no magic offsets — the container transform
  reproduces vanilla's own `((m − uvMin)/uvSize)·rectSize`, proven
  equivalent in tests across aspect-corrected uv windows) and
  `cc_roads align live`, which answers the four error classes
  SEPARATELY (A observation / B projection / C render resolution /
  D marker anchor) from live game state. The acceptance criterion —
  marker on the CC centerline ≤ 2 px across 50–100 m walks on Dirt and
  Paved, pan/zoom stable, probes and minimap unchanged — is smoke
  block R4-R.
- **#99–#102 audit**: an independent agent audited the landed UI
  clause-by-clause against the owner directive (verifying vanilla API
  claims against the game assembly). Verdict: the surface is genuine —
  toolbar, exclusivity, shared dock, routes/survey/share/settings/
  system-markers panels, rail replacement via SetActive-only with
  vanilla-state callbacks, Quick Pin one-shot semantics, and full
  route-operation coverage all confirmed with file:line evidence. The
  eight directive contradictions it found are fixed in RC7 (see
  identity above); the deliberately bounded gaps (fixed list
  capacities, the console-only batch/recovery set, restore-latest
  semantics, cycle-style pickers, select-on-open controller focus, 5 Hz
  rail enforcement) are recorded in HUMAN_ATTENTION 2026-08-31 and in
  the package README's shortcut-parity table. The #102 documentation
  clauses — the full shortcut audit table and the v1 README rewrite —
  were NOT delivered by `75d9d01` and are delivered in RC7.

Owner smoke-failure pass (2026-08-31, RC8): the owner's human smoke
FAILED RC7 on twelve directives (road source authority, doubled road
presentation, alias-only cc:* icons, non-functional survey, routes
input/stroke/style defects, toolbar/settings/drawer overlap, inert
workbench controls, quick-pin visibility, align-live guidance). All
twelve are implemented in RC8 (identity above). Gate evidence at the RC
commit: **356 domain tests green** (new `RoadSourceAuthorityTests`
restart/reopen suite, align-live guidance tests, icon-registry sprite
contract, survey default-rule matching, quick-pin ledger regression),
`validate_repo.py --require-binary` PASS, clean Release build, tcli
package build, and the six-entry ZIP audit with hash-identical DLL.
The RC8 UI layout matrix (resolutions 1280x800 / 1920x1080 / 2560x1440 /
ultrawide × UiScale 0.8/1.0/1.6) is derivation-based in code (hint-layout
toolbar, budgeted drawer grid, framed settings block, workbench columns)
and is verified live by smoke R5 items 6–8 — a game UI cannot be
screenshot-proven from the conveyor.

Owner-approved v1 map UX direction (2026-08-28, #96), implemented in
RC4 on top of the RC3 fixes: the map is button-first — [Atlas]
button with tooltip, contextual **Upgrade & Edit** (adoptable vanilla;
internally the DEF-v1.0-004-safe adoption) and **Edit Pin** actions with
the accelerator hint, and the **Enhanced Pin Palette** (searchable,
sprite-previewed marker browser over stable IconRegistry IDs, session
recents, collapse) replacing the five vanilla placeable icon buttons by
default. Palette markers are **managed from birth**: choosing a marker
selects the mapped vanilla icon type and arms a pure birth tracker
(7 tests); vanilla double-click + naming creates the pin, and the
runtime associates the AtlasPin when naming closes — one rendering, one
entity, no upgrade step. Fallback: `Pins/ShowVanillaPinPalette` /
`EnhancedPinPalette=false` restore vanilla instantly; a detected
conflicting pin manager keeps vanilla automatically; only SetActive is
ever used on vanilla objects; death/boss/system pins, Cross Off,
Remove, Ping, Visible-to-others, and uninstall safety are untouched.
Status/Scope became dropdown selects; hotkeys stay as rebindable
accelerators.

Second human smoke pass (2026-08-27) against RC `7ed20fef` found two new
P1 release blockers plus P2 UX gaps, all addressed in the RC3 line:

- **DEF-v1.0-004 (#92, P1)**: editing an adopted/managed pin created a
  duplicate map rendering. Root cause: the workbench resynced through the
  full `ReconcileOnMapReady` (reset + claim-by-position-and-name), which
  cannot re-claim a rendering after a rename. Fixed at the lifecycle
  level: tracking + decisions extracted into the pure
  `PinRenderingLedger`, all in-session mutations use the
  tracking-preserving targeted sync path, full reconcile is reserved for
  map/world reconstruction. 11 regression tests.
- **DEF-v1.0-005 (#93, P1)**: leveling still painted the map — Level/
  Raise leave Dirt terrain paint that traversal and chunk recovery
  rediscovered as road. Fixed with persistent per-world negative terrain
  intent (`<uid>.terrain-intent.tsv`, format v1): Level/Raise/Cultivate/
  Reset exclude their brush footprint, passive Dirt observations are
  refused inside exclusion, explicit Pathen/Paved clears and re-inks,
  bounded 250k cells, survives restart. 15 regression tests.
- **#94 (P2 UX)**: workbench visual fields were developer free-text.
  Now: icon picker with live sprite preview + "Keep custom" for legacy
  IDs, category suggestions, size stepper; color honestly labeled
  metadata-only (pins are not color-rendered in v1).
- **#95 (P2 UX)**: panels were hotkey-only. Now: visible `CC Atlas [L]`
  large-map button, contextual `P — Edit with Concerned Cartographer`
  hint over editable pins, README Controls section; vanilla right-click
  untouched.
- **DEF-v1.0-002 (#90)**: CLOSED as PASS — three logged `cc_roads align`
  runs show overlay pixel == native `WorldToPixel` pixel at every probe
  (residual ≤ ~0.4 texel sub-pixel, bound is 1 texel); owner screenshots
  concur. The diagnostic remains available but unadvertised, with a
  compact PASS/FAIL residual table.

First human smoke pass (2026-08-27) against RC `9eb65291` found three
release blockers, all addressed in the previous RC:

- **DEF-v1.0-001 (#89, P1)**: adopting a vanilla pin trapped map/game
  input. Proven root cause: Jötunn's `GUIManager.BlockInput` is
  reference-counted and the adopt-prompt → managed-editor transition
  issued two requests but released one. Fixed with an owned, provably
  balanced `ModalInputBlock` state machine (11 new unit tests), teardown
  on external map close / logout / dispose, and a per-frame fail-safe
  invariant (hidden workbench ⇒ no owned block).
- **DEF-v1.0-002 (#90, P1, since CLOSED as PASS — see above)**: sacrifice-stones
  icon vs dirt-road ink appeared misaligned. Static audit found no
  projection defect (both draw paths share Jötunn's
  `WorldToOverlayCoords`, overlay texture is the vanilla 2048, no
  offsets anywhere); a deterministic `cc_roads align` diagnostic (native
  pin vs overlay cross + full projection logging at five known
  positions) now proves or refutes alignment against the live game.
  Pass bound: ≤ 1 texel (~12 m), matching the v0.1 CC-009 calibration.
- **DEF-v1.0-003 (#91, P2)**: workbench labels rendered outside the
  panel (center-anchored −150/130-wide labels in a 400 px panel). Fixed
  with an explicit constant-derived two-column layout on a 460 px panel,
  left-aligned labels, and scale-aware re-docking (0.8–1.6).

## 8. Automated evidence (at the RC commit)

- **339/339 tests** in the game-free core suite (Release configuration,
  re-run at the RC commit): everything below plus the DEF-v1.0-006
  suites (29 tests: the vector container transform reproduces vanilla's
  MapPointToLocalGuiPos for every map point across full-map, deep-zoom,
  panned, and aspect-corrected uv windows; width round-trip; half-a-
  texel separates visibly at deep zoom; the four separated A/B/C/D
  verdicts with exact 3 m / 1 texel / 4 px-per-texel / 2 px boundaries
  and invariant-culture formatting under a comma-decimal locale; and
  pipeline point-acceptance semantics incl. stroke-start points), the #97
  crash-reporting suite (23 tests: forbidden-field redaction matrix over
  the outgoing envelope, consent gating, dedupe/caps/bounded queue,
  DSN/envelope codecs, release identity), the #96
  managed-from-birth palette tracker suite (7 tests), the DEF-v1.0-004
  pin-rendering-lifecycle suite (11 tests: adopt→edit→apply keeps one
  rendering, restart reconcile, claim strictness, batch sync) and the
  DEF-v1.0-005 terrain-intent suite (15 tests: exclusion blocks
  traversal/recovery, Pathen clears, codec round-trip/restart, world
  independence, bounded eviction): road geometry
  and suppression, codecs and journal recovery for all three entity
  families, migration matrix across every shipped format, pin/route
  operations with undo-convergence properties, query/clustering,
  survey bounds, sync policy/planner including tombstone
  no-resurrection, localization safety, the SEC-1.0-001 hardening
  suite (decompression-bomb rejection, revision/float/string bounds,
  deletion-name previews, display sanitization), and the DEF-v1.0-001
  modal-input-block ownership suite (balance under re-entry,
  double-close, arbitrary sequences, throwing backend).
- Validator green with `--expected-version 1.0.0`; solution builds with
  0 errors (1 known benign MSB3245).
- Scale: 10,000-pin suites (<200 ms total), 10 km road compaction
  (6,667→186 pts, 8 ms), 10k query <500 ms bound (measured far lower).

## 9. Actual in-game evidence (genuinely observed, v0.1–v0.2 era)

Owner-verified during earlier campaigns: road survey/classification,
construction capture, reconciliation, recovery (post-fix), repair tools,
persistence, world isolation, uninstall safety, v1→v3 road migration with
backups, Pinnacle+MapRoutes coexistence, fresh-profile ZIP install,
30-minute stability. **Everything v0.3+ is implemented fail-closed but
NOT in-game verified** — that is exactly what the smoke test covers.

## 10. Manual-only items

The complete list is `PRE_RELEASE_SMOKE_TEST.md` sections 1–13 (each row
with setup/action/expected/evidence/blocking). Genuinely human-only:
every visual/UX row, the two-client collaboration section (7), NoMap and
controller feel (8), live compat sessions (9), upgrade/uninstall
rehearsals (10), soak (11), Thunderstore preflight (12).

## 11–15. Result summaries

- **World isolation/persistence:** per-UID sidecar family; isolation
  verified in-game in v0.1; crash-safe journals property-tested; smoke 4.x
  re-verifies live.
- **Multiplayer/tombstone/conflict:** structurally guaranteed and
  property-tested (stale clients cannot resurrect deletions; conflicts
  converge); live two-client confirmation is smoke section 7.
- **Compatibility matrix:** detection + policies for 6 known mods;
  Pinnacle/MapRoutes verified live in v0.1-era; others are smoke rows.
- **Performance/soak:** automated numbers above; feel/soak is smoke 11.
- **Localization/controller/accessibility:** framework + template +
  overrides shipped; select-on-open chains + opt-in gamepad bindings;
  UiScale/HighContrast/non-color cues; visual rows in smoke 8.

## 16. Known limitations

Documented per version in CHANGELOG plus HUMAN_ATTENTION deferrals: pin
color/size not map-rendered; no server-side sync store; no MapRoutes
import; console-proximity (not map-click) selection; author labels not
authentication; survey matches loaded objects only.

## 17. HUMAN_ATTENTION summary

Seven open ledger entries, **none marked must-resolve-before-release**;
all are documented limitations or deferred alternatives with reversible
defaults.

## 18. Smoke test

`docs/mods/concerned-cartographer/PRE_RELEASE_SMOKE_TEST.md` — **the
owner starts at the NEW SHORT section R5 (the RC8 mini-smoke: twelve
blocking items, one per failure directive), then runs R3 (blocks A–L)
and R4 (blocks M–S, as amended for the road authority rule), NOT at the
top.** The full 2.5–4 h checklist is not restarted; sections the
earlier passes already completed stay completed. Only after R5 + R3 A–L
+ R4 M–S pass does the owner resume
routes/world-isolation/multiplayer.

## 19. Remaining Git commands (run after the smoke test passes)

The RC lives on `feat/cc-098-v1-completion` (not yet on main). After
R5 + R3 + R4 pass:

```powershell
# 1. Merge the completion branch to main (PR or fast-forward — owner's call):
git checkout main; git merge feat/cc-098-v1-completion
git push origin main
# 2. Tag the RC commit (now in main history):
git tag -a concerned-cartographer/v1.0.0 -m "Concerned Cartographer 1.0.0 - Stable Living Atlas" a053369f503c13d51e0eeed650bd01fba9e1998e
git push origin concerned-cartographer/v1.0.0
gh release create concerned-cartographer/v1.0.0 artifacts/thunderstore/TheConcernedCat-ConcernedCartographer-1.0.0.zip --title "Concerned Cartographer 1.0.0" --notes-file src/ConcernedCartographer/Package/CHANGELOG.md
```

## 20. Thunderstore upload data (owner-only)

- File: `TheConcernedCat-ConcernedCartographer-1.0.0.zip`
- Team/namespace: **TheConcernedCat** · Community: **valheim**
- Categories: **mods, client-side, utility, ai-generated**
- Dependencies: denikson-BepInExPack_Valheim 5.4.2333, ValheimModding-Jotunn 2.29.2
- Version: 1.0.0 · Upload via thunderstore.io web UI, or
  `pwsh ./scripts/publish.ps1 -Version 1.0.0` with `TCLI_AUTH_TOKEN` set
  only in that shell.

## 21. DO NOT RELEASE IF

- Any **BLOCKS** smoke row fails and cannot be fixed + re-verified.
- The ZIP hash on disk no longer matches this dossier.
- A human ZIP inspection finds anything beyond the six audited entries.
- The two-client tombstone test (smoke 7.4) shows a resurrected deletion.
- Any world save, character file, or foreign mod's data is modified in
  any test.
- The fresh-profile install (smoke 10.4) fails to reach the main menu
  cleanly.
