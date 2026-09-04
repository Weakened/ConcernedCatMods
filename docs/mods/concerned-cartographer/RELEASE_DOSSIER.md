# Concerned Cartographer v1.0 line — Release Dossier (0.10.0 Public Beta)

Prepared by the autonomous conveyor (Tankard Olafsson) per OPS-001 rev 2.
The single remaining gate is the human smoke test
(`PRE_RELEASE_SMOKE_TEST.md`); nothing is publicly visible yet.

## 0. RC16 THUNDERSTORE STATUS (2026-09-03): uploaded, auto-rejected — MODERATION STATE, NOT A PACKAGING DEFECT

The 0.10.0 (RC16) ZIP was uploaded to Thunderstore and its package page
now shows the red REJECTED banner. Assessment, corrected against the
official documentation and re-verified against the sealed RC16 bytes:

- **This is a post-upload moderation state.** Per
  `wiki.thunderstore.io/mods/mod-not-visible` ("Package rejected"), a
  rejected page is accessible only to moderators and the uploading
  team. The upload itself SUCCEEDED — the package passed Thunderstore's
  upload validation, including its manifest checks. Per
  `wiki.thunderstore.io/moderation/community-moderators`, automatic
  moderation flags packages into a Review Queue, "many of the packages
  flagged in this queue are false positives, and moderators are
  encouraged to manually approve them after confirming that they are
  safe"; automated rejections record a rejection reason that is
  **visible to the uploading team on the package page**.
- **The manifest is NOT a defect.** The RC16 manifest carries the five
  official fields plus `namespace` and `FullName`; both extras are
  intentional output of the official Thunderstore CLI (tcli 0.2.4,
  `PackageManifestV1.cs` — `FullName` is its computed property). An
  earlier hypothesis blaming these fields is WITHDRAWN; no manifest
  rewrite, no RC17, and no repackaging is warranted or performed.
- **The archive re-verified structurally valid (2026-09-03, sealed
  copy `artifacts\rc16\…-RC16.zip`):** ZIP integrity OK; exactly the
  six audited entries (manifest.json, README.md, CHANGELOG.md,
  LICENSE, icon.png 256×256, our single plugin DLL — no PDBs, no
  saves, no game files, and none of the wiki's most common rejection
  cause, "files from another mod"); ZIP SHA-256 `362AE442…` and DLL
  SHA-256 `8BC05431…` match §1–5 and `artifacts\rc16\SHA256SUMS.txt`;
  DLL InformationalVersion `0.10.0+a23bef0…`; a URL-string scan of the
  DLL (ASCII and UTF-16) surfaces only the public GitHub repository
  URL.
- **Version 0.10.0 is consumed by the existing upload.** The path
  forward is REAPPROVAL of the existing package page, not a re-upload.
  Do not delete the page and do not upload new bytes unless a
  moderator explicitly asks for a changed package (only then would a
  new version number, e.g. 0.10.1, come into play).

**Exact reapproval steps (owner-only):**

1. Open the package page while logged in as the TheConcernedCat team
   (`https://thunderstore.io/c/valheim/p/TheConcernedCat/ConcernedCartographer/`)
   and read the **rejection reason** shown there — it is recorded for
   the uploading team and states why the automated system (or a
   moderator) rejected it.
2. Create a post in the **rejected-uploads forum** of the Thunderstore
   Discord (`https://discord.thunderstore.io/`, forum channel
   `#rejected-uploads`, direct link
   `discord.com/channels/809128887366975518/1193275177782493254`)
   requesting review and reapproval. Suggested post:

   > **Package:** TheConcernedCat/ConcernedCartographer 0.10.0
   > (Valheim community), uploaded 2026-09-03, now showing the
   > rejected state. Rejection reason shown on our page: *[paste it
   > here]*.
   > This is a client-side BepInEx/Jötunn map mod (dependencies
   > pinned: denikson-BepInExPack_Valheim-5.4.2333,
   > ValheimModding-Jotunn-2.29.2), built with the official tcli
   > 0.2.4, uploaded by its author's new team as a first release. Full
   > source: https://github.com/Weakened/ConcernedCatMods (MIT). The
   > ZIP contains only manifest/README/CHANGELOG/LICENSE/icon and our
   > own plugin DLL — ZIP SHA-256 362AE442386CC6CC5B348F4B177D6DE452
   > DCD0A01597A58D7BEB5C1D8046368F. The AI-assisted development is
   > disclosed via the ai-generated category and in the README. Crash
   > reporting is opt-in-only and documented in the repo's PRIVACY.md.
   > Happy to provide anything else needed — could this be manually
   > reviewed and reapproved?

3. If a moderator instead requires changed bytes, come back to the
   conveyor for a 0.10.1 build; otherwise NOTHING is rebuilt (§21: the
   sealed ZIP hash must keep matching this dossier).
4. After reapproval: the listing may take minutes to hours to appear
   in searches and mod managers (API caching); the post-publication
   smoke (§13) then proceeds unchanged.

## 1–5. Release candidate identity

- **Version:** **0.10.0 (Public Beta)** — the owner's final pre-upload
  re-version (2026-09-03, at RC16) of the earlier owner-approved 0.9.0
  beta identity: the feature-complete v1 candidate ships first as a
  public beta, and 1.0.0 stays reserved for the stable release.
  0.10.0 was UPLOADED to Thunderstore on 2026-09-03 and is currently in
  the rejected/review moderation state (see §0 — reapprove, do not
  re-upload); the version number is therefore consumed by that upload.
  It no longer shares a
  number with the never-published INTERNAL 0.9 hardening milestone
  (commit `4931020e`, git tag `concerned-cartographer/v0.9.0`). The
  never-published DRAFT GitHub Release created hours before this
  re-version was deleted; its short-lived
  `concerned-cartographer/v0.9.0-beta` tag is protected by a
  repository ruleset (tag deletion forbidden) and remains as a
  historical, never-released marker at the RC15c commit. The release
  tag is `concerned-cartographer/v0.10.0-beta` (section 19). Data and
  schema formats are unchanged from RC12; upgrading this beta to
  1.0.0 later is automatic and lossless.
- **RC commit:** `a23bef007a75b84282c3aa0e0043b9be468f3301` (**RC16**,
  the owner-directed 0.9.0 → 0.10.0 re-version of RC15c, 2026-09-03,
  committed directly to `main` after PR #105 landed the completion
  line; the package below was built at exactly this commit with a
  clean tree, and the DLL's informational version embeds it). RC16
  changes ONLY version identity metadata — csproj `Version`,
  `PluginVersion`, `thunderstore.toml` `versionNumber`, the CHANGELOG
  heading, and one doc comment. No code path, data, or schema change;
  568/568 Release suite re-run at this commit.
  **Retires the RC15c build `87a0fec`/`e08c5d9` (ZIP `036DBD39…`,
  DLL `58BDD226…`, immutable copy in `artifacts\rc15c\`) — do not
  test, tag, or upload it.** The owner's manual smoke pass (R11 plus
  the R10 1/2/5 re-runs) is recorded as PASSED on the exact RC15c
  ZIP; RC16's byte delta from RC15c is version metadata only, and the
  mandatory post-upload clean-profile install doubles as the RC16
  exact-ZIP sanity check.
- RC15c's identity block is preserved below for the record:
- (RC15c) **RC commit** `87a0fecbe184fac7480ec7611dc9dfe96d1203ae`
  (the owner-directed Thunderstore-README revision of RC15b,
  2026-09-03; built at exactly that commit with a clean tree,
  and the DLL's informational version embeds it). RC15c changes ONLY
  the packaged `README.md` — the owner-supplied storefront copy
  (shorter, capability-focused, with the beta-status/support/privacy
  section). No source file, data, schema, or diagnostic line changed;
  the DLL was rebuilt at this commit per packaging doctrine, so its
  bytes differ from RC15b's only by the embedded commit identity
  (size unchanged, 478,720 bytes). A fresh authorship-trace/leak scan
  of the packaged bytes at this commit found no AI-authorship traces
  in source or DLL (ASCII and UTF-16), no PDB file, and no
  machine-username paths (the embedded CodeView path is the same
  `C:\code\...` shape the RC15b audit accepted).
  **Retires the RC15b build `e4abe1f6`/`c31746a` (ZIP `8AC3A779…`,
  DLL `BA8975CA…`, immutable copy in `artifacts\rc15b\`) — do not
  test, tag, or upload it.** All RC15b privacy-audit guarantees and
  RC15/RC14-good behavior are unchanged; smoke **R11 runs on the
  RC15c ZIP**.
- RC15b's identity block is preserved below for the record:
- (RC15b) **RC commit** `e4abe1f60996284eb879b9d917eed6d096b68ccc`
  (the CC-098 privacy-audit revision of RC15, 2026-09-03; built at
  exactly that commit with a clean tree). The RC15 functional work was
  green, but final acceptance failed its privacy audit: the mod's own
  log lines and the support report still carried identifiers. RC15b
  changes ONLY the privacy of emitted diagnostics — behavior, data,
  and schema formats are untouched:
  **(1) no world UIDs in any log line** — "Road atlas ready", the
  pin/route persistence skip/recover/load/save/journal lines, and the
  atlas backup/restore lines are aggregate-only now; sidecar
  filenames and the internal world-key behavior are unchanged (the
  UID still locates files, it just never reaches the log);
  **(2) no filesystem paths** (they embed machine usernames) in any
  persistence/backup/migration/survey/saved-views/localization line —
  fixed sidecar suffixes name the file instead;
  **(3) no names or contents** — player names left the sync
  share/apply lines (HUD/console still name the author), suggested
  names left the Quick Pin line, the workbench logs outcome + id
  instead of a message that can echo user-typed text;
  **(4) no positions** — the terrain-classification (DEF-v1.0-007),
  terrain-intent, reconciliation, observation-debug, and calibration
  lines are position-free; the `cc_roads align` / `align live` probe
  tables (live positions) print to the console only, with a
  coordinate-free "ALIGNMENT PASS/FAIL + projection context" log line
  kept as evidence (smoke rows updated accordingly);
  **(5) exception text is scrubbed** — everything interpolated into
  the log now routes through the new `SafeLogText` (Describe/Brief)
  over the tested #97 `CrashReportSanitizer`, so IO errors can no
  longer echo profile paths, uid-bearing file names, save names, IPs,
  or URLs into LogOutput.log (`CrashSubsystems.Infer` keyword shapes
  preserved);
  **(6) the support report is sanitized by construction AND by
  scrubbing** — rebuilt around the pure `SupportReportComposer`
  (Domain, unit-tested): the `world-uid` line is GONE; versions,
  settings, row counts, sizes in KB, and the backup count only;
  `AtlasBackupTools` merely locates files — the composer's signature
  cannot receive the UID or a path, and every emitted line passes
  through the sanitizer as defense in depth. The new
  `SupportReportPrivacyTests` (11 tests; suite now **568**) plant
  pin/road content, world UIDs, paths, usernames, coordinates, save
  names, URLs, and IPs and prove none can appear.
  **Retires the pre-audit RC15 build `e9615b00`/`ae902a2` (ZIP
  `F89AAD13…`) — do not test, tag, or upload it.** The RC15
  relog/tombstone lifecycle, map-data-loaded rebind, System Markers
  "Visible to other players", redraw teardown hardening, and all
  RC14-good behavior are unchanged; smoke **R11 runs on the RC15b
  ZIP** (row 4 now verifies the whole log and the support report).
- RC15's identity block is preserved below for the record:
- (RC15) **RC commit** `e9615b00759631d1dcc35928dd7f7ffce2c3bf00`
  (on the CC-098 line — the owner-directed final beta blocker pass of
  2026-09-03).
  RC15 delivers, on top of RC14:
  **(1) the false relog tombstone is fixed at its lifecycle root** —
  the owner reproduced on the exact RC14 DLL: cc:camp rendered as
  vanilla Fire and cc:travel as vanilla Portal after relog while the
  sidecar rewrote the same records `Deleted=1` ("deleted through
  vanilla UI" immediately after reconcile). Root cause
  (decompile-verified, deterministic): vanilla loads the character's
  saved map AFTER `Minimap.Start` — `LoadMapData → SetMapData →
  ClearPins + re-AddPin` rebuilds the whole pin list in place,
  destroying every rendering the map-available reconcile had created;
  the vanilla-edit absorber then inferred mass deletion from the
  renderings' absence, and the save-file copies stayed behind as plain
  Fire/Portal pins. The fix inverts the burden of proof: a missing
  rendering is NEVER deletion evidence — `AbsorbVanillaChanges` now
  only unlinks lost renderings and raises `NeedsRebind` (repaired by a
  `rendering-loss-repair` reconcile on the autosave cadence). A
  tombstone is written exclusively by the new explicit-delete path:
  `PinDeletionWatch` (Harmony prefix on `Minimap.RemovePin(PinData)`;
  the user-facing delete paths — large-map right click and gamepad
  JoyTabRight — both route through `RemovePin(Vector3,float)`, while
  `ClearPins` bypasses `RemovePin` entirely, so a rebuild can never
  masquerade as a deletion; adapter self-removals run in
  `BeginSelfRemoval` scopes) feeding
  `PinAdapter.HandleExplicitVanillaDelete`, decided by the pure
  `PinTombstoneRule` — explicit event AND stable fully-bound session
  (reconcile completed for the current map generation) AND at most
  once per entity; anything else keeps the pin and rebinds. Fail-soft:
  if the patch cannot install, deletions are never captured and a
  vanilla-deleted managed pin is restored by the next reconcile
  instead of tombstoned (data-keeping is the safe degraded direction;
  one startup warning documents it).
  **(2) the rebind leg** — the runtime now also subscribes Jötunn's
  `OnVanillaMapDataLoaded` (a `Minimap.LoadMapData` postfix):
  `OnMapDataReconstructed` re-reconciles right after vanilla rebuilds
  the pin list from the character save, so every living cc:* pin
  regains exactly one rendering wearing its CC sprite via the RC14
  `SpriteRebindRule` path — the persisted vanilla fallback type stays
  uninstall-safe underneath and never becomes the visible in-mod icon.
  A re-claimed own rendering that already wears the wanted live sprite
  re-records instead of rebuilding (no flicker).
  **(3) full redraws survive teardown races (directive item 8)** —
  `RoadOverlayRenderer.RedrawAll` and `RouteOverlayRenderer.RedrawAll`
  capture their live `Texture2D` handles at resolve and re-verify them
  through the pure `OverlayHandleRule.MayWrite` immediately before
  `SetPixels32`/`Apply` (the RC13 Sentry event
  `84529651470a47f2873d254cb15b7442` was an NRE at
  `Texture2D.SetPixels32` during "rebuild road map"); a teardown
  mid-redraw resets the cached handles, logs one rate-limited
  privacy-safe Warning (never an Error — the crash hub forwards Errors
  to Sentry), and retries on the next valid map session (whose
  map-available path always runs `ResetMapSession` + `RedrawAll`).
  **(4) privacy-safe lifecycle diagnostics (directive item 7)** — the
  log now records the exact build
  (`Release: ConcernedCartographer@0.9.0+<commit>`), numbered map
  session transitions via the pure `MapSessionTracker`
  ("Map session lifecycle: generation N (map-available /
  map-data-loaded / world-unloaded)"), aggregate pin reconcile lines
  ("Pin reconcile (<reason>): linked/claimed/added/removed/sprite
  rebinds"), tombstone cause lines, and overlay
  resolve/reset/redraw state with texture liveness/size — never
  world/character/player/server names or IDs, coordinates, pin/route
  contents, paths, tokens, or IPs. Verbose success traces stay behind
  `Diagnostics/DebugLogging` (default off; smoke R11.4 verifies and
  returns it to default).
  **Retires RC14 `7a160d46`/`93646d5` (ZIP `49DBB847…`) — do not
  test, tag, or upload it.** The owner's smoke evidence for surfaces
  RC15 does not touch remains valid; the relog/tombstone lifecycle,
  redraw hardening, and diagnostics are verified by the NEW smoke
  section **R11** (plus re-runs of R10 rows 1/2/5), and RC15
  deliberately changes nothing else. Data and schema formats are
  unchanged from RC12/RC13/RC14.
- RC14's identity block is preserved below for the record:
- (RC14) **RC commit** `7a160d4601d52d6c8589089be814157f0952322d`
  (the final-smoke corrective pass of 2026-09-03,
  fixing exactly the 5 defects from the owner's final RC13 smoke).
  RC14 delivers, on top of RC13:
  **(1) custom cc:* markers survive relog** — session boundaries clear
  the pin adapter/display fail-soft latches (previously
  process-permanent), the sprite rebind decision is the pure tested
  `SpriteRebindRule` (restart-claimed cc:* renderings rebuild to
  regain their art; genuine vanilla pins are never repainted),
  `AddManagedPin` applies sprites to same-frame UI elements, cluster
  markers wear their dominant cc:* sprite, and `CcIconSprites` scopes
  its failure blacklist per session with `DontUnloadUnusedAsset`
  sprites;
  **(2) Atlas drawer position persists** — noted every visible frame,
  captured on close/boundary/quit into the new `Drawer/PanelPosition`
  setting, restored through the pure `PanelPositionRule` on-screen
  clamp (resolution/UI-scale safe; malformed/empty falls back to the
  default dock; `Toggle()` no longer re-docks unconditionally);
  `CcSidePanel` UI scale now survives scene-change rebuilds;
  **(3) armed Quick Pin owns its input** — the pure `QuickPinInputGate`
  (armed lifetime + owned-press frame, tick-order independent, cancel
  wins over capture, immediate external release) applied through
  `PlayerInputGate`'s two narrow skippable prefixes on
  `Humanoid.StartAttack` (local player only) and `Menu.Update` (only
  while the menu is closed) — capture clicks cannot attack, Escape
  cancels without opening the pause menu, fail-soft and uninstalled on
  dispose; RC10 typing safety untouched;
  **(4) persisted roads render on the minimap after relog** — overlay
  handle caches are liveness-checked through the pure
  `OverlayHandleRule` (Jötunn destroys overlay textures on Minimap
  teardown; presence alone was the bug), `ResetMapSession()` runs at
  map-available before the redraws on both renderers, and
  `RoadVectorLayer`'s fail-soft session disable resets per session
  instead of lasting the process;
  **(5) the Sentry pin-update NullReferenceException is fixed at its
  lifecycle root** — every `PinAdapter`/`PinDisplayController` map
  write path is a lifecycle-guarded no-op without a live Minimap
  (teardown frames), with no blanket catch added anywhere; the next
  map-available reconcile repairs every rendering (§7 defect 5 has the
  full analysis).
  **Retires RC13 `392ab938`/`2e0dbfb` (ZIP `19ADD2E5…`) — do not
  test, tag, or upload it.** The owner's RC13 smoke evidence remains
  valid for everything it passed; the five defects above are exactly
  what failed, each is re-verified by the NEW smoke section **R10**,
  and RC14 deliberately changes nothing else. Data and schema formats
  are unchanged from RC12/RC13 (the new panel-position value lives in
  the BepInEx config file, not in any sidecar format).
- RC13's identity block is preserved below for the record:
- (RC13) **RC commit** `392ab9382f9088b72e65d9e6a530bbe030c526d6`
  (the owner-approved final beta polish pass of 2026-09-02,
  implementing exactly the four presentation/UX items from the owner's
  RC12 smoke feedback, plus the 0.9.0 re-version).
  RC13 delivered, on top of RC12:
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
- **ZIP:** `artifacts\thunderstore\TheConcernedCat-ConcernedCartographer-0.10.0.zip`
  (built at the RC16 commit; an identical immutable copy is at
  `artifacts\rc16\TheConcernedCat-ConcernedCartographer-0.10.0-RC16.zip`
  — verify the hash below before importing. The retired RC15c package
  (ZIP `036DBD39…`, DLL `58BDD226…`) survives as
  `artifacts\rc15c\TheConcernedCat-ConcernedCartographer-0.9.0-RC15c.zip`
  and a same-named `…-0.9.0.zip` copy in
  `artifacts\thunderstore\superseded\`; the retired RC15b package
  (ZIP `8AC3A779…`, DLL `BA8975CA…`) survives as
  `artifacts\rc15b\TheConcernedCat-ConcernedCartographer-0.9.0-RC15b.zip`;
  the retired pre-audit RC15
  package (ZIP `F89AAD13…`, DLL `DA62990C…`) survives as
  `artifacts\rc15\TheConcernedCat-ConcernedCartographer-0.9.0-RC15.zip`
  and a same-named copy in `artifacts\thunderstore\superseded\`; the
  retired RC14 package (ZIP `49DBB847…`, DLL `9DA6786F…`) as
  `artifacts\rc14\TheConcernedCat-ConcernedCartographer-0.9.0-RC14.zip`
  and a same-named copy in `artifacts\thunderstore\superseded\`; the
  retired RC13 package (ZIP `19ADD2E5…`, DLL `CE783057…`) as
  `artifacts\rc13\TheConcernedCat-ConcernedCartographer-0.9.0-RC13.zip`.
  The RC12 package (ZIP `7A027F7B…`, DLL `FD6DB99C…`) remains in
  `artifacts\thunderstore\superseded\` alongside the never-published
  INTERNAL 0.9.0 milestone ZIP (`…-0.9.0-internal-milestone.zip`) —
  the internal file shares only the version number, never the bytes.
  The retired copies under `artifacts\rc15c\`, `artifacts\rc15b\`,
  `artifacts\rc15\`, `artifacts\rc14\`,
  `artifacts\rc13\`, `artifacts\rc12\`,
  `artifacts\rc11\` (ZIP `C08BBBB1…`, DLL `8C5233A4…`),
  `artifacts\rc10\` (ZIP `EA523400…`, DLL `A350D0CE…`) and
  `artifacts\rc8\` (ZIP `AF267AC2…`, DLL `E9904771…`) must NOT be
  tested or uploaded.)
- **ZIP SHA-256:** `362AE442386CC6CC5B348F4B177D6DE452DCD0A01597A58D7BEB5C1D8046368F`
  (319,549 bytes — fresh RC16 / 0.10.0-beta bytes; retired hashes
  (RC15c `036DBD39…`, RC15b `8AC3A779…` and RC15 `F89AAD13…` included)
  are never reused; the immutable rc16 copy verified byte-identical to
  the staging ZIP; `artifacts\rc16\SHA256SUMS.txt` carries both lines)
- **Plugin DLL SHA-256:** `8BC0543109042BF888E27C279E6DB68AD42C5B58F511C65A6C33F6F9B5049B36`
  (478,720 bytes; informational version
  `0.10.0+a23bef007a75b84282c3aa0e0043b9be468f3301` verified in the DLL;
  the 12 `CC.Icons.cc-*.png` sprite resources re-verified embedded)
- **Assembly metadata (verified in the DLL):** Company "The Concerned Cat",
  Product "Concerned Cartographer", Copyright © 2026 Eren Cansunar,
  RepositoryUrl embedded, informational version `0.10.0+<RC16 commit>`,
  FileVersion 0.10.0.0.
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
RC8, RC10, RC11, RC12, RC13, and RC14 — all now retired — plus the
RC15 relog-persistence blocker pass) is on `feat/cc-098-v1-completion`
awaiting its post-smoke merge and tag (section 19).

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

**Owner final-blocker pass (2026-09-03, RC15) — 1 release blocker
reproduced on the exact RC14 DLL, fixed at its lifecycle root:**

1. **Managed pins falsely tombstoned by relog / map reconstruction.**
   Observed: after logout/login, the cc:camp marker "camp" rendered as
   vanilla Fire and the cc:travel marker "route" as vanilla Portal;
   the sidecar proved `IconId` persisted correctly, yet the SAME
   records were rewritten `Deleted=1`, with "deleted through vanilla
   UI" logged immediately after reconcile. NOT an icon-persistence
   loss: a false vanilla-delete inference. Deterministic mechanism
   (decompile-verified): Jötunn fires `OnVanillaMapAvailable` from a
   `Minimap.Start` postfix, so the reconcile ran BEFORE vanilla loaded
   the character's saved pins; the first `Minimap.Update` then ran
   `LoadMapData → SetMapData → ClearPins + re-AddPin`, destroying every
   tracked rendering in place and re-adding the save's plain
   fallback-type pins — the absorber's absence check read that rebuild
   as the player deleting every managed pin in vanilla, tombstoned them
   all, and left the unclaimed Fire/Portal save copies on screen.
   Fixed by inverting the burden of proof (see the RC15 identity block
   for the full design): absence never tombstones (unlink + rebind
   only); tombstones come exclusively from the explicit vanilla
   `RemovePin` event captured by `PinDeletionWatch` and decided by the
   pure `PinTombstoneRule` (explicit + bound session + at-most-once);
   and a second reconcile at Jötunn's `OnVanillaMapDataLoaded` rebinds
   every living cc:* pin to exactly one CC-sprited rendering right
   after the reconstruction. A genuine right-click/gamepad delete
   still tombstones exactly once and remains recoverable
   (`PinStore.Restore`/undo). Regressions: `Rc15RelogPersistenceTests`
   replays cc:camp→Fire and cc:travel→Portal across
   teardown/rebuild/reconcile against the shipping pure pieces.
   Directive items 7–9 (lifecycle diagnostics, RedrawAll teardown
   hardening, the R11.4 smoke row) shipped in the same pass — see the
   identity block.

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

- **568/568 tests** in the game-free core suite (Release configuration,
  re-run at the RC15c commit): everything below plus the RC15b suite —
  `SupportReportPrivacyTests` (11: the composed support report has no
  `world-uid` field and no uid-shaped digit run; pin/road sidecar rows
  carrying a name, notes, category, tag, and real coordinates reduce
  to counts only; no filesystem-path fragments; identifiers planted
  into the caller's version/config strings (path with username, world
  uid, coordinates, save-file name, URL) are scrubbed by the
  defense-in-depth pass; aggregate diagnostics — counts, sizes in KB,
  settings, timestamp, backup count — survive un-mangled; the
  route-codec describe path; unreadable-file statuses name only the
  exception type; and `SafeLogText` Describe/Brief scrub paths,
  usernames, world UIDs, save names, coordinates, and IPs out of
  exception text while keeping the exception type, with null
  tolerance) — plus the RC15 suite —
  `Rc15RelogPersistenceTests` (19: the tombstone rule truth table —
  explicit delete in a bound session tombstones, absence NEVER does
  regardless of session state, unbound-session deletes keep the pin,
  already-deleted entities are never re-tombstoned; the map-session
  tracker's generation/bind transitions; the overlay write guard incl.
  the exact alive-at-resolve/destroyed-before-write Sentry case; and
  the full relog replay against the shipping pure pieces — cc:camp
  persists Fire (0) and cc:travel persists Portal (6), the
  ClearPins-style rebuild keeps both `Deleted=false` and rebinds both
  CC sprites onto exactly one rendering each, five rapid
  teardown/rebuild cycles never tombstone/duplicate/churn revisions,
  a mid-session list rebuild resolves to rebind-not-tombstone, a
  genuine stable-session delete tombstones exactly once with one
  revision bump / survives reconcile without resurrection / restores
  with its cc:* art, and a re-claimed own rendering with the correct
  live sprite re-records instead of rebuilding) —
  plus the RC14 suite —
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
owner starts at the NEW SHORT section R11 (the RC15 / 0.9.0-beta
relog-persistence pass: five rows — the false-tombstone relog
reproduction with sidecar `Deleted=0` verification, genuine-delete
tombstone-once-and-recoverable, the retained System Markers
"Visible to other players" row, the 2026-09-03 logging/lifecycle row
(DebugLogging temporarily on, then back to default — row 4 now ALSO
verifies the CC-098 privacy audit: the whole LogOutput.log free of
world UIDs/paths/names/coordinates, and the support report free of
the `world-uid` line), and redraw teardown hardening), NOT at the
top, on the exact RC15c beta ZIP named above.** After R11, re-run R10 rows 1/2/5 on the same ZIP (their
surfaces changed in RC15), then complete whichever R10 → R9 → R8 →
R7 → R6 → R5 → R3/R4 rows (as previously amended) earlier smokes did
not finish — rows already passed stay passed, because RC15
deliberately changed only the relog/tombstone lifecycle, the redraw
teardown path, and diagnostics, RC15b changes only the privacy of
emitted log/support text (no behavior), and RC15c changes only the
packaged Thunderstore README (no code). The full 2.5–4 h checklist is
not restarted.

**STATUS 2026-09-03:** the owner recorded R11 and the R10 rows 1/2/5
re-runs as **PASSED on the exact RC15c ZIP**. RC16 (the 0.10.0
re-version) changes version metadata only; the mandatory post-upload
clean-profile install is the RC16 exact-ZIP sanity check.

## 19. Remaining Git commands (run after the smoke test passes)

**STATUS 2026-09-03 (all executed):** the completion branch merged to
`main` through PR #105 (merge commit `7cbdf3b` — a true merge, so the
RC commits stay in main history). When the owner re-versioned to
0.10.0, the never-published DRAFT GitHub Release for 0.9.0 was
deleted; its `concerned-cartographer/v0.9.0-beta` tag is protected by
a repository ruleset (tag deletion forbidden) and remains as a
historical, never-released marker at the RC15c commit `87a0fec`.
Nothing was ever uploaded anywhere under 0.9.0, and the taken
`concerned-cartographer/v0.9.0` internal-milestone tag was never
touched. The release tag is now
**`concerned-cartographer/v0.10.0-beta`** at the RC16 commit
`a23bef0` (pushed), and the DRAFT prerelease "Concerned Cartographer
0.10.0 (Public Beta)" carries the sealed RC16 ZIP plus
`artifacts\rc16\SHA256SUMS.txt`. Remaining owner-only steps: publish
the draft GitHub Release, then upload the identical ZIP to
Thunderstore (section 20), then the post-upload clean-profile
install check before any announcement.

## 20. Thunderstore upload data (owner-only)

- File: `TheConcernedCat-ConcernedCartographer-0.10.0.zip` (identical
  sealed copy: `artifacts\rc16\TheConcernedCat-ConcernedCartographer-0.10.0-RC16.zip`)
- Team/namespace: **TheConcernedCat** · Community: **valheim**
- Categories: **mods, client-side, utility, ai-generated**
- Dependencies: denikson-BepInExPack_Valheim 5.4.2333, ValheimModding-Jotunn 2.29.2
- Version: 0.10.0 — **already uploaded 2026-09-03; currently in the
  rejected/review moderation state. Do NOT upload again: follow the §0
  reapproval steps (read the team-visible rejection reason on the
  package page, then post in the Thunderstore Discord
  #rejected-uploads forum).** A fresh upload (0.10.1) happens only if
  a moderator explicitly requires changed bytes.

## 21. DO NOT RELEASE IF

- Any **BLOCKS** smoke row fails and cannot be fixed + re-verified.
- The ZIP hash on disk no longer matches this dossier.
- A human ZIP inspection finds anything beyond the six audited entries.
- The two-client tombstone test (smoke 7.4) shows a resurrected deletion.
- Any world save, character file, or foreign mod's data is modified in
  any test.
- The fresh-profile install (smoke 10.4) fails to reach the main menu
  cleanly.
