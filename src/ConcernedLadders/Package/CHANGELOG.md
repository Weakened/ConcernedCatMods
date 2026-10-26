# Changelog

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
