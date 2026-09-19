# Gunnar collects and hauls, in planned batches, without portals (CNPC-R2, #381)

Status: **planning, eligibility and accounting implemented and tested; the engine adapters that would let him
actually pick anything up are BLOCKED on an owner decision — §6.** Nothing here has been observed in game and
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

**The residue, stated rather than discovered.** The mode *word* is still Teamster's, kept in
`WorkerIdentityHold`, because `NpcRoleRegistry.ModeOf` is `internal` to the library. When it opens, `Mode` becomes
a read of the arbiter's own and this type keeps its shape — a rename, not a redesign.

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

## 6. What this slice deliberately does not do, and why it is one decision and not three

**Gunnar cannot pick anything up yet, and the reason is a shipped safety rule rather than missing code.**

Every act #381 asks for is a network send from Gunnar's runtime, and Teamster's own audits forbid those outright:

| The act | The vanilla call | What forbids it |
|---|---|---|
| Picking a loose stone or branch | `Pickable.Interact` → `RPC_Pick`, plus an ownership claim | `.Interact(` is banned in **every** Teamster source file (`TEAMSTER_OUTSIDE_WORKERS_TOKENS`, `TEAMSTER_WORKER_FORBIDDEN_TOKENS`) |
| Felling a permitted sapling or small tree | `TreeBase.Damage` → `InvokeRPC("RPC_Damage")` | `InvokeRPC` and `ZRoutedRpc` are banned inside `Adapters/Workers/`; `ZNetView::InvokeRPC` is also on the hauling audit's IL forbid-list |
| The idle hammer animation | `ZSyncAnimation.SetTrigger` → `InvokeRPC(Everybody, "SetTrigger")` | the same |

Verified against the installed `assembly_valheim.dll`, not assumed. Concerned Foreman does the first of these
legitimately (`WorldSourcePickupPort` calls `pickable.Interact`), because Foreman has no such audit; Teamster does,
because Teamster shipped as an observational mod and the audits are what make that claim true.

So this is **one owner decision**, not three: does Gunnar's opted-in worker runtime gain a bounded, named
allowance to send the vanilla interactions a player could send themselves — and if so, under which
`TeamsterFeature` and with which audit text? Routing around it textually (the calls do not literally spell the
banned tokens in our own source) would be exactly the silent weakening `CLAUDE.md` forbids, so it was not done.

Also deliberately absent, and each for its own reason:

- **A `TeamsterFeature` value for collection.** The enum documents features that touch a cart or its data. Nothing
  here touches anything yet, so a policy row describing a runtime that does not exist would be a row nobody can
  check. It lands with the runtime.
- **Felling as a target kind.** `CollectableKind` has no fellable value, and a test pins the enum so that adding
  one sends somebody to read this section first.
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

**OWNER GO-AROUND PENDING.** Nothing below has been run. Disposable world, character and profile only.

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
10. **The idle hammer.** Leave him idle beside his cart for ten minutes. He walks over, faces it and swings. The
    cart's health bar, its cargo and its weight must read identically before and after; nothing may be consumed
    from his inventory.

Evidence rows for `docs/settlement/cart-and-collection/EVIDENCE.md` stay **pending** until observed, with the
build, profile and scenario recorded.
