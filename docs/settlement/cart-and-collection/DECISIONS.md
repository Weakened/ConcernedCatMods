# Cart pulling and resource collection: decision record

Status: **accepted for the first cart and collection proof** (contract revision **C1**, 2026-09-17).
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
world exit, logout, authority loss, player takeover, plugin shutdown.

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

The Teamster worker body is persistent and re-bound the same way, so a reload does not teleport it. It carries no
inventory.

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
