# Cart pulling and resource collection: decision record

Status: **accepted for the first cart and collection proof** (contract revision **C1**, 2026-09-17), amended
by **C4** (2026-09-17, D4) and **C5** (2026-09-29, D15 and D16 - the two owner grants that close #415 and the
death door named in `GUNNAR_COLLECTION.md` §6c) and **C6** (2026-10-28, D17 - the collection tour's walking and
load size) and **C7** (2026-10-31, D21 - Gunnar's cosmetic idle hammer swing). D16's fail-closed reading was confirmed
by the owner on 2026-10-28. D20 (the chest take) and D22 (the cart-assisted tour's load into his assigned cart) are
owner decisions of 2026-10-31 recorded without a contract revision of their own.
Authority: the owner's brief *"Concerned Cat — Gunnar cart pulling and Thorstein resource collection"*, given to the
lead on 2026-09-17. It is kept outside the repo in `concernedcat-handoffs/2026-09-17-gunnar-thorstein-work/OWNER_BRIEF.md`,
SHA-256 `7fe97874…7aad2e3`. It builds on #273, #297, #295, #296 and `docs/mods/concerned-foreman/SETTLEMENT_AUTHORITY.md`.
Evidence: the three read-only audits in the same handoff folder under `audits/`:
- `CART_SEAM_AUDIT.md`: Valheim 1.0.12 `Vagon` and `Character`.
- `PICKUP_SEAM_AUDIT.md`: `Pickable`, `ItemDrop`, `Inventory` and `Container`.
- `FOREMAN_RUNTIME_AUDIT.md`: the existing worker runtime, ledgers and cross-product mechanisms.

Later explicit owner requirements supersede conflicting older scope prose. Source code and evidence decide what exists.
Every decision below is scoped to this slice unless it says otherwise.

---

## D1. Worker bodies belong to each product's opted-in worker runtime

Thorstein's body is Concerned Foreman's existing worker actor: `ForemanWorkerAI : BaseAI` on a creature prefab that is
cloned while inactive. Gunnar's body is a Concerned Teamster worker actor built the same way, with its own prefab name.
- **Construction:** the vanilla `BaseAI` and `Tameable` are removed from the inactive clone before any `Awake` runs,
  and nothing live is woken and then stripped.
- **Why a networked body:** `Character.CustomFixedUpdate` runs its motor only for a ZDO-backed, owned body, and a cart
  joint needs a real non-kinematic `Rigidbody`. The render-only companion body (Hulgi's) has neither and cannot pull a
  cart or carry storage.
- **Movement:** the body moves only through the vanilla motor (`BaseAI.MoveTo`, then `Character.SetMoveDir`, then
  `UpdateWalking`). No product code writes a force, velocity, transform or rigidbody pose for a worker or a cart.
- **One body per identity:** this slice creates no home-idle presentation body for Thorstein or Gunnar. The actor-mode
  owner (D9) still reserves the rule that a presentation body and a worker body never coexist, so adding one later
  cannot create a second Gunnar.
- **Appearance:** the owner's references (#298: Thorstein Mullet + Mustache, Gunnar Short Curls + Stonedweller) are not
  applied to a physics body yet. The worker body keeps its base creature's look, and the evidence rows for appearance
  stay **pending**. This is not a claim that the appearance is done.

## D2. Game adapters stay per product; shared code stays game-free

`AGENTS.md` and `NAMING_CONVENTIONS.md` forbid Unity, BepInEx and Jötunn types in `src/Shared`. This slice keeps that
rule:
- Engine-bound adapters live in each product. Two small, proven adapters are copied rather than shared: the vanilla
  console-command registration that fixes #307, and the inactive-clone worker prefab pattern.
- What is shared is game-free:
  - `src/Shared/Workers`: authority facts, actor modes, retry and backoff, bounded clocks, work points.
  - `src/Shared/Interop`: cross-product capability contracts.
  - `src/Shared/Settlement`: orders, collection, custody, journal.
- A shared *game* tier would remove the copies. It needs its own naming-convention change and is recorded as a
  follow-up, not done here.

## D3. Authority for this slice: explicit opt-in, single player or a host with no peers

Work runs only when all of these hold; otherwise it refuses with a stated reason and every existing utility keeps
working:
- the product's worker setting is on (Foreman `Settlement/SettlementRuntimeEnabled`; Teamster
  `Workers/GunnarHaulingEnabled`; both default **off**);
- a world is loaded;
- `ZNet.IsServer()` is true;
- the process is not a dedicated server;
- **no peers are connected.**

A peer joining mid-job pauses the job with `OtherPeersConnected`. Multiplayer and mixed-client support is out of scope
until a compatible-peer handshake and an ownership policy are designed.

## D4. The cart seam: vanilla's own attach, called directly

The seam is the private `Vagon.AttachTo(GameObject)` and `Vagon.Detach()`, bound through Teamster's publicized game
reference (Teamster already binds the private `Vagon.m_instances`). Vanilla's own joint, spring, break force,
continuous `CanAttach` validation and `attachJoint` ZDO flag do the physics.

Refuse to attach unless **all** hold, checked on the main thread in the same frame as the call:
1. The target is a hand cart: a live `Vagon` with a valid `ZNetView` and no `Catapult` or `SiegeMachine` component.
2. This client already owns the cart ZDO. `Interact`, `ClaimOwnership` and `SetOwner` are never called.
3. The cart has been owned long enough for its mass to be current: the body masses sum to
   `m_baseMass + totalWeight × m_itemWeightMassFactor`.
4. `!InUse()`: the container is not open, and there is no joint and no replicated attach flag.
5. Teamster's parking brake is not engaged on it (`BrakeService.EngagedCartId`), and the root body is not `FreezeAll`.
   Gunnar never releases or engages a brake.
6. No `Vagon` on this client has a joint. `AttachTo` detaches every loaded cart, so attaching would steal the player's.
7. The cart is upright (`transform.up.y` at least 0.5 in this slice, stricter than vanilla's 0.1).
8. Gunnar walked there: the hitch distance, measured the way `CanAttach` measures it, is at most 60 % of
   `m_detachDistance`.
9. The puller body is right: a non-kinematic `Rigidbody` on the exact GameObject passed, with gravity and collisions
   on, and mass calibrated per D5.

Right after the call, verify `m_attachJoin.connectedBody` is the puller's body, `IsAttached()` is true and the ZDO flag
is set. If any check fails, call `Detach()` and refuse.

Every teardown path calls `Detach()` **before** the body is destroyed, hidden, disabled or moved: planned stop, cancel,
world exit, logout, player takeover, plugin shutdown. A body that dies, unloads or turns out to be a duplicate while
hitched detaches in the first frame the runtime sees it.

**Amended by contract revision C4 (2026-09-17).** Lost authority used to be on that list, but it is not a teardown:
the body and the cart are both still there. Gunnar stops at once and lets go only where the cart can be parked. Anywhere
else he holds it, motionless, until the player acts (`CONTRACTS.md` §2.7). CART-06, "never releasing a loaded cart
into a roll", decided it.

A player grabbing any cart (`DetachAll`), a joint break, ownership loss, cart destruction or unload, or the cart
tipping ends the haul. It is never re-hitched automatically in the same attempt.

## D5. Pulling strength is calibrated to the player

The vanilla motor restores up to 20 m/s of velocity per physics step whatever the mass, so the puller body's
`Rigidbody.mass` is its pulling strength. Gunnar's body mass is set to the measured local player body mass plus the
cart's `m_playerExtraPullMass`. That is what vanilla gives a player through `SetExtraMass`, which skips
non-`Character` pullers.

The setting `Workers/GunnarPullStrength = MatchPlayer` is the only supported value in this slice, and the change is
applied to Gunnar's own body, never to a cart. Walking pace uses the body's vanilla walk speed, and there is no run.
Vanilla charges no stamina for walking with a cart, so Gunnar has no stamina bypass to add or avoid.

Two limits are this slice's own, documented and configurable, never presented as vanilla values:
- the maximum grade a loaded haul attempts;
- the stall thresholds (see `SPEC.md` CART-05 and CART-06).

## D6. Cart identity is per world load; leases never survive a reload

A cart is keyed by its ZDOID string plus a **world-load epoch**, a GUID minted each time a world opens. Nothing is
written into a vanilla cart's ZDO, except the `attachJoint` flag vanilla's own `AttachTo` and `Detach` write. A lease
binds (worker, cart key, epoch, revision). Any epoch change, cart destruction or unload invalidates it. After a
reload the player reselects the cart. The nearest cart is never chosen and a stale ZDOID is never trusted.

## D7. Cross-product collaboration: a provider-published, versioned, BCL-only capability map

- **Publishing:** each provider plugin exposes one public property, `ConcernedCatCapabilities`, an
  `IReadOnlyDictionary<string, object>`. Its values are
  `Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>`, keyed by contract id and major
  version (for example `concernedcat.haul/1`). Only mscorlib types cross, so separately compiled products agree on
  every type without a shared DLL or a compile-time reference.
- **Discovery:** the consumer finds the provider by plugin GUID through BepInEx `Chainloader.PluginInfos` on its
  first `Update`, then checks a version floor and the contract major. It logs one line, one of `Available`,
  `Absent`, `VersionTooLow`, `ProbeFailed` or `MajorMismatch`, and re-resolves once per world session.
- **Contract definition:** op names, field keys, statuses, phases and reasons travel as stable **names** (never
  ints). They are defined once in `src/Shared/Interop` and compiled into both sides.
- **Mutating ops:** every one carries `requestId`, `expectedRevision` and `providerEpoch`. The same id with the same
  payload answers `AlreadySatisfied`; the same id with a different payload answers `Rejected`.
- **Polling:** the consumer polls. No callback crosses assemblies.
- **Errors:** the provider catches everything and answers `ProviderError`, and the consumer treats any failure as
  `Unavailable`.

## D8. Foreman's settlement journal is the only material-custody truth

- **Order ownership:** Thorstein owns the collection order.
- **Gunnar's role:** Gunnar holds cart leases and haul legs keyed by Foreman's opaque ids, kept in memory for this
  slice. He never writes custody.
- **Standalone hauling:** a haul with no Foreman order records no custody. Its cargo stays physical in the cart's
  container and is reported, not credited.
- **Loading and unloading:** Thorstein performs cart loads and unloads, journaled in Foreman's settlement journal
  with the cart inventory as a custody location.
- **Pre-existing cargo:** it is recorded as a baseline when the lease is used for an order, and it is never counted,
  unloaded or refunded as gathered material.

## D9. Carried materials and issued tools live in the worker body's own persisted inventory

The Foreman worker prefab is registered at plugin start, before any world loads, and it is persistent. Its identity
(`WorkerKey`) and its `Inventory` live in its **own** ZDO, saved and loaded with vanilla's `Inventory.Save/Load`,
the same way `Container` persists items. Carried Stone and Wood, and the issued axe and hammer, are then written by the
same world save as the chest and the pickables they came from. A crash rolls all of them back together.

On world load the runtime re-binds the body by its stored identity. A second body carrying the same identity means
`NeedsAttention`; neither body is destroyed automatically.

**This is a world-save write, authorised here.** It writes mod data into the worker body's own network object, and
never into a vanilla object. The authority is the owner's 2026-09-17 brief: ARCH-02 says unloading must not lose
cargo, DATA-02 says every unit has one custody place, and DATA-05 requires honest accounting. The worker clone is also
stripped of its loot table and default gear, and its prefab is registered before any world loads
(`PICKUP_SEAM_AUDIT.md` §3.2, §6.2).

The journal records intents and results. Rows that describe effects the world rolled back in a crash are voided by
the world-save marker rule (`CONTRACTS.md` §5.5). After that, the journal is **reconciled** against the actual
inventories on load:
- A mismatch means `NeedsAttention`, with the evidence kept.
- An uncertain transfer is never replayed, and compensation is never minted.

This also closes the tool-loss path the Foreman audit found beyond #299: a tool given to a non-persisted body was
destroyed on despawn, unload or relog.

The Teamster worker body is persistent and re-bound the same way, so a reload does not teleport it.

**Amended by C5 (2026-09-29).** This paragraph used to end "It carries no inventory". That stopped being true when
#381 gave Gunnar a pick: his body stores its own inventory in its own network object, in Foreman's format,
byte-compatible and for the same reason (`GUNNAR_COLLECTION.md` §6c). D15's gates depend on it.

**Uninstall rule:** removing Foreman while a worker body holds items would let the host delete that body as an unknown
prefab. The player guide therefore requires "Release everything" (return carried items and tools to a chest or the
player) before uninstalling, and the Foreman README says so.

## D10. Natural sources: an evidence-based allowlist

Collection accepts only sources that pass the predicate in `CONTRACTS.md` §6. It is built from the pickup audit
(bundles and decompile):
- prefab allowlist `Pickable_Stone` and `Pickable_Branch`;
- component shape and untagged root;
- vanilla configuration and yield;
- no creator;
- a pickable state.

The name allowlist is load-bearing: `Pickable_HardRockOffspring`, bred from a hammer-placed rock, passes every other
test.

**Locations are excluded.** Stones and branches inside a location (`Location.IsInsideLocation`), including the start
temple, are not collected. They share the natural prefab identity, and excluding every location is the conservative
reading of "natural loose sources only".

The yield is what the game actually spawned for this order's own pick, traced and taken in the same call. It is never
an estimate granted. A source that cannot be classified with evidence is not collected.

## D11. Work scope for an order

An order snapshots its scope at acceptance as a `WorkScope`, with its source, geometry, anchor, revision and world-load
epoch:
- **Assigned:** Foreman's existing harvest designation circle, 4–48 m, as it stands at acceptance. A Cartographer
  work-area provider (#296) is a later optional source.
- **Fallback:** when no area is assigned, a **visible preview** of a 30 m circle centred on the **latest valid
  respawn anchor** (the claimed bed's spawn point, else the world start). It never follows the moving player.
- **Invalid assigned area:** an assigned area that is deleted, stale, unloaded or unavailable never falls back or
  expands. The order pauses with a reason.

`DesignationBook` semantics and tests are unchanged.

## D12. Readiness

Accepting a collection order requires Thorstein to be recruited **and** to hold a usable issued axe **and** hammer.
That is the starter-tool rule of #295 and GATHER-06, evaluated with `WorkerReadiness.Assess` against the `ForBuilding`
tool list. Picking up stones and branches wears no tool: the per-action list stays `ForGathering`, which is empty, so
an unused axe or hammer never loses durability. A tool that later goes missing or breaks pauses readiness. It never
relocks recruitment.

## D13. Scope bounds for the slice

These bounds apply to the whole slice:
- one active collection order per Thorstein and one active haul per Gunnar;
- sources: natural loose stones and branches only;
- work happens only in loaded ground and only while the host is running.

Out of scope: fleets, offline or background gathering, mining, chopping, food, crops, player drops, graves, arbitrary
chests, construction, combat, portals and sea transport. #280's placement decision stays parked, Evergreen stays
suspended, and #282's broader harvesting stays open as deferred scope.

## D14. Narrow rule amendments (recorded in `CLAUDE.md` and `AGENTS.md` in the same change)

1. **Companion presentation versus workers.** The companion rules govern presentation. While an identity performs an
   explicitly ordered job, its body is owned by that product's opted-in worker runtime under this record (Foreman:
   `SETTLEMENT_AUTHORITY.md`; Teamster: D1–D6 here). The presentation body and the worker body never coexist.
2. **Teamster carve-out.** The Gunnar worker runtime is off by default and has its own `TeamsterFeature` row. Within
   that runtime Gunnar may:
   - pathfind **his own body**;
   - rely on vanilla ownership held by the host;
   - attach and detach a player-assigned cart through vanilla's own methods.

   It still never teleports a cart, changes cart mass or physics defaults, writes forces or velocities, bypasses
   stamina, or writes mod data into a vanilla object's ZDO. Every other Teamster rule and default is unchanged, and
   "no pathfinding" continues to mean "no autopilot for the player's cart".
3. **Feature access versus work authority.** "Ambiguous evidence grants" applies to a product's **feature access**
   only. Worker authority, recruitment into a worker runtime, and resource or cart custody fail closed.
4. **Parallel agents.** One issue per agent and one agent per worktree. Only the lead integrates into `main`, deploys,
   or operates the game.
5. **Live criteria.** A PR whose issue carries live gameplay criteria uses `Refs #`, not `Closes #`. Such an issue
   closes only on observed evidence.
6. **Foreman ladder traversal** (owner decision 2026-11-01, #334; the owner's words and question are in
   `docs/mods/concerned-foreman/WORKER_LADDER_AUTHORITY_2026_11_01.md`). The single exception to "moved only through
   the vanilla motor". Built by #334's binding slice (2026-11-08), and only this:
   - **Who and when.** Thorstein's own worker body, on an explicit `cf_worker climb here | <x> <y> <z>` order, with
     `Ladders/NpcClimbing` on (off by default) and D3's authority granted (opted in, host, not dedicated, no other
     peers). Either switch off refuses the order and ends it. His actor mode is held through Concerned NPC's arbiter
     for the whole order, so presentation, collection, construction and a despawn all see him as busy.
   - **Where.** Only along a Concerned Ladders link the #501 receiver published after measuring it against the real
     colliders and his real capsule - supported foot, mount lane, vertical lane, supported top - reached by a route
     whose walk edges are vanilla navigation-mesh paths verified in height at both ends. A fresh survey runs at
     admission and again immediately before every ladder entry, and an entry whose link generation moved is refused.
     Vanilla ladders are not admitted.
   - **How.** One scoped motor, `src/ConcernedForeman/Runtime/Ladders/WorkerClimbMotor.cs`. While it holds him his AI
     reports sleeping - vanilla's own switch that makes `Character.UpdateMotion` skip walking - and `ForemanWorkerAI`
     neither plans nor calls `BaseAI.MoveTo`; his own rigidbody is made kinematic and its original flag is restored
     after; each step of the checked path is one rigidbody position write, after a fresh capsule sweep from where he
     is to where he is going. Walking legs stay on vanilla's motor.
   - **Every exit gives the body back where it stands.** Arrival, a blocked or refused leg, a cancel, a world unload,
     death, a lost, faulted or unsimulated body, the switch going off, lost authority and plugin teardown all restore
     the motor first; the identity is released only once that succeeds. An interrupted climb is never finished by
     placing him at a landing - vanilla physics has him from where he is (lead default pending owner confirmation:
     a non-player takes no fall damage in 1.0.12, so this costs nothing but a drop).
   - **Not granted.** No teleport, entry snap, velocity, force or rotation write, ownership takeover, RPC, cart or
     cargo movement, generic worker positioning, other body, player motor, or world-save write. Concerned Ladders
     never drives a worker.
   - **Known limit, by design.** Every survey - at admission, before each entry, and every two seconds on the way -
     counts any other character as a blocker. Somebody standing at the ladder's foot, in its lane or on the landing
     when one runs refuses the entry or ends the climb. That is the fail-closed direction; the player watches from a
     few metres away. The body's capsule is measured once per order so an unchanged ladder keeps its generation.
   - **Proof.** The #400 capability audit pins the motor's two kinematic writes and one position write; the #334
     worker climb audit pins the sweep directly in front of that write, the worker's own rigidbody, who may create,
     request, start and end a lease, and the ground-motor suspension. Planted escapes:
     `tools/tests/test_foreman_worker_climb_audit.py`; every exit: `WorkerClimbOrderTests`. Text audits, so they do not
     prove control flow. **Never observed in game**; gate G5 is OWNER GO-AROUND PENDING.

---

## D15. Gunnar may deposit into a container the player marked DEPOSIT or BOTH

Status: **accepted** (contract revision **C5**, owner decision 2026-09-29, closing #415).
Authority: the owner's answer to #415, given to the lead on 2026-09-29 in the BLD release-readiness brief. #415 asked
one question - *may Gunnar's opted-in collection runtime move items from his own inventory into a vanilla
`Container`'s inventory, when the player has explicitly marked that container DEPOSIT or BOTH?* - and the answer is
yes, scoped to exactly that.

**What is granted.** Gunnar's opted-in **collection** runtime may move items **out of his own inventory** and into a
vanilla `Container`'s inventory, when **that exact destination container** carries the player's explicit `Deposit` or
`Both` mark in `NpcContainerDesk` and every gate below holds at the moment of the move.

**Three vanilla calls, named here because the repository's convention is that each one is named.** A whole stack moves
through `Inventory.MoveItemToThis`, which adds to the destination and removes from the source inside one vanilla call
and keeps the moved instance, so a tool's wear, a crafter's name and a world level survive. A **part** of a stack has
no such call in vanilla, so its own `ItemData` is cloned for exactly the units moving, `Inventory.AddItem` puts that
clone in the destination, and `Inventory.RemoveItem` then takes exactly what **arrived** off the original stack. This
is Concerned Foreman's audited precedent, not a new pattern.

**This is not "spawning item instances", and the distinction is load-bearing rather than a nicety.** The clone is of a
stack that is already in Gunnar's hands, and the units it carries are removed from that same stack in the same
operation; total units are conserved across the pair, and the measurement below is what proves it rather than this
paragraph. What the negative list forbids is creating material **from a name** - an `ObjectDB` lookup, an
`Instantiate`, a recipe, a count invented to make a number balance - none of which appears in this path. An
implementation that reached `AddItem` with anything other than a clone of a stack it is simultaneously removing from
would be outside this grant.

**What is not granted**, each named because #415 named it:
- no chest chosen by proximity, and no "nearest chest" inference anywhere in the path;
- no write into a container the player has not marked, or has marked `Take` only, or has marked and then unmarked;
- no ownership takeover of a container, an item or an inventory;
- no synthetic or replacement resources, and no spawning of item instances;
- no extra copies of cargo, and no teleporting of cargo;
- no mod data written into a vanilla object;
- no bypass of a ward, a privacy setting, container ownership, or vanilla's own in-use check;
- no use of the death-drop behaviour of D16 for a voluntary retirement or cancellation;
- no widening of any other Teamster mutation capability.

**The gates, all re-asked in the frame of the move**, not cached from the tick that planned it:
1. Teamster's master switch (`General/Enabled`) and the collection switch (`Workers/GunnarCollectionEnabled`, off by
   default);
2. the start-up capability probe;
3. the shared work-authority rule of D3 - opted in, a world loaded, `ZNet.IsServer()`, not dedicated, **no peers
   connected**;
4. a body the census bound, whose inventory record `WorkerInventoryRecord.Trust` believes;
5. the player's `Deposit` or `Both` mark on **that exact container**, resolved the way the mark was recorded
   (`ContainerPermissionRuntime.Allowance`), never by a caller's own reconstruction of the identity;
6. this client owning the container's network object;
7. nobody having the container open (`Container.IsInUse()`), and no cart it rides being in use;
8. the ward and the container's own privacy setting allowing it;
9. the container within Gunnar's reach from where he is standing.

**Measurement and conservation.** The destination is counted before and after. Only what **actually arrived** is
removed from Gunnar. A full or unavailable container leaves the remainder **in him** and says why. A fault halfway
is recorded **uncertain**: never retried blind, never compensated, never minted. The permission is spent by the
transfer that records it, so a replay cannot duplicate cargo; an interrupted transfer asks the container again,
which re-reads it. An edited permission, a replaced container, or a container that moved fails closed.

**Where it is enforced.** One file, `Adapters/Workers/GunnarDepositPort.cs`, pinned by `tools/validate_repo.py` the
way `GunnarCollectionPort.cs` is: each of the three authorized calls matched **verbatim** and once, refused everywhere
else in the product (inside `Adapters/Workers` and out), and every other forbidden token still refused inside it. The
three tokens were not audited at all before this change, which is the gap #401 named.

The pin is proved by planted violations in `tools/tests/test_teamster_carveout.py` rather than asserted: a move in
another worker file, a move in `Domain/`, a second file of the port's name elsewhere, a second move in the port, a
space before the paren, a newline between receiver and member, **the move reversed into a withdrawal**, an add of
anything but the clone, an add into another inventory, **a remove of the count that was asked for instead of the count
that arrived**, a remove from the destination, and each port failing to inherit the other's allowance.

**How this stands beside D8, including the case the first draft of this paragraph skipped.** D8 says Foreman's
settlement journal is the only material-custody truth, that Thorstein performs cart loads and unloads, and that
*Gunnar never writes custody*. There are two cases and they need different answers:

- **A deposit outside any Foreman order** - which is every deposit this grant reaches today, because Gunnar's
  collection runtime takes no Foreman order. D8's own standalone-hauling rule already covers it: the cargo is
  physical, it is reported rather than credited, and no custody row is owed to anybody. Teamster's
  `CargoLedger`/`CollectionAccount` records where the material physically is, which is this product's accounting and
  not a custody claim.
- **A deposit while Gunnar holds a lease keyed by a Foreman order.** D8 is unchanged and wins: Foreman's journal
  would remain the custody truth for those units, and Teamster's ledger a physical record beneath it, never an
  authority over it. This grant does **not** authorize Gunnar to write a custody row, to close one, or to be the
  record a reconciliation believes.

  **That case is unreachable today, and by structure rather than by intention.** Teamster contains no Foreman order
  plumbing at all: no lease, gate or collection type in this product carries a Foreman order id, D8's "keyed by
  Foreman's opaque ids" describes a slice nobody has built, and the deposit is reachable only from the collection
  runtime, which takes no order from anywhere. The shared arbiter additionally means a haul and a collection round
  cannot both hold Gunnar. So there is no code path from a Foreman order to this grant, and the leaf that builds one
  is the leaf that must decide the hand-off - at which point this bullet becomes a requirement on it rather than a
  description of the present.

## D16. A **Teamster** worker body (Gunnar) that dies returns what it carries through vanilla's own drop

Status: **accepted** (contract revision **C5**, owner decision 2026-09-29).
Authority: the same owner brief of 2026-09-29. It closes the door `GUNNAR_COLLECTION.md` §6c named and refused to
open quietly.

**What is granted.** When a Teamster worker body **genuinely dies**, its runtime may use vanilla's own item-drop
behaviour to return the body's carried inventory to the world, rather than letting the destruction take it.

**What "genuinely dies" means, and why the definition is load-bearing.** The body's own death, observed from the
body - `Character.IsDead()` on a body that was alive in a previous observation, or vanilla's own death path running
on it. It is **not** a retirement, a cancellation, a despawn, a zone unload, a logout, a world change or a plugin
teardown. Those are the lifecycle paths of D9, §6b and §6c and they stay exactly as they are: a retire still refuses
while a body carries anything, `retire force` still destroys and still says so, and an unload still relies on the
body's own persisted inventory. **Voluntary cancellation and retirement must never reach the death path**, and the
validator refuses a death-drop call from any other verb.

**What is not granted:**
- no drop on any lifecycle event that is not the body's own death;
- no minting: exactly what the body holds, once, and nothing recreated from a name;
- no silent deletion of a drop that failed - a failure is recorded and left as durable reconciliation evidence, and
  the material stays where the last successful measurement put it;
- no change to the cart, or to anything else the body was attached to, beyond the detach D4 already requires (the
  drop of course changes the world - that is the thing being authorized);
- no drop of items the body never held.

**Ordering, because a cart is attached to the body.** The cart is detached **first**, through D4's own teardown
ordering, and the cart and everything in it are left exactly as the game left them. Only then does the drop run.

**Ward, ownership and privacy.** A death drop is vanilla's own, at the body's own position, of items the body
already holds; it reads no container, so no container check applies to it. The owner's refusal of "bypassing
ward/container ownership/use checks" is not limited to D15 all the same, and this grant does not authorize the drop
to reach into, past or around any of them. Placing items anywhere but where the body fell would be a different
decision.

**Accounting.** Every carried unit becomes either a real world drop or stays accounted for as held-and-unresolved.
The ledger records the transition; nothing is re-credited and nothing is re-acquired. An interrupted death-drop
fails closed and keeps its evidence.

**Where it is enforced.** `Adapters/Workers/GunnarDeathDropPort.cs`, a component on Gunnar's body, subscribed to
the body's own `Character.m_onDeath` - vanilla's death callback, which vanilla runs from `Character.OnDeath` only on
the owner, after it has marked the body dead and before it destroys it. One call, pinned verbatim and once by
`tools/validate_repo.py`: `Humanoid.DropItem(_inventory, item, count)`, the stack's own count from the body's own
inventory - what Foreman's worker body does on its own death (#400 pins that one). The validator's
`D16 death-drop audit` pins the rest: the call sits inside the death handler, behind the body's own `IsDead()` and a
refusing `WorkerDeathDrop` verdict; the handler is named only by its declaration and the subscription; and
`m_onDeath` is touched nowhere else in the product - so no retire, cancel, unload or teardown verb can reach it.
The planted escapes in `tools/tests/test_teamster_carveout.py`'s `DeathDropIsNarrow`, each verified to pass the
validator from before this change, are refused.

**How the paragraphs above were read where they leave a choice**, so a reader can disagree with the reading rather
than discover it. **The owner confirmed this reading on 2026-10-28**, asked directly by the lead: D16 stays
fail-closed exactly as built, and every gate below that leaves material unresolved instead of dropping it is the
intended narrowing, not a defect to be widened later.

- *Gates.* The drop runs only when Teamster's master switch and the collection switch are on, the start-up probe
  verified every member it binds, the shared work-authority rule grants (D3), and the body had read its own stored
  inventory. Each of those failing leaves the material **unresolved** - recorded, not dropped. That is narrower than
  "its runtime may use vanilla's own drop" and never wider: a drop is a world write, and every other world write this
  product makes fails closed on the same four questions.
- *The cart.* The detach runs through the hauling runtime's own frame observation (the D4 teardown that would have
  run on the next frame anyway, run now). If a cart's joint still holds the body afterwards, **nothing is dropped**:
  "only then does the drop run" is read as a precondition, and an interrupted death drop fails closed and keeps its
  evidence.
- *Measurement.* Each stack is counted before and after its drop; the ground is credited only when vanilla answered
  that it dropped, nothing threw, and exactly the stack's count left him. Anything else is unresolved, and a count
  that cannot be reconciled is never balanced away.
- *Durable evidence.* Every genuine death - dropped, refused or interrupted - appends one row for the death and one
  per stack to a per-world, append-only file beside Teamster's other sidecars
  (`teamster_worker_deaths_<world>.tsv`), and the same rows go to the log at warning level. The log alone is replaced
  on the next launch, so it is not "durable". An existing file that cannot be read is left alone and the rows go to
  the log only.
- *The ledger.* `CollectionAccount.RecordDeath` moves what this session saw him take from `Carried` to
  `DroppedOnDeath` or `UnresolvedAtDeath`. Units he carried from an earlier session were never acquired by this
  session's ledger and are not acquired on paper to make it balance; the death record has them.
- *Nothing may cause the death.* "Voluntary cancellation and retirement must never reach the death path" is read
  to cover causing a death as well as calling the handler: every way to take health away (`SetHealth`, `UseHealth`,
  `SetMaxHealth`, `.Damage(`, `ApplyDamage`, `RPC_Damage`) is refused anywhere in Teamster.
- *Vanilla inside the call.* `Humanoid.DropItem` also plays the body's `interact` trigger and the drop effects, and
  places each stack a metre in front of the body with a small push. Those are vanilla's, inside the one pinned call,
  as `Pickup`'s destruction of the item it takes is; nothing in this product calls `SetTrigger`. (Since D21, one file
  does - the idle hammer port, for a cosmetic swing - and nothing on the death path does.)

**Never observed in game.** The port binds Unity; the decision, the classification, the accounting and the record
format are unit-tested (`WorkerDeathDropTests`), the shape is pinned, and nothing has been watched happening.

## D17. Gunnar's on-foot collection tour walks his own body; a load is 225 units, any size, at most 24 sources

Status: **accepted** (contract revision **C6**, owner decision 2026-10-28, asked directly by the lead on #381's
collection tour, PR #445). It answers the owner questions `GUNNAR_COLLECTION.md` §6f listed and decided none of
itself.

**Walking.** D14.2's "pathfind **his own body**", written for the hauling runtime, **covers the on-foot collection
tour**: Gunnar may walk his own body between the loose sources the survey found and to the chest the player chose,
under exactly D14.2's terms - his own body only, through the vanilla motor, never a teleport or a portal, work
authority per D3 (opted in, the host, not dedicated, nobody else connected), and off by default behind the
collection runtime's own switch (`Workers/GunnarCollectionEnabled`), not hauling's. It is **on foot**: nothing here
lets a tour use a cart, and a cart-assisted round, which would put items into a cart's container, still needs its own
grant. (Granted on 2026-10-31 as **D22**, for the cart the player assigned him, and nothing wider.)

**Load size.** A load is sized like a vanilla player wearing Megingjord - **225 units** - whatever Gunnar wears, as
#381 states. Vanilla does not weigh a non-player down, so this remains the product's own discipline; the slot limit is
still the game's.

**The 24-source trip.** `StopSequencer.MostStopsPerRound` (24) capping a load at 24 sources is accepted **as a
planning limit**: the rest of a larger look is the next load, never dropped and never walked one source a trip.

**Any load size.** A load is not refused for being small. A tour that would carry **nothing** still does not start -
that is an empty order, not a size judgement - but one or two units are walked to the chest like any other load.

**What is not granted:** no cart use on a tour, no felling (`TreeBase.Damage`), no idle gesture
(`ZSyncAnimation.SetTrigger` - granted separately on 2026-10-31 as D21, outside the tour), no chest-to-chest take, no ownership takeover, no movement of anyone but Gunnar, and no
change to D15's deposit or D16's death drop. Each of those still needs its own owner decision.

**Never observed in game.** The tour's rows in `GUNNAR_COLLECTION.md` §10 are OWNER GO-AROUND PENDING.

## D18. A worker body may wear its character's own hair and beard, locally (all three companions)

Status: **accepted** (owner decision 2026-10-29, #295 slice S6, option (b), asked by the lead). It applies to
every companion with a worker body: Thorstein first, and Gunnar and Sunniva when their products use it.

**Decided.** The worker body stays the clone it already is: Foreman's is the `Dverger` creature, under the same
prefab name and keys. Concerned NPC's `NpcWorkerDresser` attaches the character's own player hair and beard pieces
on top of it, as **local child objects** under the body's head joint. The pieces are the same ones the presentation
figure wears, cloned dark and checked first, exactly as the figure's are. Each product offers this behind its own
setting, **on by default** so the playtest judges it; Foreman's is `Thorstein/WorkerLook`. A piece that will not
attach, or that is not drawn where it was put on that rig, is taken off, and the body keeps its plain look.

**Not granted:**
- No prefab rebase, no new prefab name, no saved-body migration.
- No write to the body's network object, inventory, equipment, renderer or material. Hair and beard pieces are
  tinted on themselves only.
- Nothing networked or saved. The pieces leave with the body and come back only when the role dresses it again.
- Nothing on any object but the registered role's own saved worker body. `NpcWorkerDresser.TryDress` refuses
  unless the object's saved prefab is the role's contracted worker prefab **and** its saved identity is the role's
  identity. `tools/validate_repo.py`'s NPC figure-parts audit pins that the guard runs first and still makes both
  comparisons.

**Never observed in game.** OWNER GO-AROUND PENDING.

**Gunnar, as built by #458 (lead default pending owner confirmation, 2026-11-08).** Teamster now has Gunnar's
presentation figure (`docs/mods/concerned-teamster/GUNNAR_PRESENTATION.md`). Two details the recorded decisions
leave open were settled the smallest way:
- **His worker body is not dressed yet.** D18 allows it behind a Teamster setting of its own; #458 does not build
  it, and the Teamster figure audit names `NpcWorkerDresser` nowhere until a leaf adds the setting, the pinned call
  and its plants.
- **Megingjord is not drawn on the figure.** #381's appearance line is leather pants, bare torso, Megingjord. The
  shared dresser dresses hair, beard, chest and legs only, so the figure wears leather trousers and no chest piece,
  and the belt is left off rather than adding a slot to Concerned NPC from a Teamster issue. His carrying capacity
  is unaffected (D17).

## D19. Thorstein may fell the four small trees, through vanilla's own damage, inside an explicit work area

Status: **accepted** (owner decision 2026-10-31, #282 and the wood remainder of #296, asked by the lead). It is in
two parts: the decision as first given, then a correction made once the installed game had been read. Recorded on
2026-10-31.

**First given.** Small trees only, saplings and small trees, felled through vanilla `TreeBase.Damage` with the real
issued axe. Felling happens only inside an explicit work area, is off by default, and leaves wood dropping exactly as
vanilla drops it. The owner added that cutting the log he felled (`TreeLog.Damage`) and picking up only the wood his
own cuts produced (same-call custody, by instance id) are inside the permission.

**What the installed game says** (Valheim **1.0.21**). The main prefab bundle `SoftRef/Bundles/c4210710` (756,139,651
bytes) was read read-only with UnityPy, and `assembly_valheim.dll` was decompiled. Nothing was extracted or committed.
- **Valheim's small trees are not `TreeBase`.** They are `Destructible` objects with a `DropOnDestroyed` component, and
  they drop their items in the same call that destroys them. There is no log and no falling physics:

  | Prefab | Tool tier | Health | Drop table |
  |---|---|---|---|
  | `Beech_small1` | 0 | 20 | 2-3 picks: Wood (weight 1), Resin (weight 0.2) |
  | `Beech_small2` | 0 | 20 | 2-3 picks: Wood (weight 1), Resin (weight 0.2) |
  | `FirTree_small` | 0 | 40 | 4-5 picks: Wood (weight 1), FirCone (weight 0.1), Resin (weight 0.1) |
  | `FirTree_small_dead` | 0 | 40 | 4-5 picks: Wood (weight 1) |

- **There are no natural saplings.** Every `*_Sapling` is `Destructible` + `Piece` + `Plant`, which is player-planted
  cultivation.
- **Berry bushes carry `Pickable`.**
- **`TreeBase` exists only on full-size trees,** and destroying one always spawns a falling physics `TreeLog`.
- **An owner-side RPC runs in this process synchronously** (`ZRoutedRpc.InvokeRoutedRPC` with `targetPeerID == m_id`
  calls `HandleRoutedRPC` directly).

**Corrected decision (owner, option A).**
- **Granted:** `Destructible.Damage` on exactly the four prefabs above, all tool tier 0, one hit per call.
  - The call is pinned in the single file `src/ConcernedForeman/Runtime/Collection/WorldFellingPort.cs`.
  - The hit is built from the issued axe's own item data: its damage, tool tier, item level and skill, with the
    worker as attacker. It is the axe's plain damage: no skill factor, attack multipliers or status effects are
    applied. Vanilla's damage handler resolves resistances, tool tier and health.
  - Durability follows vanilla's own rule. Vanilla wears a weapon on a hit only when the attacker is a player
    (`Attack`, `if (m_weapon.m_shared.m_useDurability && m_character.IsPlayer())`), so Thorstein's axe is not worn by
    felling. A broken axe is refused.
- **Granted, for the drops:** taking only the drops his own hit produced, tracked by instance id in the same call. This
  is allowed only when this process owns the tree, so the game's handler ran in the call; otherwise it fails closed.
- **Where:** only inside an explicit work area, meaning a map area the player chose or the harvest designation. The
  default camp circle never authorises felling.
- **Clearances:** never within 15 m of a player or of a player-built piece, and never through a ward.
- **Default:** off (`Collection/ThorsteinFellsSmallTrees`).
- **Decided but unused:** `TreeBase.Damage` and `TreeLog.Damage`. There is no call site.
- **The validator** (`check_foreman_felling_scope`, "D19 felling audit") covers the whole of Foreman, less `obj/`
  and `bin/`, plus every shared tree its project file compiles in (read from the project file):
  - it allows exactly one `.Damage(`: the pinned `tree.Damage(hit)` in the felling port;
  - it refuses `TreeBase`, `TreeLog`, `DestroyNow`, `ApplyDamage` and `RPC_Damage` anywhere in that scope, until a
    mature-tree scope is decided.
  - a project file that is missing, a shared include it cannot follow, or a linked tree that is missing fails the
    audit rather than shrinking its scope;
  - the Concerned NPC library, referenced as a separate project, is outside this walk. It has its own audits and
    does not fell.
- **Excluded:** saplings (cultivation), berry bushes (food), and every other prefab.

**Built in the first leaf (#282).**
- `cf_collect fell` lands one hit on the eligible small tree nearest Thorstein, inside the area an order would use.
- The drops stay on the ground; nothing is taken or counted yet.
- Taking the wood, and felling as part of a collection order, belong to the next leaf, which will use the same-call
  custody granted above.
- The eligibility rules are game-free in `src/Shared/Workers/Felling/SmallTreeFelling.cs`, so Teamster's Gunnar can
  reuse them. **D19 grants felling to Thorstein only:** Gunnar felling anything still needs its own owner decision.
- The felling audit's limit is stated rather than left to be found. It is a text audit with comments stripped, so a
  call reached through a delegate spells none of these tokens and is invisible to it.
  `tools/tests/test_foreman_felling_audit.py` plants each escape (in `Runtime/`, `Ui/`, `Plugin.cs`, `Domain/` and two
  linked shared trees). Each plant is refused by the rule and passes a validator without it.

**Built in the second leaf (#282 leaf 2, the wood remainder of #296).** Felling inside a collection order, with the
same-call custody granted above.
- **Survey.** With the switch on and an explicit work area, the order's survey also records the four small trees, as a
  Wood source of kind `SmallTree`, planned at their most Wood (beech 3, fir 5). A tree within 15 m of a player-built
  piece, under a ward, in a dungeon or inside a world location is inaccessible; one whose ownership, ward, location or
  building clearance could not be read is unknown. The default camp circle never records one, in the adapter and again
  in the game-free scheduler.
- **Labour tiers.** Cheapest labour first: loose stones and branches, then brush, then small trees. A lower tier always
  wins whatever the walk, so a tree is felled only when no branch the order can use is left.
- **One hit per swing.** At the tree he lands one hit through the same pinned `tree.Damage(hit)` as `cf_collect fell`,
  every D19 gate re-asked in that call, then waits a swing before the next.
- **Only his own Wood, only what is still needed.** On a fell the pickup port traces, by instance id, only the Wood
  drops that hit spawned, owned here and beside the tree, up to the smallest of what the order still needs, what fits
  on him and what fits the chest, and takes them through the existing pinned `Humanoid.Pickup`. Resin, fir cones and
  Wood beyond that limit are never guarded or touched and lie on the ground as ordinary items.
- **Journal first, no schema change.** Every hit is a pickup in the settlement record, started before it and finished
  after it: a standing tree finishes `Refused` with no drops, a fell finishes `Picked` with exactly the traced drops.
- **A cleared area ends exhausted.** An order that felled a small tree and finds nothing more pauses with
  `SourcesExhausted`, not "no eligible sources".
- The hit's verb, `WorldFellingPort.StrikeOnce`, is pinned to its declaration and exactly two callers (the command and
  the order's strike) by `check_foreman_felling_strike_callers`, the "#282 felling strike audit"; its plants are
  `tools/tests/test_foreman_felling_strike_audit.py`.

**Lead defaults pending owner confirmation (2026-11-08).** D19 leaves these details open. Each is the smallest,
most fail-closed reading of the decision:
1. **No per-order opt-in.** The switch plus an explicit work area is the opt-in. An order given with the switch on in a
   map area or the harvest area may fell after the branches; with the switch off, or in the camp circle, it never does.
2. **The brush tier is empty.** The installed game has no natural sapling, its berry bushes are food, and D19 grants no
   bush, so no source is in the tier. It is named only to keep the owner's order.
3. **Wood only.** Resin and fir cones are not order resources and are left on the ground, never taken.
4. **A tree may be chosen for fewer units than it can drop.** The take limit, not the plan, stops overcollection.
5. **Cadence and bounds.** One hit per 1.5 s; a tree that has not fallen after 8 hits is given up for the order; with a
   player within 15 m he waits up to 30 s at the tree, then leaves it for that survey only.
6. **Locations and dungeons are excluded for trees too**, as D10 excludes them for stones and branches.
7. **No swing animation.** `SetTrigger` stays refused in Foreman; vanilla's own hit effects play.
8. **"Exhausted" is per session.** A reload forgets which trees this order felled, so a cleared area found only after a
   reload reads "no eligible sources".
9. `cf_collect preview` still counts branches only.

**Not granted:**
- full-size trees, logs, stumps, bushes or anything not on the list;
- felling outside an explicit area;
- teleporting, forces, or movement of the player;
- ownership takeover;
- minted, estimated or replacement wood;
- a second copy of any drop;
- mod data in a vanilla object.

**Never observed in game.** OWNER GO-AROUND PENDING (docs/mods/concerned-foreman/COLLECTION.md rows F1-F11).

## D20. Gunnar may take from a chest the player marked TAKE or BOTH

Status: **accepted** (owner decision 2026-10-31, #381, asked directly by the completion lead; the issue comment of
that date records it). **Implemented** in the same change as this entry.

**What is granted.** Gunnar's opted-in collection runtime may move items **out of a vanilla `Container`'s
inventory into his own**, when **that exact source container** carries the player's explicit `Take` or `Both` mark
in `NpcContainerDesk`, measured on both sides, with the remainder left in the chest, off by default and pinned like
D15.

**The calls.** D15's three, with the roles exchanged: `Inventory.MoveItemToThis` for a whole stack, and for part of a
stack `Inventory.AddItem` of a clone of that stack's own data into Gunnar followed by `Inventory.RemoveItem` of exactly
what arrived, off the chest's stack. D15's reasoning for why the clone is not "spawning item instances" applies
unchanged: the clone is of a stack in the chest that the same operation removes from, and the measurement proves the
units are conserved.

**The gates, all re-asked in the frame of the move:** D15's nine, with `Take` where D15 has `Deposit`, plus the take's
own switch: Teamster's master switch, the collection switch and `Workers/GunnarChestTakeEnabled` (**off by
default**); the start-up probe; the work-authority rule of D3; a body whose inventory record is trusted (an
unwritable or unreadable record is handed nothing more, as D15 already contains it); the player's `Take` or `Both`
mark on that exact container, resolved the way it was recorded; this client owning it; nobody having it open, and no
cart it rides in use; the ward and privacy setting; and reach.

**Measurement.** Both sides counted before and after; only what arrived in Gunnar is removed from the chest and
credited; what does not fit stays in the chest; a fault halfway is uncertain and never retried blind, compensated or
minted; a spent permit cannot record a second transfer.

**Where it is enforced.** `Adapters/Workers/GunnarTakePort.cs`, pinned by the scope audit (each call verbatim and
once, refused everywhere else in the product) and by `#381 chest-take audit`; planted escapes in
`tools/tests/test_teamster_take_audit.py`, each refused by those rules and passing a validator without them.

**How the decision was read where it leaves a choice** - each a **lead default pending owner confirmation**, the
smallest and most fail-closed reading the brief allows:

- *What may be taken.* One item, named exactly by the player (`ct_collect take <item> [count]`), with no wildcard, no
  list and no "everything". A name the game does not know is refused. The owner's words name the chest, not the
  items; the narrowing to one named item per order is this record's.
- *How much.* At most what fits under D17's carry limit (a vanilla player's base limit plus Megingjord's bonus, less
  everything he already carries) and his own slots. Without a count, that much of the one named item.
- *Where he stands.* Within reach of the chest. Nothing here walks him; a manual chest-to-chest mode that walks him
  between chests is a separate leaf of #381 and uses D14.2's own-body pathfinding when it is built. (Built on
  2026-11-09, on foot, through this port and D15's: the manual-haul lead default at the end of this file.)
- *Putting it down again.* `ct_collect deposit <item>` deposits one named item through D15's unchanged port, so what
  he took is not stranded in him. D15's grant already covers any item in his inventory; the default deposit filter
  (what collecting yields) is unchanged. Equipped stacks are never offered. **A known edge:** if Gunnar is ever
  issued a tool (the felling decision would), an unequipped issued tool could be named and deposited; the leaf that
  issues one must filter issued items out of this path.
- *The ledger.* A take is acquired in `CollectionAccount` like a pick, so the session's "taken" total counts what he
  took from chests as well as what he collected; it is a record of where material physically is, not of what was
  collected.

**What is not granted:** no nearest-chest inference; no take from an unmarked chest, one marked `Deposit` only, or one
marked and then unmarked; no ownership takeover; no synthetic or replacement resources; no extra copies; no teleporting
of cargo; no mod data in a vanilla object; no bypass of ward, privacy, ownership or in-use checks; no take as part of a
tour or into a cart's container (the cart-assisted round is its own decision, **D22**, which loads into his assigned
cart and takes nothing out of one); no widening of any other Teamster capability.

**Never observed in game.** `GUNNAR_COLLECTION.md` §10 rows 49-55 are OWNER GO-AROUND PENDING.

## D21. Gunnar's idle hammer swing is cosmetic, and is one pinned animation trigger on his own body

Status: **accepted** (contract revision **C7**, owner decision 2026-10-31, given to the lead and relayed for #381).
It answers the question `GUNNAR_COLLECTION.md` §10 row 10 put to the owner, and the "idle gesture" D17 left out.
The brief that relayed it called it "D18"; D18 had already been used for the worker look, D19 for Thorstein's
felling and D20 for Gunnar's chest take, so it is recorded here as D21.

**What is granted.** While Gunnar is idle beside the cart assigned to him, his worker runtime may play the vanilla
hammer swing on **his own body**, through vanilla's own `ZSyncAnimation.SetTrigger`, as scenery. One call, in one
file (`Adapters/Workers/GunnarIdleHammerPort.cs`):
- the receiver is the `ZSyncAnimation` of the body carrying `TeamsterWorkerAI` - Gunnar's worker prefab, and no other
  body;
- the trigger name is the one the vanilla Hammer item itself plays, read from the item database
  (`m_shared.m_attack.m_attackAnimation`) - the name vanilla's own repair swing plays from the hammer in a player's
  hand. The product invents no animation name;
- the body's animator is asked first whether it has that trigger (`ZSyncAnimation.HasParameter`), and nothing is
  played if it does not.

Walking to the cart and turning to face it use the vanilla motor on his own body that D14.2 already allows.

**Gates, all re-read in the worker tick that acts:** Teamster's master switch, `Workers/GunnarHaulingEnabled` (off by
default), the new `Workers/GunnarIdleHammerEnabled` (**off by default**), the start-up capability probe, and the shared work-authority rule
of D3. He must hold a cart lease with nothing else happening - phase `Ready`, no haul, no leg, no joint, no hold, no
pending stop - and **nobody may hold his identity**. The cart must be the leased one, resolved, not hitched to anyone,
and within 12 m of him. A job arriving, the cart being hitched, or any gate going away disarms the routine, hands the
tick back to the haul executor, and banks no swing for later.

**What is not granted:**
- no repair, no consumption, no hit points restored, no buff, and no other effect: `CartUpkeepIdle` can say only
  nothing, walk, face or swing, and has no way to hand a cart back;
- no write to the cart or to any other object: the routine reads the executor's own last observation of the cart and
  touches nothing;
- no other animator state: no `SetBool`, `SetFloat`, `SetInt` (each of which writes the body's network object), speed,
  reset, play or cross-fade;
- no trigger on any other body, the player's included, and no trigger name the product chose;
- no network message of the product's own: `SetTrigger` routes the trigger to every client's animator inside vanilla,
  as `Pickup`'s destruction is vanilla's inside the pinned pickup;
- no idle walking without an assigned cart, no walking further than 12 m to reach one, and no walking to it for more
  than 20 s at a time;
- no widening of D14-D18.

**Where it is enforced.** `tools/validate_repo.py`: `.SetTrigger(` is refused everywhere in Teamster source and in the
three `src/Shared` trees its project file compiles in, inside `Adapters/Workers` and out, except
`animation.SetTrigger(swing)` verbatim and once in the port; the `D21 idle-hammer audit` pins the lines that decide
whose animator (`body = gunnar.GetComponent<Character>()`, `animation = body.m_zanim`, each once) and which animation
(`swing = HammerSwing()`, a read of the Hammer's own attack animation with no string literal), the `HasParameter`
refusal above the call, and the absence of every other animator write in the port. Before this decision **nothing**
refused `SetTrigger` in Teamster; the documents said "not authorized" while any file could spell it. The planted
escapes in `tools/tests/test_teamster_carveout.py`'s `IdleHammerIsCosmetic`, each verified to pass the validator from
before this change, are refused. `scripts/audit-teamster-hauling-api.ps1` pins the IL: `ZSyncAnimation::SetTrigger`
exactly once, in the port's type, and no animator bool, float, int or speed write anywhere.

**Proved to change nothing.** `CartUpkeepIdleTests` (the decision) and `CartUpkeepDriverTests` (the decision driven
end to end beside a real haul executor holding a real lease for an in-game hour: the lease, the phase, the identity
hold and the executor's revision are exactly as they were, and the only port calls are his own body's walk, turn and
stop). The load-bearing part is that journal and the structural assertions - the routine has no way to hand a cart
back - because the fake cart cannot move; a physical shove is the one effect a test here cannot see, so he walks to
where a puller stands at the handle (`ApproachPoint`), never at the cart's centre, and row 50 checks the cart's
position in game.

**Not established.** Whether the body Gunnar is cloned from (`Dverger` by default) has the hammer's trigger in its
animator could not be read without running the game. If it does not, the port refuses on every swing and logs why
once, and he walks to his cart, faces it and stands there. The swing would then need a different base creature or a
further decision; it is never replaced by a name the product invents.

**Never observed in game.** OWNER GO-AROUND PENDING (`GUNNAR_COLLECTION.md` §10).

## D22. Gunnar's collection tour may load what it picks into the cart the player assigned him, measured

Status: **accepted** (owner decision 2026-10-31, #381, asked directly by the completion lead and recorded in the
lead's status log as "CART TOURS: yes - moving his own picked items into his explicitly assigned cart's container,
measured, then unloading at the chosen chest"; the 2026-10-31 paragraph of `CLAUDE.md` carried it as decided but
unbuilt). **Its first half is implemented** in the same change as this entry: the load into the cart. The unload at
the chosen chest is not built (lead default 3 below). D17's "a cart-assisted round still needs its own grant" and
D20's exclusion of "a cart's container" are answered by this entry and by nothing wider.

**What is granted.** Gunnar's opted-in collection **tour** may move items **out of his own inventory** into the
container of **the cart the player explicitly assigned him** - the one cart the hauling runtime's lease names after
`ct_haul assign` and `ct_haul confirm` - measured on both sides, with the remainder left in him.

**The calls.** D15's three, with the assigned cart's container as the destination: `Inventory.MoveItemToThis` for a
whole stack, and for part of a stack `Inventory.AddItem` of a clone of that stack's own data into the cart followed by
`Inventory.RemoveItem` of exactly what arrived, off his own stack. D15's reasoning for why the clone is not "spawning
item instances" applies unchanged.

**The gates, all re-asked in the frame of the move:** Teamster's master switch, the collection switch, hauling's
switch (the assignment is the hauling runtime's) and the new `Workers/GunnarCartTourEnabled` (**off by default**); the
start-up probe; the work-authority rule of D3; a body whose inventory record is trusted; nothing else in flight; the
cart still being the one the tour began with - the same lease key and epoch, resolved, the hauling executor doing
nothing with it (phase `Ready`, no haul, no leg, no joint, no hold, no pending stop), unhitched, and within 1 m of
where it stood; then, through the shared permit mint, the cart's own network id matching the lease's, the container
being that cart's own `m_container` riding that cart, this client owning the cart and the container, nobody having it
open and nobody holding it hitched (vanilla's `Vagon.InUse` and `IsAttached`), the ward and the privacy setting, and
Gunnar standing within reach of the cart's handle.

**Measurement.** Both sides counted before and after; only what arrived in the cart is removed from him and moved, in
`CollectionAccount`, from his back to `InCart`; what does not fit stays in him and stops the tour; a fault halfway is
uncertain and never retried blind, compensated or minted; a spent permit cannot record a second transfer. Vanilla
recomputes the cart's mass from what it holds, as it does for a player's load; nothing here writes a mass.

**Where it is enforced.** `Adapters/Workers/GunnarCartLoadPort.cs`, pinned by the scope audit (each call verbatim and
once: `cart.MoveItemToThis(gunnar, stack)`, `cart.AddItem(part)`, `gunnar.RemoveItem(stack, arrived)`; refused
everywhere else in the product) and by `#381 cart-load audit`; the cart's subject is
`Adapters/Workers/AssignedCartContainer.cs`, and the allowance that answers `Deposit` - only for the very cart the
lease names, only while the executor is idle, never `Take` - is `GunnarHaulingRuntime.CartLoadAllowance`. Planted
escapes in `tools/tests/test_teamster_cart_load_audit.py`, each refused by those rules and passing a validator without
them. The game-free gates and the reading of the cart off a real haul executor are `CartTourGateTests`; the ledger is
`CartLoadAccountTests`.

**How the decision was read where it leaves a choice** - each a **lead default pending owner confirmation**, the
smallest and most fail-closed reading the grant allows:

1. *The assignment is the player's mark.* The cart's container needs no container mark of its own: `ct_haul assign`
   and `confirm` are the explicit act the owner's words name. The assignment allows loading into that cart and
   nothing out of it; D15's deposit into a chest the player marked is unchanged.
2. *The cart stands still.* He loads only into a cart parked and unhitched inside the area, with the hauling runtime
   doing nothing with it, and he never hitches it during a tour. A hitched cart is "in use" by vanilla's own rule and
   D15's gate 7 refuses a cart in use, so loading on the move is not available at all; re-parking the cart nearer the
   work (pulling it along the tour) is not built. Hauling the full cart is the player's existing `ct_haul go`, through
   D4 and the hauling executor exactly as before.
3. *The unload at the chosen chest is not built.* The grant's "then unloading at the chosen chest" does not say how -
   cart to chest directly, or cart to Gunnar to chest - and nothing here takes anything out of a cart. Until a leaf
   builds it and the owner confirms the mechanism, what a tour loaded stays in the cart for the player.
4. *The cart must stand inside the area* the player set (`ct_collect area`). A cart elsewhere is a chest tour with a
   worse chest.
5. *He stands at the handle* to load - the point a puller stands at, the same one D21 walks him to - within
   `CollectionLimits.PickupReachMetres` (2.2 m), measured from the live handle point; the tour counts him as arrived
   only within 1.6 m of the handle as it stood at the start, so a cart that settled a little is still within reach. He
   never walks into the cart's body, which would shove an unbraked cart; the 1 m drift watch catches it if he does.
6. *Only what this tour picked.* A cart tour refuses to start while he carries any collected yield, and each load
   offers, per material, at most what this tour has measured picking and not yet loaded. Nothing he brought with him -
   a stone from an earlier session, what a D20 take gave him - goes into the cart.
7. *A cart that moved fails closed.* More than 1 m from where it stood when the tour began, horizontally or
   vertically, stops the tour with what he holds still in him. So does the lease ending, naming another cart, the cart
   not resolving, the executor starting anything, or anybody hitching it.
8. *A full cart stops the tour* with the rest in him, as a full chest does (D17); an open, warded or refusing cart
   stops it as a refusal.
9. *Four switches.* The master switch, collection, hauling and the cart tour's own; any one off refuses the start and
   ends a running cart tour on the next frame.
10. *The plan (#379).* A cart tour writes down every trip before it, like a chest tour, under the destination key
    `cart:<the lease's cart key>`. A reload ends the lease (D6), so an interrupted cart tour is never continued as a
    cart tour: what he holds afterwards goes to a chest through `ct_collect deposit` or a chest tour.
11. *A known edge in the ledger, left as it was.* `CollectionAccount.RecordDeposit` takes a chest deposit off his back
    first and then out of `InCart`; a later D15 deposit that measured more than this session's ledger had on his back
    would move the difference out of `InCart` on paper. The totals stay conserved; it is a misattribution in the
    session's record, not a material movement.

**What is not granted:** no hitching, pulling or detaching by the tour; no unload, and no take, out of any cart; no
nearest-cart inference and no cart the player did not assign; no load into a cart that is hitched, open, moving or
busy; no ownership takeover; no synthetic or replacement resources; no extra copies; no teleporting of cargo; no mass,
force or physics write; no mod data in a vanilla object; no bypass of ward, privacy, ownership or in-use checks; no
change to D15, D16, D20 or D21; no widening of any other Teamster capability.

**Never observed in game.** `GUNNAR_COLLECTION.md` §10 rows 56-63 are OWNER GO-AROUND PENDING.

## Lead default pending owner confirmation (2026-11-08): a resting figure and doors (#295 S5, #461)

Not numbered on purpose: other lanes are adding D-numbers at the same time, and the lead numbers this one when it
merges. It grants no world authority: it says how the owner's 2026-09-16 door-permission rule applies to a
presentation figure that is **set down** at a spot and never walks.

- The figure never opens, closes or uses a door, and is never set down in or right beside a doorway.
- It is never set down on the far side of a doorway or a wall from home. A spot is refused when anything solid
  stands between it and home at chest height (a wall, a shut door; a ray cast that cannot be completed counts as
  solid), or when the straight line from home to it passes through a doorway (the shared `DoorPortal.IsCrossedBy`).
- Concerned Foreman and Concerned Teamster keep no list of doors opened to companions, so every door counts as one
  the figure may not use. This is the rule's own default (`OnlyAllowedDoors` with nothing allowed). If the doors near
  home cannot be read, the figure is seated nowhere new.
- **Teamster applies the same rule to Gunnar's standing figure (#461).** The doorway half is the shared
  `RouteDoors.Inspect` on the line from home to the spot (anything but `Clear` refuses it, which is the same
  predicate as Foreman's); a spot within 1.5 m of a doorway's opening is refused; the wall check is the same chest-
  height ray, an unanswerable one counting as a wall; and a failed look at the pieces near home shows him nowhere
  new. No door is touched, nothing is written, and no new authority is granted (`GUNNAR_CAMP.md`).
- **The open question for the owner.** A bed inside a house whose door is shut still counts as home, so the figure
  may rest in that room. Hulgi, who walks, stays out of such a house, because he would have to come in through the
  door. A figure that is only set down at a spot never passes the door. If the owner wants the stricter reading,
  the bound becomes "never indoors behind a door" and the fallback is to stand outside.

## Lead default pending owner confirmation (2026-11-08): no worker body while an order holds him (#372)

Not numbered on purpose, for the same reason as the section above. It grants no world authority; it narrows one.
Before #372, `cf_worker spawn` created Thorstein's worker body with **no lease at all** whenever an order held his
mode (the arbiter grants a body then only to the holding job, and the settlement runtime is not that job). #372 has
every consumer body built by Concerned NPC's gated build under a worker lease, so that branch now refuses instead.

- **The default.** While any order holds him, including a paused one and an order taken up again after a load,
  `cf_worker spawn` is refused and says to cancel the order first. No body is created.
- **Why not borrow the order's name.** Building under the job's lease and giving it back afterwards releases the
  job's mode with it; keeping it leaves a hold nothing in the settlement runtime could release, so his figure could
  never return. Both were rejected; refusing is the smallest choice that creates no body without permission.
- **What it changes for the player.** The `WorkerBodyLost` write-off (`docs/mods/concerned-foreman/CUSTODY_AND_RECOVERY.md`
  §10, row L12) used to spawn first. It now runs `cf_collect cancel`, `cf_worker spawn`, `cf_settle reconcile`,
  `cf_settle resolve <order> lost`. Established from source and tested: a cancel is a transition row only and keeps
  what the record holds on him; `resolve <order> lost` accepts a cancelled order, and the loss survives a restart
  (`FaultInjectionTests.AnOrderStoppedForALostBodyCanBeCancelledAndItsMaterialStillWrittenOff`,
  `CollectionRecoveryTests.AnOrderStoppedForAMissingBodyHoldsHimUntilCancelledAndThenLetsGo`,
  `ThorsteinBodyTests.AnOrderStoppedForAMissingBodyRefusesTheLeaseUntilItIsCancelled`).
- **The open question for the owner.** If the owner would rather keep the order alive through a lost-body
  recovery, the alternative is a spawn granted under that order's own lease without ending its mode. It was not
  built, because cancelling already reaches the same write-off and loses nothing the record holds.
- Not observed in game: OWNER GO-AROUND PENDING (L12).
- **Teamster, the same default (F2c, 2026-11-09).** Gunnar's body is built under the hand-over's arrival guard. While
  a job holds him (a haul, a collection round), `ct_haul spawn` is refused and says to end that job first; before
  #372 the hand-over let that case through with no lease. The case a player meets is N13 (`GUNNAR_HAULING.md`): a
  haul whose body was lost stays in `NeedsAttention` holding him, and `ct_haul stop` on a stopped haul changes nothing.
  So the order of that recovery changes: `ct_haul detach` first (it ends the haul and gives his identity back,
  `HaulingExecutionSignalTests.AHaulWhoseBodyWasLostHoldsHimUntilDetachEndsIt`), then `ct_haul spawn`. The refusal
  names both commands. Teamster has no write-off that spawns while a job holds him. The same open question applies: a
  spawn under the job's own lease was not built.
  OWNER GO-AROUND PENDING.

## Lead default pending owner confirmation (2026-11-09): the manual chest-to-chest haul (#381 item 4)

Not numbered on purpose, for the reason the two sections above give. **It grants no world authority.** It records how
#381's manual mode - "chest to chest, with an explicit source, an explicit destination and a requested cargo filter,
both containers permitting the operation ... a haul manifest, on-foot and cart capacity, the number of tours and the
loading order, and each tour as provision, travel, unload, reconcile" - was built out of grants that already exist,
and where it stops because nothing grants more.

**What it is built from.** Every take is D20's, through the pinned `GunnarTakePort`, behind D20's gate asked again in
the frame of the move; every unload is D15's, through `DepositLegs` and the pinned `GunnarDepositPort`, behind D15's
gate asked again in the frame of the move; the permit mint asks each container for the player's mark, ownership,
in-use, ward, privacy and reach at the moment of the move. Walking him between the two chests is D14.2's own-body
pathfinding, which D20's "where he stands" default already named for this leaf and D17 confirmed covers his walking
on foot. **No vanilla call is added.** The code is `Adapters/Workers/GunnarCollectionRuntime.Haul.cs` (glue),
`Domain/Collection/HaulManifest.cs` (manifest), `ChestHaul.cs` (provision, travel, unload, reconcile) and
`ChestHaulGate.cs` (start gate); `HaulRequestGate` (already there and tested, with no production caller until now) decides whether
the haul may start and its loading order. Pinned by `#381 manifest-haul audit`; plants in
`tools/tests/test_teamster_haul_audit.py`.

**The defaults** - each the smallest, most fail-closed reading the owner's words and the existing grants allow:

1. *A switch of its own.* `Workers/GunnarChestHaulEnabled`, **off by default**, and it needs
   `Workers/GunnarCollectionEnabled` and `Workers/GunnarChestTakeEnabled` as well. All of them, and the work-authority
   rule, are re-asked every frame while a haul runs and again at every move.
2. *The two chests are the ones the player pointed at.* `ct_collect source` (marked `Take` or `Both`) and
   `ct_collect destination` (marked `Deposit` or `Both`), each chosen in this world load, two different chests. No
   inference, no fallback, no other chest when one is full.
3. *The cargo filter is a short list of exact names.* `ct_collect haul [plan] <item>[:count] ...`, one to four items,
   each named exactly as the game names it, optionally with a whole-number count; without a count, all of it the
   source held when the haul was planned. No wildcard and no "everything". D20's "one named item per order" still
   holds for every take inside the haul: each take is one named item through D20's port.
4. *The manifest is worked out before anything moves, and is a plan, not a promise.* `ct_collect haul plan` says it
   and moves nothing. Capacity per tour is D17's carry limit (a vanilla player's base plus Megingjord's bonus, less
   everything he carries) and his free slots, both read from the game. The loading order is `HaulRequestGate`'s,
   heaviest first, then by name. Each tour takes as much of the first item still owed as fits under both limits, then
   the next. Bounded: at most 4 items and 32 tours; a larger haul is refused and says to ask for less, and an item
   that cannot fit even one unit refuses the whole haul rather than being left behind silently. The shared runtime's
   `TourPartitioner` is not used: it is internal to ConcernedNPC and answers a different question (indivisible
   targets at places, bin-packed by distance), where this has one place and divisible cargo.
5. *Each tour is provision, travel, unload, reconcile.* At the source, one take per item of the tour, asking for at
   most the manifest's line and what fits now; walk to the destination; one unload per item, offering at most what
   this tour **measured** taking; then a fresh count of what he carries of the cargo, which must equal what the
   measured moves add up to. He starts a haul holding none of the cargo (it refuses otherwise), so the count is exact.
6. *It stops, and what he holds stays in him,* on: an uncertain take or unload (nothing more is attempted); a
   destination that took less than he brought (full, or refusing); a count that does not agree, or could not be made;
   a tour that took nothing; a walk with no progress for 12 s (NEEDS ATTENTION, with the reason); the switches, the
   authority, his record, his body or his job changing; a cart being assigned; the world going. Nothing is retried,
   compensated, minted or moved back. A take shorter than planned is not made up later; the haul ends with what was
   delivered and says how much.
7. *On foot only.* The manifest is planned with no cart and a haul refuses while a cart is assigned to him. The plan
   says what a cart would need only when one is offered, which the runtime never does.
8. *Not written ahead, not resumed.* A haul is not part of the #379 tour plan and is refused while one is open. A
   reload ends it; what he took is in his own persisted inventory and goes back through `ct_collect deposit <item>`
   at a chest the player chose. Because nothing resumes, nothing can be moved twice by a resume.
9. *The ledger.* Takes and unloads are recorded in `CollectionAccount` exactly as the ordered take and deposit
   record them.

**Where it stops, because nothing grants more - owner questions:**

- **A cart round for a chest-to-chest haul.** It would need two things no decision grants: loading what a D20 take
  gave him into the assigned cart (D22's lead default 6 loads only what a tour picked), and an unload out of the cart
  (D22's unload half is unbuilt and its mechanism undecided). Until the owner decides both, the manual haul is on foot.
- **Whether the 4-item, 32-tour bounds and the heaviest-first loading order are right**, and whether a haul should
  re-plan to make up a short take rather than end with it reported.

**Never observed in game.** `GUNNAR_COLLECTION.md` §10 rows 64-71 are OWNER GO-AROUND PENDING.
