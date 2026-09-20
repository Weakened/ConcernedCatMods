# Gunnar collects and hauls, in planned batches, without portals (CNPC-R2, #381)

Status: **planning, eligibility and accounting implemented and tested; the pickup port is written, the validator
allowance that confines it has landed, and the port now has a call site behind an off-by-default switch (§6a) with
the two material-loss paths that wiring opened closed behind it — a deliberate retire (§6b) and every involuntary
unload, logout and reload (§6c). **Two doors still lose material and are named rather than left to be found: death,
which needs an owner decision, and the one change whose write to the network object failed** (§6c). The
automatic survey-driven job is still unwired. The authoritative gate is green. Nothing has been observed in game**
and nothing claims to have been. Concerned Teamster stays at **1.0.5**; nothing is published, tagged or released.

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
It sits behind an off-by-default `TeamsterFeature` of its own — `GunnarCollection`, switched by
`Workers/GunnarCollectionEnabled` — so a player who has not opted in gets none of it. It is fail-closed
throughout. Nothing else was authorized: no cart teleports, no mass writes, no stamina bypass, no
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
port, and none was ever live: the port had no call site when they were found and fixed. It has one now — see
§6a — so from here on a defect in it is a defect a player with the switch on could reach.

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

**Three more a second review found, in the fixes themselves.** Again none was ever live, for the same reason.

| Defect | What closed it |
|---|---|
| **The mint window reopened through `Forget()`.** The port answered a cancelled job — an ordinary caller event — with `ForgetWorld()`, wiping the unconfirmed record. `Begin → Interact → Forget → Begin` on the same source gave a second full yield, because inside the settle window neither the source nor the game's own guard refuses | two verbs instead of one. `PickAccounting.ForgetJob()` releases the pick and **keeps** the record: a job ending says nothing about whether the source has settled. Only `ForgetWorld()` — a world actually going away — may drop it, and the port now has a method for each |
| **The unconfirmed record was unbounded**, and could retire a source for ever. A pick whose second routed message lands after `Finish` never confirms, so five thousand cycles left five thousand keys; and a source that *respawns* is pickable again while still carrying a record saying it is not | a settle horizon of 60 s, stamped from the caller's clock, plus a ceiling of 128 records evicted oldest-recorded-first. The window either bound has to outlast is **one routed-RPC turn**; the port's whole gather window is two seconds. Eviction never takes the newest, which is the only record that could still be inside its window |
| **An impossible elapsed time used to forget the record.** A first version dropped it when the clock appeared to go backwards, reasoning that a clock going backwards is a world that reloaded. Wrong twice: the house clock is `Time.time`, which counts from process start and does not reset on a world load, so it would not have detected that - and forgetting a record is the *minting* direction | keep refusing. An elapsed time that cannot be measured is not evidence the window has passed. A world going away is `ForgetWorld()`'s job, which is why that verb was split from `ForgetJob()`. This also covers a caller that stamps `Began` with a real clock and lets `MayBegin` take its default |
| **The 64-collider ceiling queried every layer.** Four metres of any built-up base exceeds 64 colliders of terrain, pieces, characters and trigger volumes, so `PlaceTooCrowded` — the exceptional refusal — was going to be the ordinary outcome, and a port that always refuses never works | the gather query is masked to the layer a dropped item is on - the game’s own `item`, read out of the installed layer table the way the navigation audit already reads it - triggers included, and the ceiling doubled to 128 on top of that. Saturation now means 128 *items* within four metres |

The horizon is the one of these that trades in judgement rather than in certainty, so it is stated plainly: if a
host could stall its own routed-RPC queue for a full minute while still running frames, an expired record would be
a source genuinely mid-settle. Nothing at this layer could tell that from a respawn. Expiry alone still picks
nothing — the port re-reads `CanBePicked()` first, so a source whose confirmation *did* arrive is refused by the
world itself whatever the record says.

The mask is **inclusive within that layer**, for the same asymmetry: it seeds "what was already lying here", and a
drop missing from that seed is a drop this pick would later credit as its own. So trigger colliders are queried
too, and a mask that resolves to nothing — a game version that renamed its item layer — falls back to every layer
rather than to an empty seed. What it deliberately does *not* do is name layers no drop is on: every collider
admitted for nothing is one closer to the ceiling, and the ceiling is a refusal.

**None of this is observed in game.** The accounting is proved by unit test, which is where it was moved to so that
it could be; the port itself binds Unity and no test in this repository loads it. The layer names and the settle
window are read off the installed assemblies, not watched happening.

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

## 6a. The call site, and the two verbs it routes

**The port is reachable now. It was not before, and the difference is one file plus one switch.**

`src/ConcernedTeamster/Adapters/Workers/GunnarCollectionRuntime.cs` is the only caller, installed from
`Plugin.Awake` alongside the hauling runtime. Everything below has to be true at the moment of the order, and each
one refuses on its own while every other Teamster feature keeps working:

| Gate | Where it is decided |
|---|---|
| `Workers/GunnarCollectionEnabled` **and** Teamster's `General/Enabled` | `GunnarCollectionDefaults.CollectionEnabled` is `false`; the runtime reads both and an unreadable setting is not an opted-in one |
| the work-authority rule — a loaded world, `ZNet.IsServer()`, not dedicated, **no** connected peers | `WorkAuthorityPolicy`, through `GunnarWorkAuthority.ReadWorldFacts`, **re-asked every frame** while a pick is in flight, not once at the order |
| the start-up capability probe verified every member the pickup binds | `HaulingCapabilityProbe` (its collection block, plus `Pickable.m_amount`) |
| a body the census bound as Gunnar, alive | `GunnarHaulingRuntime.BoundBody` — collection never finds or builds a body of its own, so an ambiguous or duplicated census gives it nothing |
| the thing is on the allowlist, yields exactly what vanilla yields, and he is within reach | `CollectionOrderGate`, game-free and unit-tested |
| this client already **owns** the source, it can be picked, it is not in tar, and it is not awaiting confirmation | the port's own `Begin`, re-read from its own frame |

**What a player can actually do: order one pick.** `ct_collect pick` picks up the loose stone or fallen branch they
are pointing at, if Gunnar is standing next to it. `ct_collect status` and `ct_collect cancel` are the other two
subcommands. There is no survey, no route, no batching and **nothing that moves Gunnar** — an order he cannot reach
from where he stands is refused rather than turned into movement nobody authorized. `GunnarCollectionJob`,
`CollectionSurvey` and `GunnarTargetPredicate` are still unwired; that automatic path is its own work, and it is
where wards, locations, creators and work areas start to matter, because they are the questions that only arise
once something other than a person is choosing.

**The two lifecycle verbs, and why the choice is not spelled at the call site.** `Forget()` means a *job* ended and
**keeps** the unconfirmed-source record; `ForgetWorld()` means the *world* went away and is the only verb that may
drop it. Crossed, `begin → pick → forget → begin` on one source yields a second full load out of nothing. So the
runtime reports what happened and `Domain/Collection/CollectionLifecycle.cs` — game-free — decides which verb that
is:

| What happened | Verb | Why |
|---|---|---|
| the world went down (`ZNetScene`/`ZNet`/`ZDOMan` gone) | `ForgetWorld()` | those sources do not exist in the next world, and a record that outlived them would refuse picks of whatever inherited their ids |
| the game is shutting down, or the plugin is being removed | `ForgetWorld()` | nothing can pick afterwards, so dropping the record cannot mint |
| the player cancelled the order | `Forget()` | a cancelled job says nothing about whether the source has settled |
| work authority was withdrawn mid-pick (a peer connected, the switch went off) | `Forget()` | same: the source is still there, still mid-settle |
| the runtime faulted | `Forget()` | same again — a fault is not a world going away |
| the pick finished on its own (`Done` or `Lost`) | **neither** | the port already released it and handed its count back; routing a verb here would be reporting an event that did not happen |
| a world came **up** | **neither** | dropping the record is the minting direction, so it is never done on the strength of an edge a flicker in the game's singletons could also produce. Nothing is lost: a world that just came up has had nothing picked in it |

`CollectionLifecycleTests` drives every row of that table against a recorder and against the real `PickAccounting`,
including the one that matters — a cancelled order still refuses a second pick of the same source inside the settle
window. Swapping the verbs in either direction turns those tests red. And because the port's own two-line mapping
onto the accounting binds Unity and no test here can load it, `validate_repo.py` pins it as source: the `#381
collection lifecycle audit` refuses the port if `Forget()` forgets the world or `ForgetWorld()` merely forgets the
job, and `tools/tests/test_teamster_carveout.py` plants both crossings and requires the refusal.

## 6b. Retiring a body no longer deletes what it is carrying

**The gap this closes, and that the wiring is what opened it.** `Humanoid.Pickup` puts what Gunnar picks into his own
inventory, in his own network object — which is right, and is what the worker decisions say a worker body may keep.
The deposit half of §4 and §5 (`CollectionAccount`, `CargoLedger`, the container permissions) is **not wired**, so a
stone he picks up stays in him. Meanwhile `ct_haul retire` destroys a body through `ZNetView.Destroy()`, and a
destroyed body's inventory goes with it: **nothing is dropped on the ground.** That was harmless while nothing could
put anything into him. Wiring the pick is what made it a way to delete gathered material silently, and material
conservation (§5) is this product's hard rule, not a preference — so it is fixed here rather than noted.

**Refusal, not a drop, and the precedent is what decides that.** Concerned Foreman's `SETTLEMENT_AUTHORITY.md` §5a
settles this shape for a worker holding real material, and it settles it twice:

- **death** drops everything through *vanilla's own* drop and records the units `Lost`;
- **despawn** — the deliberate removal, which is what retire is — is **refused while he carries anything**.

Death is vanilla acting on its own. A deliberate drop here would be this product spawning item instances, which is
not one of the calls the 2026-09-19 carve-out granted and which no owner decision covers, so **the drop was not
available to take** — reaching for it would have been inventing an authorization. The precedent for this verb is the
refusal.

**And an escape hatch that can never be blocked, because otherwise the refusal is a trap.** Foreman pairs its
refusal with recovery commands that empty the worker; this slice has none. A bare refusal would strand a body
forever — and retire is the only way to resolve a duplicate Gunnar. So:

| `ct_haul retire` | What happens |
|---|---|
| he is holding nothing | retired, as before |
| he is holding anything | **refused**, naming how many things and that removing him would destroy them, and naming the way out |
| what he holds could not be read | **refused** — unknown is not empty |
| `ct_haul retire force` | retired anyway, saying plainly that what he carried was destroyed and is **not** on the ground |

`WorkerRetirement` decides it, game-free; `WorkerRetirementTests` pins every row, including the anti-trap property
that **no state of the carried-material decision refuses a forced retire**, over every combination of held count and
readability.

**Said exactly.** That universality is the *carried-material* decision's, not the whole verb's. A forced retire can
still be refused for reasons that have nothing to do with what he is holding: a cart's joint still holds the body
(detach it), or the executor says he is busy with a haul (stop it). Both are things the player can resolve, and the
case this property exists for — **the pointed-at duplicate body, which is the only way to resolve two Gunnars** —
takes neither path, so it is never blocked. What is guaranteed is that *carrying something* can never be the thing
that strands a body.

It counts **anything in
his inventory**, not "collected material": nothing at this layer can tell a picked stone from anything else, and
pretending otherwise would be a provenance claim, so it refuses more often instead.

The call site is `GunnarHaulingRuntime.Retire`, which binds Unity and no test here can load — so the same technique
as §6a closes it: `validate_repo.py`'s `#381 carried-material audit` extracts that method and requires **one**
`WorkerRetirement.Decide` and **one** `WorkerRetirement.Allows` per path that removes a body, each guard above the
removal it gates. Stated for what it is: a source-order pin, not a control-flow proof — the smallest check that
cannot pass while a removal in that method runs with no guard consulted above it. Both crossings are planted in
`tools/tests/test_teamster_carveout.py`, and both pass only with the rule registered.

**Still not a way to get material out of him.** The deposit path remains unwired; this only stops the loss. Until it
is wired, what he picks up stays in him, and that is the next slice.

## 6c. What he carries survives a load, because the game does not save it

**The same failure through the other door, and a review found it.** §6b closed the *deliberate* removal. The
*involuntary* one was wide open: **the game never saves a non-player character's inventory.** It is a plain readonly
field with no save and no load anywhere in the character hierarchy, rebuilt every time the body is created. The body
itself *is* saved — Gunnar stays in a world until he is retired, and the census reads saved bodies — so a stone he
picked up was destroyed by a **zone unload, a logout or a world reload** while the body came back empty. No refusal,
no record, no drop: exactly what `ct_haul retire` had just been taught to refuse, reached by a route no player has
to opt into.

Before this round, the only field Teamster wrote was `tcc.worker.key`. Foreman's §5a had to add the other two
**precisely because vanilla does not do it**, and its third bullet — *"zone unload loses nothing: the body is
re-bound by key and loads its stored inventory"* — was a clause Teamster had no implementation of.

**Foreman's format, not a Teamster spelling.** `TeamsterWorkerRecord` writes the body's **own** network object:

| Field | What |
|---|---|
| `tcc.worker.key` | the worker identity, as before |
| `tcc.worker.inventory` | vanilla's own `Inventory.Save` package, as a byte array — the format a chest stores |
| `tcc.worker.revision` | a count of writes; zero means it has never written |

Byte-compatible with `ConcernedForeman/Runtime/Custody/WorkerBody.cs` on purpose: same names, same package, same
companion field. Two products may share the `tcc.worker.` prefix because the prefab name is what separates their
bodies before a key is ever read, and a second spelling for the same thing would be a second format to keep in step
forever. `WorkerInventoryRecordTests` pins the literals.

**Written the way a chest is written.** From vanilla's own `Inventory.m_onChanged` callback, inside the call that
made the change. Nothing is written **before** a successful load, so an empty inventory can never be saved over a
carried one — and a body that could not read what it carries goes **inert**: it never saves, and `BoundBody` does
not offer it, so collection has nothing to act with.

**Zero migration, and it is a decision rather than a null check.** A Gunnar saved before the field existed has no
stored package. `WorkerInventoryRecord.Decide` answers `LoadEmpty` — never a refusal, never a fault — and
`NoReadEverRefuses` pins that no input can produce anything but a load. An old body comes back exactly as it
always did.

**And a third thing, which the audit got wrong before a review read it.** Widening that IL rule from one write to
four needed a longer window between a key literal and its `ZDO::Set`, because the inventory write pushes a `ZPackage`
and an `Inventory.Save` in between. At 24 IL lines the window reached back **past the previous `ZDO::Set`**, so the
second write of an adjacent pair was vouched for by the *first* one's literal: planting `tcc.bogus.inventory` on the
first write failed the audit, and planting `tcc.bogus.revision` on the second **passed it**. The window now stops at
the previous `ZDO::Set`, so a write can only be vouched for by a literal that is its own. Proved against the
installed assembly rather than reasoned about — first write FAIL/FAIL, second write PASS before and FAIL after, and
the same plant on the *spawn's* adjacent pair FAIL, with `ZDO::Set(` staying at 4 throughout, so the pinned count
alone would have seen none of it.

**Two things the off-game audit is worth reading for, since it caught this work.** First, its IL rule pinned
`ZDO::Set(` to *one* call in *one* type; this made it four in two types, and the audit refused the build until that
expectation was updated on purpose — which is the rule doing its job, not an obstacle. Second, a
process trap worth the line: the updated check first used a script-scope `@(...)` array of allowed type names inside
the scriptblock the audit hands its matcher, and PowerShell resolved it to nothing, so the rule silently refused
**everything**. That is the safe direction — it could never have produced a false green — but it looks identical to
a real violation, so the allowed types are compared explicitly instead.

**A write that fails, which is the narrower claim this section is entitled to.** A review pointed out that
`LastChangePersisted` was set on every change and **never read**: the record knew a write had failed and nothing
asked. So a pick whose write failed was followed by another, and another, each adding to a live inventory the stored
package was no longer keeping up with, and all of them lost on the next load. `BoundBody` and `ItemsHeldBy` now both
ask `WorkerInventoryRecord.Trust`, which refuses a body with no record, an unloaded one, **or one whose last change
did not reach its object** — so nothing more is handed to a body that is not keeping up, and the retire verb treats
its contents as unknown rather than as zero, which refuses a non-forced retire. `WorkerInventoryRecordTests` pins
the decision and that it is memoryless.

What that does **not** do, stated because the alternative is a reader believing otherwise: it does not recover the
units in the change that failed. Those are in the live inventory and not in the stored package, and the next load
rebuilds the live one from the stored one. So **a failed write still loses what that one change added**, and nothing
this mod does will clear the flag afterwards, because the only inventory change it makes is a pick and this refusal
is what stops the next one. A zone load re-creates the record clean from the last package that did get written. The
decision being memoryless is what keeps that a *pause* rather than a body latched off for the session; it is not a
claim that a running game will resume picking on its own.

**The door this does NOT close, named rather than left to be found.** *Death.* If Gunnar is killed, his body is
destroyed and what he holds goes with it. Foreman closes that by dropping every carried item through vanilla's own
drop as the body dies (§5a, first bullet) — and that precedent does cover this event, unlike the deliberate retire.
But implementing it means this product **spawning item instances**, which is a capability the 2026-09-19 carve-out
does not grant and no other owner decision covers. It is one call and a handful of lines behind an owner decision;
it is not being taken quietly. **Until it is: Gunnar dying loses what he is carrying.**

**Still never observed in game.** Nothing in §6a, §6b or §6c has been watched happening; §10 is the go-around.

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

### The wiring of §6a, the retirement guard of §6b and the persistence of §6c

| Check | Outcome |
|---|---|
| `pwsh ./scripts/verify.ps1 -Configuration Release`, through the build lock | **PASSED at `fe04bb2`**: Release, 14 assemblies, **4437 tests**, validator exit 0 (run from this worktree's own `scripts/verify.ps1`, under the machine-wide build lock, with every source touched first so MSBuild could not skip a rebuild) |
| `ConcernedTeamster.Tests` | **1133 passed, 0 failed** (1031 before this work) |
| `python -m unittest discover -s tools/tests` | **14 passed** (8 before this work; six new plants) |
| `pwsh ./scripts/audit-teamster-hauling-api.ps1`, through the build lock | **PASS**, 125 of 125 members and behaviours, against the installed **Valheim 1.0.15** — including the newly probed `Pickable.m_amount`, `Humanoid.GetInventory`, `Inventory.NrOfItems`/`Save`/`Load`/`m_onChanged`, `ZPackage`, and `ZDO.Set`/`GetByteArray`/`GetInt`; plus the *fact* §6c depends on, that a non-player inventory is a plain field the game never saves — so a game update that starts saving it fails here rather than as duplicated stone in somebody's world |
| The same audit's **IL** rule on `ZDO::Set(` | It caught this work: one write in one type became **four in two types**. Updated deliberately to the exact new count, both types named, both inside the one source file the validator allows, the `tcc.worker.*`-key window unchanged in kind (widened 12→24 IL lines because the inventory write pushes a `ZPackage` construction and an `Inventory::Save` between the key and the `Set`). **Planted:** writing `"gunnar.carried"` instead — the audit refuses it, so the key constraint is what passes the rule, not the type list |
| Planted defects in the §6a wiring, one per property | **7 of 7 caught**: world-down routed to the job verb; an order ending routed to the world verb; tear-down routed to the job verb; a world coming *up* also dropping the record; the off-by-default switch not consulted; reach not checked; identity not checked |
| Planted defects in the §6b guard, one per property | **7 of 7 caught**: a carrying body retired anyway; an unreadable inventory read as empty; the forcing word made refusable (the trap); the forced message no longer stating the loss; the refusal no longer naming the way out; any trailing word accepted as forcing; an unknown verdict treated as a grant |
| Planted defects in the validator's own new rules | **6 of 6 caught** — two crossing the port's lifecycle verbs, and four against the carried-material rule: the guard moved below the removal, the decision removed, the removal **lifted into a helper** (the escape a review walked through) and a removal **moved to another file**. Each fails against a validator with the `#381` rule that catches it unregistered, so the rule, not something else, is what refuses it |
| Planted defects in the §6c persistence, one per property | **5 of 5 caught**, all as test failures rather than compile errors: an absent record faulting instead of loading empty; a stored package ignored; the revision starting at zero; the inventory field drifting from Foreman's spelling; an order accepted while the world is going away |
| In game | **OWNER GO-AROUND PENDING.** Nothing has been run; §10 steps 12–27 are the rows |

**Zero migrations (the §6a wiring).** No durable key, prefab name, file path, row tag or schema number changed. The
one new setting, `Workers/GunnarCollectionEnabled`, defaults to off, and a config file written by an older build
simply does not have it — BepInEx adds it at its default on the next load. The one new probed game member,
`Pickable.m_amount`, was already pinned by `scripts/audit-teamster-hauling-api.ps1`; the runtime probe now names it
too, so the two lists agree again.

**Zero migrations (the 1.0.5 work).** No durable key, prefab name, file path, row tag or schema number changed.
`GunnarHaulingDefaults.WorkerKeyPrefix` is new and is pinned by test to be the prefix `WorkerKeyField` already had,
so the contract handed to the shared runtime describes the body this product actually saves.

## 10. In-game — OWNER GO-AROUND PENDING

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

### The ordered pick, now that it has a call site (§6a)

These are the rows the wiring itself needs. **None has been run.** Steps 12–14 are the ones that would falsify the
claim that nobody who has not opted in is affected, and step 17 is the one that would catch the mint.

12. **With the switch off.** Fresh profile, `[Workers] GunnarCollectionEnabled` left at its default. Expect the
    start-up line `Gunnar's collection is off (the default)`. Point at a loose stone with Gunnar beside it and run
    `ct_collect pick`: it must refuse naming the setting, and the stone must still be there. `ct_collect status`
    must say `OFF (the default)`.
13. **The console command exists at all.** Expect `ct_collect` in the registration line from
    `VanillaConsoleCommands.Describe`. If it is missing, the Jötunn constructor mismatch is back and every `ct_*`
    and `cc_*` command is gone with it — that is a blocker, not a collection defect.
14. **Turned on, one pick.** `GunnarCollectionEnabled = true`, `GunnarHaulingEnabled = true` (the body census lives
    there), single player, disposable world. Bring Gunnar in with `ct_haul spawn`, walk him to a loose stone with
    `ct_haul go`, point at the stone, `ct_collect pick`. Expect the stone to disappear, one Stone to be in **his**
    inventory (not the player's, and not on the ground), and the log to say `he took 1 from Pickable_Stone`.
15. **Out of reach.** Same stone from six metres: refused with the reach sentence, and he must not move a step.
16. **What he refuses.** A raspberry bush, a mushroom, a thistle, a chest, a dropped stack of stone, a sapling and a
    tree: every one refused, each naming a reason, nothing picked, nothing felled, no hammer animation.
17. **The mint, the row that matters most.** `ct_collect pick` a stone, then `ct_collect cancel` **inside the same
    second**, then `ct_collect pick` the same stone again immediately. The second pick must be refused as awaiting
    confirmation, and the world must end up with exactly one Stone from that source. Two Stones is a P0.
18. **A world reload with a cancelled pick outstanding.** Cancel a pick, log out, load a different world, and pick a
    stone there: it must not be refused. That is `ForgetWorld()` having run on the unload; a refusal here means the
    world verb did not fire.
19. **A peer connects mid-pick.** Open the world to a second player while a pick is in flight: the order must end
    with the authority sentence, and nothing must be picked afterwards until they leave.
20. **Where the stone ends up, and that it is stuck there.** After step 14, confirm the Stone is in Gunnar and not
    anywhere else, and that there is no way to take it out — that is the unwired deposit path in §6b, not a defect
    in the pick.
21. **Retire refuses while he is carrying (§6b).** With one Stone in him, `ct_haul retire` must **refuse**, name that
    he is carrying 1 thing, say removing him would destroy it, and name `ct_haul retire force`. His body must still
    be there afterwards. A retire that succeeds here is a P0: the Stone is gone with no drop and no record.
22. **The escape hatch works and tells the truth.** `ct_haul retire force` must remove him and say outright that what
    he carried was destroyed and is not on the ground. Check the ground: nothing must have dropped — the message is
    the whole warning, so it must not be softened.
23. **Retire still works on an empty body, and on a duplicate.** With nothing in him, plain `ct_haul retire` must
    behave exactly as it did before this work. Point at a second body and retire it: carrying something must never
    be what blocks that path, so a duplicate must always be removable.

### What he carries surviving a load (§6c)

24. **A zone unload.** Pick a stone, then walk far enough away that his zone unloads and come back. The Stone must
    still be in him. This is the row the whole of §6c exists for: before it, the stone was simply gone.
25. **A logout and a reload.** Pick a stone, save and quit, load the same world. The Stone must still be in him, and
    the log must not say his body is inert.
26. **An old Gunnar loads unchanged.** Load a world containing a Gunnar saved by a build from before this change.
    His body must come back with his identity, an empty inventory, no fault line and no `Destroyed invalid prefab
    ZDO`. This is the zero-migration row and it is the one that is irreversible if it is wrong.
27. **Death, which is NOT closed.** Let something kill Gunnar while he carries a Stone. Expect the Stone to be
    **lost** — nothing drops. That is the known gap in §6c awaiting an owner decision, not a defect to file; record
    what actually happened so the decision is made on an observation.

Evidence rows for `docs/settlement/cart-and-collection/EVIDENCE.md` stay **pending** until observed, with the
build, profile and scenario recorded.
