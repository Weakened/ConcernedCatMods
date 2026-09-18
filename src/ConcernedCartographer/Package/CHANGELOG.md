# Changelog

## 1.2.2 - Two files move out of your settings folder

Fixed

- **The mod's own bookkeeping no longer sits among your settings.** Gale listed
  `author-id.txt` beside the real settings file, as though it were something to
  edit (#304). It is not: it is a random identity generated once per profile,
  and changing it changes who your atlas thinks drew your own roads and pins.
  It, and the marker that remembers the first-run tip, now live in a `state`
  folder beside them.
- Your existing identity is carried across on the first start, and the old file
  is only removed once the new one has been written and read back. If anything
  goes wrong the old file is left exactly where it is and the log says why, so
  the next start tries again.
- **The first-run tip is shown before it is remembered.** It was being marked as
  shown on a pass that can run before your character exists, so on some starts
  nobody ever saw it.

Known

- Whether every mod manager stops listing these files has not been checked
  against Gale itself. If yours still shows them, please say so on #304.


## 1.2.1 - Hulgi stops walking into the same wall

Fixed

- **He no longer walks into the same wall again and again.** When something
  stopped him on his way to a new spot, he sat down where he got to and left
  *that spot* alone for a minute - but only that one spot. The next look chose
  the spot beside it, the route went through the same gap, and he walked into
  the same wall again: six times in a minute, behind one fire, in testing. Now he
  remembers several walks at once, and above all the place that stopped him and
  the way he was going. Every spot behind that gap is left alone, and he takes a
  way round, or settles somewhere else good, instead. His short idle strolls
  keep clear of it too. If the same place stops him again later, he leaves it
  alone for longer: a minute, then two, then four, at most five. A wall he
  cannot see past costs a bump now and then, not one every few seconds, and he
  still tries again in case you took it down. He is still never teleported.
  `cc_companion placement` lists what he is leaving alone, and `cc_companion
  summon` gives him a fresh start.

Everything else is unchanged from 1.2.0.

## 1.2.0 - Meet Hulgi

**Meet Hulgi**, a drowned cartographer who never made it home and would rather
you did. A broken compass near where you wake up introduces him, and from then
on he lives at your camp: by the fire nearest your bed, having a drink now and then,
under a roof at night, asleep in a spare bed if you have one. He gets up, walks
over and sits back down the moment any of that changes, talks over his head when
you speak to him, and only ever goes through the doors you open to him (look at
a door and press F8). He is local only - no other player sees him, he takes no
part in your world save, and he never blocks, fights or carries anything - and
every atlas feature from 1.0.4 is unchanged.

This is the first Thunderstore release with Hulgi. 1.1.0 and 1.1.1 below were
test builds of him that never reached Thunderstore; everything they list is part
of this release too. What changed since those builds:

Added

- **"Hulgi joined your crew."** The first time he appears after you meet him,
  you are told - once per character per world.
- **`cc_companion reset [quest|bed|day|all]`** to play the introduction again:
  `quest` brings the Broken Compass back and restarts his introduction, `bed`
  forgets your bed spawn point in this world (the bed stays; use it to claim it
  again), `day` returns the world to the morning of day 1, and `all` does all
  three. Your map tools stay unlocked through every one of them.
- **He talks like an NPC, not like a notification.** His lines appear over his
  head, the same bubble the trader uses, instead of across the middle of your
  screen. Nothing networked: the bubble is drawn locally on your own client, no
  creature is spawned, and the line does not go into your chat log.
- **He has common sense about where to be.** He likes a fire and a drink. By
  day he goes to the burning fire nearest your bed, inside or out, and takes a
  seat by it if there is one, or sits on the ground facing it. At night, or in
  the rain, he goes under a roof - a fire under a roof first - and stays by the
  fire outside only when there is no roof he can get to. If there is a bed
  nobody has claimed, he sleeps in it until morning; he never touches a claimed
  bed, and gets up if somebody claims it. He looks again the moment any of that
  changes: a fire lit or broken, a bed built or claimed, a door opened to him,
  nightfall, morning.
- **Doors are closed to companions unless you open them.** Every door starts
  "no companions". Look at a door and press **F8** to let them use it - its
  prompt says so - and press it again to stop them. A door they may use, they
  use both ways: in to a seat by your hearth, out to the fire, opening it and
  closing it behind them. A door they may not use they never open or walk
  through, open or shut, so a companion whose bed is in a closed house spends
  the day by the fire outside it. The choice is remembered per world, on your
  computer only, never in the world. `Companions/DoorAccess = AllDoors` (or
  "Companions may use every door" in Settings) opens every door you could open
  yourself; `cc_companion doors` lists the doors near you and `cc_companion
  doors clear` closes them all again.
- **He moves around his camp.** He sits most of the time, gets up occasionally,
  walks somewhere nearby - around the fire, when he sits by one - stands looking
  around, and goes back to where he belongs. He does not wander in the dark or
  the rain, while you are talking to him, or off a seat you built him. He never
  leaves the area around your home point, never blocks anything and never
  touches the world - turn it off with `Companions/Wander` if you prefer him
  still.
- **He walks to a better spot instead of appearing there.** When somewhere
  better to sit turns up nearby - a fire lit, a bench built, your bed claimed a
  short way off - he gets up and walks over, around buildings and through
  doorways. He gets up first and then walks, and at the other end he sits down
  as he arrives: off a bench onto the ground in front of it, out of bed onto the
  floor beside it, and back again, moving while the animation plays rather
  than sliding away mid-stand or snapping into place. If something stops him on
  the way he tries another way once, and if that fails too he sits down where
  he got to and tries again later - he is never simply put there. Only his
  first appearance, or a move of more than 30 m, places him directly.
- **He notices a change to his camp within about a second.** Break the campfire
  and build one somewhere else, and he heads for the new one straight away. It
  stays cheap: one small check a second of the fires, seats and doors around
  him, and the full look for a better spot runs only when one of them changed,
  with a half-minute look as a safety net.
- **He opens a door he is allowed to use, and closes it behind him.** He walks
  to it, opens it, steps through and closes it after himself; a door that was
  already open he leaves as he found it. Only a door you let companions use and
  could open yourself - never a locked door, and never inside a guard stone's
  area you have no access to - and nothing else in the world is ever changed.
- **He has a drink now and then.** Every few minutes a vanilla tankard appears
  in his hand and he drinks from it, sitting where he is. Now and then he
  raises a toast first - turning to you if you are near - getting up for it and
  sitting back down afterwards; from a seat he steps off it to toast, unless
  something is in the way, in which case he just drinks. The mug is only drawn
  in his hand while he drinks: it is not an item, and nobody's inventory is
  touched.
- **The Broken Compass sparkles** with the game's own item twinkle, and it lies
  just outside the stone circle rather than in the middle of it.
- **`cc_companion summon`** sits him on the ground in front of you, to watch him
  find his way back to the best spot he can reach; **`cc_companion drink
  [toast|plain]`** has him take a drink now instead of waiting for one; and
  **`cc_companion placement`** explains where he sits and why - every spot he
  considered and what ruled it out, and every wish on his list for your camp
  right now (fire, roof, bed, home), what each came to, and which he is
  satisfying.

Fixed

- **His hair is on his head.** It was cloth. The game simulates the braid, and
  a cloth component rebuilds itself against the skeleton it was saved with the
  moment it is switched on - which, for a piece we have just re-bound to the
  companion's own body, is a skeleton that no longer exists. Hair carries two of
  those components. Beards carry none, which is the whole reason the beard
  landed correctly for a week while the braid hung 0.99 m in the air. Measured
  in game: 0.26 m from his head against a tolerance of 0.45.
- **He takes a chair built after he has settled.** The placement plan was made
  once and revisited only when the home point moved or his seat was lost, so a
  bench built beside him stood empty until something else forced a rehome. He
  now looks again whenever a fire, seat or door near him changes, and every half
  minute besides, and moves only for a strictly better place - a warm spot by a
  fire over a cold seat, any seat over bare ground - never for an equally good
  one, so he does not drift around the camp.
- **Skinned pieces are bound the way the game binds them** - handed the body's
  bone array whole, rather than matched joint by joint on name. For Hulgi's
  hair the two happen to produce the same answer, so this fixed nothing on its
  own; it is here because Unity skins by bone *index*, the game's own contract
  is the array, and a piece whose bones are ordered differently from the body's
  would otherwise be drawn somewhere else entirely with nothing to warn you.
- **Building his body no longer wakes the game's own character script.** His
  figure was switched on while one of the source character's scripts was still
  inside it, and that script expects a live character - which a local-only
  companion deliberately is not. It failed on every placement, wrote an error
  into your log, and then sat half-built inside the game's own update loop for
  the rest of the session. His body is now assembled switched off, and the
  game's own scripts are taken out of it before the light goes on. Nothing you
  can see changes: he looks, sits, walks, dresses and speaks exactly as before.
- **Talking to him is not a reach.** Pressing Use on Hulgi made your character
  raise an arm, the same reach you use to open a chest. The game's own trader
  and raven don't do that when you talk to them, and neither does he now. The
  conversation itself is unchanged.
- **His prompt shows your key, and so does the compass's.** Hovering over him,
  or over the Broken Compass, showed the literal text `$KEY_Use` instead of the
  key you press. Both now show your actual binding - a remapped key, or your
  controller's button while you are playing on one.
- **He can live in the shelter you build him.** With his home on a bed inside a
  roofed shelter on a wooden floor - about the most ordinary camp there is - he
  never appeared at all: every spot near the bed was refused. Two mistakes did
  it. His footing was read from the top of whatever was highest, so under a
  roof he was placed on the roof; and the floor he would stand on counted as
  something in his way. Both are fixed, for placing him and for walking him, so
  he can also now actually get in out of the rain.
- **He doesn't walk through walls, and he finds his way around.** He is held by
  the world the way you are - walls, posts, closed doors, rocks and trees - and
  uses the game's own pathfinding to walk around buildings and through open
  doorways, including in out of the rain. If something appears in his way
  mid-stroll, he stops instead of passing through it. You can still walk
  straight through him.
- **He no longer gets up every few seconds at night.** In a camp with no roof in
  reach he would get up to look for shelter, sit down in the open, and get up
  again about every fifteen seconds all night. After one look finds nowhere dry
  he now spends the evening like any other: he stays on a seat you built him,
  potters about at his usual pace, and still finds a roof if you build one near
  him.

Added

- `cc_companion pose <stand|ground|seat>` poses him where he sits so his
  clothing can be looked at in more than one shape - a garment bound correctly
  and one that merely lines up in a single frozen pose look identical until
  something moves. Local and not saved; he returns to his own idle when he is
  next rebuilt.
- `cc_companion status` now reports how far each attached piece is from where it
  belongs, so "it fits" can be told from "it fits by four centimetres".

## 1.1.1 - Hulgi, played (test build, not released on Thunderstore)

1.1.0 was written and tested without anybody having played it. This is what
happened when somebody did, on Valheim 1.0.12.

Fixed

- **Every console command was missing.** `cc_roads`, `cc_pins`, `cc_atlas`,
  `cc_routes`, `cc_survey`, `cc_sync` and `cc_companion` all failed to register
  on Valheim 1.0.x and answered "is not a recognized command". That included
  `cc_companion toolsonly on`, which is the promised way to reach the map tools
  without ever finding the compass. Commands are now added to the game's own
  command table directly, matched by shape so a future signature change does not
  silently remove them again, and what the console accepted is written to the
  log at startup.
- **A brand new player was treated as an existing one.** The mod writes its own
  starter `survey-rules.tsv` during startup, and the check for prior use then
  found that file and concluded you had played before — so the introduction's
  gate never engaged for anybody, on any installation. This build's own
  bookkeeping no longer counts as evidence of a history. Genuine prior data,
  saved views and translation overrides still do, so nobody upgrading loses
  anything.
- **He never actually sat down.** Sitting in Valheim is an animator parameter,
  not an animation name, and the code was looking for the name. Both the ground
  sit and seating were affected: the ground idle is now the game's own
  `emote_sit`, and a free chair is used through the chair's own attachment
  point, heading and sitting animation. He never claims a seat — the game's
  occupancy test only sees players — and he gives one up the moment it is
  destroyed or somebody sits in it. Walking out of range of your own camp does
  not move him.
- **He was not dressed.** The extracted model wears whatever the game's own
  equipment system puts on it, and that system cannot run for a local-only
  figure - so he stood in his underclothes. He now wears a vanilla rag tunic and
  leather pants, put on the way the game puts armour on, with the body's own
  chest and leg textures. It is clothing and nothing else: no item exists, none
  is taken from anywhere, and no armour value is read. `Companions/ChestGarment`
  and `LegsGarment` name something else.
- **His appearance was the presets the design asked for, not the ones you
  chose.** Hair and beard are now looked up by the exact label on the
  character-creation screen — Long Braid and Handlebar — against this build's
  own customization list, and his colour goes through the game's own conversion
  from the sliders rather than a hard-coded value. `Companions/HairPreset` and
  `Companions/BeardPreset` override either, by label or prefab id.
- **A damaged companion entry was reported every session, forever.** A row this
  build carried last time was counted again as damage found this time. It is now
  carried without being re-counted, and nothing is dropped.
- `cc_companion where` says where he actually is.

Known, on this build

- **Hulgi has no hair.** Long Braid renders about a metre from his head on
  1.0.12, so it is removed rather than left floating; his beard is correct. The
  log says so when it happens. Tracked as issue #305.
- **The tunic and trousers have not been seen on him.** They are implemented
  and unit-tested, and the in-game pass for them was stopped rather than run.
- **Seating on furniture has not been seen working.** The mechanism is in and
  tested, but no chair happened to fall where he settles during testing, and a
  seat built after he has sat down is not noticed until something else moves
  him. Tracked as issue #306.
- **Live multiplayer testing of sailing Route Follow** remains outstanding from
  1.0.4.

## 1.1.0 - Hulgi, and the Broken Compass (test build, not released on Thunderstore)

Added

- **Hulgi.** A broken compass lies near where you wake up. Examine it and a drowned cartographer introduces himself over four short pages; welcome him, or take **Just the tools, thanks** and skip the story. Either finishes the introduction and unlocks the atlas tools permanently.
- **He lives at your home point.** Near your claimed bed, or the world's starting point if you have not claimed one. Replace the bed and he moves with it; destroy it and he returns to the start. Travelling away from home does **not** move him: a bed the game cannot currently see is still your bed.
- **He talks when spoken to.** Over thirty short lines about his cat, the weather and travelling carefully. Unprompted chatter is off by default and can be turned on in `Companions/AmbientChatter`.
- **He knows only where you have been.** Place-specific remarks are gated behind biomes *this character* has visited, read from the local character only. He can never mention somewhere you have not been, and he never names a boss, a weakness or an item.
- **A Companions section on the Settings panel**, plus `cc_companion` (status, toolsonly, story, show, where, appearance, path).

Nothing about him can cost you anything

- **He is local only.** No `ZNetView`, no networked identity, no part in your world save, no collision, no combat, no loot, no storage. No other player can see him, modded or not.
- **Existing players keep everything on upgrade.** Any roads, pins, routes, survey rules or saved views from *any* world count as prior use and unlock the tools immediately. Unreadable or ambiguous data grants access too, and the grant is written down so it cannot be re-derived away later. The only locked state is a genuinely new character in a world the mod could identify.
- **You never have to find him.** Settings is never gated, and `cc_companion toolsonly on` works from the console before you have met anybody.
- **Recruitment is saved before the compass is removed.** If the save fails, the compass stays exactly where it is and says so, rather than leaving you with no compass and no companion.
- Hiding him, losing a bed, dying, or running a game build that cannot draw him changes what you see and nothing else. `Companions/CompanionsEnabled = false` removes the whole feature and leaves every map tool available.

Unchanged

- Every atlas, marker, route, survey, sharing and Route Follow behaviour from 1.0.4. Concerned Teamster is untouched and stays at its own version.

Still outstanding

- **In-game observation of Hulgi has not been performed and is not claimed.** `cc_companion status` and `cc_companion appearance` report what was actually resolved on your machine: which prefab his appearance came from, whether hovering reaches him, which idle pose was used, and which biome names this build records. Sitting on furniture is detected but deliberately not used until it has been seen to work.
- **Live multiplayer testing of sailing Route Follow** remains outstanding from 1.0.4.


## 1.0.4 - Optional sailing Route Follow

- **Sailing Route Follow (opt-in, OFF by default).** Mark a route as a sailing route with the new **Sailing** button in the Routes panel (or `cc_routes sailing on`), turn on `Routes/SailingAutoFollowEnabled`, take the helm of a raft, karve, longship or Drakkar with that route selected, and press Q. Cartographer then feeds the same bounded rudder input a player supplies by holding the helm key, steering along the route and around its corners.
- **Your sails stay yours.** Sailing Route Follow never raises, lowers or reverses the sail, never touches wind, physics, speed, collision, damage or multiplayer authority, and never takes ownership of the ship. It writes exactly one thing: the rudder axis.
- **Everything cancels it, immediately.** Any helm input, any sail step, jump/attack/secondary/dodge, pressing Q again, leaving the helm, editing or deleting the route, going too far off it, reaching the end, a map or world change, death or teleport. If wind or an obstacle stops the ship making progress along the route for 12 seconds, it releases the helm rather than fighting. There is no tacking, no obstacle avoidance, no docking and no pathfinding.
- If the game ever changes the ship-control methods this relies on, all three hooks are removed together and the feature simply stays off for that session, leaving vanilla sailing untouched.
- **Live multiplayer testing of sailing Route Follow is still outstanding.** The automated coverage is deterministic and game-free; raft/karve/longship/Drakkar, player-hosted and dedicated-server runs remain owner-verified and are not claimed here.
- Marking a route as a sailing route is stored per route, in its own line of the route file. A route nobody marked keeps exactly the file format it already had, so existing routes and shared route files are unchanged. If you share a marked route with someone still on an older build, they get the whole route normally and simply do not see the sailing mark.
- A sailing route is not offered to walking Route Follow, and splitting a sailing route leaves both halves marked.

## 1.0.3 - Dungeon surveying fixed, plus optional walking Route Follow

Fixed

- **Survey now finds the Black Forest, Swamp and Mountain dungeon entrances.** Burial Chambers (including the half-buried ones), Troll Caves, Bear Caves, Sunken Crypts, Frost Caves and Hildir's crypt and cave are offered as pending observations when you walk up to them, exactly like berries and ore. Previously the survey only examined networked objects, and a Valheim dungeon entrance is not one: the game spawns it as a world *location*, so nothing carrying the dungeon's name ever existed for the survey to match. The scanner now also examines the world locations loaded around you. It still never reads the world location database, so nothing you have not visited is revealed, and dungeons still arrive as reviewable observations you accept or reject — never as automatic pins.
- **Bear Cave, Hildir's crypt, Hildir's cave and half-buried Burial Chambers now have starter rules.** These identities matched no shipped rule, so they could not have been offered even once the scan was fixed. An untouched starter `survey-rules.tsv` is upgraded in place on load; a file you edited is never modified.
- Mistlands and Ashlands entrances (infested mines, Dvergr town and boss entrances, charred fortresses) are **not** in the starter rules yet — add a pattern for them in the Survey panel if you want them, and see issue #260 for the tracked follow-up.
- Because the same change makes lore runestones visible as world locations, the existing `runestone*` starter rule can now also offer a **Points of interest** observation at a runestone site. It is still a review-before-pin observation, and the rule can be turned off in the Survey panel.
- The Survey panel's sweep line now reports networked objects and loaded locations separately, and says so plainly if a future game build stops exposing locations instead of silently missing dungeons again.

Added

- **Sailing Route Follow (opt-in, OFF by default).** Mark a route as a sailing route with the new **Sailing** button in the Routes panel (or `cc_routes sailing on`), turn on `Routes/SailingAutoFollowEnabled`, take the helm of a raft, karve, longship or Drakkar with that route selected, and press Q. Cartographer then feeds the same bounded rudder input a player supplies by holding the helm key, steering along the route and around its corners.
- **Your sails stay yours.** Sailing Route Follow never raises, lowers or reverses the sail, never touches wind, physics, speed, collision, damage or multiplayer authority, and never takes ownership of the ship. It writes exactly one thing: the rudder axis.
- **Everything cancels it, immediately.** Any helm input, any sail step, jump/attack/secondary/dodge, pressing Q again, leaving the helm, editing or deleting the route, going too far off it, reaching the end, a map or world change, death or teleport. If wind or an obstacle stops the ship making progress along the route for 12 seconds, it releases the helm rather than fighting. There is no tacking, no obstacle avoidance, no docking and no pathfinding.
- If the game ever changes the ship-control methods this relies on, all three hooks are removed together and the feature simply stays off for that session, leaving vanilla sailing untouched.
- **Live multiplayer testing of sailing Route Follow is still outstanding.** The automated coverage is deterministic and game-free; raft/karve/longship/Drakkar, player-hosted and dedicated-server runs remain owner-verified and are not claimed here.
- Marking a route as a sailing route is stored per route, in its own line of the route file. A route nobody marked keeps exactly the file format it already had, so existing routes and shared route files are unchanged. If you share a marked route with someone still on 1.0.x, they get the whole route normally and simply do not see the sailing mark.
- A sailing route is not offered to walking Route Follow, and splitting a sailing route leaves both halves marked.
- Adds walking Route Follow as an explicit opt-in toggle in the Routes panel, OFF by default. Select a live route, close the map, stand near it, and press Q to begin vanilla autorun with bounded yaw steering.
- Preserves vanilla `Player.SetControls` ownership: while following, the adapter feeds the held autorun signal so vanilla refreshes movement from the bounded look direction. A second Q press or any cancellation feeds false and clears autorun.
- Cancels on manual movement/look, route edit/delete/archive/replacement, map or world lifecycle changes, death, teleport, ineligible movement, off-route travel, route end, or a 2.5-second no-route-progress timeout.
- Fails closed for ships, carts, mounts, doodad controllers, saturated cart scans, unsupported game signatures, and partial Harmony installation. It never changes speed, stamina, collision, fog, attacks, interactions, or multiplayer authority.
- Audited against installed Valheim 1.0.12 (Steam build 25253764, Unity 6000.0.75f1): `PlayerController.FixedUpdate` still passes the held AutoRun input to `Player.SetControls`, with `Player.SetMouseLook` and `Character.SetLookDir` unchanged from the recorded 1.0.7 baseline. Deterministic game-free controller/control-policy tests cover the boundary.
- Carries forward the Valheim 1.0.7 HUD-message compatibility and high-resolution UI corrections from 1.0.2. No persistence or atlas-data migration is required.
- Owner in-game smoke testing remains required and is intentionally not claimed by automated validation.

## 1.0.2 - Valheim 1.0.7 and high-DPI corrective upload

Thunderstore versions are immutable. Package 1.0.1 already exists, so this 1.0.2 upload carries the reviewed Valheim 1.0.7 HUD-message compatibility fix and high-resolution UI scaling correction forward without any Route Follow changes.

- Prevents the Valheim 1.0.7 `Character.Message` signature change from breaking Cartographer's update loop; cosmetic notifications resolve the live method safely and fail closed.
- Corrects Cartographer panel sizing on high-resolution displays through the shared UI scale behavior included in the reviewed safe source line.
- Contains no dependency, persistence-format, or atlas-data migration changes.

## 1.0.1 - V1.0 patch release

V1.0 is released; this is not a beta. The package uses 1.0.1 because 1.0.0 has already been uploaded to Thunderstore.

- Preserves the Valheim 1.0 Character.Message compatibility fix.
- Hardens the adapter so binding initialization failures and unsupported method signatures disable cosmetic notifications safely.
- Updates the package details to V1.0 and corrects the road-capture description.
- Retains the technical explanation below so other mod authors can diagnose the optional-argument binary compatibility break.
- Adds ten focused adapter compatibility tests. No dependency or atlas data-format changes.

## 1.0.0

**Concerned Cartographer v1.0 - the living atlas, with the Valheim 1.0 compatibility fix.**

- Fixed the `MissingMethodException` reported on Valheim 1.0.7 / Unity 6000.0.75f1 when Cartographer tried to show a vanilla HUD message. The old binding could prevent the containing runtime update method from running, affecting much more than the notification itself.
- Routed all Cartographer HUD-message calls through the version-tolerant `VanillaMessage` adapter. It resolves the running game's method and supplies its trailing defaults instead of compiling a call to the removed four-argument overload. Binding initialization is guarded and unsupported signatures fail closed. Unavailable messaging logs one startup warning; invocation failure drops the cosmetic notification.
- Promoted package, plugin, and assembly metadata to 1.0.0 and updated the package details from public beta to v1.
- Corrected the README's road-capture description: only successful local-player Pathen/Paved construction creates road data. Walking existing paint does not create roads.
- No dependency or atlas data-format changes in this release. Keep your existing configuration and Cartographer sidecar files when upgrading.

### For other mod authors: why an optional argument broke existing DLLs

The former method was `Character.Message(MessageHud.MessageType, string, int, UnityEngine.Sprite)`. The affected game build adds a fifth argument, `bool log = false`. A source call such as `player.Message(type, text)` still compiles against the new assemblies, but C# embeds omitted optional arguments at compile time. An already-built DLL still references the old four-parameter signature, which no longer exists.

On Mono the failure can occur while the containing method is JIT-compiled, before an in-method try/catch can run. Catching exceptions around the old direct call is therefore insufficient protection for the update path.

Rebuild against the current game assemblies and inspect the final DLL's member references. For compatibility across known signatures, put the game call behind a narrow runtime adapter: check the leading parameter types, reject unsupported signatures, supply trailing defaults, and keep optional HUD failures from disabling gameplay. Cartographer's implementation is in `src/ConcernedCartographer/Runtime/VanillaMessage.cs`. This fix addresses this particular method change; it is not proof that every game API or older/newer game build is compatible.

## 0.10.1 (Public Beta)

Storefront and documentation refresh only: the corrected Thunderstore thumbnail and a new in-game screenshot gallery in the package README (images are GitHub-hosted, not packaged). No gameplay, dependency, privacy, synchronization, or data-format changes — the plugin differs from 0.10.0 only in its version metadata.

## 0.10.0 (Public Beta)

**The Stable Living Atlas — public beta.** The roads your Vikings actually build become a durable, searchable, shareable map — and everything on it can be trusted. This is the feature-complete candidate for the 1.0.0 release, published as a beta for wider testing; upgrading from this beta to 1.0.0 will be automatic and lossless, like every Concerned Cartographer upgrade.

Relog persistence root fix and hardening (RC15):

- **Custom markers can no longer be falsely "deleted" by a relog.** The real story behind markers reverting to vanilla icons (Camp→Fire, Travel→Portal): the game rebuilds its whole pin list while loading your character's map data, and the mod's vanilla-edit absorber mistook that rebuild for you deleting the pins in vanilla — writing them off as deleted while the save file's plain-vanilla copies stayed on screen. The lifecycle is fixed at its root: a missing rendering is NEVER treated as a deletion anymore. Only an explicit vanilla delete action (right-click / gamepad remove, captured at the game's own RemovePin entry point) during a stable, fully-bound map session writes a tombstone — exactly once, and still recoverable. A second reconcile now runs right after the game loads your saved map, so every living cc:* marker regains exactly one rendering wearing its Concerned Cartographer art; the vanilla fallback icon remains what uninstall/downgrade shows, never what the mod shows. If any other reconstruction path ever drops renderings, the absorber now repairs by re-linking instead of deleting.
- **Full map redraws survive teardown races** (the RC13 crash report CONCERNED-CARTOGRAPHER-2 family): the road and route full-texture redraws re-verify their live textures immediately before writing pixels; a map teardown mid-redraw now resets the overlay handles, logs one privacy-safe warning, and retries on the next map session instead of throwing a reportable exception.
- **Privacy-safe lifecycle diagnostics** for support bundles: the log now records the exact build (version+commit), numbered map-session transitions (map available / map data loaded / world unloaded), aggregate pin-reconcile results (linked/added/removed/sprite-rebind counts) with the cause of any tombstone, and overlay resolve/reset/redraw state with texture liveness — never world, character, player, or server names or IDs, coordinates, pin or route contents, paths, or IPs. Verbose success traces stay behind `Diagnostics/DebugLogging` (default off).
- **Every log line and the support report passed a privacy audit**: a full sweep of the mod's log output removed the identifiers older lines still carried — the world UID is gone from the road/pin/route persistence and "Road atlas ready" lines, file paths (which embed machine usernames) are gone from every backup/migration/error line, player names are gone from the sync share/apply lines, suggested names are gone from the Quick Pin line, positions are gone from the terrain-classification and reconciliation lines, and the `cc_roads align` probe tables (which contain live positions) now print to the console only, with a coordinate-free verdict in the log. Exception text bound for the log is scrubbed through the same tested sanitizer as crash reports, so an I/O error can no longer echo your profile path. The `cc_atlas support` report no longer contains a `world-uid` line at all — versions, settings, row counts, sizes, and backup count only, with every line scrubbed as defense in depth — and regression tests now prove both properties.

Final smoke fixes (RC14):

- **Custom markers survive relog**: cc:* markers (road, harbor, fishing, objective, and the rest) keep their Concerned Cartographer art after logging out and back in, instead of degrading to vanilla Dots. The marker data always survived — the session rebind did not: one teardown-frame failure could silently disable the pin adapter for the rest of the game process. Session boundaries now clear that state, the sprite rebind decision is an explicitly tested rule, and clusters dominated by a cc:* marker wear its art too. Genuine vanilla pins are never repainted.
- **Roads survive relog on the minimap**: roads from previous sessions render again on the minimap (and the fallback texture view) after relogging. The road atlas always loaded correctly — the renderer painted it into the PREVIOUS map's destroyed overlay because its cached overlay handles outlived the session. Handles are now liveness-checked and re-resolve against the live map, and the large-map vector layer's fail-soft disable no longer leaks across sessions either. Dirt/Paved identity, layer toggles, and the road-source-authority rules are untouched.
- **The Atlas drawer remembers where you put it**: the drawer reopens at the position you dragged it to — across map opens, relogs, and restarts. Restored positions are clamped fully on-screen for the current resolution and UI scale, so an old coordinate can never strand the panel; if nothing was ever dragged, the default right-edge dock behaves exactly as before. (Relatedly, side panels no longer lose a non-default UI scale after a relog.)
- **Quick Pin owns its input**: while the toolbar's armed Quick Pin is waiting for your click, that click no longer swings your weapon, and Escape now only cancels Quick Pin — it no longer also opens the pause menu on the same press. The suppression is narrowly scoped to the armed interaction (plus the press's own frame) and releases immediately on capture, cancel, world switch, disable, or uninstall. Typing-safety behavior is unchanged.
- **Fixed a crash-report NullReferenceException during pin updates** (Sentry CONCERNED-CARTOGRAPHER-2): pin sync and display updates ran on login/logout teardown frames when no map exists, threw, and disabled pin management for the rest of the process — the same latch behind the marker-relog bug. All pin write paths are now lifecycle-guarded no-ops without a live map, and the next map-open reconcile repairs every rendering.

Final beta polish (RC13):

- **Softer large-map road ink**: the high-precision Dirt/Paved road lines on the large map now wear a gently feathered edge, matching the minimap's softer presentation the way the map's hand-drawn style intends — same centerline, same perceived width, same colors, zoom-stable, no extra rendering cost. Routes intentionally stay crisp (they are drawn plans, not terrain ink).
- **Faster palette scrolling**: the mouse wheel moves the [Markers] palette list about three times as far per notch — still smooth, still bounded, and the map underneath still never zooms.
- **The last orphaned map decoration is gone**: the empty vanilla backplate that lingered at the bottom-right of the large map after its controls were replaced is now hidden with the rest of the rail — only ever shown/hidden, never destroyed, and restored exactly under `Map/ShowVanillaMapControls`, a conflicting pin manager, any CC UI failure, disable, or uninstall. The bottom control tips are untouched.
- **Markers open with the map**: the [Markers] palette now opens automatically as the starting side panel on every fresh large-map open (when the enhanced palette is active). Close it or switch panels and it stays out of your way for the rest of that map visit; the next map open starts fresh. Disabled palette, conflicting pin managers, and fallback cases are respected — nothing auto-opens then.

Highlights across the line (developed as the internal 1.0 release candidates):

- **Roads map themselves as you build them**: every successful Pathen/Paved action inks the map instantly — and nothing else ever does — with ghost-free reconciliation and a self-compacting atlas.
- **Pins with memory**: adopt your vanilla pins, edit everything in place, batch, merge, undo — durable identities, recoverable deletes, uninstall-safe by construction.
- **A readable map at any scale**: the Atlas Drawer, real search, saved views, and lossless clustering.
- **Routes that follow your roads**: freehand or waypoints with road-aware routing, measures, and travel-time estimates.
- **Collaboration you can trust**: explicit sharing, preview-before-apply, honest conflicts, and deletions that can never resurrect.
- **For every Viking**: NoMap table mode, controller path, translations, UI scaling, high contrast, backups, and a sanitized support report.
- **Pre-release security audit**: the sync receive path was adversarially audited and hardened — bounded decompression, sanity bounds on every parsed field, deletion names in the sync preview, and sanitized author labels.
- **Release-candidate smoke fixes**: adopting a vanilla pin can no longer trap map/game input (the workbench now provably balances Jötunn's global input block and fail-closes on map close, logout, and shutdown); the Pin Workbench uses a padded two-column layout that keeps every label inside the panel at all UI scales; and a `cc_roads align` diagnostic verifies road-overlay/map alignment against the live game.
- **Owner-feedback pass (RC12)**: paved roads now wear a light stone-gray ink that always reads clearly LIGHTER than dirt at the same width and style (high contrast keeps near-black dirt / near-white paved). The Routes panel list mirrors the route table live — drawing, erasing, deleting, splitting, merging, restoring, console edits, and sync all update the visible list the same moment, erasing the last of a route's ink removes the route entirely (undoable) instead of leaving a ghost row, and stale entries can never accumulate. The dotted route style can no longer stall or freeze the game on any route, however long or oddly stored: the shared dash/dot walkers are structurally bounded (integer-counted stamps, real per-route budgets, non-finite geometry skipped) and the route texture reuses one pixel buffer across redraws so repeated style changes stop causing memory spikes. The Survey panel layout was rebuilt on an exact vertical-band system — header, note, status, rows, bulk actions, output, and Close each own their space at every UI scale, with status text kept within its band. And the two marker-creation flows are now guaranteed: naming a palette marker and pressing Enter always leaves exactly one visible managed marker (even if the game replaces or drops the pin object at naming close, the committed marker is adopted or recreated — only a real cancel creates nothing), and accepting a survey observation immediately creates exactly one visible managed marker while the observation leaves Pending — a marker you just created is temporarily exempt from cluster folding and search filters so it can never vanish the frame it is born, and Survey rows act on exactly the entry shown even while background sweeps reshuffle the list.
- **Smoke-fix pass (RC11)**: toggling **Map Overlays** checkboxes can never double-render or strand stale road/route ink — one visibility rule now writes the overlay state unconditionally (the panel's own click handler used to race a cached write). Roads render at every zoom: the vector layer's rebake decisions moved into a deterministic, sweep-tested scheduler, a bake that cannot project retries within a quarter second instead of going invisible, and the layer's graphics carry real rects so no clipper can cull them in pan/zoom bands. The mouse wheel over any Concerned Cartographer panel, list, or field scrolls that UI only — the map underneath no longer zooms. Free Draw creates a route only once a stroke has actually travelled (no more one-click fragment routes), the route list keeps a stable order with a "more not shown" count, **Snap to roads** lives in the bottom control area beside a confirmed **Clear all routes**, and the panel's status lines can no longer overlap the list or color swatches. Replaced vanilla map chrome is now hidden per button group with its backplate and decor (validated, logged, pixel-perfect restore). The survey grew up: **Reject is durable** — rejected observations move to a persistent Rejected view (restore or accept them later; they never re-offer on their own), repeated sweeps can never duplicate the same physical object, and survey **rules are edited entirely in the Survey panel** (view, enable/disable, delete, add — `survey-rules.tsv` remains the shareable import/export). Names are humanized everywhere: "Raspberry Bush", "Silver Vein", "Treasure Chest Meadows" — never "Raspberrybush" — on survey rows, map labels, and quick pins, and the survey notice points at the [Survey] panel, not a console command.
- **Road authority by what you actually did (RC10)**: the road rule is now enforced by the **identity of the terrain action itself** — the game's own placed-piece identity for the hoe's Pathen and Paved road actions — never by settings flags or by what the paint looks like (in the live game, "Level ground" and "Pathen" lay down nearly identical dirt-painting operations; only identity can tell them apart, and an always-on rate-limited log line records how every terrain action was classified). Level ground, Raise ground, Cultivate, digging, and unknown/modded terrain operations create **zero** road data and erase the recorded road ink they cover — which is also how any road ink polluted by the earlier misclassification is cleaned up: level or re-pave over it once (or `cc_roads delete` near it), and it is gone for good. Your explicit Pathen/Paved roads are never touched.
- **One map language for roads and routes (RC10)**: large-map road ink is twice as wide and routes now render through the very same screen-space vector system — per-route colors, solid/dashed/dotted with a geometric cadence measured in screen pixels, stable while zooming and panning, with the route texture overlay serving the minimap and fallback exactly like roads (no doubled lines anywhere). Dotted routes read as a tight, continuous bead line. Jötunn's overlay button now reads **Map Overlays** (restored on uninstall), and its checkboxes genuinely show/hide each CC layer in both presentations — a checkbox always tells the truth about the layer, and clicks mirror into the Atlas Drawer settings.
- **Survey that feels immediate, markers that feel right (RC10)**: survey scanning is continuous on a bounded per-frame budget (nearby matches surface within about a second; the top-left notice is coalesced to one per ~10 s), the starter rules broaden to dandelions, flint, wild seeds, guck sacks, beehives, frost caves and lore runestones (an untouched older starter file upgrades in place; edited files are never touched), and the Survey panel's status block got room to breathe. The marker palette is draggable and scrollable with collapsible category sections — every marker reachable, nothing capped away — and a palette placement wears its chosen cc:* icon from the first frame of the naming flow, never a temporary or permanent vanilla Dot. All 12 cc:* icons were regenerated toward Valheim's hand-drawn map-icon look (soft edges, gentle wobble, ink texture) with identical silhouettes and stable IDs.
- **Typing is typing, chrome is honest (RC10)**: while any Concerned Cartographer text field is focused, keystrokes only type — no Valheim actions, no mod hotkeys — and the first Escape just ends the typing; normal input returns the moment the field blurs, and nothing is intercepted when no field is focused. Quick Pin names come from the localized hover name or the real prefab identity — internal names like "Collider (1)" can never become a pin name ("Marked object" is the honest fallback). The Share panel sits on a clean two-column grid. Hiding the replaced vanilla map rail now hides its whole backplate (validated, reversible, restored on fallback/disable/uninstall), with the bottom control tips untouched. Routes are explicitly framed in the panel for what they are in v1: manual planning/navigation overlays that never move your character.
- **Roads you built, and only roads you built (RC8)**: the strict v1 road rule — road atlas data is created exclusively by your own successful **Pathen ⇒ dirt** and **Paved ⇒ paved** construction actions. Walking existing paint, world-generated dirt (spawn circles, sacrificial stones), and Level Ground side-effect paint never become roads; passive traversal/chunk-recovery capture is disabled, and existing atlases migrate automatically (passive-only strokes are cleaned with a one-time `.pre-authority.bak` backup; your explicitly built roads are preserved untouched). Level/Raise/Cultivate/Reset erase the road ink they cover; a later deliberate Pathen/Paved always wins. On the large map, the high-precision vector ink is now the **only** road presentation while it is healthy (the texture overlay stays on the minimap and returns automatically as a fallback) — no more doubled road lines.
- **A real marker set, a survey that works, and a UI polish pass (RC8)**: 12 distinct Concerned Cartographer marker icons (road/junction, harbor, resource, danger, farm, mine, fishing, camp, travel, trader, dungeon, objective) with stable IDs — saves stay vanilla-safe and unknown IDs still fall back cleanly. Survey Rules ship useful bounded starter rules (gatherables, ore deposits, dungeon entrances, boss runestones) and the [Survey] panel shows scanner/rules/last-scan/pending status live with a Scan now button; accepted observations appear on the map immediately, as do Quick Pins. Routes: panels are draggable, the pointer over any CC panel never adds route points, Free Draw strokes end on LMB release (each stroke its own route), and dashed/dotted styles pattern by real distance at every zoom. The toolbar derives its height from the vanilla control-tips layout instead of a fixed offset, the Settings panel reports into a dedicated middle status block, the Atlas Drawer uses an explicit no-overlap grid, and the Pin Workbench no longer shows controls for size/color (stored metadata without v1 behavior). `cc_roads align live` now tells you exactly how to get a full A/B/C/D check.
- **Optional, privacy-first crash reporting (RC5)**: on your first large-map open, Concerned Cartographer asks once whether to send anonymous crash reports when it hits an internal error — off by default, never asked again once answered, changeable anytime under **CC Atlas → Privacy**. Reports carry only allowlisted technical fields (versions, subsystem, exception type, sanitized stack); identity, world/character names, seeds, coordinates, pins/routes, server details, saves, and logs are never sent, with automated redaction tests over the exact outgoing payload and provider-side IP scrubbing. No gameplay analytics of any kind. Full policy: PRIVACY.md. Support routing is now canonical: GitHub issues for bugs, **support@theconcernedcat.com** for security/privacy/sensitive material.
- **The full-UI map surface (RC7)**: one compact toolbar — **[Atlas] [Markers] [Routes] [Survey] [Share] [Quick Pin] [Settings]** — puts every feature behind a visible button, one side panel at a time, Escape always closes, and the vanilla right-side rail is replaced by default (reversibly: `Map/ShowVanillaMapControls`, automatic fallback on conflicts or failures; pin-type filters and visible-to-others live on in **[Atlas] → System Markers**, driven through vanilla state). Routes are drawn from the **[Routes]** panel with explicit modes — no modifier key, no map-drag fighting — and every route operation works from the panel on the selected route. Survey review, sharing (preview with deletion names), privacy, backups, the support bundle, and the road repair tools are all panels now. The package README was rewritten for v1 with a full shortcut-parity table.
- **High-precision large-map roads (RC7, DEF-v1.0-006)**: road ink on the large map is now zoom-stable vector geometry that sits exactly where the game itself projects the world — sub-texel precision at any zoom, so the player marker stays on the road line you are walking. The minimap keeps the classic texture overlay, `Map/HighPrecisionLargeMapRoads=false` restores the old behavior, and a new end-to-end `cc_roads align live` diagnostic answers the four alignment error classes separately.
- **The map is now button-first (RC4)**: an **[Atlas]** button (with tooltip) opens the Atlas Drawer; hovering any editable marker shows **Upgrade & Edit** (existing vanilla markers — position preserved, internally the same safe adoption) or **Edit Pin** (managed markers); and the new **Enhanced Pin Palette** replaces the five raw vanilla icon buttons with a searchable, previewed marker browser — pick a marker, double-click the map, and the pin is managed from birth with exactly one rendering. The vanilla selector returns instantly via `Pins/ShowVanillaPinPalette` (and automatically when a known conflicting pin manager is installed); death/boss/system pins, Cross Off, Remove, Ping, Visible-to-others, and uninstall safety are untouched. Status and Scope in the workbench became dropdown selects. Hotkeys (`L`, `P`, `F7`) remain as rebindable accelerators.
- **Second smoke-pass fixes (RC3)**: editing an adopted/managed pin now updates its single map rendering in place — renames and icon changes never leave a duplicate or orphan pin, in-session or after restart. Ground you **Level/Raise/Cultivate/Reset is remembered per world as explicitly-not-road**, so leveling a base never becomes road ink (walked or recovered, this session or later); a deliberate Pathen/Paved action always wins and re-inks normally. The Pin Workbench gained an icon picker with live sprite preview, category suggestions, and a size stepper — color stays raw hex at the bottom, honestly labeled metadata-only until pins can truly render it. A visible **CC Atlas** button and a contextual **P — Edit** hint make the panels discoverable without reading docs. The alignment diagnostic is smaller and quieter and prints one PASS/FAIL residual table; overlay alignment itself was verified in game (max residual ≤ 1 map texel) and its defect closed.

Upgrading from any earlier version is automatic and lossless.

## 0.9.0 internal hardening milestone

(An internal, never-published milestone that happened to share the 0.9.0 number — kept for the development record; the public beta above supersedes it.) Public beta hardening: no new features, everything sturdier.

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
