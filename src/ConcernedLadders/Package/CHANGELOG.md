# Changelog

## 1.1.0

Three experimental additions for building basements, each behind its own
setting that is **off by default**. None of the three has been tried in game
yet, so leave them off. With all three off, the ladders, the climb and the
Climbing skill are the same as in 1.0.0, and nothing new appears in the hammer
or crafting menus. Every player and the host or server must use the same
version and the same values for these settings.

- Floor trapdoors (CL-004, #485), behind `Trapdoors` / `Enabled` (off by
  default). Adds nine trapdoor pieces to the hammer. A trapdoor's lid opens and
  closes with the door's own Use, over the matching door's swing time, and
  waits instead of moving into anyone or anything on or under it. A ladder
  aimed at a trapdoor's frame in Auto or Top hangs inside its opening; a
  climber can open and close the lid from the ladder without letting go,
  climbs out through an open lid, and is held below a closed or closing one.
  Turning the setting off again, or uninstalling, makes the game delete placed
  trapdoors, with no refund, the next time the world loads. The Wood Gate
  trapdoor's lid is a gate of boards and rails, so it no longer looks the same
  as the Wood trapdoor (#547). Not yet observed in game.
- Sloped cellar doors (CL-005, #486), behind `CellarDoors` / `Enabled` (off by
  default). Adds nine sloped 2 m cellar doors to the hammer, at twice the
  matching door's cost. Both leaves open and close together with the door's
  own Use, over the matching door's swing time, and wait instead of moving into
  a player, a creature, an item, a cart or another building piece. A ladder
  aimed at a cellar door's frame in Auto or Top hangs inside its opening; a
  climber can open and close the leaves from the ladder without letting go,
  climbs out over the front panel or down into the opening only while both
  leaves are open, and is held below a closed or moving pair. Ladders never
  treat a cellar door as a floor. Turning the setting off again, or
  uninstalling, makes the game delete placed cellar doors, with no refund, the
  next time the world loads. The Wood Gate cellar door's leaves are boards
  and rails, unlike the Wood cellar door's solid leaves (#547). Not yet
  observed in game.
- Iron Shovel and measured basement excavation (CL-006, #487), behind
  `Excavation` / `Enabled` (off by default). Adds the Iron Shovel, crafted at
  the Forge from 10 Iron and 4 Wood once you have found Iron (Iron Scrap alone
  does not unlock it) and repaired there. It digs one square basement hole per
  press, in two sizes: 2x2x2 m (a flat 2 x 2 m floor, 2 m deep) and 4x4x4 m (a
  flat 4 x 4 m floor, 4 m deep). The depth is measured from the highest ground
  at the hole's edge, so the floor is level on a slope. The walls cannot be
  vertical: each side slopes over one more metre, and that whole border is
  shown.
  - While the shovel is held, an outline drawn through the ground shows the
    floor, the depth at each corner, the sloping border and, if the floor
    would sit below water, the water line. It is green when a press would dig,
    amber when it would do nothing, and red when it would be refused. Text
    under the crosshair gives the size, what a press would do and its cost
    (or why not, and what to do about it), and the controls from your
    current key bindings.
  - Attack digs the size shown, where it is shown; Secondary Attack switches
    the size and never digs. Aiming, switching, menus, the inventory, chat and
    the console never dig. The ground you aim at must be within 5 m and be the
    first thing you see, so the shovel never digs through a wall, a floor or a
    rock; to dig under a floor, look through an opening in it. The square
    follows the world grid and does not rotate. No digging from a ladder.
  - A dig costs 20 stamina and 4 durability (2x2x2 m) or 40 stamina and 16
    durability (4x4x4 m), before the world's stamina and durability
    settings, charged once and only when the dig is placed. A
    refused dig, a hole that is already dug, or a cancelled press costs
    nothing. It is refused inside a ward that does not grant you access, in
    no-build areas, below the terrain's dig limit, and over rocks, trees, ore,
    plants, dropped items, carts or ships, which it never removes. Your own
    building pieces in the square are not protected: their support is the
    game's, so a piece standing on dug ground can fall.
  - The shovel has no attack and plays no animation yet, and it looks like a
    darker iron pickaxe for now. It raises no skill. A read-only `cl_excavate`
    console diagnostic plans a dig without digging.
  - Turning the setting off again, or uninstalling, removes the shovel from
    the game, and the game discards crafted shovels when their inventory or
    chest loads. The mod does not undo holes already dug. Not yet observed
    in game.

## 1.0.0

First public release.

- Vertical Wood, Copper and Iron Ladders: 2 m hammer pieces in the Building
  tab that stack into one continuous climb, with Auto, Bottom, Top, Left side
  and Right side snapping onto floors, walls and other ladders.
- An original climbing animation: each hand and foot takes its own rung going
  up and coming down.
- Get on from the ground, from a floor above, in midair, or out of the water.
  Step off at the top, onto a floor beside the ladder partway up, or at the
  bottom. Jump off, let go, or slide down.
- Climbing skill: earned per metre climbed, makes fast climbing and sliding
  cheaper, and unlocks the slide at level 20.
- Fixed: a climber who grabbed the ladder from the water showed the swimming
  pose under the climbing arms and legs. The game's swimming and falling
  animation states are now cleared while on the ladder.
- Multiplayer, dedicated servers and controllers are not tested yet. See the
  README for what uninstalling does to placed ladders and the skill.

The entries below are internal development candidates. None of them was
published.

## 0.2.7

Internal owner playtest candidate. Not a release.

- Climbing posture (owner-approved): the climber stands off the ladder with
  the hips and chest square to it, steps two rungs at a time with the support
  leg nearly straight, and the knees stay in line instead of folding to the
  chest or falling outward.
- Getting on and off is quick and clean (owner decision): mounting from the
  ground, catching in midair, stepping off at the top, onto an intermediate
  floor or at the bottom, and getting on from a floor above all take about a
  quarter to half a second, with the limbs simply taking or leaving the rungs.
- No squat near the top: climbing stops (or steps off onto a floor) about 1.3 m
  below the top, with both hands on the top rungs; getting on from a floor
  above ends in the same normal climbing stance.
- Grab a ladder while swimming, like climbing aboard a boat: Interact pulls
  you up out of the water onto the rungs.
- Renamed to Vertical Wood Ladder, Vertical Copper Ladder and Vertical Iron
  Ladder so they no longer share a name with the game's own Wood ladder.
  Placed ladders are unaffected.
- Mounting from a floor no longer refuses a step or two back from the edge;
  a jump that brushes a rung is still caught as a midair grab; top landings
  are found along a floor's edge, and small floors beside the ladder are found
  for the intermediate exit.
- Snapping (owner revision): Cycle Snap now offers Auto, Bottom, Top,
  **Left side** and **Right side**. Left side and Right side line the ladder's
  left or right rail up with the left or right corner of the floor edge or wall
  you aim at (left and right as you face the ladder's climbing side), standing
  on a wall's bottom edge or hanging below a floor edge like Auto. Auto, Top and
  Bottom still centre the ladder. Every mode sits the rails against the real
  face of the wall or floor edge, including wood walls whose snap points lie
  inside the wall.
- Rotate while snapped: the hammer's rotation now turns a ladder snapped to a
  floor or wall about the chosen anchor (the middle, or the left or right
  corner), up to 45 degrees either way, and never into the surface. Square to
  the building it faces straight out, as before. A turned section does not join
  the climbing run of the ladder below it. Ladders stacked on ladders always
  line up with the column.
- Copper and Iron Ladders have new artwork: slim metal rails and round rungs.
  Copper has rounded tube rails and a warmer colour; iron has sharp flat-bar
  rails and a cooler colour. The Wood Ladder is unchanged. Height, rung
  spacing, recipes, durability and placed ladders are unchanged.

## 0.2.6

Internal owner test candidate. Not a release.

- Climbing is slower and more natural (owner decision): 0.9 m/s, 1.2 m/s with
  Run (were 1.8 and 2.43). Sliding, stamina and the Climbing skill are
  unchanged.
- New climbing animation authored in Blender: hands and feet move one at a
  time on their own timing (a hand reaches, the opposite foot recovers knee
  first, then the other pair), knees point into the ladder, and the body rises
  more slowly while a foot comes up. Grabbing the ladder sets hands and feet in
  turn; a midair catch grabs with the hands first.
- Snapping redone (owner decision): Cycle Snap offers Auto, Bottom and Top.
  Aimed at a floor, the ladder centres on the floor edge nearest your aim,
  flush against it, hanging below so you climb up onto the floor (Auto, Top;
  Auto stands it on the edge when there is no room below) or standing on the
  edge (Bottom). Aimed at a wall, it centres on the wall face toward you,
  standing on the wall's bottom edge (Auto, Bottom) or hanging from its top
  edge (Top). Aimed at a ladder, it extends that column. Any hammer rotation.
- The two wall-corner snap modes are removed.

## 0.2.5

Internal owner test candidate. Not a release.

- Snapping: aim at a ladder to extend it; the new section lines up as one
  straight column above (Bottom) or below (Top), and Auto picks the nearer end
  without ever burying a section in the ground. A wall corner centres the
  ladder on a 2 m wall at the wall's own angle, facing you. Corner modes aimed
  at a ladder, and ends beyond your building reach, are refused (red ghost)
  instead of showing a displaced ghost. All the game's own placement checks
  still apply to the final position.
- New climbing animation: right hand with right foot, then left with left,
  every hand and foot on a real rung. Limbs cycle half as fast as 0.2.3 at the
  same climbing speed (a four-rung stride). Stopping settles every limb on the
  nearest rung; climbing down retraces the motion. Near a flush top the hands
  take the rails and the step onto the floor uses the game's own walk.
- With the hammer out only your right arm swings; the body, legs and left hand
  keep holding the ladder.
- Fixed: the game's own foot placement pulled the climber's feet to the
  ground under the ladder.

## 0.2.4

Internal development builds for in-game checks (several, one version number).
Superseded by 0.2.5.

## 0.2.3

Internal animation checkpoint, replacing 0.2.2 before any gameplay use.

- Fixed: 0.2.2 let go of every ladder the moment you mounted. It called a
  game method that is not accessible at runtime (found in game).

## 0.2.2

Internal animation checkpoint for the owner test lane. Not a release.

- New original climbing animation: hands and feet follow the rungs with your
  actual climbing, replacing the 0.2.x limb pose. It is a first prototype and
  still being fitted.
- Crouch while climbing drops straight off the ladder, with no hop and no
  stamina cost. Jump still hops off.
- You can mount, climb and build with the hammer equipped. While the hammer is
  out your right arm uses the game's own hammer animation; the rest of the body
  stays on the ladder.
- Building a new ladder section above you while climbing extends the climb
  without getting off. Removing the section you are holding lets you fall
  normally.
## 0.2.1

- Pressing Use partway up now steps off onto a landing beside the ladder. In
  0.2.0 the safe-dismount search looked only 0.75 m to the side, where a standing
  footprint still overlapped the rails, so a real side landing was refused.

## 0.2.0

Owner-test candidate implementing the owner revision of 25 October 2026.

- Grab from about 1.5 m, measured from the chest to the rungs (or, standing on a
  floor at the top, the step onto the rails); the prompt says "Move closer" when
  a press would be out of reach.
- Climb either face; the route stays on the face you approached. Grab while
  rising or falling; a refused grab leaves the fall untouched.
- Jump hops off upward and in the steered direction with the game's own jump and
  stamina; without stamina it simply lets go. Re-grabbing needs 0.4 s and a new
  press.
- Run climbs at 2.43 m/s for stamina; native Climbing skill; slide at level 20.
- Stepping onto floors at the top now also works to either side and around a lip.
- Refusals name the real reason; only another building piece is blamed as one.
- Wood Ladder, Copper Ladder and Iron Ladder names; readable Cycle Snap labels.
- Custom procedural climbing animation on the native rig.

## 0.1.1

- Owner-test candidate built against Valheim 1.0.20, BepInExPack 5.4.2351 and
  Jotunn 2.30.2; implements the 1.0.20 hover interface.
- Mounting works from flat ground, slopes, off-centre approaches and uneven
  ground; the bottom, top and side-landing exits complete and leave the player
  standing.
- Use while climbing asks for a safe dismount wherever the player is looking;
  with no safe landing the player stays on the ladder with a message.
- Wall corner snaps sit against the wall face instead of inside it.
- A push or other displacement the ladder did not make is released without
  overwriting that motion or the fall it started.
- An icon that fails to render keeps the fallback icon and is reported; the
  cloned pieces hold no references into the removed donor model.
- Development status: single-player only. Multiplayer, dedicated server and
  controller play are not yet verified.

## 0.1.0

- Initial independent Concerned Ladders source candidate.
- Original runtime-built vertical wood, copper and iron pieces, 2 m snaps and
  native building resource requirements.
- Explicit local-owner climbing with collision-aware movement and safe release
  policies; normal locomotion animation fallback.
- Dependency-only test-profile import and narrowly scoped deployment tooling.
- Development status: licensed build, single-player, controller, multiplayer,
  durability visuals and save/reload acceptance remain unverified.
