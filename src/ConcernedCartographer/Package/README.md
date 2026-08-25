# Concerned Cartographer

**Alpha road-survey release by The Concerned Cat.**

Concerned Cartographer turns the dirt Pathen and paved terrain your Viking actually traverses into separate map layers. It creates a local, per-world atlas without modifying Valheim's terrain or world save.

## Current features

- Detects dirt Pathen and paved terrain beneath the local player.
- Records road strokes with spacing and teleport-gap protection.
- Draws separate dirt and paved overlays on the full map and minimap.
- Uses Jötunn's overlay controls so each road layer can be toggled.
- Respects unexplored fog by default.
- Saves road data separately for each world under the BepInEx config folder.

## Important alpha limitations

- Roads are discovered as **you walk along them after installing the mod**.
- This version does not scan the whole world or immediately recover every old road.
- Any road-like terrain paint you walk on is recorded, including world-generated dirt patches such as the circle around the spawn stones — the mod does not yet distinguish player-made from world-generated paint.
- A road line can sit up to ~6 m from its true position at maximum zoom; that is the native resolution of Valheim's 2048-pixel map texture.
- Road data is local to each client; there is no multiplayer sharing yet.
- Marker editing, richer legends, and cartography-table sharing are planned but not in this build.

## Installation

Install with a Thunderstore-compatible mod manager. BepInExPack Valheim and Jötunn are declared dependencies and should install automatically.

## Use

1. Start the game modded.
2. Enter a world and walk along a dirt Pathen or paved road.
3. Open the map.
4. Use Jötunn's map-overlay menu to toggle the dirt and paved layers.

## Data and uninstall safety

The atlas is stored at:

```text
BepInEx/config/ConcernedCatMods/ConcernedCartographer/<world-uid>.roads.tsv
```

The mod does not edit the Valheim world file. Removing the DLL stops surveying/rendering; deleting the sidecar folder removes the locally recorded atlas.

## Compatibility

The first release is client-side and intentionally avoids replacing vanilla pin UI. Compatibility with Pinnacle and MapRoutes is part of the release test matrix. Report conflicts with a BepInEx log and the exact mod versions installed.

## AI disclosure

AI coding agents materially assisted implementation and review. Releases are manually reviewed and tested in game before publication, and the package uses Thunderstore's **AI Generated** category.

## Support and source

Use the GitHub issue tracker linked by the package website. Include the game version, mod version, profile mod list, reproduction steps, and `BepInEx/LogOutput.log` excerpt.
