# Concerned Cartographer

**The living atlas for Valheim, by The Concerned Cat.**

Concerned Cartographer turns the roads your Viking actually traverses into a durable, editable, shareable atlas: dirt and paved road layers, fully editable markers, routes, search, and cartography-table sharing — all local files, never modifying Valheim's terrain or world save, always uninstall-safe.

## Features

- **Roads**: detects dirt Pathen and paved terrain as you traverse it, captures your own hoe/stonecutter paint actions instantly, and recovers narrow road paint from loaded terrain you have explored. Separate dirt/paved layers on the large map and minimap. On the large map, roads render as **high-precision vector ink** that stays put at any zoom (the minimap keeps the classic texture overlay).
- **Markers**: a searchable marker palette replaces the five raw vanilla icon buttons; markers placed through it are managed from birth. Existing vanilla pins upgrade in place ("Upgrade & Edit") and every property — name, icon, category, size, notes, tags, status, scope — is edited in the Pin Workbench without ever deleting and recreating a pin.
- **Atlas Drawer**: layers, clustering, token search (`tag:iron`, `near:0,0,500`), saved views, System Markers (pin-type filters + visible-to-others), and Privacy.
- **Routes**: free-draw and waypoint routes from the [Routes] panel with explicit on-map modes, snapping to your recorded roads, distance/time estimates, and full editing (rename, style, status, color, lock, archive, split, merge, undo/redo).
- **Survey Rules** (opt-in): nearby loaded objects become reviewable observations — nothing is pinned until you accept it.
- **Sharing**: private/table/server scopes with a preview inbox — nothing from other players applies automatically, deletions are named before you accept them, and deleted entities can never resurrect.
- **One toolbar**: [Atlas] [Markers] [Routes] [Survey] [Share] [Quick Pin] [Settings] on the large map. Every feature has a visible UI path; hotkeys and console commands stay as rebindable accelerators.

## Known limitations

- Roads are discovered as **you walk along them after installing the mod**; the mod never scans the whole world at once.
- World-generated dirt patches (such as the circle around the spawn stones) can be recorded when you walk on them — the mod cannot distinguish world-generated paint from roads. Your **own terraforming is understood**: ground you Level/Raise/Cultivate/Reset is remembered as explicitly-not-road (persistently, per world), and only a later deliberate Pathen/Paved action turns it into road ink.
- The atlas lives inside the **active mod-manager profile's** BepInEx config folder. Each profile keeps its own atlas; copy the `ConcernedCatMods` config folder between profiles to carry one over.
- Pin color and display size are stored, edited, and synced, but not yet rendered on the vanilla map (planned for a later release; no migration will be needed).
- Sharing is peer-to-peer between online players; there is no dedicated-server-side store. MapRoutes routes coexist but are not imported.
- On the minimap (and with `Map/HighPrecisionLargeMapRoads` off, on the large map too), a road line can sit up to ~6 m from its true position at maximum zoom — the native resolution of Valheim's 2048-pixel map texture.

## Installation

Install with a Thunderstore-compatible mod manager. BepInExPack Valheim and Jötunn are declared dependencies and should install automatically.

## Use

1. Start the game modded, enter a world, and walk your roads — they appear on the map as you traverse them.
2. Open the large map: the Concerned Cartographer toolbar sits at the bottom center.
3. [Atlas] opens the drawer (layers, search, saved views, System Markers, Privacy). [Markers] opens the palette — pick an icon, double-click the map, done.
4. Hover any of your markers and click **Edit Pin** / **Upgrade & Edit** to open the workbench.

## Controls and shortcut parity

Everything is reachable with the mouse from the large map. Hotkeys are rebindable accelerators — every one of them has a visible UI path that does the same thing:

| Feature | Shortcut | Visible UI path | Shortcut still works |
|---|---|---|---|
| Atlas Drawer | `L` | Toolbar **[Atlas]** | ✓ |
| Marker palette | — | Toolbar **[Markers]** (Escape closes) | — |
| Edit / upgrade the hovered pin | `P` | Hover a marker → context button **Edit Pin** / **Upgrade & Edit** | ✓ |
| Quick Pin what you look at | `F7` | Toolbar **[Quick Pin]** — the map closes, your next click (or `F7`) captures one-shot; `Esc` cancels | ✓ (instant, no arming) |
| Routes: draw, waypoints, erase, edit ops | `Shift+LMB` after a `cc_routes` mode | Toolbar **[Routes]** — Free Draw / Waypoints / Erase buttons enter explicit modes (no modifier needed; map drag is consumed; Finish/Undo/Redo/Escape) | ✓ (console modes keep the modifier) |
| Survey review | `cc_survey` console | Toolbar **[Survey]** — enable, pending list, accept/reject, bulk with confirm, reload | ✓ |
| Sharing | `cc_sync` console | Toolbar **[Share]** — status, share now, inbox, preview (deletions named), apply mine/theirs, clear | ✓ |
| Privacy, backup/restore, support bundle, road repair | `cc_atlas` / `cc_roads` console | Toolbar **[Settings]** — privacy, backup, confirmed restore (most recent backup), sanitized support bundle, road repair under Advanced | ✓ |
| Pin-type filters, visible-to-others | vanilla rail (hidden by default) | **[Atlas] → System Markers** — vanilla state, never touches pins; `Map/ShowVanillaMapControls = true` brings the vanilla rail back | ✓ (when rail shown) |
| Close the active panel | `Esc` | Every panel's Close button | ✓ |
| Gamepad | `Accessibility/*GamepadButton` bindings | Panels select their first control on open | opt-in |

Console-only (scriptable/advanced, by design): `cc_pins` batch and recovery operations (`list`, `move`, `dup`, `archive`, `merge`, `restore`, `deleted`, `undo`, `redo`, `coords`, `create`, `adoptall`), `cc_atlas view del` / `compat` / `restore <n>` for older backups, `cc_survey path`, and arbitrary hex route colors. The [Routes] **Restore** button recovers the most recently deleted route.

Right-click pin delete, Cross Off, Remove, Ping, and double-click placement remain pure vanilla input. By default the vanilla right-side map rail (icon selectors, death/boss filters, visible-to-others) is hidden because the toolbar and System Markers replace it — set `Map/ShowVanillaMapControls = true` to show it alongside, and it comes back automatically if a conflicting pin manager is detected, any CC surface fails, or the mod is disabled.

## Configuration

Settings live in `BepInEx/config/com.theconcernedcat.valheim.concernedcartographer.cfg`. Out-of-range values are clamped to the documented range; effective values are logged once at startup.

| Setting | Default | Purpose |
|---|---|---|
| General / Enabled | true | Master switch for surveying and overlays |
| Sources / CaptureConstructionActions | true | Record your own successful hoe/stonecutter paint actions instantly |
| Sources / ReconcileTerrainChanges | true | Cultivating/resetting removes covered road ink; repainting converts kind |
| Sources / RecoverLoadedChunks | true | Recover narrow road paint from loaded terrain in explored areas |
| Sources / RecoveryBudgetCellsPerFrame | 256 | Paint cells examined per frame by chunk recovery |
| Survey / SampleIntervalSeconds | 0.35 | Seconds between terrain samples |
| Survey / MinimumPointSpacingMeters | 1.5 | Minimum distance before a new road point is stored |
| Survey / MaximumStrokeGapMeters | 8.0 | Larger gaps start a new stroke instead of a connector line |
| Survey / DuplicateSuppressionMeters | 2.0 | Skip samples near already-recorded ink (0 disables) |
| Survey / SurveyRulesEnabled | false | Opt-in survey observations (accept/reject review) |
| Survey / SurveyScanIntervalSeconds, SurveyScanRadius, SurveyBaseExclusionRadius, SurveyMaxObservations | 10 / 40 / 30 / 200 | Survey scan cadence, radius, base exclusion, hard cap |
| Persistence / AutosaveIntervalSeconds | 15 | Seconds between dirty-atlas autosaves |
| Detection / PaintThreshold | 0.40 | Minimum averaged paint value that counts as road |
| Detection / PaintSampleRadius | 1 | Paint pixels averaged around the player |
| Map / LineWidthPixels | 1 | Texture-overlay road width in map texels (~11.6 m each) |
| Map / HighPrecisionLargeMapRoads | true | Sub-texel vector road ink on the large map (texture overlay stays for the minimap and as fallback) |
| Map / ShowVanillaMapControls | false | Show Valheim's own right-side map rail alongside the CC toolbar |
| Workbench / WorkbenchHotkey | P | Edit/upgrade the hovered pin |
| Workbench / QuickPinHotkey | F7 | Instant quick pin (also fires an armed [Quick Pin]) |
| Workbench / QuickPinDuplicateRadius | 25 | Suppress duplicate quick pins within this range |
| Drawer / DrawerHotkey | L | Atlas Drawer toggle |
| Drawer / ShowDirtRoads, ShowPavedRoads, ShowPins, Clustering | true | Layer defaults |
| Pins / EnhancedPinPalette | true | The managed marker palette (managed-from-birth placement) |
| Pins / ShowVanillaPinPalette | false | Keep Valheim's five icon buttons visible (auto-true with a conflicting pin manager) |
| Routes / RouteDrawModifier | LeftShift | Modifier for console-entered map modes (panel modes need none) |
| Routes / RouteEraseRadius, RouteSnapRadius, RouteOnRoadTolerance | 8 / 15 / 6 | Erase brush, road snapping, on-road tolerance (meters) |
| Routes / RouteOffRoadSpeed, RouteOnRoadSpeed | 2.5 / 5 | Travel speeds (m/s) for time estimates |
| Accessibility / UiScale | 1.0 | Panel scale 0.8–1.6 |
| Accessibility / HighContrast | false | Near-black dirt / near-white paved ink, brighter route colors |
| Accessibility / WorkbenchGamepadButton, DrawerGamepadButton | (empty) | Opt-in ZInput bindings |
| Privacy / SendCrashReports | Unknown | Tri-state crash-report consent (asked once on first large-map open) |
| Privacy / SentryDsn | (empty) | Advanced: override the crash-report ingestion key — never put an auth token here |
| Diagnostics / DebugLogging | false | Opt-in, rate-limited classification/recording diagnostics |
| Diagnostics / DrawCalibrationMarkers | false | Overlay alignment calibration crosses (development aid) |

## Enhanced Pin Palette

While the large map is open, **[Markers]** opens a searchable palette with icon previews, human names, category grouping, and your recent picks. Choose a marker, double-click the map, name it — the pin is a managed Concerned Cartographer marker from birth, rendered as one ordinary saved vanilla pin (uninstall-safe as always).

Prefer the vanilla selector? Set `Pins/ShowVanillaPinPalette = true` (or `Pins/EnhancedPinPalette = false`) — the vanilla buttons come back instantly and everything else keeps working. When a known conflicting pin manager is installed, the vanilla selector stays automatically. Death, boss, bed, and other system pins are never touched.

## Pin Workbench

Hover any marker on the large map and click the context button (or press `P`): managed markers open the editor, your existing vanilla markers offer **Upgrade & Edit** — it keeps the marker exactly where it is and enables editing, notes, categories, and atlas features. Foreign and system pins show read-only info. Every edit keeps the pin's identity — nothing is deleted and recreated — and deletes are recoverable tombstones.

Visual properties are edited with pickers, not raw IDs: the icon field is a dropdown with the live pin sprite as preview ("Keep custom" preserves legacy IDs), category offers suggestions while staying free text, size is a stepper, and status/visibility are dropdown selects. Pin color is stored and synced but not yet rendered on the map, so it sits at the bottom of the panel labeled **metadata** rather than pretending to be visual.

The `cc_pins` console command drives everything scriptably: `edit`, `status`, `list [filter]`, `adopt`, `adoptall confirm`, `create <name>`, field editors (`name`, `icon`, `category`, `color`, `size`, `note`, `tag+/tag-`, `setstatus`, `check/uncheck`, `scope`), `move`, `dup`, `archive/unarchive`, `delete`, `restore`, `deleted`, `dups`, `merge confirm`, `undo`, `redo`, and `coords`. Batch adoption and merges always preview first and require `confirm`.

Pins are stored per world next to the road atlas with crash-safe snapshot+journal persistence. Removing the mod leaves every managed pin on the map as a plain vanilla pin.

## Atlas Drawer, search, and clustering

**[Atlas]** (or `L`) opens the drawer: toggle road/pin layers and clustering, search the atlas (plain words, or tokens like `tag:iron`, `category:travel`, `is:unchecked`, `near:0,0,500`), click a result to edit it, and save the current filter/layer state as a named view. **System Markers** hosts the vanilla pin-type filters and the visible-to-others toggle; **Privacy** hosts crash-report consent. Zoomed out, crowded pins fold into cluster markers; filters and clusters are display-only and never change stored data. Console: `cc_atlas`.

## Routes

**[Routes]** lists your routes by name, kind, status, distance, and lock/archive state. **Free Draw** and **Waypoints** enter explicit on-map modes — no modifier key, the map does not pan while you draw, and waypoints snap to your recorded roads (toggleable). Finish/Undo/Redo sit next to the mode buttons; Escape ends the mode first, then closes the panel. Rename, style, status, ink color swatches, lock, archive, delete/restore, split, merge, and measure all operate on the selected route. Estimates use your configured on/off-road speeds. Console: `cc_routes` (scriptable alias, classic `Shift+LMB` modes included).

## Survey Rules and Quick Pin

**[Quick Pin]** on the toolbar closes the map and arms a one-shot capture: your next deliberate click (or `F7`) pins what you are looking at — never creatures, never duplicates within the configured radius; `Esc` cancels. `F7` in the world stays the instant path.

Opt-in **Survey Rules** (`Survey/SurveyRulesEnabled`, plus a shareable `survey-rules.tsv`) turn nearby loaded objects into reviewable observations in the **[Survey]** panel — enable, review the pending list, accept or reject each (or all, with confirmation), reload rules. Nothing is pinned until you accept it. Console: `cc_survey`.

## Sharing

**[Share]** shows scoped entity counts, shares your table/server-scoped entities, and previews the inbox: every incoming share lists what it changes — including, by name, anything it would delete — before you apply mine-wins or theirs-wins. Nothing ever applies automatically. Deleted entities are durable tombstones: a stale client cannot resurrect them. Console: `cc_sync`.

## Road repair tools

**[Settings] → Road repair (Advanced)** offers the full toolset; the console equivalent is `cc_roads` — every operation targets the recorded road nearest your character, and an optional number widens the search radius in meters:

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

The tools edit only the mod's own atlas; they can never modify terrain or world saves. Before the first destructive change each session the sidecar is backed up to `.pre-reconcile.bak`.

## Feedback, privacy, and security

Found a problem? Open an issue at https://github.com/Weakened/ConcernedCatMods/issues and attach the file from **[Settings] → support bundle** (or `cc_atlas support`) — it is sanitized by construction (versions, settings, and counts only; never positions, names, or notes). For security vulnerabilities, privacy questions, or logs you should not post publicly: **support@theconcernedcat.com**.

**Privacy**: everything the mod records stays in local files under your BepInEx config folder. Nothing is uploaded anywhere. Sharing happens only between players on your server, only for entities you explicitly scope, and only after the receiver reviews and applies it.

**Optional crash reporting (off by default)**: on your first large-map open the mod asks once whether to send anonymous crash reports when Concerned Cartographer itself hits an internal error. If you say yes, a report carries only mod/game versions, the affected subsystem, the exception type, and a sanitized stack trace — never your identity, world/character names, seeds, coordinates, pins/routes, server details, saves, or logs (automated tests enforce this, and the provider is configured not to store IPs). Change your answer anytime under **[Atlas] → Privacy**. Full policy: `PRIVACY.md` in the repository. No gameplay analytics, ever.

**Security model**: incoming shares are size-capped (including bounded decompression, so oversized payloads are rejected before they can use memory), parsed with malformed-row skipping and sanity bounds on every field, and never applied automatically. The sync preview names any entity a share would delete so you can review deletions before accepting them. Deletions are durable — a stale or misbehaving client cannot resurrect them. Author labels identify who edited what but are not cryptographic proof of identity.

## Data and uninstall safety

The atlas is a per-world sidecar family:

```text
BepInEx/config/ConcernedCatMods/ConcernedCartographer/<world-uid>.roads.tsv
                                                      <world-uid>.pins.tsv (+ journal)
                                                      <world-uid>.routes.tsv (+ journal)
                                                      <world-uid>.terrain-intent.tsv
```

The mod does not edit the Valheim world file. Removing the DLL stops surveying/rendering and leaves managed pins as plain vanilla pins; deleting the sidecar folder removes the locally recorded atlas.

## Compatibility

Client-side only. In enhanced mode the mod replaces the vanilla right-side map rail with its own toolbar and panels — reversibly, using only show/hide, with automatic fallback: when a known conflicting pin manager is detected the vanilla controls stay, and `Map/ShowVanillaMapControls` / `Pins/ShowVanillaPinPalette` bring them back at any time. Verified compatible with **Pinnacle 1.16.0** and **MapRoutes 1.1.0** in the same profile — road layers, pins, and both mods' routes coexist. Report conflicts with a BepInEx log and the exact mod versions installed.

## AI disclosure

AI coding agents materially assisted implementation and review. Releases are manually reviewed and tested in game before publication, and the package uses Thunderstore's **AI Generated** category.

## Support and source

Use the GitHub issue tracker linked by the package website for bugs and feature requests. Include the game version, mod version, profile mod list, reproduction steps, and a `BepInEx/LogOutput.log` excerpt. For security vulnerabilities, privacy questions, or anything that should not be public: **support@theconcernedcat.com** (human support only — crash reports are never sent by email).

<!-- CC-PACKAGE-ATTRIBUTION -->
## Original project, source, and contributions

Concerned Cartographer is created and maintained by **Eren Cansunar / The Concerned Cat**. AI coding agents materially assisted implementation, tests, research and documentation; releases use the appropriate AI disclosure and are validated through the project's release process.

Source, technical documentation, issue tracker, and contribution guide are available in the canonical repository: `Weakened/ConcernedCatMods`.

The source-code license is in `LICENSE`. Original project attribution/provenance is documented in `NOTICE.md` and `AUTHORS.md`.
