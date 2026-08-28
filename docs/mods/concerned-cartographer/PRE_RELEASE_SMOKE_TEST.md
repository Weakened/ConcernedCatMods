# Pre-release smoke test — Concerned Cartographer v1.0

The single-session human release checklist. This document accumulates every
manual-only verification deferred by the autonomous conveyor (OPS-001
rev 2) from v0.3 onward and is finalized against the exact v1.0 RC. Rows
marked **BLOCKS** must pass before publication; others are record-and-ship.

> Status: FINAL for v1.0, amended 2026-08-27 (second amendment) after the
> second smoke pass found two new P1 blockers (DEF-v1.0-004/-005, issues
> #92–#93) plus UX gaps (#94–#95). **Do NOT restart the full 2.5–4 h
> checklist.** Start at section R2 below and resume the shortened golden
> path from where the second pass stopped.

## R2. RC3 mini-regression — RESUME SMOKE FROM HERE

The second human smoke pass (2026-08-27) ran against RC `7ed20fef…`
(ZIP `B47E7C9D…`). That RC is **superseded** — do not test or upload it
again. It PASSED: adoption input trap (DEF-v1.0-001), workbench layout
(DEF-v1.0-003), and overlay alignment (DEF-v1.0-002, closed on logged
residuals ≤ 1 texel — see #90). It FAILED on managed-pin edit
duplication (DEF-v1.0-004, #92) and leveling-paints-roads
(DEF-v1.0-005, #93), with UX gaps filed as #94/#95 — all addressed in
this RC. Run blocks A–E in order against the NEW RC (identity in
`RELEASE_DOSSIER.md`); every block **BLOCKS**.

### A. Startup

1. Clean import of the exact RC3 ZIP into the smoke profile (verify its
   SHA-256 against the dossier).
2. Start modded: Concerned Cartographer **1.0.0** banner, no CC errors,
   menu responsive.

### B. Edit-in-place identity (DEF-v1.0-004, #92)

1. In a disposable world, create a vanilla pin named `Home`.
2. Adopt it (`P` over the pin → Adopt).
3. Rename to `Smoke Home`; Apply.
4. Change the icon via the picker; Apply.
5. Change category/notes/tags; Apply.
6. Close and reopen the large map.
7. Restart the game and re-enter the world.

PASS: exactly **ONE** map pin throughout, at the intended position;
metadata persists; no orphan pin with the old name; no duplicate after
restart. *On failure capture:* map screenshots + `LogOutput.log`
("Pin reconcile"/pin-adapter lines).

### C. Editor and discoverability (#94, #95)

1. Icon is selected via the picker (sprite preview + list); a pin with a
   custom/legacy icon ID keeps it via "Keep custom".
2. Color appears only at the bottom labeled **metadata** — no prominent
   picker that visibly does nothing.
3. Size adjusts via the −/+ stepper with Reset (labeled metadata).
4. Every label/control sits inside the panel (spot-check UiScale 0.8 and 1.6).
5. The **CC Atlas [L]** button is visible on the large map and opens the
   Atlas Drawer.
6. Hovering an editable pin shows `P — Edit with Concerned Cartographer`
   and the hint is understandable.
7. Clicking an Atlas Drawer search result opens the Pin Workbench for
   that pin.
8. Vanilla right-click pin deletion still behaves exactly vanilla.

### D. Terrain intent (DEF-v1.0-005, #93)

1. Level (hoe) a patch of untouched ground away from real roads.
2. Walk back and forth over it.
3. Stay nearby long enough for chunk recovery to scan the area.
4. **NO** Dirt road ink appears on the map.
5. Restart, revisit, walk it again: still **NO** ink.
6. Pathen across part of it: Dirt ink appears exactly there.
7. Pave part of the pathen strip: Paved ink replaces it, no double ink.

*On failure capture:* before/after map screenshots + `LogOutput.log`.

### E. Alignment spot check (diagnostic only)

1. `cc_roads align`: the console table must end
   `ALIGNMENT PASS: max residual … texels` (≤ 1.00); markers are small
   dots/crosses and labels do not obscure the marker centers.
2. `cc_roads align clear` removes every diagnostic pin and cross
   immediately.

Only after A–E pass, resume the shortened golden path at
routes/world-isolation/multiplayer (sections 2, 4, 6, 7 onward),
skipping rows earlier passes already completed.

## R. First replacement-RC mini-regression — COMPLETED 2026-08-27 (second pass); kept for the record

The first human smoke pass (2026-08-27) ran against RC `9eb65291…`
(ZIP `9F1F4128…`). That RC is **superseded / failed human smoke** — do
not test or upload it again. What it already proved stays proven and is
NOT re-run beyond step 5's quick check:

```text
Valheim 0.221.12, Unity 6000.0.61f1, BepInEx 5.4.23.3, Jötunn 2.29.2.0
Concerned Cartographer 1.0.0 startup — banner logged, no CC errors
```

Run these steps in order against the NEW RC (identity in
`RELEASE_DOSSIER.md`). If any of steps 7–10 fails, capture the listed
evidence and STOP the human test.

1. Fresh `TCC-v1-Smoke` profile, or cleanly replace the mod in an
   existing test profile.
2. Install the pinned BepInEx/Jötunn dependencies.
3. Import the exact new RC ZIP (verify its SHA-256 against the dossier).
4. Start modded.
5. Main menu: Concerned Cartographer 1.0.0 banner, no CC exceptions,
   menu responsive. **BLOCKS** (short regression only — code changed).
6. Enter a disposable world (e.g. ModrTestWorld).
7. **Pin Workbench adoption FIRST** (DEF-v1.0-001, #89): adopt a vanilla
   pin → edit → Apply; open again → Close; open again → Escape;
   close/reopen the large map; zoom and pan; then repeat the whole
   adopt/open/close cycle twice more. Everything must be fully normal —
   no stuck map, no dead zoom, no unclosable panel. **BLOCKS**
   *On failure capture:* LogOutput.log (look for the workbench
   invariant error line) + a clip of the stuck input.
8. Verify every workbench label/control sits inside the wood panel, at
   UiScale 0.8, 1.0, and 1.6 (DEF-v1.0-003, #91). **BLOCKS**
   *On failure capture:* screenshots at the failing scale/resolution.
9. Run `cc_roads align` at a known road/player coordinate
   (DEF-v1.0-002, #90): every "CC align" dot pin must sit on its magenta
   cross within one map texel (~12 m). **BLOCKS**
   *On failure capture:* the full "Alignment probe" log block +
   a zoomed screenshot of pin vs cross; then `cc_roads align clear`.
10. Build one short dirt path and one paved stretch; confirm the ink
    lands at the correct world location visually. **BLOCKS**
    *On failure capture:* map screenshot + the align log from step 9.
11. Only then resume the shortened golden-path smoke at
    roads/routes/persistence/multiplayer (sections 2, 4, 6, 7 onward),
    skipping rows the first pass already completed.

## 0. RC identity

- Version: **1.0.0**
- RC commit, ZIP path, and ZIP SHA-256: see
  `docs/mods/concerned-cartographer/RELEASE_DOSSIER.md` (written against
  the exact final package).
- Package audit: ZIP root = manifest.json, README.md, CHANGELOG.md,
  LICENSE, icon.png (256×256), plugins/TheConcernedCat.ConcernedCartographer.dll
  and nothing else; dependencies pinned to BepInExPack 5.4.2333 and
  Jötunn 2.29.2. **BLOCKS**

## 1. Fresh install and lifecycle

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 1.1 | Fresh mod-manager profile | Import RC ZIP via "Import local mod"; dependencies auto-install; Start modded | Game reaches menu; log shows the version banner with real Valheim/BepInEx/Jötunn versions and the effective config | LogOutput.log | Yes |
| 1.2 | 1.1 | Enter a disposable world; open/close map repeatedly; logout to menu; re-enter | No exceptions, no stale overlay references, atlas ready line logged | LogOutput.log | Yes |

## 2. Roads (v0.1–v0.2 regressions)

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 2.1 | Any world | Walk a dirt path and a paved road | Distinct dark dirt/paved lines appear; re-walking never thickens or grows them | Map screenshot + sidecar size | Yes |
| 2.2 | Hoe + stonecutter | Place pathen/paved pieces without walking | Ink appears immediately; leveling/raising ground adds NO ink | Clip | Yes |
| 2.3 | Recorded road | Cultivate/reset over part of it; pave over a dirt stretch | Covered ink vanishes; kind converts without doubles | Before/after screenshots | Yes |
| 2.4 | Recorded roads | `cc_roads delete` then `cc_roads rebuild` | Road returns from terrain paint; unexplored regions stay empty | Log + screenshot | Yes |
| 2.5 | Console | Run the cc_roads operation set (status/kind/hide/unhide/split/join/undo) | Summaries correct; map updates; undo reverts | Console screenshot | No |
| 2.6 | Near a recorded road | `cc_roads align`, inspect map, then `cc_roads align clear` (DEF-v1.0-002 regression) | Every "CC align" dot pin sits on its magenta cross within one texel (~12 m) at all probe positions incl. the latest dirt point; clear removes the pins | "Alignment probe" log block + zoomed screenshot | Yes |

## 3. Pin Workbench (v0.3)

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 3.1 | World with several vanilla pins | Hover one on the large map, press the workbench hotkey (default P) | Adopt prompt opens; Close leaves the pin completely untouched | Screenshot | Yes |
| 3.2 | 3.1 | Adopt, then edit every field (name, icon, category, color, size, notes, tags, status, crossed-off, scope) and Apply | Pin identity/position unchanged; visible name/icon update on the map; one `cc_pins undo` reverts the whole edit | Screenshot + `cc_pins status` | Yes |
| 3.3 | 3.2 | Restart the game, reopen the world | Every edited field persists; NO duplicate pin appears; `cc_pins status` shows the same counts | Screenshot + pins.tsv | Yes |
| 3.4 | Managed pin | Cross it off and delete another via vanilla map UI | Cross-off appears in workbench state; vanilla deletion shows in `cc_pins deleted` as a tombstone; `restore` brings it back | Console output | Yes |
| 3.5 | Two similar pins ~10 m apart | `cc_pins dups` then `merge confirm` | Preview first; merged pin keeps notes + provenance line; undo separates again | Console output | No |
| 3.6 | Death/bed/boss/another mod's pin | Hotkey on it; try any cc_pins operation nearby | Read-only panel; no operation ever alters it | Screenshot | Yes |
| 3.7 | ~50+ adopted/created pins | `cc_pins adoptall confirm`, batch `cc_pins category`, map browsing | Responsive UI, no errors, one-step undo works | Console output | No |
| 3.8 | Profile with the mod removed | Launch vanilla after using pins | All managed pins remain as ordinary vanilla pins with names/icons/positions/cross-offs | Screenshot | Yes |
| 3.9 | Panel open | Resolution sanity at 1080p and 1440p/ultrawide | Panel fits, vanilla map controls (pin bar, toggles) stay reachable | Screenshots | No |
| 3.10 | Vanilla pin | Adopt → edit → Apply; reopen → Close; reopen → Escape; close/reopen map; zoom/pan; repeat cycle ×2 (DEF-v1.0-001 regression) | Map input NEVER sticks: map closes normally, zoom/pan normal after every cycle, panel always closable | LogOutput.log (workbench invariant error) + clip | Yes |
| 3.11 | Workbench open | Check all labels/fields/buttons at UiScale 0.8 / 1.0 / 1.6 (DEF-v1.0-003 regression) | Every label/control fully inside the wood panel; labels left-aligned to one column; panel fully on screen at every scale | Screenshots | Yes |

## 4. World isolation and persistence

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 4.1 | Two worlds A and B | Record roads + pins in A; switch to B; return to A | B shows none of A's data; A restores everything | Sidecar listing | Yes |
| 4.2 | Mid-session | Kill the game process (Task Manager) shortly after edits | On next launch the journal recovers to the last flushed state; log shows the recovery line | LogOutput.log | Yes |

## 5. Atlas Drawer, search, clustering, quick pins, survey (v0.4)

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 5.1 | Large map open | Press `L` | Drawer opens left of center; vanilla controls reachable; Escape closes; reopen after logout/login works | Screenshot | Yes |
| 5.2 | Drawer | Toggle dirt/paved/pins/clustering | Layers hide/show immediately; state survives restart (config) | Screenshots | Yes |
| 5.3 | ≥20 varied pins | Search `tag:x`, `category:y`, plain words; Clear | Counts update instantly; results click opens workbench; Clear restores all pins | Screenshot | Yes |
| 5.4 | 5.3 | Save a view, change everything, apply the view | Exact query+layer+cluster state restores | Screenshot | No |
| 5.5 | ~30 pins in one area | Zoom fully out / mid / close | Cluster markers with counts at world view; progressively more detail closer; no flicker while panning | Screenshots ×3 | Yes |
| 5.6 | 5.5 | Restart after clustering | No cluster marker was saved as a real pin; counts match | `cc_pins status` | Yes |
| 5.7 | In world | `F7` on a rock/portal/crypt; on a creature; on nothing | Sensible pin at target; creature refused; no-target message; duplicate radius blocks repeat | Clips | Yes |
| 5.8 | Enable SurveyRulesEnabled | Walk near copper rocks ~1 min | HUD reports observations; `cc_survey list` shows them; nothing pinned until accept; base exclusion respected near a Base pin | Console output | Yes |
| 5.9 | 5.8 | `cc_survey accept all`, disable survey | Pins appear tagged "surveyed"; scanner stays silent when disabled | Console output | No |

## 6. Routes (v0.5)

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 6.1 | Large map | `cc_routes draw Test`, hold Shift+LMB and sketch, `cc_routes stop` | Line appears live while drawing; no map pan fighting; survives restart | Clip | Yes |
| 6.2 | 6.1 | `cc_routes erase`, brush the middle | Only the brushed stretch vanishes; route splits into two; undo restores | Clip | Yes |
| 6.3 | Recorded road network | `cc_routes waypoint Trip`, click two points near roads | Route follows the roads across junctions, not a straight cut; snap off → straight lines | Screenshot | Yes |
| 6.4 | Any route | `cc_routes measure` | Plausible distance, on-road %, minutes | Console output | No |
| 6.5 | Any route | style/status/color/lock/archive cycle | Dashed/dotted render distinctly; status colors differ; locked rejects edits; archived hides | Screenshots | No |
| 6.6 | "CC Routes" overlay toggle | Toggle in Jötunn panel | Route layer hides/shows independently of roads | Screenshot | No |

## 7. Collaborative atlas (v0.6) — needs two clients (or one client + a second profile on another PC/steam account)

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 7.1 | Two clients A+B in one world | A: `cc_pins scope table` on a pin, `cc_sync share` | B gets a HUD notice; `cc_sync inbox` lists A; nothing changed yet | Console output | Yes |
| 7.2 | 7.1 | B: `cc_sync preview A`, then `apply A` | Preview counts match; pin appears for B with A's authorship in the workbench info line | Screenshots | Yes |
| 7.3 | 7.2 | A deletes the shared pin, shares; B applies | B's `cc_sync preview A` lists the pin BY NAME under "Would DELETE" (SEC-1.0-001) before apply; pin then disappears for B; `cc_pins deleted` shows the tombstone | Console output | Yes |
| 7.4 | 7.3 | B (stale copy) shares back without applying A's deletion first | A's pin stays deleted after preview/apply — NO resurrection | Console output | Yes |
| 7.5 | Both edit one shared pin while separated | Share both ways | Conflict appears in preview; `apply <name> theirs` converges both sides | Console output | Yes |
| 7.6 | B tries `cc_pins delete` on A's shared pin, shares | A's preview shows 1 rejected (non-owner delete) | Console output | Yes |
| 7.7 | Private pin on A | A shares | B never receives it | Console output | Yes |

## 8. NoMap, controller, localization, accessibility (v0.7)

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 8.1 | World with `nomap` global key | Try cc_pins/drawer away from a table, then beside a cartography table | Denied with the table message away; everything works beside the table | Console output | Yes |
| 8.2 | Gamepad connected | Bind Accessibility/DrawerGamepadButton (e.g. JoyBack); open drawer; navigate with stick/dpad | Focus visibly walks the controls; toggles/buttons actuate | Clip | No |
| 8.3 | Copy template → `cartographer-strings.tsv`, translate 3 keys | Restart | Translated strings appear; untranslated fall back to English | Screenshot | No |
| 8.4 | Accessibility/UiScale 1.4 + HighContrast on | Open both panels; view roads/routes | Panels larger and usable; dirt near-black, paved near-white, routes bright; dashed/dotted still distinct | Screenshots | No |
| 8.5 | Fresh profile first world | Enter world | One-time hotkey tip appears once, never again | Screenshot | No |

## 9. Compatibility, recovery, scale (v0.8)

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 9.1 | TCC-Compat (Pinnacle + MapRoutes) | Play 15 min using both mods and CC | No conflicts/errors; `cc_atlas compat` lists both with policies; hotkey on a vanilla pin shows read-only info (Pinnacle present) | LogOutput.log | Yes |
| 9.2 | Any world with data | `cc_atlas backup`, delete a few pins, `cc_atlas restore 1`, relog | Atlas back to the snapshot; a pre-restore backup also exists | Console output | Yes |
| 9.3 | 9.2 | Copy a backup folder to another PC/profile and restore there | Atlas travels (export/import path) | Console output | No |
| 9.4 | Any world | `cc_atlas support`; open the file | Only versions/settings/counts/sizes — no coordinates, names, or notes | The file | Yes |
| 9.5 | Large real atlas | Map open/pan/zoom/search feel at your biggest world | No perceptible hitching | Subjective + clip | Yes |

## 10. Upgrade, migration, and uninstall (v0.9/v1.0)

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 10.1 | Profile still on 0.2.0-era data (or fixtures via `scripts/make-test-fixtures.ps1`) | Install the 1.0.0 RC over it; load the world | Everything migrates (log shows format upgrades + maintenance); zero data loss; `.v1.bak` style backups appear where applicable | Log + sidecars | Yes |
| 10.2 | 10.1 | Downgrade check: remove the mod, launch vanilla | World loads fine; managed pins persist as vanilla pins; no errors | Screenshot | Yes |
| 10.3 | 10.2 | Reinstall the RC | Atlas returns exactly; vanilla cross-offs made while unmodded were absorbed | `cc_pins status` | Yes |
| 10.4 | Fresh profile | Import the RC ZIP via "Import local mod" | Dependencies auto-install; smoke section 1 passes | Log | Yes |

## 11. Performance feel and soak (v1.0)

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 11.1 | 10k-pin + 10 km fixtures | Map open, pan, zoom, search, cluster at full scale | No perceptible hitching on the baseline PC (i9-9900K/RTX 4080-class) | Clip | Yes |
| 11.2 | Normal world | 45+ min continuous play with capture/recovery/survey(on)/routes active | No creeping errors, no log spam, memory stable in Task Manager | Log + observation | Yes |

## 12. Thunderstore preflight (owner-only)

- [ ] `python ./tools/validate_repo.py --expected-version 1.0.0` passes. **BLOCKS**
- [ ] ZIP inspected by a human for secrets/saves/game DLLs/unrelated files. **BLOCKS**
- [ ] README/CHANGELOG on the package page match actual behavior. **BLOCKS**
- [ ] Categories: mods, client-side, utility, **ai-generated**. **BLOCKS**
- [ ] Upload via thunderstore.io web UI or `pwsh ./scripts/publish.ps1 -Version 1.0.0` (token via env var, never stored). **BLOCKS**

## 13. Post-publication smoke

- [ ] Install the published package from Thunderstore into a clean profile; smoke section 1 passes.
- [ ] Package page renders README/icon/changelog correctly.
- [ ] First community-visible version pinned in the GitHub release notes.
