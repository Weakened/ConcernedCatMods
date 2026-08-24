# Project: Concerned Cartographer

## Product identity

```text
Creator:          The Concerned Cat
Mod:              Concerned Cartographer
Thunderstore ID:  TheConcernedCat-ConcernedCartographer
Plugin GUID:      com.theconcernedcat.valheim.concernedcartographer
Assembly:         TheConcernedCat.ConcernedCartographer
Initial version:  0.1.0 (public alpha only after validation)
```

## One-sentence promise

**The roads Vikings physically create in the world become an evolving, readable atlas on Valheim's map.**

## Problem

Valheim's map is useful for exploration and basic pins, but it does not represent the player's built travel network. Dirt paths, paved roads, bridges, ports, and route planning exist in the world but are absent from the strategic view. Pin editing is also cumbersome in vanilla.

## Market/overlap research

This project must not claim that every map improvement is novel:

- **Pinnacle** already offers extensive pin editing, search, filters, colors, tags, and management.
- **PinAssistant** overlaps with search, color, auto-pin, and replacement workflows.
- **MapRoutes** already proves manual freehand routes on the full map/minimap, persistence, and modded-client synchronization.
- Jötunn already supplies supported map-overlay APIs and automatic GUI toggles.

Therefore, the mod's differentiator is not “more pins” or “draw a line.” It is **automatic cartography derived from player-made terrain roads**, with careful interoperability rather than a forced replacement for established pin mods.

## Target users

- Solo builders who create road networks between bases and resources.
- Cooperative worlds that want an atlas reflecting shared infrastructure.
- Immersive/vanilla-plus players who prefer discovered infrastructure over GPS-like omniscience.
- Server communities that may later share roads through cartography tables.

## Product principles

1. **Valheim-like, not GPS-like.** Use restrained map layers and preserve fog-of-war.
2. **World-safe.** The mod reads terrain and writes only its own sidecar data; it never mutates world terrain or save files.
3. **Progressive discovery.** Early versions map roads the player traverses. Later versions capture edits and inspect loaded chunks without revealing unexplored territory.
4. **Interoperable.** Coexist with pin and route mods whenever practical.
5. **Measured performance.** No continuous whole-world scans.
6. **Honest alpha scope.** Publish only features that have direct in-game evidence.

## MVP user stories

### Road atlas

- As a player, when I walk along Pathen terrain, I see a dirt-road line appear on my map.
- As a player, when I walk along paved terrain, I see a visually distinct paved-road line.
- As a player, I can toggle dirt and paved layers independently.
- As a player, road lines respect unexplored fog.
- As a player, my atlas persists after restarting the game.
- As a player with multiple worlds, each world has an isolated atlas.

### Future cartography tools

- As a player, I can edit an existing marker without deleting/recreating it.
- As a player, I can search, categorize, filter, and annotate markers.
- As a cooperative player, I can deliberately share selected roads/markers through a cartography table.

## Version 0.1 scope

Version 0.1 is a local, client-side **road survey alpha**:

- detect dirt Pathen and paved terrain beneath the local player;
- sample at a configurable interval and minimum spacing;
- group points into strokes without connecting teleports or large gaps;
- draw separate dirt and paved Jötunn overlays;
- persist strokes in a per-world sidecar file under BepInEx config;
- restore and redraw on world load;
- provide configuration and actionable logs.

### Explicit v0.1 limitations

- It discovers a road as the local player traverses it.
- It does not scan the entire world.
- It does not recover every old road immediately.
- It does not synchronize road data between players.
- It does not yet replace or extend vanilla pin UI.
- It is not server-authoritative.

## Non-goals for the first release

- No world-save parsing.
- No dedicated-server component.
- No custom Unity asset bundle.
- No automatic revealing of unexplored roads.
- No migration/import from other route mods.
- No attempt to replace Pinnacle or MapRoutes.

## Roadmap

### Phase 0 — foundation

- Monorepo, package validation, plugin identity, local deploy flow.
- Prove clean startup and map lifecycle.

### Phase 1 — traversed-road atlas

- Terrain paint probe.
- Dirt/paved overlays.
- Per-world persistence.
- Configuration and performance guardrails.

### Phase 2 — direct construction capture

- Patch the successful Pathen/paved terrain-modification path.
- Record the brush footprint immediately.
- Detect repaint/removal and reconcile segments.

### Phase 3 — loaded-chunk recovery

- Inspect only loaded heightmaps.
- Convert paint pixels to bounded road candidates.
- Merge/simplify candidates and avoid broad cleared-area false positives.
- Never reveal unexplored map regions.

### Phase 4 — cartography UX

- In-place marker editor.
- Curated icons/categories/notes/search.
- Legend/layer panel that does not overwhelm vanilla UI.
- Compatibility mode for pins owned by other mods.

### Phase 5 — cooperative atlas

- Selective cartography-table sharing.
- Ownership and conflict handling.
- Optional server persistence and versioned network protocol.

## Success metrics

For the first public alpha:

- 100% correct classification in the small manual dirt/paved test matrix.
- No cross-world data leakage.
- No world file changes caused by the mod.
- No recurring errors during a 30-minute traversal test.
- No visible hitch from a single new road sample on the test PC.
- Fresh-profile package install succeeds.
- Basic coexistence with Pinnacle and MapRoutes.

## Primary risks

| Risk | Mitigation |
|---|---|
| Valheim internal field/method changes | Keep terrain access inside `GroundPaintProbe`; fail closed with one actionable warning. |
| Map texture work causes hitches | Incrementally draw only new segments; full redraw only on map/world load. |
| A player teleports between road points | Break a stroke when distance exceeds a configurable maximum gap. |
| Atlas leaks between worlds | Key every persistence file by `ZNet.GetWorldUID()` and test switching. |
| Old roads are missing | State the traversal limitation; implement loaded-chunk recovery later. |
| Conflict with map mods | Use named Jötunn overlays and avoid patching pin UI in v0.1. |
| AI-generated defects | Independent agent review plus mandatory human in-game test evidence. |

## Release gate

No public upload until every item in `TEST_PLAN.md` under **Public alpha gate** has a recorded pass, fail, or justified deferral. A build produced by an agent without game-test evidence is not releasable.

## Research references

- Jötunn map overlays: https://valheim-modding.github.io/Jotunn/tutorials/map.html
- Jötunn quickstart: https://valheim-modding.github.io/Jotunn/guides/quickstart.html
- Thunderstore package format: https://wiki.thunderstore.io/mods/creating-a-package
- Pinnacle: https://thunderstore.io/c/valheim/p/ComfyMods/Pinnacle/
- MapRoutes: https://thunderstore.io/c/valheim/p/SOPMEHUA/MapRoutes/
