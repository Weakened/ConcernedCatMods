# Cart pulling and resource collection: decision record

Status: **accepted for the first cart and collection proof** (contract revision **C1**, 2026-09-17), amended
by **C4** (2026-09-17, D4) and **C5** (2026-09-29, D15 and D16 - the two owner grants that close #415 and the
death door named in `GUNNAR_COLLECTION.md` §6c) and **C6** (2026-10-28, D17 - the collection tour's walking and
load size). D16's fail-closed reading was confirmed by the owner on 2026-10-28.
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
  as `Pickup`'s destruction of the item it takes is; nothing in this product calls `SetTrigger`.

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
grant.

**Load size.** A load is sized like a vanilla player wearing Megingjord - **225 units** - whatever Gunnar wears, as
#381 states. Vanilla does not weigh a non-player down, so this remains the product's own discipline; the slot limit is
still the game's.

**The 24-source trip.** `StopSequencer.MostStopsPerRound` (24) capping a load at 24 sources is accepted **as a
planning limit**: the rest of a larger look is the next load, never dropped and never walked one source a trip.

**Any load size.** A load is not refused for being small. A tour that would carry **nothing** still does not start -
that is an empty order, not a size judgement - but one or two units are walked to the chest like any other load.

**What is not granted:** no cart use on a tour, no felling (`TreeBase.Damage`), no idle gesture
(`ZSyncAnimation.SetTrigger`), no chest-to-chest take, no ownership takeover, no movement of anyone but Gunnar, and no
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

**Not granted:**
- full-size trees, logs, stumps, bushes or anything not on the list;
- felling outside an explicit area;
- teleporting, forces, or movement of the player;
- ownership takeover;
- minted, estimated or replacement wood;
- a second copy of any drop;
- mod data in a vanilla object.

**Never observed in game.** OWNER GO-AROUND PENDING (docs/mods/concerned-foreman/COLLECTION.md rows F1-F6).
