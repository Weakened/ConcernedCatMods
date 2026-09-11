# Concerned Cartographer V1.1

**V1.1.0 by The Concerned Cat adds opt-in walking Route Follow while preserving vanilla movement, safety boundaries, and the Valheim 1.0.7 compatibility fixes.**

Concerned Cartographer turns Valheim’s map into a living atlas.

The roads your Vikings build can map themselves. Existing pins can become durable, editable entries with notes, categories, tags, search, saved views, and recoverable deletion. Routes can follow your recorded road network, and selected atlas information can be shared with other players through an explicit preview-and-apply process.

## Highlights

* Dirt paths and paved roads appear as separate map layers.
* Your successful Pathen and Paved construction actions record roads; walking existing terrain does not create road ink.
* Existing vanilla pins can be upgraded and edited without being recreated.
* The Atlas Drawer provides search, filtering, clustering, and saved views.
* Freehand and waypoint routes can follow recorded roads.
* Optional walking Route Follow can steer vanilla Q autorun along the selected route; it is OFF by default and manual input cancels immediately.
* Backups, restoration, migration, and sanitized support reports are built in.
* Multiplayer sharing is deliberate: nothing is applied without review.
* Fog of war is respected.
* World saves are never modified.
* Removing the mod leaves managed pins usable as ordinary vanilla pins.

## See it in action

![A road being paved in the forest, then the map opening to show the same road already drawn as map ink](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/main/docs/media/concerned-cartographer/road-generation.gif)

*Build a path in the world, open the map — the road has already mapped itself.*

![The Atlas Drawer over the map, with layer toggles for dirt roads, paved roads, pins and clustering, plus search and saved views](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/main/docs/media/concerned-cartographer/atlas.jpg)

*The Atlas Drawer: per-layer toggles, atlas search, clustering, and saved views in one panel.*

![The New Marker palette listing categorized marker types such as Fire, Camp, House, Farm, Portal, Harbor and Road/Junction](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/main/docs/media/concerned-cartographer/markers.jpg)

*The marker palette: categorized, searchable marker types placed by double-clicking the map.*

![The Routes panel with a named freehand route drawn as a dashed line across the map to a port, with style, snap-to-roads and editing controls](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/main/docs/media/concerned-cartographer/routes.jpg)

*Routes are planning overlays — freehand or waypoints, styled and named, with snap-to-roads. When you explicitly opt in, walking Route Follow can steer vanilla Q autorun along the selected route.*

![The Survey panel showing pending observations awaiting review, with accept and reject controls for each](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/main/docs/media/concerned-cartographer/survey-before.jpg)

*Survey finds nearby points of interest as pending observations — nothing becomes a marker until you accept it.*

![The Survey panel after accepting, with the accepted observations now visible as named markers on the map](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/main/docs/media/concerned-cartographer/survey-accepted.jpg)

*Accepted observations become ordinary editable markers on the map.*

![The Share panel with explicit Share now, preview, and apply-keep-mine or apply-take-theirs choices](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/main/docs/media/concerned-cartographer/share.jpg)

*Multiplayer sharing is deliberate: preview every incoming share and choose how conflicts resolve — nothing is ever applied automatically.*

![The Settings panel beside the crash-reporting consent dialog, which lists exactly what is and is not sent and offers a turn-off button](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/main/docs/media/concerned-cartographer/settings.jpg)

*Settings with backups, a sanitized support bundle, road-repair tools — and opt-in crash reporting that spells out what is never sent.*

## Version 1.1 and support

Version 1.1 adds optional walking Route Follow. Enable it in the Routes panel, select a live route, close the map, stand near the route, and press Q. A second Q press, manual movement/look, route changes, unsafe lifecycle states, leaving the route, reaching its end, or making no route progress for 2.5 seconds cancels it. Ships, carts, mounts, speed, stamina, collision, fog, and multiplayer authority are never controlled. Version 1.1 also carries forward the Valheim 1.0.7 HUD-message and high-resolution UI fixes from 1.0.2.

Development and compatibility support continue beyond v1.0. Confirmed crashes, data-loss risks, installation failures, stuck input, duplicated pins, and multiplayer consistency problems receive the highest priority.

Report ordinary bugs and compatibility problems through the GitHub issue tracker:

https://github.com/Weakened/ConcernedCatMods/issues

Run `cc_atlas support` to create a sanitized report containing versions, settings, and counts—but never coordinates, names, pins, routes, or notes.

For security vulnerabilities, privacy questions, or sensitive logs:

[support@theconcernedcat.com](mailto:support@theconcernedcat.com)

Crash reporting is optional, disabled until the player explicitly opts in, and contains no gameplay analytics.
