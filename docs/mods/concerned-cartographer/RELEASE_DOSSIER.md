# Concerned Cartographer v1.0 line — Release Dossier (0.9.0 Public Beta)

Prepared by the autonomous conveyor (Tankard Olafsson) per OPS-001 rev 2.
The single remaining gate is the human smoke test
(`PRE_RELEASE_SMOKE_TEST.md`); nothing has been published.

## 1–5. Release candidate identity

- **Version:** **0.9.0 (Public Beta)** — the owner-approved RC13
  re-version of the public package identity: the feature-complete v1
  candidate ships first as a public beta, and 1.0.0 stays reserved for
  the stable release. Nothing has ever been uploaded to Thunderstore
  under any version, so the 0.9.0 package identity is free there.
  ⚠ The GIT tag namespace is NOT free: the public repo already carries
  `concerned-cartographer/v0.9.0` pointing at the never-published
  INTERNAL 0.9 hardening milestone (commit `4931020e`) — the
  post-smoke tag for this beta therefore needs a distinct name (owner
  decision recorded in section 19). Data and schema formats are
  unchanged from RC12; upgrading this beta to 1.0.0 later is automatic
  and lossless.
- **RC commit:** `392ab9382f9088b72e65d9e6a530bbe030c526d6` (**RC13**,
  on the CC-098 line — the owner-approved final beta polish pass of
  2026-09-02, implementing exactly the four presentation/UX items from
  the owner's RC12 smoke feedback, plus the 0.9.0 re-version; the
  package below was built at exactly this commit with a clean tree,
  and the DLL's informational version embeds it).
  RC13 delivers, on top of RC12:
  **(1) feathered large-map road ink** — Dirt/Paved vector quads widen
  4/3 and stretch a 1×64 alpha-gradient texture across their width
  (pure `RoadInkSoftening`: opaque core, symmetric monotone falloff,
  50%-alpha extent exactly the crisp RC12 width), so the large map
  matches the minimap's softer look with the same centerline,
  perceived width, colors, quad count, and budget — no under-stroke,
  no double-render; routes stay crisp by design;
  **(2) 3× palette wheel** — the [Markers] list wheel step is
  `PaletteScrollTuning.Scaled` (3× the stock ScrollRect sensitivity,
  floored at three rows per notch), inside the owner's 2–3× target
  window by regression test; bounds and the RC11 map-zoom wheel guard
  untouched;
  **(3) orphan chrome sweep** — `OrphanChromeSweep` + the pure
  `OrphanChromeRule` climb a bounded number of parents from every rail
  object CC already hid and hide the highest ancestor provably empty
  decoration (map image / hint bars / shared-map hint / pin roots /
  biome label protected; any would-be-visible control or text blocks),
  catching the backplate that framed the replaced controls from
  outside both button groups — the empty rectangle at the bottom-right
  of the owner's RC12 screenshot. SetActive only; tracked exact
  restore on ShowVanillaMapControls, conflict, CC UI failure, disable,
  and teardown; "Vanilla chrome sweep:" logged once per change;
  **(4) Markers default panel** — `DefaultPanelRule` opens the palette
  as the initial CC side panel exactly once per fresh large-map open;
  firing, any already-visible surface, or palette unavailability
  (setting, conflicting pin manager, palette/toolbar failure, NoMap
  gate) disarms it for the rest of that map-open, so the user's
  close/switch is never fought and nothing pops late.
  **Retires RC12 `846e9dbc`/`061ab4a` (ZIP `7A027F7B…`) — do not
  test, tag, or upload it.** The owner's RC12 smoke approved RC12
  behavior as the baseline (that evidence remains valid); everything
  RC13 touched is verified by the NEW smoke section **R9**, and RC13
  deliberately changes nothing else.
- RC12's identity block is preserved below for the record:
- (RC12) **RC commit** `846e9dbc2dbaff7d766eb3be36413ed9d8118eb8`
  (the owner-feedback pass of 2026-09-02 addressing all 6 feedback
  items (4 release blockers) from the owner's RC11 smoke).
  RC12 delivered, on top of RC11:
  **(1) paved ink reads lighter than dirt** — the shared road palette's
  normal-mode paved ink is now a light stone gray (176,180,190) so
  paved is unmistakably the lighter kind at identical width/style in
  BOTH presentations (one `DirtColor`/`PavedColor` source drives the
  texture overlay and the vector layer); high contrast keeps near-black
  dirt / near-white paved unchanged;
  **(2, blocker) live route list** — `RouteStore` gained a monotonic
  `ChangeStamp` bumped on every published change, and the Routes panel
  polls it per visible frame: draw strokes, erase, delete, split,
  merge, restore, console edits, sync, and undo/redo all update the
  visible list the same frame through any path. Erasing the LAST of a
  route's ink tombstones the route inside the same undoable erase step
  (no more ghost zero-point rows), and the RC11 stable sort keeps row
  order deterministic;
  **(3, blocker) dotted style can never stall** — the shared
  `RoutePatternMath` walkers are structurally terminating: dots are
  stamped from an INTEGER per-segment count (the old float countdown
  stalled below float precision on huge segments — `1e9f - 3f == 1e9f`
  — an infinite loop and hard game freeze), the dash walk carries its
  phase modulo one cycle and aborts on any non-advancing step, and
  non-finite segments/cadences are skipped. The texture path passes a
  real 24 000-stamp per-route budget (was `int.MaxValue`) and reuses
  ONE 16 MB pixel buffer across redraws (repeated style changes used to
  allocate it every redraw — GC hitches); the vector path estimates
  styled stamp counts in float with NaN-safe negated comparisons (the
  old `(int)` cast wrapped negative on huge lengths and waved near-zero
  cadences through the budget) and treats non-finite baked points as
  projection failures;
  **(4) survey layout on exact vertical bands** — the shared
  `CcSidePanel.AddBody` now places every body rect so its TOP edge sits
  at the reserved y and truncates overflow inside its band (the
  center-pivot rects used to reach half their height ABOVE the band —
  the root cause of the recurring survey text overlap: any taller block
  after a shorter one, like the 60 px status after the 30 px note,
  painted over it); Survey/Share/Settings/SystemMarkers/Routes panels
  carry matching clearance offsets, the survey status block owns a
  four-line band with every status string kept within it, and the
  whole system is scale-invariant so 0.8/1.0/1.6 behave identically;
  **(5, blocker) naming always leaves one visible managed marker** —
  palette births resolve through the pure `PaletteBirthResolution`
  rule: adopt the surviving rendering in place (normal), adopt an
  adoptable replacement standing at the same spot (naming close
  replaced the object), or RECREATE the managed marker from the
  newborn's committed state whenever a non-empty name lost its
  rendering — only a genuine cancel creates nothing, and every
  fallback logs a "Palette birth:" line for smoke evidence;
  **(5/6, blockers) just-created markers cannot vanish** —
  `PinClusterer`'s existing `alwaysVisible` exemption is finally WIRED:
  palette births, survey accepts, and quick pins mark their new pin
  sticky-visible in the display controller (exempt from cluster
  folding and search filters; the master ShowPins switch still
  applies) until the player changes the zoom tier — previously a
  marker born next to existing pins folded into a cluster (or fell to
  an active search filter) the same frame its creation flow closed,
  which read as the marker disappearing;
  **(6, blocker) survey accept is exact and immediate** —
  `SurveyEngine.Accept`/`AcceptRejected` surface the created pin so
  the runtime guarantees its rendering in the same `ResyncPins`, and
  Survey panel rows address observations by stable id (`id:<guid>`)
  and rejected entries by identity key (`key:<prefab|x|z>`) instead of
  1-based indexes a background sweep could shift between the panel's
  one-second refreshes — a click acts on exactly the row shown, and a
  stale target reports "the list just updated" instead of acting on a
  neighbor.
  **Retires RC11 `be0af44e`/`078de40` (ZIP `C08BBBB1…`, failed the
  owner's RC11 smoke on the 6 items above). Do not test, tag, or
  upload it.** Already-passed evidence that remains valid: startup
  environment, DEF-v1.0-001/002/003, and the RC10 P1 road-authority
  fix confirmed live in the owner's RC10 smoke. Everything RC12
  touched is re-verified by smoke section **R8** (then R7 as amended).
- RC11's identity block is preserved below for the record; its 15
  blockers remain in RC12 as amended:
- (RC11) **RC commit** `be0af44eb0b5fc8812b89d26333c8762741ec3e5` —
  delivered, on top of RC10:
  **(1) authoritative overlay visibility** — the texture overlays are
  written unconditionally from `OverlayVisibilityRule` (Jötunn's own
  checkbox listener races any cached write; the doubled/stale ink on
  Map Overlays toggling is gone; the Jötunn setter self-no-ops);
  **(3) roads at every zoom** — the rebake decision extracted into the
  pure sweep-tested `VectorBakeScheduler` (full 0.01–1.0 zoom sweep in
  wheel steps, thresholds both directions, debounce, periodic,
  invalidation), incomplete bakes (projection unavailable) retry within
  0.25 s instead of clearing the dirty flag, and the vector graphics
  carry real full-map rects so rect clippers can never cull the layer
  in pan/zoom bands;
  **(7) modal wheel** — a `Minimap.UpdateMap` prefix/postfix restores
  both zoom levels and uv windows while the pointer is over CC UI or a
  CC field is focused, so the wheel scrolls only the UI;
  **(4/5) routes** — `FreeDrawStrokeGate` (pure, tested) creates a
  route only for a stroke that actually travelled (fragment spam
  impossible), stable list order + overflow count, deterministic
  restore-latest by DeletedUtc, Snap in the bottom control area beside
  a confirmed Clear all routes, panel 672 with an explicit no-overlap
  budget; **(6)** RC10 vector style/cadence untouched;
  **(9/13) durable survey rejection** — rejected observations move to a
  persistent per-world Rejected list (`<uid>.survey-rejected.tsv`,
  bounded 500, pure codec) with stable prefab+cell identities
  suppressed from all future sweeps until restored/accepted from the
  new Rejected view; the identity also dedupes repeated sweeps even
  for zero-radius rules;
  **(10) rules in the UI** — the Survey panel's Rules view lists,
  enables/disables, deletes, and adds rules (enabled rules keep the
  5-field RC10 file shape; disabled rows carry "off"; the tsv remains
  the shareable import/export with Reload as advanced tooling);
  **(8/12) survey copy/UI** — no player-facing console pointers, the
  top-left notice points at [Survey], three-view layout with deliberate
  spacing and paging;
  **(11/14) humanized names** — the shared `NameHumanizer`
  (case/underscore/digit splitting, compound expansion) behind survey
  rows, survey pins, and quick pins: "Raspberry Bush", never
  "Raspberrybush";
  **(2) vanilla chrome** — per-button-group validated rail containers
  (shared-panel path included) hide backplates/decor/raycast objects
  with a once-per-change diagnostic log and pixel-perfect restore;
  **(15)** RC10's action-identity road authority, marker art, typing
  safety, quick-pin candidates, shared vector rendering, and palette
  are preserved (spot-checked by smoke R7.13).
  **Retires RC10 `16ce394b`/`98a1947` (ZIP `EA523400…`, failed the
  owner's RC10 smoke on the 15 blockers above).**
- RC10's identity block is preserved below for the record; its 23
  feedback items remain in RC11 as amended:
- (RC10) **RC commit** `16ce394bda3aef1563047b7e4df576152b2e5da9` —
  delivered all 23 owner feedback items:
  **(P1, DEF-v1.0-007) road authority by ACTION IDENTITY** — the
  live game places `mud_road_v2` for the hoe's "Level ground" and
  `path_v2` for "Pathen" (verified in the owner's own Player.log and
  the decompiled game assembly), both as smooth-and-paint-Dirt
  TerrainOps with `m_level`/`m_raise` false, so the RC8 settings-flag
  filter classified Level as road building. RC10 classifies by the
  placed prefab identity + Piece token + selected-piece corroboration
  in a pure, 17-test `TerrainActionClassifier` (paint must AGREE with
  identity; PaintType.Dirt alone is never authority; the classifier
  takes no settings flags at all, making the failure mode structurally
  impossible). Level/Raise/Cultivate/Reset/digging/unknown ops create
  zero road data, mark negative terrain intent, and erase covered ink
  of both kinds; an always-on rate-limited "Terrain action classified"
  log line names every action's classification. Pre-RC10 polluted ink
  is Construction-tagged and indistinguishable in the data, so it is
  NOT auto-deleted (explicit Pathen/Paved data must survive); it is
  removed interactively — Level/re-pave over it once, or
  `cc_roads delete` — and can never come back;
  **(5/6) one large-map vector language** — road ink 2× wider (6 px),
  routes render through the same screen-space vector system (per-route
  color, geometric screen-pixel dash/dot cadence via the shared
  `RoutePatternMath`, budget degrade to solid), route texture overlay
  suppressed on the large map exactly like roads (minimap/fallback
  kept), tightened minimap dot cadence;
  **(7) honest overlay toggles** — Jötunn checkboxes hooked as real
  layer switches driving BOTH presentations and the drawer settings,
  checkbox visuals re-synced to user state after suppression writes
  (pure `OverlayVisibilityRule`), the panel label renamed
  "Mod Overlays" → "Map Overlays" (exact-match, reversible);
  **(8/9/10) survey** — continuous bounded per-frame scanning
  (~1 s discovery; `SurveyScanIntervalSeconds` documented no-op),
  top-left notices coalesced to one per ~10 s only on new finds,
  status block lowered/enlarged with spaced result rows, starter rules
  broadened (dandelion/flint/wild seeds/guck/beehives/frost caves/
  runestones) with untouched RC8-era files upgraded in place;
  **(11/12/13) markers** — palette draggable + scrolling with
  collapsible category sections (row cap removed; search/recents
  kept), palette placements wear their cc:* sprite from the FIRST
  naming frame via the live icon element (never a temporary or
  permanent Dot; adoption recognizes the pre-applied sprite), and all
  12 sprites regenerated toward the hand-drawn map-icon language
  (seeded wobble/tilt/soft edges/ink texture; silhouettes and IDs
  unchanged; byte-reproducible generator);
  **(14) typing safety** — a reference-counted Jötunn input block held
  exactly while any CC text field is focused, all CC hotkey paths
  check the same state, first Escape only blurs; nothing intercepted
  when no field is focused;
  **(15) quick-pin naming** — candidate-chain naming (hover first
  line → ZNetView prefab → root) with technical-name sanitization and
  the "Marked object" fallback;
  **(16/17) routes framing** — explicit v1 planning-overlay copy in
  panel and docs, NO autowalk, pointer guards unchanged;
  **(18/19/20) UI** — Share panel on a strict two-column grid,
  replaced vanilla rail hides its VALIDATED container (deepest common
  ancestor of the seven rail buttons; never map image/hints/large
  root; per-button fallback on any surprise; restored on
  fallback/disable/teardown), layout re-audited by derivation with the
  visual matrix in smoke R6.15.
  **Retires RC8 `a053369`/`0241831` (ZIP `AF267AC2…`, road authority
  still defective) and every earlier RC — RC7 `f493178c`/`9b43e25`
  (ZIP `8E76CD0A…`, FAILED human smoke), RC6 `17b0524350` (ZIP
  `EE0F3A6E…`), RC5 `7881cbcd` (ZIP `1849C62E…`), RC4 `35f20e1a` (ZIP
  `8B4B41AD…`), RC3 `86050cd2` (ZIP `710183B3…`), RC `7ed20fef` (ZIP
  `B47E7C9D…`), RC `9eb65291` (ZIP `9F1F4128…`). Do not test, tag, or
  upload ANY of those ZIPs.** Already-passed evidence that remains
  valid: startup environment (Valheim 0.221.12 / Unity 6000.0.61f1 /
  BepInEx 5.4.23.3 / Jötunn 2.29.2.0, clean 1.0.0 banner, no CC
  errors), the adoption input-trap fix (DEF-v1.0-001), the workbench
  two-column layout discipline (DEF-v1.0-003), and overlay projection
  alignment (DEF-v1.0-002, residuals ≤ 1 texel). Everything RC10
  touched is re-verified by smoke section **R6** (then R5 4–12 and
  R3/R4 as amended).
- RC8's identity block is preserved below for the record; its twelve
  directives remain in RC10 as amended:
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
- **ZIP:** `artifacts\thunderstore\TheConcernedCat-ConcernedCartographer-0.9.0.zip`
  (built at the RC13 commit; an identical immutable copy is at
  `artifacts\rc13\TheConcernedCat-ConcernedCartographer-0.9.0-RC13.zip`
  — verify the hash below before importing. The retired RC12 package
  (ZIP `7A027F7B…`, DLL `FD6DB99C…`) was moved to
  `artifacts\thunderstore\superseded\` alongside the never-published
  INTERNAL 0.9.0 milestone ZIP (`…-0.9.0-internal-milestone.zip`) —
  the internal file shares only the version number, never the bytes.
  The retired copies under `artifacts\rc12\`, `artifacts\rc11\` (ZIP
  `C08BBBB1…`, DLL `8C5233A4…`), `artifacts\rc10\` (ZIP `EA523400…`,
  DLL `A350D0CE…`) and `artifacts\rc8\` (ZIP `AF267AC2…`, DLL
  `E9904771…`) must NOT be tested or uploaded.)
- **ZIP SHA-256:** `19ADD2E56C0B9CDF4A69772D862D630A9E9BB35749EDE8C177B579EC8123E0ED`
  (321,833 bytes — fresh RC13 / 0.9.0-beta bytes; retired hashes are
  never reused; the immutable rc13 copy verified hash-identical)
- **Plugin DLL SHA-256:** `CE783057743E8E9990DAE5167F09133CDE051ED911DC10278F3C675869536BDD`
  (465,408 bytes; the DLL inside the ZIP verified hash-identical to the
  Release build output; informational version
  `0.9.0+392ab9382f9088b72e65d9e6a530bbe030c526d6` verified in the DLL;
  the 12 `CC.Icons.cc-*.png` sprite resources re-verified embedded)
- **Assembly metadata (verified in the DLL):** Company "The Concerned Cat",
  Product "Concerned Cartographer", Copyright © 2026 Eren Cansunar,
  RepositoryUrl embedded, informational version `0.9.0+<RC13 commit>`,
  FileVersion 0.9.0.0.
- **Package audit:** ZIP root contains exactly `manifest.json`, `README.md`,
  `CHANGELOG.md`, `LICENSE`, `icon.png` (256×256),
  `plugins/TheConcernedCat.ConcernedCartographer.dll`. No PDBs, game DLLs,
  saves, logs, or secrets. Dependencies pinned:
  denikson-BepInExPack_Valheim 5.4.2333, ValheimModding-Jotunn 2.29.2;
  manifest `version_number` 0.9.0 matches Plugin.cs and the csproj
  (validator-enforced).

## 6. Sprints and issues

Every sprint v0.3→v1.0 shipped through its internal gate; all 42 child
issues and 8 controllers (#8, #27–#81) are closed with evidence comments.
Shipped versions on main with tags: 0.3.0, 0.4.0, 0.5.0, 0.6.0, 0.7.0,
0.8.0, 0.9.0. The RC6 line is on main; the CC-098 completion line (RC7,
RC8, RC10, and RC11 — all now retired — plus the RC12 owner-feedback
pass) is on `feat/cc-098-v1-completion` awaiting its post-smoke merge
and tag (section 19).

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
twelve were implemented in RC8; RC8's road-authority mechanism was
itself defective (below) and RC8 is retired.

**Owner consolidated-feedback pass (2026-09-01, RC10) — DEF-v1.0-007
(P1, the FOURTH road-authority report):** Level Ground STILL produced
road ink under RC8. Root cause established against the live game, not
another heuristic: the owner's own Player.log shows the hoe placing
`mud_road_v2` for "Level ground" and `path_v2` for "Pathen" (the
prefab names do not match the menu labels — a historical misnomer),
and the decompiled `TerrainOp`/`Player.PlacePiece`/`TerrainComp`
chain confirms both actions arrive as smooth-and-paint-Dirt operations
with `m_level`/`m_raise` false — exactly the combination RC8's
settings-flag filter classified as road construction. The fix removes
settings flags from authority entirely: a pure
`TerrainActionClassifier` (Domain, 17 regression tests including the
exact failure mode) classifies by placed-prefab identity + Piece token
+ selected-piece corroboration, requires paint to AGREE with identity,
and authorizes ONLY Pathen ⇒ Dirt and Paved road ⇒ Paved. Everything
else — Level, Raise, Cultivate, Reset paint, pickaxe digging, unknown
or modded ops, corroboration mismatches — creates zero road data,
marks negative terrain intent, and erases the covered ink of both
kinds. An always-on rate-limited "Terrain action classified" log line
makes any future regression visible without a debug build. Migration
honesty: pre-RC10 polluted strokes are Construction-tagged and
indistinguishable from genuine Pathen ink in the data, so nothing is
auto-deleted (the owner's explicit roads must survive); the pollution
is removed interactively — one Level/re-pave pass over it, or
`cc_roads delete` — and the fixed classifier prevents recurrence.
The remaining 22 feedback items (rendering/toggles/survey/markers/
input/quick-pin/routes-framing/share/chrome/audit/tests/docs) are
delivered as itemized in the identity section above.

Gate evidence at the RC10 commit is recorded in §8. The UI layout
matrix (1080p/1440p × UiScale 0.8/1.0/1.6) remains derivation-based in
code and is verified live by smoke R6.15 — a game UI cannot be
screenshot-proven from the conveyor.

**Owner final-smoke pass (2026-09-03, RC14) — 5 defects from the
owner's final RC13 smoke, all lifecycle/input class, all fixed:**

1. **Custom cc:* markers degraded to vanilla Dots after relog.** Root
   cause: the pin adapter's fail-soft `_disabledForSession` latch was
   never cleared by `Reset()` and the adapter object outlives every
   game session, so one teardown-frame failure (defect 5) silently
   disabled sprite rebinding for the rest of the process — and four
   cc:* icons' saved vanilla fallback IS the Dot. Fixed as one
   coherent lifecycle repair with defect 5: session boundaries
   (`Reset`/`ReconcileOnMapReady`) clear the latch, the rebind
   decision is the pure tested `SpriteRebindRule` (restart-claimed
   cc:* renderings rebuild to regain their art; genuine vanilla pins
   are NEVER repainted), `AddManagedPin` applies sprites to same-frame
   UI elements too, cluster markers wear their dominant cc:* sprite,
   and `CcIconSprites` scopes its failure blacklist per session and
   marks sprites `DontUnloadUnusedAsset`.
2. **Atlas drawer forgot its dragged position on relog** (and even on
   every reopen — `Toggle()` re-docked unconditionally). Nothing ever
   read the dragged RectTransform back, and Jötunn rebuilds
   `CustomGUIFront` every scene change. Fixed: the position is noted
   every visible frame, captured on close/boundary/quit into the new
   `Drawer/PanelPosition` setting, and restored through the pure
   `PanelPositionRule` clamp (fully on-screen for the current canvas
   and UI scale; malformed/empty stored values fall back to the
   default dock). Adjacent relog defect fixed with it: `CcSidePanel`'s
   `_appliedScale` survived the scene change, so rebuilt side panels
   lost a non-default UI scale.
3. **Armed Quick Pin leaked input to vanilla** — the capture click
   also swung the weapon and Escape also opened the pause menu. The
   RC13 armed mode was a passive raw-Input observer. Fixed: the pure
   `QuickPinInputGate` owns the interaction (armed lifetime + the
   owned press's whole frame, tick-order independent; cancel wins
   over capture; external disarm releases immediately) and the new
   `PlayerInputGate` applies it through two narrow skippable Harmony
   prefixes — `Humanoid.StartAttack` (local player only) and
   `Menu.Update` (only while the menu is closed) — fail-soft,
   uninstalled on dispose. Text-field typing suppression (RC10
   feedback 14) is untouched.
4. **Persisted roads did not render on the minimap after relog.**
   Persistence was fine (the "Road atlas ready" line showed full
   counts); the renderer's cached Jötunn overlay handles are
   process-lifetime while Jötunn destroys every overlay texture on
   `Minimap.OnDestroy` — session 2 painted into a dead texture
   ("Could not rebuild road map overlays" once per redraw). Fixed:
   handle caches are liveness-checked through the pure
   `OverlayHandleRule` (verified against Jötunn 2.29.2 IL: a
   destroyed `OverlayTex` stays reference-non-null and does NOT
   lazily recreate, so the Unity-null check is decisive) and
   re-resolve against the live map; `ResetMapSession()` on both
   renderers runs at map-available BEFORE the redraws; and
   `RoadVectorLayer`'s fail-soft "session" disable — which actually
   lasted the process — now resets per session. Road source
   authority, Dirt/Paved identity, hidden strokes, and the honest
   checkbox rule are untouched.
5. **Sentry CONCERNED-CARTOGRAPHER-2 (event #109, 2026-09-03): a real
   NullReferenceException during a pin update on 0.9.0.** The
   reported frame names do not exist in this codebase (hand-scrubbed
   before filing; CC has no `Minimap.UpdatePins` patch — its only
   Minimap patches are the RC11 wheel guard and click gate, both
   fully wrapped). The demonstrable in-code source matching the
   signature: `PinAdapter`/`PinDisplayController` wrote through
   `Minimap.instance` unguarded on six pin-update paths, and the
   runtime's map-open block runs before its world-boundary check, so
   teardown frames dereferenced a destroyed map; the caught NRE was
   re-emitted via `LogError`, which `CrashReportingHub` forwards to
   Sentry as a fatal subsystem failure — proving, incidentally, that
   the #97 capture pipeline works end-to-end in the field. Fixed with
   defect 1's lifecycle repair: every pin write path is now a
   lifecycle-guarded no-op without a live map, and the next
   map-available reconcile repairs every rendering. No blanket catch
   was added anywhere. Smoke R10.5 watches for recurrence on the
   owner's Sentry project.

**Owner RC11 smoke-feedback pass (2026-09-02, RC12) — 6 items, 4
release blockers, all fixed:** the owner's RC11 smoke (reported via a
separate thread) found paved ink reading as dark as dirt, a route list
that only refreshed after the panel's own buttons (stale rows piling
up), the dotted route style freezing the game on long routes and
repeated style changes, survey text still overlapping, and both
marker-creation regressions: a palette marker vanishing after its
naming flow closed, and survey accept not producing a visible marker.
Root causes were located, not guessed: the `AddBody` helper reserved
`[y, y-height]` but centered its rect ON y, so any taller body block
after a shorter one physically overlapped it (the recurring survey
overlap); `WalkDots` counted distance down in float and stalled below
float precision on long segments (a real infinite loop), while the
vector budget estimate's `(int)` cast wrapped negative on huge lengths;
the route panel had no store-change signal at all; and the pin
clusterer's `alwaysVisible` exemption existed but was never wired, so a
newborn marker folded into a neighboring cluster (or fell to a search
filter) the same frame its creation flow closed. Fixes are itemized in
the RC12 identity above; each carries focused regression tests where
the logic is pure (`Rc12RouteTests`: change-stamp semantics, erase
tombstone + undo, walker termination on huge/non-finite/tiny-cadence
inputs; `Rc12PinSurveyTests`: sticky clusterer exemption, every
`PaletteBirthResolution` branch, accept surfacing + identity-addressed
rejected rows), and the runtime-only behaviors (ink shade, live-list
feel, no-stall feel, layout at scale, the two creation flows) are smoke
rows R8.1–7 with "Palette birth:" fallback diagnostics logged for the
record.

**Owner RC10 smoke-fix pass (2026-09-02, RC11) — 15 release blockers,
all fixed:** the RC10 smoke confirmed the P1 road-authority fix live
(the owner's LogOutput.log shows "Terrain action classified:
level-ground (mud_road_v2) … => no road" and "pathen (path_v2) … =>
Dirt road" exactly as designed) but failed on overlay toggle
double-render/stale ink, zoom-band road holes, wheel-over-UI zooming,
route fragment spam and list churn, routes-panel overlap, remaining
vanilla backplates, survey reject amnesia and duplicate observations,
console-pointing survey copy, file-only rule editing, and raw prefab
names ("Raspberrybush"). Root causes and fixes are itemized in the
RC11 identity above; each carries focused regression tests where the
logic is pure (`VectorBakeSchedulerTests`, `FreeDrawStrokeGateTests`,
`SurveyRc11Tests`, `NameHumanizerTests`, updated
`QuickPinSuggesterTests`/`SurveyTests`), and the Unity-side fixes
(unconditional overlay writes, graphic rects, wheel snapshot/restore,
rail containers) are smoke rows R7.1–3/12 with a once-per-change
"Vanilla rail chrome:" diagnostic so the smoke run records WHAT was
hidden.

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

- **538/538 tests** in the game-free core suite (Release configuration,
  re-run at the RC14 commit): everything below plus the RC14 suite —
  `Rc14FinalSmokeTests` (30: the panel-position "x,y" round-trip with
  malformed/non-finite input degrading to "nothing stored"; the
  on-screen clamp for in-bounds, off-screen, and UI-scaled restores
  plus the oversized-panel centering case; the overlay-handle liveness
  truth table with the dead-texture relog regression named; the Quick
  Pin input gate's owned arming/cancel/capture frames,
  whole-armed-lifetime suppression, cancel-over-capture, one-shot
  capture, unarmed pass-through, and immediate external-disarm
  release; and the sprite-rebind rule — restart claim regains cc:*
  art, vanilla pins never repainted, destroyed sprite rebuilds,
  cc:*-to-cc:* changes sharing one vanilla fallback rebuild) —
  plus the RC13 suite —
  `Rc13PolishTests` (26: the road-ink feather profile's opaque core,
  full-transparency edge, symmetry, monotone falloff, the
  preserved-perceived-width invariant (50% alpha exactly at the crisp
  half width) and modest-widen bounds; the palette wheel factor held
  in the owner's 2–3× window with multiplicative scaling, the
  three-rows-per-notch floor, and monotonicity; the default-panel
  rule's exactly-once-per-fresh-open, first-open-of-session,
  never-fight-after-close/switch, disarm-on-busy-surface,
  disarm-on-unavailable-with-no-late-pop, re-arm-on-close, and the
  IsArmed gating contract; and the orphan-chrome truth table — empty
  decoration hides, large root / protected objects / live controls /
  live text each block, fallback forces restore, bounded climb) —
  plus the RC12 suites —
  `Rc12RouteTests` (10: change-stamp bump on every published change and
  none on rejected ones, full-erase tombstone with undo restoring ink
  and liveness, partial-erase survival, dot/dash termination at the
  budget on 1e9-length segments, non-finite segment/cadence skipping,
  tiny-cadence budget bounding) and `Rc12PinSurveyTests` (11: sticky
  pins never fold while neighbors still cluster, the un-sticky control
  case, all five `PaletteBirthResolution` branches, accept surfacing
  the created pin and emptying Pending, unknown-id refusal,
  accept-all reporting every pin, rejected rows resolving by identity
  after the list shifts) — plus the RC11 suites —
  `VectorBakeSchedulerTests` (9: full zoom sweep in wheel steps,
  threshold boundaries both directions, debounce, periodic,
  incomplete-bake retry, invalidation), `FreeDrawStrokeGateTests` (6:
  twitch-discard, travel threshold with first-point carry,
  pointer-over-UI stroke end, hold-without-movement, reset),
  `SurveyRc11Tests` + `NameHumanizerTests` (28: durable rejection and
  restore/accept, identity dedupe incl. zero-radius rules, rejected
  codec round-trip + malformed rows, rule enable/disable file
  round-trip, session reset semantics, the humanizer matrix, humanized
  names flowing into pins) — and the RC10 suites —
  `TerrainActionClassifierTests` (17: the exact Level-carries-Dirt
  failure mode, every terraform/unknown action refused, paint-identity
  agreement, selection corroboration, token fallback, clone/name
  normalization, diagnostics), `RoutePatternMathTests` +
  `OverlayVisibilityRuleTests` (14: dash phase carry, vertex-density
  invariance, corner flow, zoom-linear cadence, budgets, the
  texture/vector/checkbox truth table), the broadened survey starter
  matching + RC8-starter upgrade recognition, the every-cc:*-icon-
  ships-a-sprite contract behind the instant-sprite placement fix, and
  the quick-pin technical-name sanitization suite (candidate chains,
  first-line hover, friendly fallback) — plus the RC8 suites
  (`RoadSourceAuthorityTests` restart/reopen, align-live guidance,
  icon-registry sprite contract, quick-pin ledger) and the DEF-v1.0-006
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
- Validator green with the 0.9.0 identity (csproj = thunderstore.toml =
  Plugin.cs enforced); solution builds with 0 errors (1 known benign
  MSB3245).
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
owner starts at the NEW SHORT section R10 (the RC14 / 0.9.0-beta
final-smoke-fix pass: five rows — marker relog, road-minimap relog,
drawer position, Quick Pin input ownership, no Sentry pin-exception
recurrence — each with its restore/fallback paths), NOT at the top, on
the exact RC14 beta ZIP named above.** After R10, re-verify any R9 row
whose surface the pass touched, then complete whichever R9 → R8 → R7 →
R6 → R5 → R3/R4 rows (as previously amended) earlier smokes did not
finish — rows already passed stay passed, because RC14 deliberately
changes only the five lifecycle/input behaviors. The full 2.5–4 h
checklist is not restarted.

## 19. Remaining Git commands (run after the smoke test passes)

The RC lives on `feat/cc-098-v1-completion` (not yet on main). After
R10 and the remaining amended sections pass:

⚠ **Tag-name decision needed first**: `concerned-cartographer/v0.9.0`
already exists on the public remote, pointing at the internal 0.9
hardening milestone (`4931020e`). Never force-move a published tag.
The commands below use the collision-free name
`concerned-cartographer/v0.9.0-beta` for this beta; if the owner
prefers a different scheme, substitute it consistently.

```powershell
# 1. Merge the completion branch to main (PR or fast-forward — owner's call):
git checkout main; git merge feat/cc-098-v1-completion
git push origin main
# 2. Tag the RC13 commit named in the identity section (now in main history):
git tag -a concerned-cartographer/v0.9.0-beta -m "Concerned Cartographer 0.9.0 (Public Beta) - Stable Living Atlas beta" 392ab9382f9088b72e65d9e6a530bbe030c526d6
git push origin concerned-cartographer/v0.9.0-beta
gh release create concerned-cartographer/v0.9.0-beta artifacts/thunderstore/TheConcernedCat-ConcernedCartographer-0.9.0.zip --title "Concerned Cartographer 0.9.0 (Public Beta)" --prerelease --notes-file src/ConcernedCartographer/Package/CHANGELOG.md
```

## 20. Thunderstore upload data (owner-only)

- File: `TheConcernedCat-ConcernedCartographer-0.9.0.zip`
- Team/namespace: **TheConcernedCat** · Community: **valheim**
- Categories: **mods, client-side, utility, ai-generated**
- Dependencies: denikson-BepInExPack_Valheim 5.4.2333, ValheimModding-Jotunn 2.29.2
- Version: 0.9.0 (Thunderstore has no earlier upload; the number is
  free there despite the internal git history) · Upload via
  thunderstore.io web UI, or `pwsh ./scripts/publish.ps1 -Version 0.9.0`
  with `TCLI_AUTH_TOKEN` set only in that shell.

## 21. DO NOT RELEASE IF

- Any **BLOCKS** smoke row fails and cannot be fixed + re-verified.
- The ZIP hash on disk no longer matches this dossier.
- A human ZIP inspection finds anything beyond the six audited entries.
- The two-client tombstone test (smoke 7.4) shows a resurrected deletion.
- Any world save, character file, or foreign mod's data is modified in
  any test.
- The fresh-profile install (smoke 10.4) fails to reach the main menu
  cleanly.
