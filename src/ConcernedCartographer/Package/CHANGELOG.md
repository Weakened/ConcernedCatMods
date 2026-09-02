# Changelog

## 1.0.0

**The Stable Living Atlas.** The roads your Vikings actually build become a durable, searchable, shareable map — and everything on it can be trusted.

Highlights across the 1.0 line:

- **Roads map themselves as you build them**: every successful Pathen/Paved action inks the map instantly — and nothing else ever does — with ghost-free reconciliation and a self-compacting atlas.
- **Pins with memory**: adopt your vanilla pins, edit everything in place, batch, merge, undo — durable identities, recoverable deletes, uninstall-safe by construction.
- **A readable map at any scale**: the Atlas Drawer, real search, saved views, and lossless clustering.
- **Routes that follow your roads**: freehand or waypoints with road-aware routing, measures, and travel-time estimates.
- **Collaboration you can trust**: explicit sharing, preview-before-apply, honest conflicts, and deletions that can never resurrect.
- **For every Viking**: NoMap table mode, controller path, translations, UI scaling, high contrast, backups, and a sanitized support report.
- **Pre-release security audit**: the sync receive path was adversarially audited and hardened — bounded decompression, sanity bounds on every parsed field, deletion names in the sync preview, and sanitized author labels.
- **Release-candidate smoke fixes**: adopting a vanilla pin can no longer trap map/game input (the workbench now provably balances Jötunn's global input block and fail-closes on map close, logout, and shutdown); the Pin Workbench uses a padded two-column layout that keeps every label inside the panel at all UI scales; and a `cc_roads align` diagnostic verifies road-overlay/map alignment against the live game.
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
## 0.9.0

Public beta hardening: no new features, everything sturdier.

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
