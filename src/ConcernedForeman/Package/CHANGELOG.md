# Changelog

All notable changes to Concerned Foreman are documented here.
This project follows [Semantic Versioning](https://semver.org/).

## [0.1.0] - unreleased

The first code this product has ever had. **Not published, and not a
playable release**: it is the worker actor spike from CF-SET-002 (#279),
and the thing it is meant to prove has not yet been observed in a game.

### Added

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

### Known limits

- **No offscreen work.** The worker acts only in loaded ground, and says so.
- **Solo and local host only.** On a dedicated server the runtime refuses.
- One worker at a time. Recruitment, orders, harvesting, carrying and
  construction are later work and are not in this build.
