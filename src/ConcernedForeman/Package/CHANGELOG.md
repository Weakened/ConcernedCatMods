# Changelog

All notable changes to Concerned Foreman are documented here.
This project follows [Semantic Versioning](https://semver.org/).

## 0.1.0 - The worker actor spike, with an opt-in ladder climb (unreleased)

The first code this product has ever had. **Not published, and not a
playable release**: it is the worker actor spike from CF-SET-002 (#279)
plus the ladder feature from CF-LAD-001..005 (#324), and **nothing in it
has been observed working in a game**. The version has deliberately not
moved: 0.1.0 has never shipped, so these are still its notes.

### Added — small-tree felling (#282, owner decision 2026-10-31, D19)

- **Thorstein can fell small trees** (small beech and small fir) with his own axe,
  one hit per `cf_collect fell`. This works only inside a work area you chose
  from the map or marked as your harvest area: never in the default camp circle,
  never within 15 m of a player or anything you built, and never through a ward.
- **Off by default**, under `Collection/ThorsteinFellsSmallTrees`.
- **What falls is exactly what the game drops, and it stays on the ground.** He
  does not pick it up yet. His axe is not worn, because the game wears weapons
  only for players.
- **Never felled:** full-size trees, saplings you planted, berry bushes, and
  anything else.
- Not yet observed in game.

### Added — work areas from your map (#296, slice 3)

- **Thorstein can collect in a work area you drew in Concerned Cartographer.**
  Choose it with `cf_collect area <name>` or the collection panel's **Next map
  area** button. He works inside the shape you drew, not the circle around it.
- **A chosen area that cannot be used refuses the order and says why.** That
  covers an area that is deleted, too wide (more than 48 m from its middle),
  from another world, or that Cartographer cannot supply. He never quietly
  works in the harvest area or the camp circle instead. **Usual area** or
  `cf_collect area default` goes back to those.
- Cartographer is optional and detected while the game runs. Without it,
  nothing changes.
- **Cartographer shows the order on its map.** When an order runs in a map
  area, the Areas panel shows Thorstein, the progress and why the order
  stopped, and colours the area: green while working, red when it needs
  attention. Foreman publishes this read-only; nothing on the map can change
  an order.
- Not yet observed in game.

### Added — his own hair and beard while he works (#295 S6, owner decision 2026-10-29)

- **While Thorstein works, his worker body wears his Mullet and Mustache**, the same
  pieces as his figure at home, put on locally by Concerned NPC. His worker body is
  still the same creature with the same saved data; nothing is saved, networked or
  migrated. A piece that will not attach or does not fit on that body is taken off
  and he keeps the plain look. `cf_worker status` says which ("Worker body look").
- New setting `Thorstein/WorkerLook` (**on** by default). It applies the next time
  his worker body loads. **Not observed in game.**

### Added — Tools of the Trade, the story record (#295, slice S1)

- **Thorstein's introduction has a record now.** It is kept per character and
  per world. Bring him an axe and a hammer, in either order, through the
  existing handover (`cf_settle give`), and he joins your settlement with
  his completion line. The tools stay his real equipment; nothing is
  destroyed at turn-in.
- Joining is the settlement register's own recruitment, with all its checks.
  The story records it only after the roster is written. An unresolved
  handover, a read-only record, no authority or no settlement area each
  waits and says why.
- **Nothing relocks him.** A lost tool never relocks a recruited Thorstein,
  replaying the story grants nothing, and a finished story never re-hires a
  worker you dismissed.
- `cf_settle quest [status|examine]`. `examine` stands in for the carpenter's
  square, which comes in the next slice. **Not observed in game.**

### Added — Thorstein at home, and one body at a time (#295, #298)

- **While Thorstein rests and has no worker body, he stands near your bed**
  as a local figure: the game's own character, with the owner's reference
  look, a **Mullet** and a **Mustache**, and a deeper complexion than Hulgi.
  He also wears working leathers, which are this build's proposal. Nobody
  else sees him. He is never saved, has no collision, and changes nothing
  in the world. He appears only on the host, only with the settlement runtime
  on, and only once he is recruited.
- **He never has two bodies.** A saved worker body anywhere in the world is
  his body, and while one exists the figure is never shown. `cf_worker
  spawn` takes the figure down before the worker body exists and puts the
  body where he stood. `cf_worker despawn` gives the figure back.
  Concerned NPC's arbiter refuses the second kind of body while the first
  is held.
- **`cf_worker spawn` now asks Concerned NPC first.** If the library did not
  accept Thorstein, the spawn is refused and the reason is given. Before
  this, the spawn went ahead without the library.
- New `[Thorstein]` settings: `ShowAtHome` (on), plus `Hair`, `Beard`,
  `ChestGarment` and `LegsGarment` overrides, which are empty for the
  reference. A player's own choice is never overwritten.
- **Requires Concerned NPC 0.5.0.** Thorstein's role now admits a figure as
  well as a worker body, with the **same prefab name and keys**, so no saved
  body is affected.
- **His dresser is Concerned NPC's.** The code that puts his hair, beard,
  colouring and leathers on the figure moved into the library
  (`NpcFigureDresser`), so Sunniva and Gunnar can share it rather than
  carry copies. What Foreman keeps is his look itself: Mullet, Mustache,
  the slider readings, the proposed leathers and the `[Thorstein]` settings.
  The look and the settings are unchanged. A handful of log lines are worded
  for any companion now, rather than for him alone.

### Known limits — Thorstein's figure

- **Not observed in game.** The decision and the leases are tested. The
  figure, its fit and its colouring are not.
- **While he works he still looks like the `Dverger` clone.** That body
  cannot wear the reference hair or beard. Once he has been given a worker
  body with his tools in it, that body stays until it is despawned.
- **No routine yet:** no sitting, no dialogue, no quest, no hand-over
  window. He stands at home and faces you.
- If the character screen's colour palette was not read this session, his
  hair uses a stated dark-brown fallback and his skin is the model's own.
  The status line says which was used.

### Changed — player ladder climbing is off by default (#436)

- **Player ladder climbing now belongs to Concerned Ladders**, which is
  published as its own mod. Concerned Ladders deliberately will not run
  while Foreman's climb is installed, so a Foreman that climbed by default
  would switch the published mod off for everybody who has both. Foreman's
  climb is therefore **off by default**: at the defaults Foreman patches no
  ladder, and Concerned Ladders works beside it.
- **The switch has a new name: `Ladders/PlayerClimbing`.** Development
  builds before this one wrote `Ladders/Enabled = true` into their config
  as the default, without anybody choosing it. That value no longer
  turns the climb on, so an old config is off like a new one. Nobody
  outside development has a Foreman config, because Foreman has never
  been published.
- **`Ladders/Enabled` is kept as an off switch only.** `Enabled = false`
  turns the climb off whatever `PlayerClimbing` says, and `true` does
  nothing on its own. It is kept because Concerned Ladders 1.0.0's message
  tells a player who opted in to disable `Ladders/Enabled`, and that text
  is published and cannot change.
- **Nothing was removed.** Turning `PlayerClimbing` on gives exactly the
  climb described under "Added — climbable ladders" below. The pieces
  workers may one day use to traverse a ladder (#334) are untouched and
  do not depend on this switch.
- Foreman has **no build-time reference to Concerned Ladders**, in either
  direction.

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

- *Off by default since #436; see above.* With `Ladders/PlayerClimbing = true`:
- **Valheim's ladders are climbable instead of teleporting.** Walk into
  one, or press Use, and your character takes hold of it: forward climbs
  up, back climbs down, releasing stops, and you step off at the bottom,
  over the edge at the top, or jump away.
- **The game's own pieces, unchanged.** `wood_stepladder`, the grausten
  stone ladder, and anything else in the game built as a ladder that
  measures like one. No new piece, no new recipe, no new entry in the
  hammer, and nothing of this mod's written into a world: uninstall and
  every ladder you built is still standing, teleporting again.
- **The `Ladders` configuration section**: `PlayerClimbing` (originally
  `Enabled`; see #436 above), `AutoMount`, `ClimbSpeed`, `StaminaCost`,
  `UseTeleport`, `NpcClimbing`.
- **Off is off.** With `Ladders/PlayerClimbing = false` at startup, nothing is
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
