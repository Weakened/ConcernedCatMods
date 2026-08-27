# Changelog

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
