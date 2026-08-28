# Concerned Cartographer v1.0 — Release Dossier

Prepared by the autonomous conveyor (Tankard Olafsson) per OPS-001 rev 2.
The single remaining gate is the human smoke test
(`PRE_RELEASE_SMOKE_TEST.md`); nothing has been published.

## 1–5. Release candidate identity

- **Version:** 1.0.0 (unchanged — 1.0.0 has never been publicly tagged or published)
- **RC commit:** `7881cbcd93630c3d1eb17577f6796da9fa4b3262` (main; the
  **RC5 / opt-in crash-reporting pass, #97** — consent-gated,
  allowlist-only crash reporting behind ICrashReporter with a
  Sentry-envelope backend (no SDK bundled; DSN ships EMPTY so the
  pipeline is inert until the owner embeds it per CRASH_REPORTING.md),
  automated forbidden-field redaction tests over the exact outgoing
  payload, one-time consent dialog + Atlas → Privacy surface,
  once-per-subsystem failure notices, PRIVACY.md, and canonical support
  routing (support@theconcernedcat.com) — on top of the RC4 v1 map UX
  pass (#96) and the RC3 fixes (#92–#95, #90). **Supersedes RC4
  `35f20e1a` (ZIP `8B4B41AD…`) and RC3 `86050cd2` (ZIP `710183B3…`),
  both superseded before human testing; RC `7ed20fef` (ZIP `B47E7C9D…`,
  FAILED the second human smoke pass); and RC `9eb65291`
  (ZIP `9F1F4128…`). Do not test or upload those ZIPs.** Already-passed
  evidence that remains valid: startup environment (Valheim 0.221.12 /
  Unity 6000.0.61f1 / BepInEx 5.4.23.3 / Jötunn 2.29.2.0, clean 1.0.0
  banner, no CC errors), the adoption input-trap fix (DEF-v1.0-001),
  the workbench layout (DEF-v1.0-003), and overlay alignment
  (DEF-v1.0-002, closed as PASS on logged residuals ≤ 1 texel).)
- **ZIP:** `artifacts\thunderstore\TheConcernedCat-ConcernedCartographer-1.0.0.zip`
- **ZIP SHA-256:** `1849C62E250FBAF900137FB0284E5A097F20FCB45E9822EAD04438B99ABA1495` (237,080 bytes)
- **Plugin DLL SHA-256:** `E1C981E84C1858076372192E2D1DA8D4C3DEA4B1044D2A9C7D3CDC54D6022136` (317,440 bytes; the DLL inside the ZIP is hash-identical to the Release build output; informational version `1.0.0+7881cbcd…` verified in the DLL)
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
0.8.0, 0.9.0; 1.0.0 is merged to main awaiting its post-smoke tag.

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
background sender, empty embedded DSN by policy (owner ship-time action
in HUMAN_ATTENTION + CRASH_REPORTING.md), PRIVACY.md, SECURITY.md
telemetry clause updated, support@theconcernedcat.com routing everywhere
(no personal email anywhere in mod/package/docs; crash reports never by
email). **Publish/tag remains blocked until the redaction tests pass in
the gate (they do) AND the human consent flow passes smoke block R3.L.**

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

- **310/310 tests** in the game-free core suite (Release configuration,
  re-run at the RC commit): everything below plus the #97
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
owner resumes at its section R3 (RC5 mini-regression, blocks A–L), NOT
at the top.** The full 2.5–4 h checklist is not restarted; sections the
earlier passes already completed stay completed. Only after A–L pass
does the owner resume routes/world-isolation/multiplayer.

## 19. Remaining Git commands (run after the smoke test passes)

```powershell
git tag -a concerned-cartographer/v1.0.0 -m "Concerned Cartographer 1.0.0 - Stable Living Atlas" 7881cbcd93630c3d1eb17577f6796da9fa4b3262
git push origin concerned-cartographer/v1.0.0
gh release create concerned-cartographer/v1.0.0 artifacts/thunderstore/TheConcernedCat-ConcernedCartographer-1.0.0.zip --title "Concerned Cartographer 1.0.0" --notes-file src/ConcernedCartographer/Package/CHANGELOG.md
```

(main already contains the RC via the sprint merge.)

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
