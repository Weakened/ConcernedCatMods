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
- World-generated dirt patches (such as the circle around the spawn stones) can still be recorded when you walk on them — the mod cannot distinguish world-generated paint from roads. Your **own terraforming is understood**, though: ground you Level/Raise/Cultivate/Reset is remembered as explicitly-not-road (persistently, per world), and only a later deliberate Pathen/Paved action turns it into road ink.
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

## Controls

Everything is reachable with the mouse from the large map — no hotkeys
required (they remain as rebindable accelerators).

| Control | Where | Action |
|---|---|---|
| **[ Atlas ]** button (or `L`) | Large map, bottom right | Open/close the Atlas Drawer — layers, search, filters, saved views |
| **[ Markers ]** palette | Large map, right side | Create managed markers: pick an icon, then double-click the map — the marker is fully editable immediately, no upgrade step |
| Hover an existing marker | Large map | A context button appears: **Upgrade & Edit** for your vanilla markers, **Edit Pin** for managed ones (or press `P`) |
| `F7` | In the world | Quick-pin what you are looking at |
| Right-click on a pin | Large map | Vanilla pin delete — this mod never changes vanilla map input |

Cross Off, Remove Pin, Ping, and Visible-to-other-players all remain
pure vanilla. Console commands (launch with `-console`): `cc_roads`,
`cc_pins`, `cc_atlas`, `cc_survey`, `cc_routes`, `cc_sync`.

## Enhanced Pin Palette

While the large map is open, the **Markers** palette replaces Valheim's
five raw icon buttons as the way to place your own pins: a searchable
list with icon previews, human names, and your recent picks. Choose a
marker, double-click the map, name it — the pin is a managed
Concerned Cartographer marker from birth, rendered as one ordinary
saved vanilla pin (uninstall-safe as always).

Prefer the vanilla selector? Set `Pins/ShowVanillaPinPalette = true`
(or `Pins/EnhancedPinPalette = false`) — the vanilla buttons come back
instantly and everything else keeps working. When a known conflicting
pin manager is installed, the vanilla selector stays automatically.
Death, boss, bed, and other system pins are never touched.

## Pin Workbench

Your pins become a durable, editable atlas. Hover any marker on the
large map and click the context button (or press `P`): managed markers
open the editor, your existing vanilla markers offer **Upgrade & Edit**
— it keeps the marker exactly where it is and enables Concerned
Cartographer editing, notes, categories and atlas features. Foreign and
system pins show read-only info. Every edit keeps the pin's identity —
nothing is deleted and recreated — and deletes are recoverable
tombstones.

Visual properties are edited with pickers, not raw IDs: the icon field is
a dropdown with the live pin sprite as preview (custom/legacy icon IDs are
preserved and offered as "Keep custom"), category offers suggestions while
staying free text, size is a stepper, and status/visibility are dropdown
selects. Pin color is stored and synced but not yet rendered on the map,
so it sits at the bottom of the panel labeled **metadata** rather than
pretending to be visual.

The `cc_pins` console command drives everything scriptably: `edit`,
`status`, `list [filter]`, `adopt`, `adoptall confirm`, `create <name>`,
field editors (`name`, `icon`, `category`, `color`, `size`, `note`,
`tag+/tag-`, `setstatus`, `check/uncheck`, `scope`), `move`, `dup`,
`archive/unarchive`, `delete`, `restore`, `deleted`, `dups`,
`merge confirm`, `undo`, `redo`, and `coords` (copies to clipboard).
Batch adoption and merges always preview first and require `confirm`.

Pins are stored per world next to the road atlas with crash-safe
snapshot+journal persistence. Removing the mod leaves every managed pin on
the map as a plain vanilla pin.

## Atlas Drawer, search, and clustering

Press `L` on the large map for the Atlas Drawer: toggle road/pin layers and
clustering, search the atlas (plain words, or tokens like `tag:iron`,
`category:travel`, `is:unchecked`, `near:0,0,500`), click a result to edit
it, and save the current filter/layer state as a named view. Zoomed out,
crowded pins fold into cluster markers; filters and clusters are display
only and never change stored data. Console: `cc_atlas`.

Press `F7` in the world to quick-pin whatever you're looking at (never
creatures). Opt-in **Survey Rules** (`Survey/SurveyRulesEnabled`, plus a
shareable `survey-rules.tsv`) turn nearby loaded objects into reviewable
observations — `cc_survey list/accept/reject` — with hard caps, duplicate
radii, and base-exclusion zones so your map never floods.

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

## Beta feedback, privacy, and security

Found a problem? Open an issue at
https://github.com/Weakened/ConcernedCatMods/issues and attach the file
from `cc_atlas support` — it is sanitized by construction (versions,
settings, and counts only; never positions, names, or notes). For
security vulnerabilities, privacy questions, or logs you should not post
publicly: **support@theconcernedcat.com**.

**Privacy**: everything the mod records stays in local files under your
BepInEx config folder. Nothing is uploaded anywhere. Sharing happens only
between players on your server, only for entities you explicitly scope,
and only after the receiver reviews and applies it.

**Optional crash reporting (off by default)**: on your first large-map
open the mod asks once whether to send anonymous crash reports when
Concerned Cartographer itself hits an internal error. If you say yes, a
report carries only mod/game versions, the affected subsystem, the
exception type, and a sanitized stack trace — never your identity,
world/character names, seeds, coordinates, pins/routes, server details,
saves, or logs (automated tests enforce this, and the provider is
configured not to store IPs). Change your answer anytime under
**CC Atlas → Privacy**. Full policy: `PRIVACY.md` in the repository.
No gameplay analytics, ever.

**Security model**: incoming shares are size-capped (including bounded
decompression, so oversized payloads are rejected before they can use
memory), parsed with malformed-row skipping and sanity bounds on every
field, and never applied automatically. The sync preview names any entity
a share would delete so you can review deletions before accepting them.
Deletions are durable — a stale or misbehaving client cannot resurrect
them. Author labels identify who edited what but are not cryptographic
proof of identity.

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

Use the GitHub issue tracker linked by the package website for bugs and
feature requests. Include the game version, mod version, profile mod
list, reproduction steps, and a `BepInEx/LogOutput.log` excerpt. For
security vulnerabilities, privacy questions, or anything that should not
be public: **support@theconcernedcat.com** (human support only — crash
reports are never sent by email).

<!-- CC-PACKAGE-ATTRIBUTION -->
## Original project, source, and contributions

Concerned Cartographer is created and maintained by **Eren Cansunar / The Concerned Cat**. AI coding agents materially assisted implementation, tests, research and documentation; releases use the appropriate AI disclosure and are validated through the project's release process.

Source, technical documentation, issue tracker, and contribution guide are available in the canonical repository: `Weakened/ConcernedCatMods`.

The source-code license is in `LICENSE`. Original project attribution/provenance is documented in `NOTICE.md` and `AUTHORS.md`.
