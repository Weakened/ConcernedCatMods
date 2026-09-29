# Multiplayer trust and authority policy (CT-026)

This is the written contract behind `Domain/Authority/CartAuthorityPolicy`:
who may read, who may act, and when a reading is remote, for every shipped
Concerned Teamster feature. The policy class is the single source of truth;
this document is written to match it, `tools/validate_repo.py` fails if any
feature enum value is missing here, and the parking brake enforces its right
to act *through* `CartAuthorityPolicy.MayMutate` (test-asserted).

## Foundational invariants

1. **Teamster sends no network messages and takes no ownership.** It reads
   the game's own replicated/local state and writes only its own per-world
   sidecar files. Two further exceptions to the second half of this sentence:
   D15's deposit writes a vanilla container's inventory - **built, pinned to one file** - and D16's death drop puts
   item instances into the world, which is **decided and not built**
   (`docs/settlement/cart-and-collection/DECISIONS.md`). There is no RPC and no `SetOwner`/ownership claim anywhere
   in the source, and no `ZDO.Set` except one scoped exception — validator-audited
   (comments that state this absence are the only other occurrences). The
   exception (#313): the opt-in Gunnar worker runtime writes his identity,
   `tcc.worker.key`, into **his own worker body's** network object, and nothing
   else; the scoped audit fails on any other key and on any such write outside
   `Adapters/Workers/`.
2. **Two features mutate cart state** — the parking brake and the opt-in
   Gunnar hauling runtime — and only under **live local vanilla authority**
   (`ZNetView.IsValid() && ZNetView.IsOwner()`, the verified surface in
   `CART_INTERNALS.md`). Every authority ambiguity fails closed:
   `CartAuthority.Unknown` (value 0) denies mutation, an engaged brake releases
   the instant authority is not locally held, and a hitched Gunnar lets go the
   instant it is not. Gunnar's attach and detach are the cart's own
   `Vagon.AttachTo`/`Detach`, so what they replicate (the cart's pose while it
   moves, and vanilla's own `attachJoint` flag) is exactly what a player's pull
   replicates.
3. **Observation is client-side and read-only.** Any client may read
   replicated or local state. Numbers that the game keeps fresh only on the
   owning client (cart mass, grade, pull state) are **labeled remote** when
   observed without local authority, so an observer is never shown a stale
   value as current truth.

## Authority states

| State | Meaning | Mutation |
|---|---|---|
| `Local` | This client owns the cart under vanilla rules right now | permitted (brake; Gunnar hauling when work authority is also granted) |
| `Remote` | Cart is valid but owned by another client | denied |
| `Unknown` | Capability off, invalid view, or probe failure (fail-closed default) | denied |

## Feature matrix

Every value of the `TeamsterFeature` enum appears here. "Class" is the
feature's only relationship to cart state; "Remote-labeled" marks
observations whose values are owner-fresh and must be flagged when observed
without local authority.

| Feature (enum) | Class | Right to act | Remote-labeled |
|---|---|---|---|
| `CartTelemetry` | Observation | — (read-only) | yes |
| `CartStatusPanel` | Observation | — (read-only) | yes |
| `CargoManifest` | Observation | — (read-only) | yes |
| `LoadWarnings` | Observation | — (read-only) | yes |
| `DescentRisk` | Observation | — (read-only) | yes |
| `RecoveryGuidance` | Observation | — (read-only) | no (advisory text over local reads) |
| `TripRecording` | Observation | — (read-only, own sidecar) | no (local history, not owner-fresh cart state) |
| `RouteProfiling` | Observation | — (read-only) | no (route geometry + terrain, not owner-fresh cart state) |
| `ParkingBrake` | **Mutation** | **Local authority only** | no |
| `GunnarHauling` | **Mutation** | **Local authority only, and work authority granted** (`Workers/GunnarHaulingEnabled` on, a loaded world, `ZNet.IsServer()`, not dedicated, no connected peers), re-checked before every attach, motor step and lease; detach is the one mutation authority never blocks, because it releases control. Refuses to attach a braked, in-use, unowned, tipped or out-of-reach cart, or while any cart on this client holds a joint (`docs/settlement/cart-and-collection/DECISIONS.md` D3, D4; `GUNNAR_HAULING.md`) | no |
| `GunnarCollection` | **Mutation** (of a picked source, never of a cart) | **Work authority granted** (`Workers/GunnarCollectionEnabled` on, Teamster's `General/Enabled` on, a loaded world, `ZNet.IsServer()`, not dedicated, no connected peers), re-asked every frame while a pick is in flight, plus the start-up capability probe and **a source this client already owns** — ownership is required, never taken. Picks only a loose stone or a fallen branch the player points at, only one at a time, only within `CollectionLimits.PickupReachMetres`, and only through the source's own vanilla pickup; it reads no cart, writes no mass, force, velocity, position or ownership, and moves nobody. **Two vanilla calls, each pinned verbatim** in `Adapters/Workers/GunnarCollectionPort.cs` and nowhere else: `Pickable.Interact`, which drops the yield, and `Humanoid.Pickup`, which takes one dropped item into Gunnar's own inventory — and which, **inside vanilla**, destroys that `ItemDrop`'s network object through `ZNetScene.instance.Destroy(go)`. That destruction is vanilla's, of an object this mod never created, and is named here rather than folded into "picks things up" because a destruction of a networked object is exactly the kind of thing this table exists to disclose (owner decision 2026-09-19, #381; `GUNNAR_COLLECTION.md`). `DECISIONS.md` D15 (owner decision 2026-09-29, #415) additionally authorizes three inventory calls - `Inventory.MoveItemToThis`, and for part of a stack `Inventory.AddItem` of a clone of that stack's own data followed by `Inventory.RemoveItem` of exactly what arrived - to move what he carries **out of his own inventory** into a vanilla `Container` the player explicitly marked `Deposit` or `Both`, in `Adapters/Workers/GunnarDepositPort.cs` and nowhere else, with every gate re-asked in the frame of the move and both sides measured. All three are pinned verbatim and once in that file and refused everywhere else in the product, and the pin is proved by planted violations | no |

### The two 2026-09-29 grants (`DECISIONS.md` D15, D16)

**D15 is implemented and pinned. D16 is decided and NOT implemented** - `GunnarDeathDropPort.cs` does not exist, no
rule in `tools/validate_repo.py` pins or refuses its call, and a Gunnar who dies loses what he is carrying. Read D16's
paragraph as a description of what is authorized, not of what runs.

**D15, the deposit.** The only way material leaves Gunnar other than a reload, an explicitly forced retirement, or
D16's death drop once that is built.
It is a write into a **vanilla object's inventory**, so it is disclosed here rather than folded into "collection":
`Inventory.MoveItemToThis` for a whole stack and, for part of one, `Inventory.AddItem` of a clone of that stack's
own data followed by `Inventory.RemoveItem` of exactly what arrived - on a `Container` the player marked. Three
calls, named in `DECISIONS.md` D15 as well, which also records why a clone of a stack being simultaneously removed
from is not the "spawning of item instances" that same decision forbids. Nine gates, every one
re-asked in the frame of the move: both switches, the capability probe, the work-authority rule, a trusted body
record, the player's mark on **that exact container** resolved by `ContainerPermissionRuntime.Allowance`, this
client owning it, `!IsInUse()`, ward and privacy, and reach. The destination is counted before and after and only
what arrived is removed from him; a full chest leaves the remainder in him; a fault halfway is `Uncertain` and is
never retried blind, compensated or minted; the permit is spent by the transfer that records it. No nearest-chest
inference exists in the path at all.

**D16, the death drop.** A write of item instances into the world, and therefore the widest thing on this page. It
runs on **one** event: a worker body's own death, observed from the body. Every other lifecycle path - retire,
`retire force`, cancel, despawn, zone unload, logout, world change, plugin teardown - is unchanged and will be
refused this call by the validator, which is to pin the death-drop call verbatim to one file and audit which verb
may reach it. Until that lands, a Gunnar who dies loses what he is carrying.
The cart detaches first under D4's teardown ordering. Exactly what the body holds, once; nothing is recreated from
a name; a drop that failed is durable reconciliation evidence rather than a silent deletion.

## Per-actor summary

- **Local player, owns the cart (`Local`):** full observation (fresh) and the
  only actor who may engage the brake.
- **Local player, cart owned by a peer (`Remote`):** full observation, but
  owner-fresh readouts are labeled remote; brake is unavailable.
- **Other modded peer:** each client runs this same policy independently
  against its own authority; no Teamster-to-Teamster messages exist, so peers
  never coordinate or contend through Teamster.
- **Unmodded peer:** sees pure vanilla behavior; Teamster neither sends them
  anything nor alters any state they replicate. Gunnar's hauling never runs
  while any peer is connected (it pauses and lets go of the cart); a peer
  without Teamster that joins a world holding Gunnar's saved body gets the
  game's own "missing prefab" notice for it and no object.

## Enforcement

- `CartAuthorityPolicy.MayMutate(feature, authority)` — true only for the
  mutation features (`ParkingBrake`, `GunnarHauling`) under `Local`.
  `BrakeLifecycle` calls it at engage and on every tick; an engaged brake that
  loses authority releases with one log line, and an engage-time refusal
  surfaces through the toggle's returned reason (not the log). Gunnar's hitch
  preconditions (`Domain/Hauling/Execution/HitchPreconditions`) call it before
  every attach, and a hitched Gunnar that loses ownership lets go.
- `CartAuthorityPolicy.RequiresRemoteLabel(feature, authority)` — true for
  owner-fresh observations viewed without local authority.
- `tools/validate_repo.py`: (a) asserts every `TeamsterFeature` value is
  documented here; (b) audits Teamster source for outbound-network and
  ownership-takeover tokens (fails the build if any appear outside comments),
  with one scoped allowance: in `Adapters/Workers/` only, a network-object
  write whose key is a `tcc.worker.` literal; (c) the worker-runtime scope
  audit (#313) keeps the cart's attach and detach calls inside
  `Adapters/Workers/`, and fails there on any other network-object key, any
  teleport, position or rotation write, force or velocity write, kinematic,
  gravity, collision or constraint change, ownership request or interaction
  call, and on a mass write anywhere but Gunnar's own calibration file.
- `ConcernedTeamster.Tests` proves matrix completeness, the mutation truth
  table, fail-closed resolution, and that the brake's authority gate equals
  the policy's.

## Changing this policy

A new cart-touching feature adds a `TeamsterFeature` value, a
`CartAuthorityPolicy` entry, and a row here in the same change — the validator
and the completeness test both fail until all three agree. A new *mutating*
feature must additionally justify its authority gate in its own issue
(mutations are explicit, reversible, fail-closed, separately authorized).
