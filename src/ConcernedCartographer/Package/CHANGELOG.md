# Changelog

## 0.2.0 (in development)

- Roads you build now appear on the map as you build them: your own successful hoe path and stonecutter paving actions are captured directly (configurable, on by default). Cultivating and resetting terrain are never recorded as roads.
- Old roads recover themselves: nearby loaded terrain is scanned on a small per-frame budget, and narrow road paint in areas you have already explored is added to the atlas without re-walking it. Unexplored regions stay hidden, and broad cleared areas (bases, plazas) are deliberately not turned into roads.
- No more ghost roads: cultivating or resetting terrain removes the covered road ink, and paving over a dirt path (or vice versa) converts it instead of drawing both. Before the first such change each session the sidecar is backed up to `.pre-reconcile.bak`.
- Roads are recorded through a source-neutral observation pipeline; every stroke remembers whether it came from walking, a construction action, or terrain recovery.
- Sidecar format v2 adds the origin column. v1 files still load, and the original is backed up once to `.v1.bak` before the first v2 save; deleting the v2 file and renaming the backup rolls back to 0.1.0.
- Isolated road points render as dots instead of being invisible.

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
