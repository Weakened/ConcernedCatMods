# Gunnar collects and hauls, in planned batches, without portals (CNPC-R2, #381)

Status: **planning, eligibility and accounting implemented and tested; the pickup port is written and the
validator allowance that confines it has landed. The authoritative gate is green. Nothing has been observed in
game.** Nothing here has been observed in game and
nothing claims to have been. Concerned Teamster stays at **1.0.5**; nothing is published, tagged or released.

## 1. What this is

Gunnar is Concerned Teamster's worker. He already walks to a cart a player explicitly assigned, hitches himself to
it and pulls it (`GUNNAR_HAULING.md`, #313/#314). #381 makes him the primary resource collector as well, in two
workflows:

- **Automatic**: a work area to a designated chest. Survey the area, judge what is eligible, work out what one trip
  holds, plan the batch and its route, collect along it, travel **once** to the chest, deposit, then the next
  planned batch. Never one branch at a time, and never a trip to the chest per item.
- **Manual**: chest to chest, with an explicit source, an explicit destination and a requested cargo filter, both
  containers permitting the operation, worked out into trips before he takes a step.

## 2. The division of labour, and why it is drawn where it is

**Concerned NPC sequences. Concerned Teamster says what is worth doing and what happens to the material.**

That line is not a preference. The shared runtime's job driver (`NpcJobDriver`) is the one place in this repository
that knows the difference between *every step of this plan came off* and *this plan covered the whole job* — and
getting that wrong is precisely how a job of nine trips silently becomes a job of eight and reports that it
finished. So Teamster hands in providers and consumes steps, and there is no second answer to any question the
driver already answers.

| Question | Whose | Where |
|---|---|---|
| Which target is next, and in what order | ConcernedNPC | `NpcJobDriver`, `StopSequencer` |
| How many trips a job takes, and what is in each | ConcernedNPC | `TourPartitioner` |
| What a plan did **not** cover, and whether to ask again | ConcernedNPC | `JobReconciliation`, `NpcJobProgress` |
| Which chests to draw from, in what order | ConcernedNPC | `SourceSelector` |
| What Gunnar may collect at all | **Teamster** | `GunnarTargetPredicate` |
| How much he can carry | **Teamster** | `GunnarCarry`, from the game's own numbers |
| Where every unit of material physically is | **Teamster** | `CargoLedger`, `CollectionAccount` |
| Whether a manual haul may start | **Teamster** | `HaulRequestGate` |
| The idle hammer | **Teamster** | `CartUpkeepIdle` |

An earlier draft of this work contained a batch planner, a round loop and a tour partitioner of its own. They were
written before the driver existed and they are **deleted**, not kept beside it: a second answer to "how many trips
is this" is one to keep in step forever.

### The library adoption, and the four rules that hold together

Teamster consumes ConcernedNPC. `tools/validate_repo.py` enforces all four or none:

1. a `ProjectReference` with `<Private>false</Private>`, so the library's DLL is never copied into Teamster's
   output and cannot reach its ZIP;
2. `TheConcernedCat-ConcernedNPC` pinned in `Package/thunderstore.toml`, so the storefront installs it;
3. `[BepInDependency("com.theconcernedcat.valheim.concernednpc")]` in `Plugin.cs`, so a missing package is one
   clear line at load rather than a null reference later in somebody's evening;
4. no `ActorModeOwner` of Teamster's own.

The gate reports `1 of 4 products consume it` and `4 products checked, 1 consume ConcernedNPC, none builds its own
mode owner`.

### Gunnar's identity now comes from the arbiter

`HaulExecutor` used to build its own mode owner. It now takes an `IWorkerIdentityAuthority`, and the one
implementation in a running game is `GunnarIdentityAuthority`, which asks the shared runtime's arbiter. The mode
vocabulary is unchanged — the same five modes, the same outcomes, the same rule that only releasing a job returns
the identity to `Resting` — so every shipped haul test still asserts exactly what it asserted. What changed:

- a haul and a collection round **cannot both hold Gunnar**, which is the whole reason this had to happen before a
  second job type existed;
- a world unload ends the hold whether or not this product noticed, because the arbiter ends its own holds;
- an authority in no position to answer (no world, no registration) is a **refusal**, never a grant.

**The residue, stated rather than discovered.** `NpcRoleRegistry.ModeOf` is public, so a product can *read* an
identity's mode owner. It still cannot *drive* one — `Enter` and `Release` on it are internal, and nothing inside
the library calls them either; the only callers anywhere are its own tests, which link its sources. The arbiter's
mode is therefore never set by anybody today, and reading it instead of keeping the word in `WorkerIdentityHold`
would be a regression with three faces: `Mode` would report `Resting` while Gunnar is pulling a loaded cart;
`JobId` would be null, so the refusal that stops a haul and a collection round holding him at once would stop
refusing; and `MayRetireBody` would be permanently true, which is the guard on destroying a body a cart is
jointed to. Either of two library changes closes it — make `Enter`/`Release` public and this becomes a forwarder,
or have the arbiter enter a mode itself when a body is claimed and this becomes a read. The second keeps "a
product holds one and cannot drive one" intact and is the better shape.

## 3. What Gunnar may collect, and what he may not

`GunnarTargetPredicate` decides from facts an adapter read. Every clause is exercised without the game.

**Identity is an allowlist, not a capability test.** "Has something takeable and gives Stone" admits two things it
must not: the hoe-placed stone a player put down, and the procreation-born rock that grows on a timer. Only the
network prefab name rejects the second before anything else is read — which is why the name is load-bearing and
why provenance and state are still checked behind it rather than instead of it.

| Clause | Refuses |
|---|---|
| Identity | not a loose stone or a fallen branch, by network prefab |
| Shape | a built piece, structural health, a cultivated plant, a destructible, **a dropped item pile**, **a container**, a display stand, a breeding pair, a creature, a carried item |
| FoodBearing | anything that bears food, named on its own so the refusal can say so |
| Yield | a patched amount, a populated extra-drop table, a yield that is not what the game gives, or the wrong renew/hide shape for its kind (which is how a **timer wearing a branch's name** is refused) |
| Provenance | a creator is recorded: somebody placed or dropped it, so it is theirs |
| State | already taken, **not visible** (a timer, not a resource), or not takeable now |

Site clauses, in the order a player would fix them, and **every unknown refuses**: not owned here → outside the
work area → inside a place the world built (or unknown) → ward denied → ward unknown → over the carry limit. There
is **no unlimited-radius searching**: an area is a boundary handed in, and an exhausted one is reported, never
widened.

**A player's mark widens which candidates are looked at, never which rules apply.** A marked chest is still a
chest.

## 4. Carrying, and the cart

His capacity approximates a vanilla player wearing Megingjord, and **not one number of it is written down here**.
The adapter reads the base carry limit off the player and the belt's contribution off the belt's own effect; this
layer adds up. Verified against the installed game (Valheim 1.0.14): the base limit is a field on `Player`, and
`Player.GetMaxCarryWeight()` adds whatever equipped effects contribute through `SE_Stats.m_addMaxCarryWeight` —
which lives in an asset, not in code, and therefore cannot honestly be a constant.

**Every failure goes the same way.** A reading that failed gives him a limit of zero, so he picks up nothing and a
player asks why — never an unbounded inventory discovered later. A unit weight nobody could read fits *nothing*.
Being over the limit is a fact to report, exactly as vanilla treats it, not a fault to correct by deleting
something.

**A cart's room is its slots, never a weight.** Vanilla's hand cart holds what fits in its grid and gets heavier as
it fills; there is no weight at which it refuses an item. Nothing in this work writes, scales or caps a cart's
mass.

## 5. Material is conserved, and a destroyed cart proves it

`CargoLedger` never subtracts. A unit is acquired once and thereafter is in exactly one **place**: carried, in the
cart, delivered, spilled from a destroyed cart, or lost. So "was anything invented" is one comparison —
`Acquired` against `TotalEverywhere` — and the tests ask it after every single step rather than at the end.

Every movement carries the name of the step that caused it, so a retry after an interruption re-states what it
already did and is told so. **Re-using a name for something else is refused outright**, because a name that means
two things is how forty stone comes out of a chest and is recorded nowhere.

**If the cart is destroyed**, everything it held is on the ground where it stood, exactly as the game left it. The
job stops. Nothing is re-credited, nothing is re-acquired, nothing is re-planned to make the job whole, and the
totals do not move.

A deposit that measured more than is there moves what it can and leaves the discrepancy visible. A refusal and an
unmeasurable answer both move **nothing**: he still has it, or nobody knows, and writing either down as fact is how
material is lost or invented.

## 6. The carve-out: one file, one allowance

**The owner granted it on 2026-09-19, and it is exactly one token in exactly one file.**

`.Interact(`, in `src/ConcernedTeamster/Adapters/Workers/GunnarCollectionPort.cs`, and nowhere else in this
product, now or later. Every other forbidden token still fails in that file, and that token still fails in every
other file.

**Two things were relayed as authorized and are not.** `TreeBase.Damage` (felling) and
`ZSyncAnimation.SetTrigger` (the idle gesture) were named while the owner's approval was being passed on, before
the verification the owner then required — *allow only what is independently verified as required for loose
branch and stone pickup*. That verification cut the allowance to one token, because the sanctioned pickup makes
exactly one game call and names no RPC send at all: the pick routes `RPC_Pick` and claims ownership inside
vanilla, not in our source. **Felling and the idle gesture each need their own owner decision**, and neither is
implemented here.
It stays behind the existing off-by-default `TeamsterFeature`; a player who has not opted in gets none of it. It is
fail-closed throughout. Nothing else was authorized: no cart teleports, no mass writes, no stamina bypass, no
forces or velocities, no ownership takeover, no mod data in a vanilla object.

**Why those calls need an allowance at all.** Picking is `Pickable.Interact`, which routes `RPC_Pick`; the handler
drops the items on the ground on the *owner's* machine and needs a local player to place its effect. Teamster
shipped as an observational mod and the audit is what makes that claim true rather than stated, so the interaction
had to be authorized rather than assumed.

**What the port refuses, and why each refusal is the safe direction:**

| Refusal | Because |
|---|---|
| the feature is off | nobody who installed Teamster for its telemetry is enrolled in this |
| the start-up probe did not verify every member | a changed game disables collection with one line rather than throwing out of a worker tick |
| no local player | the game's own handler dereferences it and would throw |
| **this client does not own the source** | the pick routes to the owner and the drop happens *there*; a source owned elsewhere would spill its contents somewhere we cannot count. Ownership is required, never taken — the same rule the cart seam follows |
| the source is one the game will not pick out of tar | that path speaks to the character, and `Character.Message`'s signature is what killed a shipped Cartographer on 1.0.7 |
| anything unreadable | unknown refuses |

**Picking writes the world save, and that is inherent to what was authorized.** Teamster's own rule otherwise
says no world-save mutation, so it is worth stating rather than leaving to be discovered: vanilla's `SetPicked`
stores the picked flag and the pick time on the source's own network record. That is the capability the owner
granted — a pick that did not persist would be a pick that undid itself on the next load — and it is the only
world state this product's collection writes. Nothing mod-shaped is written into any vanilla object.

**Four defects an independent review found, and what closed them.** All four were in the first version of the
port; none was ever live, because the slice has no call site.

| Defect | What closed it |
|---|---|
| A source could be picked **twice** inside the settle window, for a second full yield out of nothing — the game's pick raises *two* routed messages, and between them the source still reports it can be picked | a record of what was picked; a source is refused until the world confirms it, not until the pick finishes |
| A refused start could report the **previous** pick's count | the count is handed back once, by the `Poll` that finishes, and cleared with it |
| Vanilla's take **destroys the item and answers true** for a character with no network record; only the source's view was checked | the worker's own record is checked at the start and on every gather |
| The seed set was taken once, and above the collider ceiling a **pre-existing** item could fall outside it and be credited later | the ceiling is a refusal rather than a clamp, and only single-unit stacks count, up to what the pick expected |

The last of those is **a bound, not a proof of provenance**: a player dropping single units one at a time beside a
source he is picking would still be counted, and nothing available to this layer can tell those apart. What it
guarantees is that the error can never exceed what the source was expected to give — a pick can be short, never
generous.

**Two calls, one window.** The game's pick hands nothing back, so `Begin` starts it and `Poll` gathers what it
dropped over a bounded window, into Gunnar's own inventory, through vanilla's own `Humanoid.Pickup` — so weight,
stacking and the pickup delay are the game's arithmetic and not ours. **What is reported is what was measured**,
never what the source was expected to give, and a window that closes empty asserts nothing about the source,
because the pick may well have happened.

**He gets what a source gives and nothing extra.** Verified against the installed game rather than assumed: the
skill, statistic and bonus-yield branches inside `Pickable.Interact` are `character is Player` only, and Gunnar is
not a Player. `audit-teamster-hauling-api.ps1` pins that, that `RPC_Pick` is owner-only, and that it dereferences
the local player — 114 of 114 members and behaviours verified, up from 102.

**The route that was considered and rejected**, because the next reader will think of it too: moving the pickup
into ConcernedNPC so that Teamster's own source stays clean. That is the same dodge across an assembly boundary,
and this repository already has the precedent against it — the cart hitch seam deliberately stays in Teamster
because moving it would move a shipped safety property out of the product audited for it.

Still deliberately absent, and each for its own reason:

- **Felling.** Not authorized, and it would not have belonged here even if it were: real damage and drop-table
  semantics belong with #282 rather than riding in on a pickup commit. `CollectableKind` has no fellable value,
  and a test pins the enum so that adding one sends somebody here first.
- **The idle gesture's one call.** Not authorized. The state machine that decides when he would do it lives in
  `Domain/Collection/CartUpkeepIdle` — game-free, and proved by test to change nothing — and the single call that
  would make it visible is deliberately absent, with the reason written where the call would go. It is one line
  when a decision comes.
- **A `TeamsterFeature` value of its own.** The collection runs under the existing off-by-default worker feature.
  A second one lands if and when collection is separately switchable.
- **A ground probe.** `INpcJobRole.Probe` is null, with the reason in the code: every candidate is a world object
  the survey has just read out of the loaded scene, so its place is proved rather than proposed.
- **Reservation-aware availability.** `INpcJobRole.Availability` is null. Gunnar's accounting lives where the
  library cannot see it, and he holds the only identity that could run a second job. The cost is stated in the
  code: the moment a second worker in this process plans against the same chests, two jobs plan the same material.

## 7. No portals

He walks. There is no branch anywhere in this work that changes where he is standing other than by asking the
worker to walk, and `.Teleport(` and every transform-position write are already banned across all of Teamster by
`check_teamster_no_force_injection`. A stop he cannot reach after bounded local recovery is reported
(`StopOutcome.Unreachable`); a job where nothing can be reached ends in `CollectionAttention.NoRouteOnFoot` with a
reason, and stays there. Not a portal, not with a cart, and never to recover a stuck route.

## 8. The idle hammer is scenery

When Gunnar is idle near his assigned cart he occasionally walks over, faces it and plays the hammer animation.
It repairs nothing, consumes nothing, restores no hit points and grants no buff.

That is not a promise about the code. `CartUpkeepIdle` can say exactly four things — nothing, walk, face, swing —
and there is no value for repair, consume, heal or bless, so the code that drives it cannot ask for one. It is
handed a `CartCondition` and has no way to hand one back: no method returns one, no parameter is by reference, and
every property is read-only. `An_hour_of_idling_changes_nothing_about_the_cart` runs it for an in-game hour and
compares the cart before and after; `The_routine_can_only_ever_say_four_things` and
`The_routine_is_handed_the_cart_and_has_no_way_to_hand_one_back` are the structural half.

He never does it while the cart is attached to anything, never while a job holds him, never for a cart that is not
the one he was assigned, and never more often than the interval — and a period of work does not bank a swing he
owes.

## 9. Verification

| Check | Result |
|---|---|
| `pwsh ./scripts/verify.ps1 -Configuration Release`, through the build lock | PASSED at `84d47c0`: Release, 14 assemblies, **3998 tests**, validator exit 0 |
| `ConcernedTeamster.Tests` | **1031 passed, 0 failed** (944 before this work) |
| `pwsh ./scripts/audit-teamster-hauling-api.ps1` | PASS |
| `pwsh ./scripts/audit-teamster-navigation-api.ps1` | PASS |
| Planted defects | **8 of 8 caught.** One survived the first round — a refused-deposit test that handed the refusal an empty list and would have passed with the guard deleted. Strengthened, re-planted, caught. |

**Zero migrations.** No durable key, prefab name, file path, row tag or schema number changed.
`GunnarHaulingDefaults.WorkerKeyPrefix` is new and is pinned by test to be the prefix `WorkerKeyField` already had,
so the contract handed to the shared runtime describes the body this product actually saves.

## 10. In-game, when the blocker in §6 is resolved

**OWNER GO-AROUND PENDING.** Nothing below has been run. Disposable world, character and profile only. The two
rows to run **first** are Gate B2 in `docs/settlement/cart-and-collection/EVIDENCE.md`, which are about whether the
Gunnar who already exists still works.

1. **Start-up.** With `[Workers] GunnarHaulingEnabled = true`, expect `Gunnar is registered with the shared NPC
   runtime as 'teamster/gunnar'.` and, on world load, `a world loaded (epoch …)`. A log line naming a refusal from
   the shared runtime is a blocker; copy it verbatim.
2. **Zero migrations, the only irreversible check.** Load a world that already contains a saved Gunnar from a build
   before this change. His body must come back with his identity, and the log must show no
   `Destroyed invalid prefab ZDO`.
3. **One identity, one job.** Start a haul, then order a collection round. It must be refused while the haul holds
   him, and the refusal must name the haul.
4. **Reload.** Log out mid-haul and back in: the hold from the previous world must not survive, and `ct_haul
   status` must not report Gunnar busy with a job whose name nothing still has.
5. **Eligibility, in a real base.** Stand a chest, a dropped stack of stone, a raspberry bush, a cultivated crop and
   a hoe-placed stone inside the work area. None may be collected. A natural loose stone and a fallen branch in the
   same area must be.
6. **Carrying.** With and without Megingjord: the number of trips must change, and the weight he leaves with must
   match what a player wearing the same belt could carry.
7. **One trip per batch.** Watch a round of a dozen stones: he must collect along a route and go to the chest once,
   not once per stone.
8. **The cart destroyed mid-haul.** Everything it held must be on the ground where it stood, the job must stop with
   a reason, and the next order must not conjure replacement cargo.
9. **No route.** Put the work area across water with no walkable approach: `NeedsAttention`, with a reason, and he
   must stay where he is.
10. **The idle gesture — a decision to take, not a thing to observe yet.** The one call that would make Gunnar
    visibly do this is not authorized and is not in the build, so there is nothing to watch. What the owner is
    being asked is whether to authorize it, knowing two things: it is a transient animation trigger on his own
    body that saves nothing and touches no other object; and what it would play is vanilla's own `interact`
    gesture rather than a bespoke hammer swing, because an invented animator parameter warns on every call and
    animates nothing. Whether that reads as "he is fiddling with his cart" is a thing only a session can answer.
    One line to change, and nothing durable depends on it.
11. **What he gets from one source.** Pick a single loose stone with Gunnar and with your own character in the same
    world. The counts must match: he is not a Player, so no skill, statistic or bonus-yield branch runs for him.

Evidence rows for `docs/settlement/cart-and-collection/EVIDENCE.md` stay **pending** until observed, with the
build, profile and scenario recorded.
