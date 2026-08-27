# Pre-release smoke test — Concerned Cartographer v1.0

The single-session human release checklist. This document accumulates every
manual-only verification deferred by the autonomous conveyor (OPS-001
rev 2) from v0.3 onward and is finalized against the exact v1.0 RC. Rows
marked **BLOCKS** must pass before publication; others are record-and-ship.

> Status: LIVING DOCUMENT — grows each sprint; the RC identity section is
> filled in when the final package is built.

## 0. RC identity (filled at final packaging)

- Version: _pending_
- RC commit: _pending_
- ZIP path: _pending_
- ZIP SHA-256: _pending_
- Package audit: ZIP root = manifest.json, README.md, CHANGELOG.md,
  LICENSE, icon.png (256×256), plugins/TheConcernedCat.ConcernedCartographer.dll
  and nothing else. **BLOCKS**

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
| 7.3 | 7.2 | A deletes the shared pin, shares; B applies | Pin disappears for B; `cc_pins deleted` shows the tombstone | Console output | Yes |
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

## 10–13. Later-sprint sections

Placeholders grow as sprints complete: Atlas Drawer/search/views (v0.4),
routes (v0.5), multiplayer/tombstones (v0.6), NoMap/controller/
localization/accessibility (v0.7), compatibility/import-export/backup
(v0.8), migration/upgrade/beta items (v0.9), final performance-feel and
Thunderstore preflight (v1.0).
