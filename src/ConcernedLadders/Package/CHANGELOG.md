# Changelog

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
