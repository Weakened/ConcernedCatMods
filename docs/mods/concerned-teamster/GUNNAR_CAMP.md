# Gunnar at camp (#461)

Status: **wired in production; never observed in game.** Every in-game row
below is OWNER GO-AROUND PENDING.

## What he does

While he rests (no worker body anywhere in the world, no job), Gunnar's local
presentation figure (#458) stands near the respawn anchor - the claimed bed, or
the world start - **standing**, on bare ground. He faces you when he is shown
and turns to face you when you talk to him. He says nothing on his own: speech
is reached from the talk key alone (#459), so there is no ambient line.

He does **not** sit in this build. Concerned NPC 0.5.0's dresser has a ground
sitting pose (`NpcFigureDresser.SitOnGround`, #295 S5) and Thorstein uses it;
for Gunnar it is left for its own slice (see "Not in this leaf"). Chairs,
stools and benches stay out of scope until a slice watches for a player taking
the seat. No seat is offered, named or claimed.

## Where he stands

`Domain/Companions/GunnarCampRoutine.cs` decides, game-free:

1. `GunnarFigurePlacement`: the shared `PlacementPlanner`'s deterministic ring
   sweep 1.5-4 m from home, on bare ground only (the probe's seat finder is
   hidden), 1.25 m clear of another product's keepsake.
2. Then, lazily and best spot first, `Admits`:
   - **Camp.** Once Concerned NPC's camp is established (a home and at least one
     player-built piece), the spot must be inside it. Before that, home alone
     decides. Camp is the library's public facade `Camp.NpcCamp` /
     `NpcCampView` / `NpcCampHome` (PR #515): no reflection, no private type, no
     copied camp.
   - **The door rule** (the lead default in
     `docs/settlement/cart-and-collection/DECISIONS.md`, "a resting figure and
     doors"): never set down on the far side of a doorway from home. It is the
     shared `RouteDoors.Inspect` on the straight line home-to-spot, and anything
     but `Clear` refuses the spot, so a shut door he would have to open is a door
     he does not pass. A spot in or right beside a doorway - within 1.5 m of the
     opening's plane and no more than 1.5 m past a jamb - is refused as well
     (`GunnarRestBounds.BesideADoorway`). Teamster keeps no list of doors opened to companions, so
     every door says `allowed: false`: the rule's own default
     (`OnlyAllowedDoors` with nothing allowed). This is the same rule Thorstein's
     figure applies; the doorway half is the shared code rather than a copy.
   - **Room to stand**: nothing solid from 0.6 m to 1.7 m (a 0.3 m capsule). An
     unanswerable check counts as no room.
   - **The wall check**: nothing solid between home and the spot at 1.4 m (one
     bounded ray; the home bed and loose physics objects do not count). An
     unanswerable check, or a full hit buffer, counts as a wall.
3. A plan is kept until the anchor moves, camp's revision changes or the pieces
   near home change. Then, if home has not moved, the spot he stands on is asked
   again first (one probe and the same admission) and kept while it still
   passes on the same ground (re-measured within 2 cm), so something built or
   removed elsewhere does not move him; only a spot that no longer passes, or a
   moved home, gets a fresh plan. A step in which nothing changed allocates
   nothing and probes nothing (measured: 0 bytes over 100,000 warmed steps, no probe, no wall
   check). A deferral because ground is not loaded is asked again on the next
   pass; a figure already standing keeps its spot meanwhile.

`Adapters/Companions/GunnarCampSight.cs` is the read-only adapter that feeds it:

- One `Piece.GetAllPiecesInRadius` within 32 m of home, when home moves, in a new
  world, and otherwise at most every 10 s while you are near home. Camp is fed
  only the pieces `GunnarRestBounds.IsCampPiece` admits: **placed by a player,
  not a terrain tool's piece, not a crop**. Keys are the pieces' network ids for
  this world load, scoped by the world epoch, so a renumbered reload is a new
  camp, never a stale one.
- Every door within 9 m of home becomes a `DoorPortal` (`allowed: false`).
- **Fails closed.** A look that throws is reported once per world and answers
  false; the routine then shows him **nowhere new**: a figure already standing
  keeps the spot it was admitted to while the doors were known, and none is
  shown otherwise.

It reads and changes nothing: no door is used, opened or closed, no piece is
claimed, no bed is claimed, nothing is networked, saved or written. There is no
new world authority and no new validator allowance.

## Lifecycle

- Range: leaving range (48 m to show, 64 m to keep) no longer takes a shown
  figure down. Coming back keeps the same figure on the same spot, so it is not
  rebuilt each time (the gap #443 left open for Thorstein, closed here for
  Gunnar). The limit, stated: once home's zone is no longer loaded the anchor
  cannot be confirmed, so the figure comes down and is built again on return.
  That is the game unloading the ground under him, not a range threshold.
- A bed change re-plans the figure only. The routine has no worker, cart, cargo,
  door or motor port, so it cannot move a worker body, a cart or cargo, and the
  figure is never shown while a worker body exists (`GunnarBodyPlan`, the
  leases, the presenter's order - #458's three ways, unchanged).
- A worker body, a job, a missing home, the story not met, the opt-out, or not
  the host: the figure comes down and the plan is forgotten. A hand-over to the
  worker body forgets the plan too. A world unload forgets camp, the look and the
  plan.

## Automated

`GunnarCampTests` (18): steady-state allocation and probe budget; range
retention without re-probing; bed re-plan and worker suppression without another
probe; the library camp's foreign-epoch and non-structure refusal and survey
cache; the camp-piece rule; no seat offered to a standing figure; unknown camp
and stale epoch; a spot across a doorway refused, including one clear of the
doorway that only the crossing refuses; a spot in or beside a doorway refused; every door - shut, open,
allowed-but-shut - refusing (the rule's helper checked directly too); a wall, no
room, and an unanswerable wall check; an established camp bounding the spot and
no camp leaving home to decide; a failed look showing him nowhere new and keeping
his spot; a change near home asking about his spot again and keeping it, a spot
built over moving him, and an unrelated change not moving him off a spot that
still passes; unloaded ground asked again while a shown figure holds; unknown
keepsakes deferring. Mutation-checked, one rule removed at a time: dropping the
door-crossing call from the routine fails
`ACrossingAloneRefusesASpotClearOfTheDoorway`; dropping the doorway clearance
fails `ASpotInOrBesideADoorwayIsNeverHis`; dropping the failed-look refusal fails
`AFailedLookShowsHimNowhereNewAndKeepsTheSpotHeHas`; dropping the keep-if-it-
still-passes check fails `AnUnrelatedChangeNearHomeDoesNotMoveHimOffASpotThatStillPasses`;
dropping the wall check, the room check or the camp bound each fails its own test
too.

`GunnarHandOverOrderTests` and `GunnarDialogueCatalogTests` still pin the pass's
order (the story before anything physical, the body decision before the figure)
and talk-only speech. The validator's `[teamster] #458 figure audit` is
unchanged and green: the presenter's figure calls keep their pinned spelling and
population (the facing turn reuses the one pinned re-stand), and the new adapter
in `Adapters/Companions` builds, adds, re-parents, activates and poses nothing.

## Limits, stated

- Every doorway is read as 2 m wide (1 m either side of the door's origin), the
  vanilla wooden door's opening. A wider gate is under-measured: a line through
  its outer part may not count as crossing it, and an open gate is no wall to
  the ray. Thorstein's figure shares this. Reading each door's own bounds would
  close it.
- A look allocates a short string per loaded piece within 32 m (its network id),
  at most every 10 s while you are near home, as Thorstein's does every 30 s. The
  routine's own step allocates nothing.
- A look that fails after home moved keeps him at the old spot until a look
  succeeds (the next try is within 10 s): nowhere new, by design.

## Not in this leaf

- Sitting on the ground or by a fire. The library pose exists; Gunnar's probe
  answers no warmth, and his body-room check is for a standing figure. Thorstein's
  sitting band (1.5-6 m, same floor) and probe are Foreman's.
- Chairs, stools and benches.
- Walking. He is set down at a spot; he never walks, so no door is ever used.

## Owner go-around (OWNER GO-AROUND PENDING)

On the exact integrated candidate, in a disposable world and profile:

1. With a bed in an open camp, he stands 1.5-4 m from it, not inside a table,
   wall or bench, and faces you; talking to him turns him to you.
2. With the bed inside a house with a shut door, he stands inside, never outside
   the door or behind a wall from the bed (and see the owner question in
   `DECISIONS.md`).
3. Walk beyond 64 m and back: the same figure, the same spot, no rebuild flicker.
4. Build a wall on his spot: he moves within about 10 s, or comes down if no spot
   is left.
5. Move the bed: he re-plans to the new bed; a worker body and cart are untouched.
6. `ct_haul spawn` / `retire`: never two bodies; no figure while the body exists.
7. `ct_haul status` shows the camp line (established or not, pieces, doorways).
8. World change and logout: no figure left behind.
