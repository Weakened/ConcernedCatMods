# Changelog

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
