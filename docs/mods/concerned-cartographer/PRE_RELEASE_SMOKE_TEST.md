# Pre-release smoke test — Concerned Cartographer v1.0

The single-session human release checklist. This document accumulates every
manual-only verification deferred by the autonomous conveyor (OPS-001
rev 2) from v0.3 onward and is finalized against the exact v1.0 RC. Rows
marked **BLOCKS** must pass before publication; others are record-and-ship.

> Status: FINAL for v1.0, amended 2026-08-31 (sixth amendment): **RC7
> FAILED its human smoke** and is permanently retired — do not test,
> tag, or upload it. RC8 addresses all twelve failure directives (road
> source authority, single road presentation, real cc:* icons, survey,
> routes input/strokes/styles, toolbar/settings/drawer/workbench layout,
> quick-pin sync, align-live guidance). **Do NOT restart the full
> 2.5–4 h checklist.** Run the NEW section **R5 (RC8 mini-smoke)**
> first, then R3 A–L and R4 M–S (as amended for the RC8 road rule),
> then resume the shortened golden path from where the second pass
> stopped. Test only the RC ZIP named in RELEASE_DOSSIER.md.

## R5. RC8 release-blocker mini-smoke — RUN FIRST (short)

Every item below verifies one RC8 failure directive. All **BLOCK**.
Prep: disposable world, hoe + stonecutter materials, one leveled pad.

1. **Road rule — creation**: walk across world-generated dirt (spawn
   circle or any native path) and across the leveled pad — NO road ink
   appears, walking never records. Pathen a short path with the hoe and
   pave a stretch with the stonecutter — both ink the map instantly.
2. **Road rule — erase & win**: Level Ground across the middle of your
   pathen road — the covered stretch disappears from the map. Pathen
   over the same ground again — it re-inks (later explicit action wins).
3. **Road rule — migration**: if this profile's world has a pre-RC8
   atlas: on first load the log shows the authority migration line and a
   `.pre-authority.bak` beside the sidecar; only your explicitly built
   roads remain; restart — nothing passive returns.
4. **Single road presentation**: large map open with vector ink healthy:
   exactly ONE set of road lines (no doubled texture+vector ink).
   Minimap still shows roads. `Map/HighPrecisionLargeMapRoads = false`:
   the texture ink returns on the large map.
5. **Icons**: [Markers] palette shows the 12 CC icons with DISTINCT
   sprites (not vanilla duplicates) plus the vanilla five. Place one
   cc:* marker — it renders its CC sprite on the map; restart — still
   the CC sprite, exactly one rendering. Uninstall-degradation spot
   check optional (record-and-ship): with the mod removed the pin shows
   its vanilla fallback icon.
6. **Toolbar**: at your resolution (and 1280x800 + 2560x1440 if
   available) with UiScale 0.8/1.0/1.6, the toolbar sits clearly ABOVE
   the vanilla Add pin/Cross off/Remove/Ping control tips — zero
   overlap, at keyboard AND gamepad hint variants if available.
7. **Settings panel**: click Back up / Restore (first click) / support
   bundle / each Advanced road button — every response lands in the
   framed middle status block; nothing ever overlays the buttons.
8. **Pin Workbench**: NO Size stepper, NO Color hex field; everything
   else edits and applies as before.
9. **Survey**: enable in [Survey]; walk near berry bushes / a copper
   node / a burial chamber; Scan now — observations appear with live
   scanner/rules/last-scan/pending status; accept one — the pin appears
   on the map IMMEDIATELY with its CC sprite.
10. **Routes**: panel drags like the workbench. Free Draw: hold LMB
    draws, release ends the stroke, a second hold starts a NEW route,
    Finish exits. Pointer over the panel/toolbar NEVER adds points.
    Erase removes ink only under the held brush. Set a route Dashed
    then Dotted: real dash-gap pattern / separated dots at min and max
    zoom. The selected-route line above the list matches your click.
11. **Quick Pin**: F7 (or toolbar arming) on a rock/tree — the pin
    appears on the map immediately, once; restart — still exactly one.
12. **Align live**: map closed + off-road: `cc_roads align live` prints
    the OPEN THE LARGE MAP / STAND ON guidance naming Pathen/Paved.
    Standing on your built road with the map open: A/B/C/D all PASS.

Only after R5 passes, run R3 A–L and R4 M–S (amended below), then the
shortened golden path (sections 2, 4, 6, 7 onward).

## R3. RC5 mini-regression — RESUME SMOKE FROM HERE

The second human smoke pass (2026-08-27) ran against RC `7ed20fef…`
(ZIP `B47E7C9D…`); it PASSED the adoption input trap (DEF-v1.0-001),
workbench layout (DEF-v1.0-003), and overlay alignment (DEF-v1.0-002,
closed on logged residuals ≤ 1 texel — #90), and FAILED on managed-pin
edit duplication (DEF-v1.0-004, #92) and leveling-paints-roads
(DEF-v1.0-005, #93). RC3 (`86050cd2…`, ZIP `710183B3…`) fixed those and
RC4 (`35f20e1a…`, ZIP `8B4B41AD…`) added the v1 map UX (#96); both were
superseded before human testing (RC5 adds opt-in crash reporting, #97) —
do not test the old ZIPs. Run blocks A–L in order against the NEW RC
(identity in `RELEASE_DOSSIER.md`); every block **BLOCKS**.

### A. Startup

1. Clean import of the exact RC ZIP named in `RELEASE_DOSSIER.md` into
   the smoke profile (verify its SHA-256 against the dossier).
2. Start modded: Concerned Cartographer **1.0.0** banner, no CC errors,
   menu responsive.

### B. Atlas discoverability (#95/#96)

1. Open the large map: the **[Atlas]** button is visible, unobtrusive,
   inside the map, and shows its tooltip on hover.
2. Click it: the Atlas Drawer opens. Click again/close: it closes.
3. `L` still toggles the drawer.
4. Clicking an Atlas Drawer search result opens the Pin Workbench for
   that exact pin.

### C. Vanilla pin upgrade — edit-in-place identity (#92, #96)

1. In a disposable world, create an ordinary vanilla pin named `Home`
   (use the vanilla fallback flow of block F, or a pre-existing pin).
2. Hover it: the hint plus a visible **Upgrade & Edit** button appear.
3. Click **Upgrade & Edit**: the editor opens; the marker has not moved
   or duplicated.
4. Rename to `Smoke Home`; Apply. Change the icon via the picker; Apply.
   Change category/notes/tags; Apply.
5. Close and reopen the large map; then restart and re-enter the world.

PASS: exactly **ONE** map marker throughout, at the intended position;
metadata persists; no orphan old-name pin; no duplicate after restart.
*On failure capture:* map screenshots + `LogOutput.log`
("Pin reconcile"/pin-adapter lines).

### D. Managed edit (#92, #94)

1. Hover a managed marker: **Edit Pin** is visible; click it (also try
   `P`).
2. Icon picker shows sprite preview + list; a custom/legacy icon ID is
   preserved via "Keep custom".
3. Status and Scope are dropdown selects; size is a −/+ stepper with
   Reset; color appears only at the bottom labeled **metadata**.
4. Exactly one marker remains after each Apply and after restart.

### E. Enhanced Pin Palette — managed from birth (#96)

1. The five vanilla player icon buttons are hidden by default; the CC
   **Markers** palette is visible (collapsible via its button).
2. Search for an icon (e.g. "harbor"); choose it — the palette shows
   "Double-click the map to place: …".
3. Double-click the map; name the marker in the vanilla name input.
4. The marker exists exactly once and is managed immediately: hovering
   it shows **Edit Pin** (NOT Upgrade & Edit) and the workbench shows
   the palette's icon and category. No upgrade step ever appears.
5. Repeat once via the Recent group. Also place one and CANCEL the name
   input: still exactly one managed (unnamed) marker.

### F. Vanilla fallback (#96)

1. Set `Pins/ShowVanillaPinPalette = true` (config or Configuration
   Manager): the vanilla five-icon selector returns immediately;
   vanilla pin placement works as stock.
2. CC atlas, editor, and context actions keep working.
3. Set it back to false: the palette returns, vanilla buttons hide.

### G. Vanilla controls untouched

Cross Off (left-click), Remove Pin (right-click), Ping (middle-click),
and Visible-to-other-players all behave exactly vanilla, with the
palette both shown and hidden.

### H. Controller

1. The Atlas button is focusable and clickable with a controller.
2. Palette rows can be focused/navigated; an icon can be selected and a
   marker created.
3. The pin edit action is reachable (context button or the configured
   gamepad binding / `P`).

### I. Layout

At 1080p, 1440p, and ultrawide (if available), with UI scale 0.8 / 1.0 /
1.6: no CC panel or button sits outside the map or covers vanilla map
controls (icon bar area, shared-position toggle, biome text, hints bar).

### J. Terrain intent (DEF-v1.0-005, #93)

1. Level (hoe) a patch of untouched ground away from real roads.
2. Walk back and forth over it.
3. Stay nearby long enough for chunk recovery to scan the area.
4. **NO** Dirt road ink appears on the map.
5. Restart, revisit, walk it again: still **NO** ink.
6. Pathen across part of it: Dirt ink appears exactly there.
7. Pave part of the pathen strip: Paved ink replaces it, no double ink.

*On failure capture:* before/after map screenshots + `LogOutput.log`.

### K. Alignment spot check (diagnostic only)

1. `cc_roads align`: the console table must end
   `ALIGNMENT PASS: max residual … texels` (≤ 1.00); markers are small
   dots/crosses and labels do not obscure the marker centers.
2. `cc_roads align clear` removes every diagnostic pin and cross
   immediately.

### L. Crash-reporting consent and privacy (#97)

Note: the RC now carries the LIVE ingestion DSN (embedded 2026-08-28,
ingestion pre-verified with a direct envelope POST → HTTP 200). While
consent is Unknown or Disabled nothing can be sent (steps 1–3, 7); once
you toggle reporting On in step 4, a subsequent forced/observed CC
subsystem failure should appear in the Sentry project within a minute —
verify the event contains no names/coordinates/paths (it is built from
the tested allowlist). Also confirm the Sentry project settings per
CRASH_REPORTING.md: "Prevent Storing of IP Addresses" ON and Data
Scrubbers ON (PRIVACY.md promises both). **BLOCKS**

1. Fresh (or reset: `Privacy/SendCrashReports = Unknown`,
   `AcceptedPrivacyPolicyVersion = 0`) profile → enter world → open the
   large map: the "Help improve Concerned Cartographer" dialog appears
   exactly once. It does NOT appear on the title screen.
2. Choose **No thanks** → close/reopen the map → no prompt.
3. Restart the game → no prompt; config shows `Disabled`.
4. CC Atlas → **Privacy** → toggle reporting **On** (state line flips
   immediately; [Learn more] opens/points at PRIVACY.md).
5. Restart → remains On, still no prompt.
6. Enter a different world and a different character → no prompt
   (consent is profile-level, and nothing is written to world saves).
7. While `Unknown` or `Disabled`: no crash-report traffic is possible —
   with the DSN empty this is structural; the notice after a forced/
   observed subsystem failure must read the "Crash reporting is off…
   cc_atlas support" variant (with reporting on AND a DSN configured it
   reads "An anonymous crash report was sent."), and appears at most
   once per subsystem.
8. Escape/dismiss variant (fresh profile again, optional): opening the
   map and pressing Escape with the dialog up counts as **No thanks**.

Only after A–L pass, continue with R4 below, then resume the shortened
golden path at routes/world-isolation/multiplayer (sections 2, 4, 6, 7
onward), skipping rows earlier passes already completed.

## R4. RC7 full-UI surface + road precision (#98–#102) — run AFTER R3 A–L

RC7 lands the unified toolbar/panel surface (#99–#102, plus audit-driven
fixes) and the DEF-v1.0-006 high-precision road layer with its
`cc_roads align live` diagnostic. None of it has been seen in game.
Every block **BLOCKS** unless marked otherwise.

### M. Toolbar and panel dock (#100)

1. Open the large map: the toolbar shows **[Atlas] [Markers] [Routes]
   [Survey] [Share] [Quick Pin] [Settings]** at the bottom center,
   inside the map.
2. Open each panel in turn: exactly one side panel is ever visible;
   opening the next closes the previous. Each docks at the same
   right-edge position with the wood-panel look.
3. Escape closes whichever surface is open — including the **[Markers]**
   palette (RC7 fix).
4. `Accessibility/UiScale` 0.8 and 1.6: panels re-dock, nothing clips at
   your resolution (record the resolution tested).
5. Hover an editable marker: the hint and context button still appear
   (tooltip and hint may share a spot — record, does not block).

### N. Vanilla rail replacement (#99)

1. Default config: the vanilla right-side rail is fully hidden — the
   five icon selectors, the death/boss filter buttons, AND the
   visible-to-others toggle. No dead empty strip misbehaves.
2. **[Atlas] → System Markers**: toggling a pin-type filter immediately
   filters the map exactly like the vanilla button did; toggling
   visible-to-others flips the real setting (verify on the minimap
   position-sharing behavior or a second client if handy). No pin is
   ever deleted by filtering.
3. `Map/ShowVanillaMapControls = true` (live config reload or restart):
   the whole vanilla rail is back and works; the CC toolbar coexists.
   Set back to false: hidden again.
4. `General/Enabled = false` mid-session with the map open: the full
   vanilla rail returns immediately; no CC panel is left stuck open
   (RC7 fix). Re-enable: CC surface returns on next map open.

### O. Routes panel (#101)

1. **[Routes]** → **Free Draw**: a visible mode banner appears; drawing
   with plain LMB inks a route and the map does NOT pan while drawing;
   no pin is created by map clicks. **Finish** ends the mode; vanilla
   drag/clicks return instantly.
2. **Waypoints** mode with snap on: waypoints snap to your recorded
   road; snap off: they do not.
3. Escape during a mode ends the mode first; second Escape closes the
   panel.
4. Start a mode, then open another toolbar panel: the mode ENDS with the
   panel (RC7 fix) — vanilla map input is back (drag pans, double-click
   places a pin).
5. Select a route in the list: rename, style, status, a color swatch,
   lock (then verify an edit is rejected), archive, split, merge,
   measure, delete — all act on the selected route without standing
   near it. **Restore** brings back the most recently deleted route.
6. `cc_routes draw` from the console still needs the classic
   `Shift+LMB` (console alias unchanged).

### P. Survey, Share, Settings panels (#102)

1. **[Survey]**: enable Survey Rules from the panel; pending
   observations list; accept one (pin appears only now), reject one
   (gone, nothing pinned), bulk accept/reject asks for a confirming
   second click; reload re-reads the rules file.
2. **[Share]**: status shows scoped counts; with a second client (or
   deferred to section 7): share now, inbox, preview (deletions named),
   apply mine/theirs, clear. Single-client: verify the panel renders
   and status/clear behave; the two-client rows stay in section 7.
3. **[Settings]**: privacy opens the consent surface; **Back up atlas**
   reports success; **Restore** asks for a confirming second click and
   names the backup, then restores the most recent one; support bundle
   writes the sanitized file; road repair buttons under Advanced work
   (spot-check `status` and one `undo`-able operation); the support
   email is shown.

### Q. Quick Pin armed mode (#102)

1. Toolbar **[Quick Pin]**: the map closes, a HUD hint appears; your
   next click captures exactly one quick pin (creature refusal and
   duplicate radius still apply); a second click does nothing more.
2. Arm again, press Escape: cancelled, no pin.
3. `F7` in the world remains the instant path, no arming.
4. (NoMap world only, with section 8) Arming away from a cartography
   table is denied with the standard message (RC7 fix).

### R4-R. High-precision large-map roads (DEF-v1.0-006 acceptance)

> RC8 amendment: "recorded road" now means a road YOU built with
> Pathen/Paved (walking records nothing), and while the vector layer is
> healthy it is the ONLY large-map road ink (the texture overlay stays
> on the minimap and returns as the fallback) — so step 6's toggle also
> swaps which presentation is visible rather than stacking them.

1. Stand ON a recorded dirt road; open the large map and zoom to
   maximum useful zoom: the player marker sits ON the CC road
   centerline (≤ ~2 px). Walk 50–100 m along the road with the map
   open: the marker stays on the line the whole way.
2. Repeat on a paved road.
3. Pan and zoom around: the road ink is rock-stable against the map
   (no swimming, no jumping at zoom-step changes beyond a brief width
   settle).
4. The minimap still renders roads (texture overlay, unchanged), and
   the origin/east/north calibration probes of `cc_roads align` still
   PASS (block K).
5. Dirt/paved layer toggles (drawer or Jötunn panel) hide/show BOTH the
   texture and vector ink together; high contrast recolors both.
6. `Map/HighPrecisionLargeMapRoads = false` (+ map reopen): the vector
   ink is gone, texture-only rendering as in RC6. Set true: it returns.
7. Unexplored area (if any peer/shared roads exist beyond your fog):
   vector ink does not reveal roads under fog beyond what the texture
   overlay shows (record-and-ship: fog parity refreshes within ~30 s).

### S. `cc_roads align live` diagnostic (#98)

1. Standing ON a road you built with the large map open, run
   `cc_roads align live`: the report prints player position, terrain
   classification, latest traversal sample (diagnostics-only), latest
   recorded construction point, nearest stored road point + distance,
   all three projections, texture size / m-per-texel / zoom /
   screen-px-per-texel, the live marker anchor vs expected, and four
   separated verdicts — **A PASS, B PASS, C PASS (vector ACTIVE),
   D PASS**.
2. Standing OFF road (or with the map closed): the report ends with the
   explicit guidance to open the large map and stand on a road you
   explicitly built (RC8-11).
3. With `Map/HighPrecisionLargeMapRoads = false` at deep zoom: C reads
   FAIL naming the setting (honest resolution verdict), A/B/D
   unaffected.
4. The full report also lands in `LogOutput.log` (evidence trail).

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
