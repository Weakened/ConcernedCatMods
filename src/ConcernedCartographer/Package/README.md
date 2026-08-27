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
- The atlas is stored inside the **active mod-manager profile's** BepInEx config folder. Each profile keeps its own atlas, so switching to a fresh profile starts an empty atlas for the same world and roads re-record as you traverse them. Copy the `ConcernedCatMods` config folder between profiles to carry an atlas over.
- Road data is local to each client; there is no multiplayer sharing yet.
- Marker editing, richer legends, and cartography-table sharing are planned but not in this build.

## Installation

Install with a Thunderstore-compatible mod manager. BepInExPack Valheim and Jötunn are declared dependencies and should install automatically.

## Use

1. Start the game modded.
2. Enter a world and walk along a dirt Pathen or paved road.
3. Open the map.
4. Use Jötunn's map-overlay menu to toggle the dirt and paved layers.

## Configuration

Settings live in `BepInEx/config/com.theconcernedcat.valheim.concernedcartographer.cfg`. Out-of-range values are clamped to the documented range; the effective values are logged once at startup.

| Setting | Default | Range | Purpose |
|---|---|---|---|
| General / Enabled | true | — | Master switch for surveying and overlays |
| Sources / CaptureConstructionActions | true | — | Record your own successful hoe/stonecutter paint actions instantly, without walking them |
| Sources / ReconcileTerrainChanges | true | — | Cultivating/resetting terrain removes covered road ink; repainting converts road kind |
| Sources / RecoverLoadedChunks | true | — | Recover narrow road paint from loaded terrain near you, only in map areas you have explored |
| Sources / RecoveryBudgetCellsPerFrame | 256 | 32–8192 | Paint cells examined per frame by chunk recovery |
| Survey / SampleIntervalSeconds | 0.35 | 0.10–5.0 | Seconds between terrain samples |
| Survey / MinimumPointSpacingMeters | 1.5 | 0.5–20 | Minimum distance before a new road point is stored |
| Survey / MaximumStrokeGapMeters | 8.0 | 2–100 | Larger gaps start a new stroke instead of a connector line |
| Survey / DuplicateSuppressionMeters | 2.0 | 0–10 | Skip samples near already-recorded ink of the same kind; re-walking a road never grows the atlas (0 disables; values above ~3 can also suppress tight hairpins) |
| Persistence / AutosaveIntervalSeconds | 15 | 5–300 | Seconds between dirty-atlas autosaves |
| Detection / PaintThreshold | 0.40 | 0.10–0.95 | Minimum averaged paint value that counts as road |
| Detection / PaintSampleRadius | 1 | 0–3 | Paint pixels averaged around the player |
| Map / LineWidthPixels | 1 | 1–6 | Road line width in map texels (~11.6 m each; widths above 1 make nearby roads merge) |
| Diagnostics / DebugLogging | false | — | Opt-in, rate-limited classification/recording diagnostics |
| Diagnostics / DrawCalibrationMarkers | false | — | Overlay alignment calibration crosses (development aid) |

## Road repair tools

Open the console (launch with `-console`) and use `cc_roads` — every
operation targets the recorded road nearest your character, and an optional
number widens the search radius in meters:

| Command | Effect |
|---|---|
| `cc_roads status` | Atlas totals and the nearest road's kind/size/source |
| `cc_roads delete [radius]` | Delete the nearest road (undoable) |
| `cc_roads kind` | Toggle the nearest road between Dirt and Paved |
| `cc_roads hide` / `unhide` | Hide a road from the map without deleting it |
| `cc_roads split` | Split the nearest road at its closest interior point |
| `cc_roads join [radius]` | Stitch the two nearest same-kind road ends |
| `cc_roads rebuild [radius]` | Clear recorded roads nearby and re-scan explored terrain |
| `cc_roads undo` | Undo the last tool operation (up to 20) |

The tools edit only the mod's own atlas; they can never modify terrain or
world saves. Before the first destructive change each session the sidecar is
backed up to `.pre-reconcile.bak`.

## Data and uninstall safety

The atlas is stored at:

```text
BepInEx/config/ConcernedCatMods/ConcernedCartographer/<world-uid>.roads.tsv
```

The mod does not edit the Valheim world file. Removing the DLL stops surveying/rendering; deleting the sidecar folder removes the locally recorded atlas.

## Compatibility

The first release is client-side and intentionally avoids replacing vanilla pin UI. Verified compatible with **Pinnacle 1.16.0** (pin create/edit/list/filter) and **MapRoutes 1.1.0** (route drawing and persistence) in the same profile — road layers, pins, and manual routes coexist, and toggling one system never hides another. Report conflicts with a BepInEx log and the exact mod versions installed.

## AI disclosure

AI coding agents materially assisted implementation and review. Releases are manually reviewed and tested in game before publication, and the package uses Thunderstore's **AI Generated** category.

## Support and source

Use the GitHub issue tracker linked by the package website. Include the game version, mod version, profile mod list, reproduction steps, and `BepInEx/LogOutput.log` excerpt.
