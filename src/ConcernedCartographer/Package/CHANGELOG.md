# Changelog

## 0.9.0 (Public Beta)

**The Stable Living Atlas — public beta.** The roads your Vikings actually build become a durable, searchable, shareable map — and everything on it can be trusted. This is the feature-complete candidate for the 1.0.0 release, published as a beta for wider testing; upgrading from this beta to 1.0.0 will be automatic and lossless, like every Concerned Cartographer upgrade.

Relog persistence root fix and hardening (RC15):

- **Custom markers can no longer be falsely "deleted" by a relog.** The real story behind markers reverting to vanilla icons (Camp→Fire, Travel→Portal): the game rebuilds its whole pin list while loading your character's map data, and the mod's vanilla-edit absorber mistook that rebuild for you deleting the pins in vanilla — writing them off as deleted while the save file's plain-vanilla copies stayed on screen. The lifecycle is fixed at its root: a missing rendering is NEVER treated as a deletion anymore. Only an explicit vanilla delete action (right-click / gamepad remove, captured at the game's own RemovePin entry point) during a stable, fully-bound map session writes a tombstone — exactly once, and still recoverable. A second reconcile now runs right after the game loads your saved map, so every living cc:* marker regains exactly one rendering wearing its Concerned Cartographer art; the vanilla fallback icon remains what uninstall/downgrade shows, never what the mod shows. If any other reconstruction path ever drops renderings, the absorber now repairs by re-linking instead of deleting.
- **Full map redraws survive teardown races** (the RC13 crash report CONCERNED-CARTOGRAPHER-2 family): the road and route full-texture redraws re-verify their live textures immediately before writing pixels; a map teardown mid-redraw now resets the overlay handles, logs one privacy-safe warning, and retries on the next map session instead of throwing a reportable exception.
- **Privacy-safe lifecycle diagnostics** for support bundles: the log now records the exact build (version+commit), numbered map-session transitions (map available / map data loaded / world unloaded), aggregate pin-reconcile results (linked/added/removed/sprite-rebind counts) with the cause of any tombstone, and overlay resolve/reset/redraw state with texture liveness — never world, character, player, or server names or IDs, coordinates, pin or route contents, paths, or IPs. Verbose success traces stay behind `Diagnostics/DebugLogging` (default off).

Final smoke fixes (RC14):

- **Custom markers survive relog**: cc:* markers (road, harbor, fishing, objective, and the rest) keep their Concerned Cartographer art after logging out and back in, instead of degrading to vanilla Dots. The marker data always survived — the session rebind did not: one teardown-frame failure could silently disable the pin adapter for the rest of the game process. Session boundaries now clear that state, the sprite rebind decision is an explicitly tested rule, and clusters dominated by a cc:* marker wear its art too. Genuine vanilla pins are never repainted.
- **Roads survive relog on the minimap**: roads from previous sessions render again on the minimap (and the fallback texture view) after relogging. The road atlas always loaded correctly — the renderer painted it into the PREVIOUS map's destroyed overlay because its cached overlay handles outlived the session. Handles are now liveness-checked and re-resolve against the live map, and the large-map vector layer's fail-soft disable no longer leaks across sessions either. Dirt/Paved identity, layer toggles, and the road-source-authority rules are untouched.
- **The Atlas drawer remembers where you put it**: the drawer reopens at the position you dragged it to — across map opens, relogs, and restarts. Restored positions are clamped fully on-screen for the current resolution and UI scale, so an old coordinate can never strand the panel; if nothing was ever dragged, the default right-edge dock behaves exactly as before. (Relatedly, side panels no longer lose a non-default UI scale after a relog.)
- **Quick Pin owns its input**: while the toolbar's armed Quick Pin is waiting for your click, that click no longer swings your weapon, and Escape now only cancels Quick Pin — it no longer also opens the pause menu on the same press. The suppression is narrowly scoped to the armed interaction (plus the press's own frame) and releases immediately on capture, cancel, world switch, disable, or uninstall. Typing-safety behavior is unchanged.
- **Fixed a crash-report NullReferenceException during pin updates** (Sentry CONCERNED-CARTOGRAPHER-2): pin sync and display updates ran on login/logout teardown frames when no map exists, threw, and disabled pin management for the rest of the process — the same latch behind the marker-relog bug. All pin write paths are now lifecycle-guarded no-ops without a live map, and the next map-open reconcile repairs every rendering.

Final beta polish (RC13):

- **Softer large-map road ink**: the high-precision Dirt/Paved road lines on the large map now wear a gently feathered edge, matching the minimap's softer presentation the way the map's hand-drawn style intends — same centerline, same perceived width, same colors, zoom-stable, no extra rendering cost. Routes intentionally stay crisp (they are drawn plans, not terrain ink).
- **Faster palette scrolling**: the mouse wheel moves the [Markers] palette list about three times as far per notch — still smooth, still bounded, and the map underneath still never zooms.
- **The last orphaned map decoration is gone**: the empty vanilla backplate that lingered at the bottom-right of the large map after its controls were replaced is now hidden with the rest of the rail — only ever shown/hidden, never destroyed, and restored exactly under `Map/ShowVanillaMapControls`, a conflicting pin manager, any CC UI failure, disable, or uninstall. The bottom control tips are untouched.
- **Markers open with the map**: the [Markers] palette now opens automatically as the starting side panel on every fresh large-map open (when the enhanced palette is active). Close it or switch panels and it stays out of your way for the rest of that map visit; the next map open starts fresh. Disabled palette, conflicting pin managers, and fallback cases are respected — nothing auto-opens then.

Highlights across the line (developed as the internal 1.0 release candidates):

- **Roads map themselves as you build them**: every successful Pathen/Paved action inks the map instantly — and nothing else ever does — with ghost-free reconciliation and a self-compacting atlas.
- **Pins with memory**: adopt your vanilla pins, edit everything in place, batch, merge, undo — durable identities, recoverable deletes, uninstall-safe by construction.
- **A readable map at any scale**: the Atlas Drawer, real search, saved views, and lossless clustering.
- **Routes that follow your roads**: freehand or waypoints with road-aware routing, measures, and travel-time estimates.
- **Collaboration you can trust**: explicit sharing, preview-before-apply, honest conflicts, and deletions that can never resurrect.
- **For every Viking**: NoMap table mode, controller path, translations, UI scaling, high contrast, backups, and a sanitized support report.
- **Pre-release security audit**: the sync receive path was adversarially audited and hardened — bounded decompression, sanity bounds on every parsed field, deletion names in the sync preview, and sanitized author labels.
- **Release-candidate smoke fixes**: adopting a vanilla pin can no longer trap map/game input (the workbench now provably balances Jötunn's global input block and fail-closes on map close, logout, and shutdown); the Pin Workbench uses a padded two-column layout that keeps every label inside the panel at all UI scales; and a `cc_roads align` diagnostic verifies road-overlay/map alignment against the live game.
- **Owner-feedback pass (RC12)**: paved roads now wear a light stone-gray ink that always reads clearly LIGHTER than dirt at the same width and style (high contrast keeps near-black dirt / near-white paved). The Routes panel list mirrors the route table live — drawing, erasing, deleting, splitting, merging, restoring, console edits, and sync all update the visible list the same moment, erasing the last of a route's ink removes the route entirely (undoable) instead of leaving a ghost row, and stale entries can never accumulate. The dotted route style can no longer stall or freeze the game on any route, however long or oddly stored: the shared dash/dot walkers are structurally bounded (integer-counted stamps, real per-route budgets, non-finite geometry skipped) and the route texture reuses one pixel buffer across redraws so repeated style changes stop causing memory spikes. The Survey panel layout was rebuilt on an exact vertical-band system — header, note, status, rows, bulk actions, output, and Close each own their space at every UI scale, with status text kept within its band. And the two marker-creation flows are now guaranteed: naming a palette marker and pressing Enter always leaves exactly one visible managed marker (even if the game replaces or drops the pin object at naming close, the committed marker is adopted or recreated — only a real cancel creates nothing), and accepting a survey observation immediately creates exactly one visible managed marker while the observation leaves Pending — a marker you just created is temporarily exempt from cluster folding and search filters so it can never vanish the frame it is born, and Survey rows act on exactly the entry shown even while background sweeps reshuffle the list.
- **Smoke-fix pass (RC11)**: toggling **Map Overlays** checkboxes can never double-render or strand stale road/route ink — one visibility rule now writes the overlay state unconditionally (the panel's own click handler used to race a cached write). Roads render at every zoom: the vector layer's rebake decisions moved into a deterministic, sweep-tested scheduler, a bake that cannot project retries within a quarter second instead of going invisible, and the layer's graphics carry real rects so no clipper can cull them in pan/zoom bands. The mouse wheel over any Concerned Cartographer panel, list, or field scrolls that UI only — the map underneath no longer zooms. Free Draw creates a route only once a stroke has actually travelled (no more one-click fragment routes), the route list keeps a stable order with a "more not shown" count, **Snap to roads** lives in the bottom control area beside a confirmed **Clear all routes**, and the panel's status lines can no longer overlap the list or color swatches. Replaced vanilla map chrome is now hidden per button group with its backplate and decor (validated, logged, pixel-perfect restore). The survey grew up: **Reject is durable** — rejected observations move to a persistent Rejected view (restore or accept them later; they never re-offer on their own), repeated sweeps can never duplicate the same physical object, and survey **rules are edited entirely in the Survey panel** (view, enable/disable, delete, add — `survey-rules.tsv` remains the shareable import/export). Names are humanized everywhere: "Raspberry Bush", "Silver Vein", "Treasure Chest Meadows" — never "Raspberrybush" — on survey rows, map labels, and quick pins, and the survey notice points at the [Survey] panel, not a console command.
- **Road authority by what you actually did (RC10)**: the road rule is now enforced by the **identity of the terrain action itself** — the game's own placed-piece identity for the hoe's Pathen and Paved road actions — never by settings flags or by what the paint looks like (in the live game, "Level ground" and "Pathen" lay down nearly identical dirt-painting operations; only identity can tell them apart, and an always-on rate-limited log line records how every terrain action was classified). Level ground, Raise ground, Cultivate, digging, and unknown/modded terrain operations create **zero** road data and erase the recorded road ink they cover — which is also how any road ink polluted by the earlier misclassification is cleaned up: level or re-pave over it once (or `cc_roads delete` near it), and it is gone for good. Your explicit Pathen/Paved roads are never touched.
- **One map language for roads and routes (RC10)**: large-map road ink is twice as wide and routes now render through the very same screen-space vector system — per-route colors, solid/dashed/dotted with a geometric cadence measured in screen pixels, stable while zooming and panning, with the route texture overlay serving the minimap and fallback exactly like roads (no doubled lines anywhere). Dotted routes read as a tight, continuous bead line. Jötunn's overlay button now reads **Map Overlays** (restored on uninstall), and its checkboxes genuinely show/hide each CC layer in both presentations — a checkbox always tells the truth about the layer, and clicks mirror into the Atlas Drawer settings.
- **Survey that feels immediate, markers that feel right (RC10)**: survey scanning is continuous on a bounded per-frame budget (nearby matches surface within about a second; the top-left notice is coalesced to one per ~10 s), the starter rules broaden to dandelions, flint, wild seeds, guck sacks, beehives, frost caves and lore runestones (an untouched older starter file upgrades in place; edited files are never touched), and the Survey panel's status block got room to breathe. The marker palette is draggable and scrollable with collapsible category sections — every marker reachable, nothing capped away — and a palette placement wears its chosen cc:* icon from the first frame of the naming flow, never a temporary or permanent vanilla Dot. All 12 cc:* icons were regenerated toward Valheim's hand-drawn map-icon look (soft edges, gentle wobble, ink texture) with identical silhouettes and stable IDs.
- **Typing is typing, chrome is honest (RC10)**: while any Concerned Cartographer text field is focused, keystrokes only type — no Valheim actions, no mod hotkeys — and the first Escape just ends the typing; normal input returns the moment the field blurs, and nothing is intercepted when no field is focused. Quick Pin names come from the localized hover name or the real prefab identity — internal names like "Collider (1)" can never become a pin name ("Marked object" is the honest fallback). The Share panel sits on a clean two-column grid. Hiding the replaced vanilla map rail now hides its whole backplate (validated, reversible, restored on fallback/disable/uninstall), with the bottom control tips untouched. Routes are explicitly framed in the panel for what they are in v1: manual planning/navigation overlays that never move your character.
- **Roads you built, and only roads you built (RC8)**: the strict v1 road rule — road atlas data is created exclusively by your own successful **Pathen ⇒ dirt** and **Paved ⇒ paved** construction actions. Walking existing paint, world-generated dirt (spawn circles, sacrificial stones), and Level Ground side-effect paint never become roads; passive traversal/chunk-recovery capture is disabled, and existing atlases migrate automatically (passive-only strokes are cleaned with a one-time `.pre-authority.bak` backup; your explicitly built roads are preserved untouched). Level/Raise/Cultivate/Reset erase the road ink they cover; a later deliberate Pathen/Paved always wins. On the large map, the high-precision vector ink is now the **only** road presentation while it is healthy (the texture overlay stays on the minimap and returns automatically as a fallback) — no more doubled road lines.
- **A real marker set, a survey that works, and a UI polish pass (RC8)**: 12 distinct Concerned Cartographer marker icons (road/junction, harbor, resource, danger, farm, mine, fishing, camp, travel, trader, dungeon, objective) with stable IDs — saves stay vanilla-safe and unknown IDs still fall back cleanly. Survey Rules ship useful bounded starter rules (gatherables, ore deposits, dungeon entrances, boss runestones) and the [Survey] panel shows scanner/rules/last-scan/pending status live with a Scan now button; accepted observations appear on the map immediately, as do Quick Pins. Routes: panels are draggable, the pointer over any CC panel never adds route points, Free Draw strokes end on LMB release (each stroke its own route), and dashed/dotted styles pattern by real distance at every zoom. The toolbar derives its height from the vanilla control-tips layout instead of a fixed offset, the Settings panel reports into a dedicated middle status block, the Atlas Drawer uses an explicit no-overlap grid, and the Pin Workbench no longer shows controls for size/color (stored metadata without v1 behavior). `cc_roads align live` now tells you exactly how to get a full A/B/C/D check.
- **Optional, privacy-first crash reporting (RC5)**: on your first large-map open, Concerned Cartographer asks once whether to send anonymous crash reports when it hits an internal error — off by default, never asked again once answered, changeable anytime under **CC Atlas → Privacy**. Reports carry only allowlisted technical fields (versions, subsystem, exception type, sanitized stack); identity, world/character names, seeds, coordinates, pins/routes, server details, saves, and logs are never sent, with automated redaction tests over the exact outgoing payload and provider-side IP scrubbing. No gameplay analytics of any kind. Full policy: PRIVACY.md. Support routing is now canonical: GitHub issues for bugs, **support@theconcernedcat.com** for security/privacy/sensitive material.
- **The full-UI map surface (RC7)**: one compact toolbar — **[Atlas] [Markers] [Routes] [Survey] [Share] [Quick Pin] [Settings]** — puts every feature behind a visible button, one side panel at a time, Escape always closes, and the vanilla right-side rail is replaced by default (reversibly: `Map/ShowVanillaMapControls`, automatic fallback on conflicts or failures; pin-type filters and visible-to-others live on in **[Atlas] → System Markers**, driven through vanilla state). Routes are drawn from the **[Routes]** panel with explicit modes — no modifier key, no map-drag fighting — and every route operation works from the panel on the selected route. Survey review, sharing (preview with deletion names), privacy, backups, the support bundle, and the road repair tools are all panels now. The package README was rewritten for v1 with a full shortcut-parity table.
- **High-precision large-map roads (RC7, DEF-v1.0-006)**: road ink on the large map is now zoom-stable vector geometry that sits exactly where the game itself projects the world — sub-texel precision at any zoom, so the player marker stays on the road line you are walking. The minimap keeps the classic texture overlay, `Map/HighPrecisionLargeMapRoads=false` restores the old behavior, and a new end-to-end `cc_roads align live` diagnostic answers the four alignment error classes separately.
- **The map is now button-first (RC4)**: an **[Atlas]** button (with tooltip) opens the Atlas Drawer; hovering any editable marker shows **Upgrade & Edit** (existing vanilla markers — position preserved, internally the same safe adoption) or **Edit Pin** (managed markers); and the new **Enhanced Pin Palette** replaces the five raw vanilla icon buttons with a searchable, previewed marker browser — pick a marker, double-click the map, and the pin is managed from birth with exactly one rendering. The vanilla selector returns instantly via `Pins/ShowVanillaPinPalette` (and automatically when a known conflicting pin manager is installed); death/boss/system pins, Cross Off, Remove, Ping, Visible-to-others, and uninstall safety are untouched. Status and Scope in the workbench became dropdown selects. Hotkeys (`L`, `P`, `F7`) remain as rebindable accelerators.
- **Second smoke-pass fixes (RC3)**: editing an adopted/managed pin now updates its single map rendering in place — renames and icon changes never leave a duplicate or orphan pin, in-session or after restart. Ground you **Level/Raise/Cultivate/Reset is remembered per world as explicitly-not-road**, so leveling a base never becomes road ink (walked or recovered, this session or later); a deliberate Pathen/Paved action always wins and re-inks normally. The Pin Workbench gained an icon picker with live sprite preview, category suggestions, and a size stepper — color stays raw hex at the bottom, honestly labeled metadata-only until pins can truly render it. A visible **CC Atlas** button and a contextual **P — Edit** hint make the panels discoverable without reading docs. The alignment diagnostic is smaller and quieter and prints one PASS/FAIL residual table; overlay alignment itself was verified in game (max residual ≤ 1 map texel) and its defect closed.

Upgrading from any earlier version is automatic and lossless.

## 0.9.0 internal hardening milestone

(An internal, never-published milestone that happened to share the 0.9.0 number — kept for the development record; the public beta above supersedes it.) Public beta hardening: no new features, everything sturdier.

- Feature freeze: 0.9.x is hardening-only on the road to 1.0.
- Automated migration matrix across every format the mod has ever written.
- Deterministic test-fixture generator for community testing (`scripts/make-test-fixtures.ps1` in the repo).
- Public documentation completed: feedback channel, privacy statement, and the security model in plain language.

## 0.8.0

Plays well with others, and never loses your atlas.

- **Compatibility awareness**: known neighbors (Pinnacle, PinAssistant, AutoMapPins, MapRoutes, Better Cartography Table, OneMap) are detected and coexistence policies apply automatically — with another pin manager installed, the hotkey never prompts adoption (explicit `cc_pins adopt` remains). `cc_atlas compat` shows the report.
- **Backups and restore**: `cc_atlas backup` snapshots your whole atlas; `restore <n>` brings any snapshot back (with its own safety backup first). The backup folders double as the export/import format — copy them between machines or profiles.
- **Support report**: `cc_atlas support` writes a sanitized report (versions, settings, counts, sizes — never positions, names, or notes) safe to paste into a bug report.

### Known limitations

- MapRoutes routes are not imported (both layers coexist independently).

## 0.7.0

The atlas for every Viking: NoMap tables, controllers, translations, and accessibility.

- **NoMap worlds**: the atlas becomes a cartography-table ritual — panels and console tools work only near a table, keeping immersive servers immersive.
- **Controller support**: panels focus their first element for gamepad navigation, and opt-in rebindable gamepad bindings open the workbench and drawer. Every keyboard hotkey was already rebindable.
- **Translations**: all UI and HUD text lives in a string catalog; a translator template is generated next to your config, and a `cartographer-strings.tsv` file translates the mod into any language. Partial translations safely fall back to English.
- **Accessibility**: UI scaling (0.8–1.6×), a high-contrast map ink mode, and non-color cues everywhere (line styles, icons, text labels).
- A one-time first-run tip points at the two hotkeys. Defaults stay conservative.

## 0.6.0

The trustworthy collaborative atlas: share deliberately, review everything, lose nothing.

- **Share what you choose**: mark pins/routes with `scope table` and `cc_sync share` broadcasts them to connected players. Everything else stays private — always.
- **Review before it lands**: incoming shares wait in an inbox (`cc_sync inbox`); `cc_sync preview` shows exactly what would change (new, updated, deletions, conflicts); apply is explicit and selective.
- **Deletions never resurrect**: shared deletions travel as durable tombstones, and a teammate who was offline for a week cannot bring your deleted pin back — guaranteed structurally, not by luck.
- **Honest conflicts**: when two people edited the same thing offline, you see it and choose your side (`apply <name> mine` / `theirs`); either choice converges for everyone.
- Every shared entity carries who created it and who last edited it; only the owner's deletions are honored.
- Hardened transport: compressed, size-capped, protocol-versioned envelopes; malformed data is skipped row-by-row, never trusted.

### Known limitations

- Sync is peer-to-peer between online players (the server relays; it does not store the atlas itself). A rejoining player gets the current state from any online teammate's share.
- Author identity labels edits but cannot cryptographically prove who sent a share; every structural protection holds regardless.

## 0.5.0

Routes and planning: draw where you'll go, and let the roads do the navigating.

- **Freehand routes**: `cc_routes draw <name>`, then hold Shift+LeftClick on the large map and sketch. Partial erase (`cc_routes erase`) rubs out just the stretch you brush over, splitting cleanly.
- **Waypoint routes with road-aware routing**: `cc_routes waypoint <name>` places waypoints that snap to your recorded roads — and when both ends touch the road network, the route follows the actual roads across junctions instead of cutting straight lines.
- **Full editing**: split, merge, lock (blocks all geometry edits), archive, styles (solid/dashed/dotted), status (planned/active/done), custom colors, undo/redo.
- **Measure anything**: `cc_routes measure` gives distance, how much of the route runs on roads, and a travel-time estimate at configurable speeds.
- Routes render on their own "CC Routes" map layer with per-status colors, persist per world with crash-safe journaling, and never touch the world or other mods' data.

### Known limitations

- Route drawing needs the large map and mouse (controller pass arrives in v0.7); the modifier key avoids vanilla map-drag conflicts.
- Road-aware routing follows your recorded road atlas — unexplored roads can't route until discovered.

## 0.4.0

The atlas becomes readable at any scale: one drawer, real search, and calm maps.

- **Atlas Drawer** (default hotkey `L` on the large map): layer toggles for dirt/paved roads, pins, and clustering; search with live counts and click-to-edit results; saved views. Everything also drives from the `cc_atlas` console.
- **Search and queries**: plain words search names, notes, tags, and categories; power tokens (`name:`, `category:`, `tag:`, `icon:`, `status:`, `scope:`, `source:`, `is:checked`, `near:x,z,r`) narrow precisely. Filters are display-only — clearing the query always restores everything.
- **Saved views** capture your query and layer state as named presets.
- **Semantic zoom and clustering**: zoomed out, crowded pins fold into count markers by dominant category; zooming in progressively reveals detail. Clusters are pure display — nothing is ever merged or deleted underneath.
- **Quick pins** (default `F7`): pin what you're looking at, with a sensible name, icon, and category. Never pins creatures; duplicate radius prevents spam.
- **Survey Rules** (opt-in, off by default): pattern rules in a shareable `survey-rules.tsv` turn nearby loaded objects into reviewable observations — never directly into pins. Hard caps, duplicate radii, base-exclusion zones, and expiry keep it bounded; review with `cc_survey`.

### Known limitations

- Cluster markers and drawer visuals need the large map; NoMap support arrives in v0.7.
- Survey rules match loaded objects near you only — no world scanning, by design.

## 0.3.0

The Pin Workbench: your pins become a durable, editable atlas.

- Adopt your vanilla pins (one at a time or all at once, with a reviewed preview) — position, icon, name, and crossed-off state are preserved exactly, and the map pin itself is never touched by adoption.
- Edit pins in place on the map: press the workbench hotkey (default P) over a pin on the large map, or use the `cc_pins` console. Name, icon, category, color, size, notes, tags, status, crossed-off, and sharing intent — all without deleting and recreating anything.
- Every pin has a durable identity and revision history; deletes are recoverable tombstones with restore and a recently-deleted list.
- Full operation set: move, duplicate, archive, batch edits, duplicate detection and merge (notes and provenance preserved), bounded undo/redo.
- Crash-safe pin storage: per-world snapshot plus journal with automatic recovery; edits made through vanilla UI (cross-off, delete) are absorbed into the atlas.
- Curated icon registry with stable namespaced IDs; unknown icons render safely without losing their identity.
- Uninstall-safe by construction: managed pins remain ordinary vanilla pins if the mod is removed.

### Known limitations

- Pin color and display size are stored and editable but not yet rendered on the vanilla map (planned).
- Foreign and system pins (other mods, death/bed/boss/server markers) are read-only by design.
- Sharing intent is stored only; synchronization arrives with the collaborative atlas.

## 0.2.0

- Roads you build now appear on the map as you build them: your own successful hoe path and stonecutter paving actions are captured directly (configurable, on by default). Cultivating and resetting terrain are never recorded as roads.
- Old roads recover themselves: nearby loaded terrain is scanned on a small per-frame budget, and narrow road paint in areas you have already explored is added to the atlas without re-walking it. Unexplored regions stay hidden, and broad cleared areas (bases, plazas) are deliberately not turned into roads.
- No more ghost roads: cultivating or resetting terrain removes the covered road ink, and paving over a dirt path (or vice versa) converts it instead of drawing both. Before the first such change each session the sidecar is backed up to `.pre-reconcile.bak`.
- Roads are recorded through a source-neutral observation pipeline; every stroke remembers whether it came from walking, a construction action, or terrain recovery.
- Sidecar format v2 adds the origin column. v1 files still load, and the original is backed up once to `.v1.bak` before the first v2 save; deleting the v2 file and renaming the backup rolls back to 0.1.0.
- Isolated road points render as dots instead of being invisible.
- The atlas compacts itself on load: road fragments merge into continuous polylines and straight stretches thin out (a 10 km atlas shrinks ~97%), with no visible change on the map and no loss of re-walk suppression.
- Road repair tools: the `cc_roads` console command deletes, reclassifies, hides/unhides, splits, and joins the road nearest you, rebuilds a region with current detection settings, and undoes up to 20 operations. Tools edit only the mod's atlas, never terrain or saves.

### Known limitations

- Construction capture and ghost-road reconciliation see only your own actions; other players' roads and removals arrive through chunk recovery of loaded, explored terrain.
- Chunk recovery targets narrow paths (up to ~2 brush widths); broad paved plazas and leveled bases are deliberately not auto-recovered.
- A road line can sit up to ~6 m from its true position — the native resolution of the 2048-pixel map.
- The atlas is stored per mod-manager profile, and there is no multiplayer synchronization; everything is client-side and local.
- Repair-tool selection is nearest-to-player via console; there is no map-click editor yet.

## 0.1.0

Initial public alpha: the roads your Viking actually walks become a per-world map atlas.

- Detect dirt Pathen and paved terrain beneath the local player.
- Draw independent dirt and paved Jötunn overlays on the full map and minimap, with per-layer toggles ("CC Dirt Paths", "CC Paved Roads").
- Persist road strokes in per-world sidecar files under the BepInEx config folder, with atomic writes and malformed-row recovery.
- Suppress duplicate ink: re-walking a recorded road never grows the atlas (configurable radius).
- Never connect teleports, portals, respawns, or large gaps with straight lines.
- Configuration for sampling cadence, spacing, gap, suppression, autosave, detection thresholds, and line width; effective values and environment versions are logged once per session.
- Opt-in, rate-limited classification diagnostics and an overlay-alignment calibration aid (both off by default).
- Verified compatible with Pinnacle 1.16.0 and MapRoutes 1.1.0.

### Known limitations

- Only roads traversed while the mod is installed are discovered.
- World-generated dirt paint (such as the circle at the spawn stones) is recorded as road.
- A road line can sit up to ~6 m from its true position — the native resolution of the 2048-pixel map.
- The atlas is stored per mod-manager profile; a fresh profile starts an empty atlas.
- No multiplayer synchronization; the atlas is client-side and local.
- No in-place pin editor or expanded legend yet.
