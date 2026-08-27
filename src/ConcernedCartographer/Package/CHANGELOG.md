# Changelog

## 1.0.0

**The Stable Living Atlas.** The roads your Vikings actually build become a durable, searchable, shareable map — and everything on it can be trusted.

Highlights across the 1.0 line:

- **Roads map themselves**: walk them, build them, or let recovery find them — with ghost-free reconciliation and a self-compacting atlas.
- **Pins with memory**: adopt your vanilla pins, edit everything in place, batch, merge, undo — durable identities, recoverable deletes, uninstall-safe by construction.
- **A readable map at any scale**: the Atlas Drawer, real search, saved views, and lossless clustering.
- **Routes that follow your roads**: freehand or waypoints with road-aware routing, measures, and travel-time estimates.
- **Collaboration you can trust**: explicit sharing, preview-before-apply, honest conflicts, and deletions that can never resurrect.
- **For every Viking**: NoMap table mode, controller path, translations, UI scaling, high contrast, backups, and a sanitized support report.

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
