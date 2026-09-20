# Changelog

All notable changes to Concerned Foreman are documented here.
This project follows [Semantic Versioning](https://semver.org/).

## 0.1.0 - The worker actor spike, and ladders you can climb (unreleased)

The first code this product has ever had. **Not published, and not a
playable release**: it is the worker actor spike from CF-SET-002 (#279)
plus the ladder feature from CF-LAD-001..005 (#324), and **nothing in it
has been observed working in a game**. The version has deliberately not
moved: 0.1.0 has never shipped, so these are still its notes.

### Changed — Thorstein's identity comes from Concerned NPC (#380)

- Concerned Foreman now requires **Concerned NPC**, which ships as its own
  package. Thorstein's actor mode — what he is doing, and which order holds
  him — comes from that library's one arbiter instead of from a copy of the
  rule compiled into this mod. Without the package the mod says so at load
  rather than failing later.
- **What that buys a player.** While a collection order holds Thorstein,
  nothing else running in the same game may take his body: not another mod
  of ours, not a second runtime in this one. Before this, each mod kept its
  own private answer to "is he busy?", and none of them could see the
  others.
- **If the shared runtime ever refuses Thorstein, you are told which thing
  went wrong.** This is a new failure mode rather than a fixed bug — there was
  nothing to fix, because before this change the only answer available was
  "yes" or "he is busy". Now that a shared runtime can decline him at startup,
  every surface says so plainly: an order refused for that reason names
  Concerned NPC and says waiting will not help, and a stopped order says his
  identity was not accepted rather than blaming a body that is standing right
  in front of you.
- **While that is the case, he cannot be dismissed either.** `cf_worker
  despawn`, and the uninstall step that asks you to retire his body, both
  refuse — deliberately. A body nothing can account for is not destroyed on a
  guess, because his tools and gathered materials are inside it. Install or
  re-enable Concerned NPC and restart, and both work again.

### Unchanged on purpose

- **No durable name moved, and no migration code exists or ran.** His
  identity is still `foreman/thorstein`, his prefab is still
  `CF_SettlementWorker`, and his body still stores itself under
  `tcc.worker.key`, `tcc.worker.inventory` and `tcc.worker.revision`. A
  settlement folder from the previous build is read by exactly the same
  names, so an existing Thorstein keeps his issued tools and his
  designations.
- Concerned Foreman still registers its own worker prefab, from its own
  start, under its own name. A prefab registered late, or under a new name,
  would delete every saved worker on the next load, so that half is
  deliberately not handed over.
- The settlement record gains one new pause reason name. A record written by
  this build and read by an older one shows that pause as "no reason
  recorded"; nothing else about it changes, and a record written by an older
  build reads here exactly as it always did.

### Added — climbable ladders (#324)

- **Valheim's ladders are climbable instead of teleporting.** Walk into
  one, or press Use, and your character takes hold of it: forward climbs
  up, back climbs down, releasing stops, and you step off at the bottom,
  over the edge at the top, or jump away.
- **The game's own pieces, unchanged.** `wood_stepladder`, the grausten
  stone ladder, and anything else in the game built as a ladder that
  measures like one. No new piece, no new recipe, no new entry in the
  hammer, and nothing of this mod's written into a world: uninstall and
  every ladder you built is still standing, teleporting again.
- **The `Ladders` configuration section**: `Enabled`, `AutoMount`,
  `ClimbSpeed`, `StaminaCost`, `UseTeleport`, `NpcClimbing`. The defaults
  are the whole feature — a player who never opens the config file
  climbs ladders.
- **Off is off.** With `Ladders/Enabled = false` at startup, nothing is
  patched at all — not the player's motor, not the ladder's Use — and
  the game behaves as if Concerned Foreman were not installed.
  `UseTeleport = true` hands the Use key back to vanilla's teleport in
  every state, while walking into a ladder still climbs it.

### Known limits — ladders

- **Unproven.** The automated suite covers the decisions; it proves
  nothing about Valheim. No climb has been watched in a game.
- **Stacking is unobserved.** Whether two vanilla ladder pieces snap end
  to end into one tall run has not been tested. If they do not, each
  piece is its own climb and a tall run stops and re-grabs at each join.
  Whether a Concerned Foreman ladder piece is ever added is the owner's
  decision and has not been taken.
- **The climb pose is not an authored animation.** Valheim ships no climb
  clip and this mod ships no game art, so the pose is built from the
  game's own wall-running lean.
- **Multiplayer is untested**, and no settlement worker can climb:
  `NpcClimbing` is bound, off, and nothing reads it.
- **A ladder on a moving ship ends the climb** as soon as the ship moves,
  and **jumping off is a let-go, not a push.**

### Added — the settlement worker (#279)

- The **settlement runtime**, off by default. Turning it on is a separate,
  explicit choice; the product's building-diagnostics half never requires it.
- A **settlement worker** built on `BaseAI`, which walks to a designated
  point and stops, and does nothing else — no wandering, no alerting, no
  threat response, no combat, no loot.
- **Budgeted pathfinding.** At most one path request per 0.05 s tick and
  five attempts per goal, after which the goal is deferred with a stated
  reason rather than retried forever.
- **Honest refusals.** A goal that is unreachable, too far, hazardous,
  outside loaded ground, or outside this peer's authority is refused with a
  reason a player can read. An authority check that cannot be made is a
  refusal, never an assumption.
- The `cf_worker` console command (`status`, `spawn`, `goto <x> <z>`,
  `stop`, `despawn`), which exists so the above can actually be watched.

### Known limits — the settlement worker

- **No offscreen work.** The worker acts only in loaded ground, and says so.
- **Solo and local host only.** On a dedicated server the runtime refuses.
- One worker at a time. Recruitment, orders, harvesting, carrying and
  construction are later work and are not in this build.
