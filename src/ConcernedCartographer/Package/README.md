# Concerned Cartographer V1.3.0

**V1.3.0 by The Concerned Cat surveys the mines blacks7ar's OreMines adds, and brings Hulgi: a drowned cartographer who lives at your camp, introduces the atlas, and has some common sense about it — by the fire nearest your bed with a drink now and then, under a roof at night, asleep in a spare bed if you have one. He is local only: no other player can see him, he takes no part in your save, and he never blocks, fights or carries anything. Every atlas feature from 1.0.4 is unchanged. Audited against Valheim 1.0.12.**

![Hulgi sitting on a log by the campfire beside the player, saying "The meadows are kind. That is how they talk you into wandering." over his head](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/4b154a596ee4b762c5c9dc23cfad4b2e22846e67/docs/media/concerned-cartographer/hulgi-campfire.jpg)

*Hulgi at the campfire nearest your bed. Press Use to talk to him — he answers over his head, the way a trader does.*

Concerned Cartographer turns Valheim’s map into a living atlas.

The roads your Vikings build can map themselves. Existing pins can become durable, editable entries with notes, categories, tags, search, saved views, and recoverable deletion. Routes can follow your recorded road network, and selected atlas information can be shared with other players through an explicit preview-and-apply process.

## Highlights

* **Hulgi**, a local-only companion who introduces the atlas and then lives at your camp.
* Dirt paths and paved roads appear as separate map layers.
* Your successful Pathen and Paved construction actions record roads; walking existing terrain does not create road ink.
* Existing vanilla pins can be upgraded and edited without being recreated.
* The Atlas Drawer provides search, filtering, clustering, and saved views.
* Freehand and waypoint routes can follow recorded roads.
* Optional walking Route Follow can steer vanilla Q autorun along the selected route; it is OFF by default and manual input cancels immediately.
* Optional sailing Route Follow can steer the vanilla rudder along a route you marked as a sailing route while you hold the helm; it is OFF by default, your sails stay manual, and any helm input cancels immediately.
* Backups, restoration, migration, and sanitized support reports are built in.
* Multiplayer sharing is deliberate: nothing is applied without review.
* Fog of war is respected.
* World saves are never modified.
* Removing the mod leaves managed pins usable as ordinary vanilla pins.

## Meet Hulgi (new in V1.2.0)

A broken compass lies on the ground near where you wake up. Examine it and Hulgi introduces
himself — a drowned cartographer who never made it home, and who would rather you did. Welcome
him and the atlas tools are yours; or choose **Just the tools, thanks** and skip the story
entirely. Either choice is permanent in the only direction that matters: once the tools are
unlocked, nothing can lock them again.

![At night inside a cottage, Hulgi lies asleep in a spare bed while the player stands beside it](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/4b154a596ee4b762c5c9dc23cfad4b2e22846e67/docs/media/concerned-cartographer/hulgi-spare-bed.gif)

*Night falls: the fire is outside, so Hulgi comes in through the door you opened to him and sleeps in the spare bed until morning.*

* **He has common sense about where to be.** He likes a fire and a drink. By day he goes to the
  burning fire nearest your bed, inside or out, and takes a seat by it if there is one. At night
  or in the rain he goes under a roof — a fire under a roof first — and if there is a bed nobody
  has claimed, he sleeps in it until morning. He never touches a claimed bed. The moment
  something changes — a fire lit or broken, a bed built or claimed, nightfall — he gets up, walks
  over, and sits back down, around buildings and through doorways.
* **Doors are closed to him until you open them.** Look at a door and press **F8** to let
  companions use it; its prompt says so, and pressing it again stops them. A door he may use, he
  opens and closes behind him; a door he may not use, he never walks through. The choice is
  remembered per world, on your computer only. `Companions/DoorAccess = AllDoors` (or "Companions
  may use every door" in Settings) opens every door you could open yourself.
* **He has a drink now and then** — a tankard in his hand, and now and then a toast first, turned
  to you if you are near. The tankard is only drawn while he drinks; it is not an item.
* **He talks when spoken to.** Over thirty short lines about his cat, the weather, and travelling
  carefully. Place-specific remarks only mention biomes *this character* has already visited, so
  he can never spoil somewhere you have not been. Unprompted chatter is **off by default**.
* **He lives where you do.** Around your claimed bed, or the world’s starting point if you have
  not claimed one. Replace the bed and he moves; destroy it and he goes back to the start. Walking
  away from home does not move him — a bed the game cannot currently see is still your bed.
  Turn `Companions/Wander` off if you would rather he stayed put.
* **He is local only.** No other player sees him, he has no networked identity, he takes no part
  in your world save, he has no collision, and he never fights, carries, stores or picks anything
  up. The one thing in the world he ever touches is a door you let companions use. Turning
  `Companions/CompanionsEnabled` off removes all of it and leaves every map tool available.
* **Existing players are never locked out.** If this installation has ever recorded roads, pins,
  routes, a survey rule or a saved view — in *any* world — you keep everything on upgrade, and
  Hulgi is simply someone you can still go and meet. Unreadable or ambiguous data grants access
  too: the only locked state is a genuinely new character in a world the mod could identify.
* **You never have to find him.** The Companions section of the Settings panel, and
  `cc_companion toolsonly on`, both reach the tools without meeting him. Settings is never gated.
* **Nothing about him can cost you anything.** Hiding him, a lost bed, a death, a failed save or a
  game build that cannot draw him changes what you see and nothing else.

`cc_companion status` reports exactly what was resolved on your machine, and `cc_companion
placement` explains where he sits and why.

## See it in action

![A road being paved in the forest, then the map opening to show the same road already drawn as map ink](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/main/docs/media/concerned-cartographer/road-generation.gif)

*Build a path in the world, open the map — the road has already mapped itself.*

![The Atlas Drawer over the map, with layer toggles for dirt roads, paved roads, pins and clustering, plus search and saved views](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/main/docs/media/concerned-cartographer/atlas.jpg)

*The Atlas Drawer: per-layer toggles, atlas search, clustering, and saved views in one panel.*

![The New Marker palette listing categorized marker types such as Fire, Camp, House, Farm, Portal, Harbor and Road/Junction](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/main/docs/media/concerned-cartographer/markers.jpg)

*The marker palette: categorized, searchable marker types placed by double-clicking the map.*

![The Routes panel with a named freehand route drawn as a dashed line across the map to a port, with style, snap-to-roads and editing controls](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/main/docs/media/concerned-cartographer/routes.jpg)

*Routes are planning overlays — freehand or waypoints, styled and named, with snap-to-roads. When you explicitly opt in, Route Follow can steer vanilla Q autorun along the selected route on foot, or the vanilla rudder along a route you marked for sailing.*

![The Survey panel showing pending observations awaiting review, with accept and reject controls for each](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/main/docs/media/concerned-cartographer/survey-before.jpg)

*Survey finds nearby points of interest as pending observations — nothing becomes a marker until you accept it.*

![The Survey panel after accepting, with the accepted observations now visible as named markers on the map](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/main/docs/media/concerned-cartographer/survey-accepted.jpg)

*Accepted observations become ordinary editable markers on the map.*

![The Share panel with explicit Share now, preview, and apply-keep-mine or apply-take-theirs choices](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/main/docs/media/concerned-cartographer/share.jpg)

*Multiplayer sharing is deliberate: preview every incoming share and choose how conflicts resolve — nothing is ever applied automatically.*

![The Settings panel beside the crash-reporting consent dialog, which lists exactly what is and is not sent and offers a turn-off button](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/main/docs/media/concerned-cartographer/settings.jpg)

*Settings with backups, a sanitized support bundle, road-repair tools — and opt-in crash reporting that spells out what is never sent.*

## Version 1.3.0 and support

Version 1.3.0 surveys the eight mines blacks7ar's OreMines adds, as fixed places like a crypt rather than as respawning ore, and brings an untouched survey rules file up to date while leaving an edited one exactly as you wrote it. It also renames the support report from `support-report.txt` to `support-report.log`, so a mod manager's configuration editor stops offering it as a setting to edit while a profile export still carries it. Version 1.2.2 moved two files the mod writes for itself into a `state` subfolder and renamed `author-id.txt` to `author-id.dat`; we have since measured that it was the rename, not the subfolder, that stopped the configuration editor listing it — that editor descends into every folder in your profile and chooses by extension. This was measured in the Thunderstore Mod Manager / r2modman bundle only; Gale is a separate program and has not been assessed. Your existing author identity is carried across on the first start. Version 1.2.1 fixed Hulgi walking into the same wall again and again. When a wall stops him on his way somewhere, he now remembers where and which way he was going, so he takes a way round, or settles somewhere else, instead of trying every spot behind it. If the same place stops him again, he waits longer before trying it again. Nothing else changed.

Version 1.2.0 was the first Thunderstore release with Hulgi. Versions 1.1.0 and 1.1.1 were test builds of him that never reached Thunderstore; everything they changed is part of 1.2.0, and the changelog records them. Every atlas feature from 1.0.4 is unchanged.

Version 1.0.4 added optional sailing Route Follow. Mark a route as a sailing route (Routes panel **Sailing** button, or `cc_routes sailing on`), enable `Routes/SailingAutoFollowEnabled`, take the helm with that route selected, and press Q. Cartographer feeds only the bounded rudder input a player supplies by holding the helm key. Your sails stay manual, and wind, physics, speed, collision, damage, ownership and network authority are untouched. Any helm input, any sail step, jump/attack/secondary/dodge, a second Q, leaving the helm, route changes, going too far off route, reaching the end, or 12 seconds without progress cancels it. There is no tacking, obstacle avoidance, docking or pathfinding. **Live multiplayer verification of sailing Route Follow is still outstanding** and is not claimed.

Version 1.0.3 fixed dungeon surveying: Burial Chambers, Troll Caves, Bear Caves, Sunken Crypts, Frost Caves and Hildir's crypt and cave are offered as pending observations when you walk up to them. They never were before, because the survey only examined networked objects and a Valheim dungeon entrance is not one.

Version 1.0.3 also added optional walking Route Follow. Enable it in the Routes panel, select a live route, close the map, stand near the route, and press Q. A second Q press, manual movement/look, route changes, unsafe lifecycle states, leaving the route, reaching its end, or making no route progress for 2.5 seconds cancels it. Ships, carts, mounts, speed, stamina, collision, fog, and multiplayer authority are never controlled. It carries forward the Valheim 1.0.7 HUD-message and high-resolution UI fixes from 1.0.2, verified against the current Valheim 1.0.12 build.

Development and compatibility support continue beyond v1.0. Confirmed crashes, data-loss risks, installation failures, stuck input, duplicated pins, and multiplayer consistency problems receive the highest priority.

Report ordinary bugs and compatibility problems through the GitHub issue tracker:

https://github.com/Weakened/ConcernedCatMods/issues

Run `cc_atlas support` to create a sanitized report containing versions, settings, and counts—but never coordinates, names, pins, routes, or notes.

For security vulnerabilities, privacy questions, or sensitive logs:

[support@theconcernedcat.com](mailto:support@theconcernedcat.com)

Crash reporting is optional, disabled until the player explicitly opts in, and contains no gameplay analytics.
