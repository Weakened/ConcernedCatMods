# Cart pulling and resource collection: contracts

Contract revision **C4** (C1 frozen 2026-09-17 before dependent coding; C2 is additive; C3 and C4 are documentation only, see §10). The compilable half lives in:

| Area | Source | Compiled into | Tests |
|---|---|---|---|
| Worker identity, authority, actor modes, retries | `src/Shared/Workers` | Foreman, Teamster, `Shared.Settlement.Tests`, `ConcernedTeamster.Tests` | `WorkContractTests` |
| Cross-product capabilities | `src/Shared/Interop` | Foreman, Teamster, both test projects | `WorkContractTests` |
| Collection orders, sources, custody | `src/Shared/Settlement/Collection`, `…/Custody/MaterialTransfer.cs` | Foreman, `Shared.Settlement.Tests` | `WorkContractTests` |
| Gunnar's haul domain | `src/ConcernedTeamster/Domain/Hauling` | Teamster, `ConcernedTeamster.Tests` | `HaulContractTests` |

**Change control.** A change to a name, value, transition or semantic in this document or in those files is a contract
revision:
- bump the revision here;
- add a row to §10;
- ask the lead, who owns contract files.

Agents extend behaviour in their own files and never edit a contract file directly. A needed change goes to the lead
through the handoff.

---

## 1. Workers (`TheConcernedCat.Workers`)

- **`WorkerKey`**:
  - The form is `product/worker`, as slugs. The ids are `foreman/thorstein` and `teamster/gunnar`.
  - Keys are stable across sessions and never derived from a game object id.
- **`WorkPoint`**: a game-free position. It travels as `x;y;z` in the invariant culture.
- **`WorkAuthorityPolicy.Evaluate(facts)`**:
  - The checks run in this order: `RuntimeDisabled`, `NoWorld`, `NotHost`, `DedicatedServer`, `OtherPeersConnected`,
    then `Granted`.
  - A peer count that is not exactly zero, including unknown (negative), refuses.
  - It is evaluated **before every mutation**: attach, detach, every step of the motor command, pick, every transfer.
- **`ActorModeOwner`** (one per worker identity, owned by that product's runtime):
  - **Modes:** Resting, Surveying, Working, Paused, Recovering.
  - **One job at a time:** `Enter(mode, jobId)` lets one job hold the identity and move between the non-resting modes.
    A different job gets `RefusedBusy`.
  - **Releasing:** `Release(jobId)` returns to Resting.
  - **Resting-only actions:** home relocation, presentation changes and body retirement (`MayRelocateHome`,
    `MayRetireBody`).
  - **Recovering:** entered before any teardown that has to detach, park or reconcile.
- **`BoundedRetry` / `PhaseDeadline` / `AttentionThrottle`**:
  - **Retries:** every asynchronous phase has a failure ceiling and a doubling backoff.
  - **Deadlines:** every phase has one.
  - **Notifications:** at most one per reason key per cooldown.
  - **Clock:** the caller's, in seconds (`Time.time` in adapters).

## 2. Gunnar's haul domain (`TheConcernedCat.ConcernedTeamster.Domain.Hauling`)

### 2.1 Phases

`HaulPhase` names travel on the wire (`HaulWirePhase`, name-for-name and value-for-value, pinned by a test).

**Legal transitions** (`HaulPhases.CanTransition`). Anything else is refused and logged as a bug:

| From | To |
|---|---|
| Unassigned | Ready |
| Ready | Approaching, Unassigned, Paused, NeedsAttention |
| Approaching | Hitching, Ready, Paused, NeedsAttention |
| Hitching | Pulling, Approaching, Detaching, NeedsAttention |
| Pulling | Stopping, Recovering, Detaching, NeedsAttention |
| Stopping | Waiting, Detaching, NeedsAttention |
| Waiting | Pulling, Unloading, Detaching, Paused, NeedsAttention |
| Unloading | Waiting, Paused, NeedsAttention |
| Detaching | Ready, NeedsAttention |
| Recovering | Pulling, Stopping, Detaching, NeedsAttention |
| Paused | Ready, Approaching, Waiting, Detaching, NeedsAttention |
| NeedsAttention | Ready, Detaching, Unassigned |

**Rules the executor (agent A) adds on top of the table:**
- Paused and NeedsAttention may be reached while hitched. Leaving them for Ready or Unassigned while a joint exists
  goes through Detaching.
- Unloading forbids motion (`ForbidsMotion`). No steering goal is followed and no leg is accepted until Waiting.
- When the joint vanishes unexpectedly (§2.5), the next phase is NeedsAttention with the specific reason. The haul is
  never re-hitched automatically within the same attempt.

### 2.2 Leases (`CartLeaseBook`)

- **Cart identity:** `CartKey` is the cart's in-session network id as text plus a world-load epoch (a GUID minted per
  world open).
- **One lease per worker and one per cart.** `Assign` answers:
  - `AlreadySatisfied` for the same lease id with the same worker and cart;
  - `RejectedDifferentPayload` for the same id with anything different;
  - `RefusedStaleEpoch` for a key from another epoch.
- **Ending a lease:** `Release` for a player release, `Invalidate(reason)` for a lost cart, ownership, authority or
  body. `BeginWorldLoad(newEpoch)` invalidates every active lease as `WorldReloaded`.
- **Revalidation:** before every attach, motion leg, load and unload, the executor checks that the lease is active,
  that the cart still resolves from its key in this epoch, and the D4 preconditions that apply.

### 2.3 Refusals and attention

`CartAssignmentRefusal`, `HitchRefusal` and `HaulAttentionReason` are exhaustive. C2 adds `WorkerBodyDuplicated` and `PausedByPlayer`. Every `HaulAttentionReason` name has a `HaulWireReason` twin, pinned by a test, and `HaulCommandResult.Detail` carries a wire reason name, such as `HaulBusy`, that no attention reason can say. Each value has one player sentence,
written by agent A for refusals and agent E for presentation. None is ever `Unspecified`.

### 2.4 Navigation seam (agent B implements, agent A calls)

- **`ICartRoutePlanner.Plan(CartRouteRequest, now)`** returns a `CartRoutePlan` or a refused verdict. It never throws
  for game reasons.
  - **Suitable:** at least two waypoints; steepest grade, narrowest clearance and a `StopPoint` measured.
  - **Checks:** footprint width plus side clearance, grade (`MaxGradeRatio`), footing, water and supported crossings.
  - **Doors (C3):** a cart route **never passes a doorway** in this slice (`ForbiddenDoor`), for three reasons:
    - a door opening (1.39–1.68 m) is narrower than the 1.72 m vanilla cart;
    - Teamster cannot read the companions' door permissions;
    - opening a door is an RPC Teamster may not send.
  - **`CartRouteRequest.From` (C3)** is the **cart's** position. Once hitched, the planner is asked with Gunnar's
    body position as well, so the first turn uses the cart's real heading.
  - **Budgets:** stays within `PathQueriesPerMinute` and `ClearanceProbesPerPlan`; `BudgetExhausted` when exceeded.
- **`ICartRoutePlanner.NextGoal(plan, puller, cart)`** returns the next `SteeringGoal`, or null when the plan is done
  or no longer valid from here.
  - A final goal (`IsFinalStop`) means Stopping on arrival.
  - Agent B never moves any body.
- **`IHaulMotionMonitor`** (agent B implements, agent A feeds):
  - **Samples:** Gunnar's and the cart's positions, and whether the motor is commanded.
  - **Stalled:** neither body progresses over `StallWindowSeconds`.
  - **Wedged:** Gunnar moves or strains but the cart moves less than `StallCartDisplacementMetres`.
  - **Recovery budget:** at most `MaxRecoveryAttempts`, with backoff `RecoveryBackoffSeconds`, doubling, capped.
  - **Cache:** repeated failures at one place are remembered, as in the shared `WalkSetbacks`, so no leg oscillates.
- **`HaulLimits`**: C1 fixes the names and units. Agent B may change **default values** with measured evidence in its
  PR, recorded in `EVIDENCE.md`. New limits are a contract revision.

### 2.5 The attach seam (agent A)

`DECISIONS.md` D4 is normative, and summarised here.
- **Calls:** `Vagon.AttachTo(pullerGameObject)` and `Vagon.Detach()` through Teamster's publicized reference, with
  `Interact`, ownership requests and `SetOwner` never used.
- **Preconditions:** checked in the same frame as the call.
- **Verification right after the call:**
  - `m_attachJoin.connectedBody` is the puller's `Rigidbody`;
  - `IsAttached()` is true;
  - the attach flag is set.

  Any failure means `Detach()` and `HitchRefusal.VerifyFailed`.
- **While hitched, signals checked each frame:**
  - cart gone: `CartDestroyed` or `CartUnloaded`;
  - the joint is null or reconnected elsewhere: `JointBroke`, `CartTipped`, `PlayerTookOver` or `OwnershipLost`,
    decided from the cart's up axis, ownership and the local player's joint state;
  - not owner: `OwnershipLost`;
  - brake engaged or `FreezeAll`: `BrakeEngaged`, then detach;
  - authority verdict not Granted: `AuthorityLost` or `OtherPeersConnected`, then the lost-authority stop (§2.7).
- **Detach first** on every teardown path: planned stop, cancel, world exit, logout, player takeover, plugin destroy,
  body retirement. A body that dies, unloads or turns out to be a duplicate while hitched is also a teardown path; it
  detaches in the first frame the runtime sees it, and before the body is unbound. Lost authority is not a teardown
  (§2.7).
- **Capability probe at startup:** `AttachTo`, `Detach`, `m_attachJoin`, `m_bodies`, `m_attachPoint`,
  `m_attachOffset`, `m_detachDistance`, `m_breakForce`, `m_playerExtraPullMass`, `InUse()`, `IsAttached()`. Anything
  missing means hauling is unavailable (`HitchRefusal.SeamUnavailable`), logged once, with telemetry and brake
  unaffected.
- **Policy:** a new `TeamsterFeature` (`GunnarHauling`) with a `CartAuthorityPolicy` entry and an
  `AUTHORITY_POLICY.md` row in the same change.
- **Validator:** the audits (CT-026 network ownership, CT-028 no force, CT-002 game tokens) stay absolute for every
  existing folder. The worker runtime lives in `src/ConcernedTeamster/Adapters/Workers/`, with a scoped allowlist that
  still bans teleports, force and velocity writes, `transform.position =`, and `ZDO.Set` on any object except the
  worker's own identity key.

### 2.6 The worker body (agent A)

- **Construction:** a Teamster copy of Foreman's inactive-clone worker prefab pattern, with its own prefab name
  `CT_TeamsterWorker`, registered at plugin start, persistent, with identity stored in its own network object under
  `tcc.worker.key`.
- **Motion:** only through `BaseAI.MoveTo`, `Character.SetMoveDir` and vanilla `UpdateWalking`.
- **Mass:** calibrated per D5 at spawn and re-checked before each hitch.
- **Re-binding:** by key on world load. A second body with the same key means NeedsAttention (`WorkerBodyDuplicated`
  in presentation); neither is destroyed automatically.

### 2.7 Lost authority while hitched (C4)

The work authority verdict can stop being Granted while Gunnar holds a cart that is still here: a peer connects
(`OtherPeersConnected`), or authority is lost (`AuthorityLost`). CART-06 forbids releasing a loaded cart into a roll,
so this is a stop, not an immediate release:
1. **At once:** Gunnar stops moving. The motor stops and no leg is accepted. An Unloading hold ends, with a new
   revision, so the consumer's hold check refuses any further transfer. The haul rests in Paused
   (`OtherPeersConnected`) or NeedsAttention (`AuthorityLost`), with the joint still held.
2. **Once the cart is still** (`StillForSeconds`), upright, and on ground within `MaxParkingGradeRatio`, Gunnar lets
   go. The phase and reason stay the same, and the lease follows D6.
3. **Otherwise he keeps holding it, motionless.** Status and the log say he is holding the cart because it would roll
   there. Nothing resumes or re-hitches on its own, even when authority returns. The hold ends when:
   - the player takes the cart (vanilla's Use detaches it: `PlayerTookOver`);
   - the player gives a detach or release command, which works without authority because it only gives up control;
   - the player engages the parking brake (`BrakeEngaged`, then detach);
   - a teardown path from §2.5 runs.

## 3. `concernedcat.haul/1` capability (`TheConcernedCat.Interop.Haul`)

**Provider:** Concerned Teamster (`com.theconcernedcat.valheim.concernedteamster`) publishes
`ConcernedCatCapabilities["concernedcat.haul/1"]` as a
`Func<IReadOnlyDictionary<string,string>, IReadOnlyDictionary<string,string>>`.

**Consumer:** Concerned Foreman. Discovery, logging and error handling follow D7 and `CapabilityMap`.

### 3.1 Common fields

- **Every request** carries `op` and `contractMajor=1`.
- **Every reply** carries `status`, and when not Accepted or AlreadySatisfied also `reason` (a `HaulWireReason` name)
  and an optional `detail`.
- **Mutating requests** (`requestHaul`, `acknowledgeWait`, `cancelHaul`) also carry:
  - `providerEpoch`, from `hello`;
  - `requestId`, a settlement slug unique per attempt;
  - `expectedRevision`, the haul revision the consumer last read, or 0 before any haul.
- **Idempotence:** by `requestId`. The same id with the same payload answers `AlreadySatisfied` with the current state.
  The same id with a different payload answers `Rejected` / `DuplicateRequestDifferentPayload`.
- **Staleness:**
  - `providerEpoch` ≠ current: `Stale` / `EpochMismatch`.
  - `expectedRevision` ≠ current haul revision: `Stale` / `RevisionMismatch`.
  - **Revision** increments on every phase, lease or reason change, not on position updates.

### 3.2 Ops

| Op | Request fields | Reply fields (besides status/reason) | Semantics |
|---|---|---|---|
| `hello` | `consumerVersion` | `contractMinor`, `providerVersion`, `providerEpoch`, `authority` (`WorkAuthorityVerdict` name), `workerAvailable`, `phase`, `leaseId` (if a lease is active) | No side effects. `Unavailable` only when the provider is shutting down. |
| `describeLease` | `providerEpoch` | `leaseId`, `cartSessionKey`, `cartPosition`, `cartStill`, `cartUpright`, `attached`, `phase`, `revision` | Returns `Rejected/NoLease` when none is active. The consumer resolves the cart's container in its own process from `cartSessionKey` for loading and unloading, within this epoch only. |
| `requestHaul` | `providerEpoch`, `requestId`, `expectedRevision`, `orderId`, `haulId`, `leaseId`, `purpose` (`HaulLegPurpose`), `target` (point), `arrivalRadius` | `haulId`, `phase`, `revision` | Starts a leg to `target`. Legal when no haul is active (then Ready → Approaching/Hitching/Pulling as needed), or when this `haulId`'s haul is in Ready or Waiting. Otherwise `Rejected/HaulBusy`. One haul per Gunnar. The provider plans with `ICartRoutePlanner`; an unsuitable route answers `Rejected` with the route reason and does not move. |
| `getHaul` | `providerEpoch`, `haulId` | `phase`, `reason` (attention), `revision`, `arrived`, `attached`, `cartPosition`, `workerPosition`, `cartStill` | `arrived` is true once the leg's final goal is reached and the phase is Waiting with `cartStill`. Polling is no faster than `HaulLimits.PollIntervalSeconds`. |
| `acknowledgeWait` | `providerEpoch`, `requestId`, `expectedRevision`, `haulId`, `activity` (`HaulWaitActivity`) | `phase`, `revision` | `Transferring` is legal only in Waiting with the cart still (→ Unloading; the cart must not move). `Done` is legal only in Unloading (→ Waiting). A transfer never starts without an Accepted `Transferring` at the current revision. **Hold liveness (C4):** while Unloading, every `getHaul`, `acknowledgeWait` or `cancelHaul` naming the haul keeps the hold alive, and the consumer calls `getHaul` at least every third of `RendezvousTimeoutSeconds` while it holds. After `RendezvousTimeoutSeconds` with no such call, the provider ends the hold itself: Unloading → NeedsAttention `RendezvousTimedOut`, new revision. The consumer's hold check then refuses further transfers, and it reconciles the cart (§5.5). |
| `cancelHaul` | `providerEpoch`, `requestId`, `haulId`, `disposition` (`HaulCancelDisposition`) | `phase`, `revision` | Stops safely. `StopAndWait` ends in Waiting when a moving haul is hitched; on a haul that is already stopped (Ready, Waiting, Paused or NeedsAttention) it is Accepted with no transition and no pending intent (C4). `DetachAndPark` ends in Ready after Detaching on suitable ground, or NeedsAttention/`UnsafeParking`. Never refused while Unloading, but it completes only after the consumer's `Done` (the consumer must first finish or abandon its transfer through custody) or when the hold ends by the liveness deadline, whichever comes first. |

### 3.3 Provider-ended control

Any of these moves the haul to NeedsAttention (or Paused for `OtherPeersConnected`) with its reason and a new revision:
- player takeover;
- cart destroyed or unloaded;
- ownership or authority lost;
- a peer connects;
- world unload;
- plugin destroy;
- body lost.

The lease is invalidated where D6 says so. The consumer then:
1. stops transfers at that cart;
2. reconciles custody for the cart location (§5.5);
3. only then replans solo or pauses (COOP-03).

The provider never moves material.

## 4. Collection orders (`TheConcernedCat.Settlement.Collection`)

- **`CollectionOrderDefinition`** is immutable once accepted: `OrderId`, `WorkerId`, quotas (distinct resources,
  1–500 each), a `WorkScope` snapshot, a `DeliveryTarget` (a container key plus epoch, or HoldForPlayer),
  `ParticipationMode` (Solo or WithHauler) and the issuing character.
  - **Acceptance** checks `CheckShape()` plus authority, readiness (D12), scope validity, destination availability
    and "no other active order for this worker".
  - **Acceptance is journaled** (`CollectionAccepted`) before any work.
- **Resuming after a reload (C2).** The definition is immutable, with one exception. After a reload,
  `ICustodyRuntime.TryRecoverOrder` returns the worker's non-terminal order, and it is adopted **Paused**. Its scope
  snapshot and container key still carry the previous load's epoch, so the player confirms a rebind:
  - the scope is re-snapshotted from the same source;
  - the container is selected again.

  `RecordRebound` journals that as `CollectionRebound`. Quotas, progress and custody never change. Until the rebind,
  the order stays Paused (`DestinationStale` / `ScopeChanged`).
- **`WorkScope`**:
  - **Sources:** HarvestDesignation (a copy of the designation circle at acceptance), DefaultCampCircle (30 m on the
    latest valid respawn anchor, shown as a preview before acceptance) or CartographerWorkArea (later).
  - **Contents:** radius 4–48 m, the source revision and the world-load epoch.
  - **Revalidation:** at safe checkpoints only (before a reservation, before a pickup, before a delivery leg).
    - When the source revision changed, the order pauses with `ScopeChanged`.
    - When the scope is unloaded, it pauses with `ScopeUnloaded`.
    - The scope never silently falls back or expands.
- **`CollectionOrderStates`**:
  - **Table:**

    | From | To |
    |---|---|
    | Accepted | Surveying, Paused, NeedsAttention, Cancelled |
    | Surveying | Collecting, Paused, NeedsAttention, Cancelled |
    | Collecting | Surveying, WaitingForHauler, Delivering, HoldingForPlayer, Paused, NeedsAttention, Cancelled |
    | WaitingForHauler | Collecting, Delivering, Paused, NeedsAttention, Cancelled |
    | Delivering | Collecting, Completed, Paused, NeedsAttention, Cancelled |
    | HoldingForPlayer | Completed, Paused, NeedsAttention, Cancelled |
    | Paused | Surveying, Collecting, WaitingForHauler, Delivering, HoldingForPlayer, NeedsAttention, Cancelled |
    | NeedsAttention | Paused, Cancelled |

  - **Terminal states:** Completed and Cancelled.
  - **NeedsAttention** is left only through a person's resolution (→ Paused) or a cancel.
- **`ResourceProgress`**:
  - **Buckets:** every unit is in exactly one of OnGround, Carried, InCart, Delivered, HandedOver or Lost.
  - **Estimates:** `ReservedEstimate` is shown, never counted.
  - **`StillToCollect`** = requested − (OnGround + Carried + InCart + Delivered + HandedOver).
  - **Completion:** Delivered (+ HandedOver in hold mode) ≥ requested.
  - **Pre-existing cart or chest contents** are never in any bucket.

## 5. Custody (`TheConcernedCat.Settlement.Custody`)

### 5.1 Places

`CustodyPlace`: SourceGround (only drops traced to this order's own pick), Worker (the worker body's persisted
inventory), Cart (above the recorded baseline), Destination, Player, Lost. A unit is Delivered when it reaches
Destination; that credit is recorded once, and later changes to the chest are the player's.

### 5.2 Transfers

A `TransferIntent` is:
- a request id (the idempotence key);
- an order;
- a from and a to location;
- a `MaterialItem` (prefab, quality, variant);
- a count of at least 1;
- an expected custody revision.

The executor (`ITransferExecutor`, agent D) does, **in this order**:
1. **Check.** Refuse if any of these fail: authority; journal writable; both ports available; expected revision
   current; source count ≥ count; destination `CanAccept`. Outcome `Refused` or `Stale`, nothing mutated.
2. **Persist the intent** (`TransferStarted`). If persistence fails: `Refused`, nothing mutated, the unsaved row
   discarded.
3. **Engine add** to the destination: `accepted = to.Add(item, min(count, canAccept))`.
4. **Engine remove** from the source: `removed = from.Remove(item, accepted)`.
5. **Classify.**
   - `removed == accepted`: `Completed` if `accepted == count`, else `Partial` (the remainder stays at the source).
   - Otherwise `Uncertain`, with evidence (counts before and after) and the order in NeedsAttention. There is no
     compensation.
6. **Persist the receipt** (`TransferFinished`, with actual accepted counts). If that write fails, the in-memory
   ledger marks the transfer uncertain, and reconciliation on the next load decides from the actual inventories.

**Deposits.** A container deposit adapter may implement steps 3–4 with vanilla
`containerInventory.MoveItemToThis(workerInventory, item)`, one engine call per carried stack. It then verifies
**both** inventories' count deltas and classifies from those deltas, never from the requested count.
- Before the move, the adapter requires: this process owns the container; the container is not in use (and neither is
  a cart it belongs to); ward and privacy access; and reach.
- A non-owner write is silently discarded by the game, so ownership is load-bearing.

**Carts in use (C3, ratified).** Vanilla `Vagon.InUse()` is true while a cart is attached. The **cart** custody port
may treat an attached cart as not in use only when **all** of these hold:
- the joint's connected body is **not** the local player's body;
- nobody has the cart's container open;
- the cooperative caller holds an **Accepted `acknowledgeWait Transferring` at the current haul revision**, which
  agent E enforces before every cart transfer.

A delivery chest that is itself a cart's container stays strict: `m_wagon.InUse()` refuses.

**Why add before remove.** A crash between steps 3 and 4 leaves a duplicate, not a loss. Reconciliation detects it
from the persisted intent and actual counts, and a person resolves it. The reverse order would lose real items with no
evidence left.

**Fault injection.** Agent D's tests inject a failure before and after steps 2, 3, 4 and 6, and a crash with the world
save rolled back or not. They prove that no path mints, loses silently or replays an uncertain transfer.

### 5.3 Pickups

`ISourcePickupPort.TryPick` (agent C) does:
1. revalidate: the §6 predicate, availability, the loaded zone, ward access, reach, and the reservation held by this
   order;
2. persist `PickupStarted` (agent D's journal API);
3. perform the game's own pick once;
4. identify the drops it spawned (`SpawnedDrop` session ids);
5. persist `PickupFinished` with the drops.

`TryTakeDrop` then moves each drop SourceGround → Worker as a transfer (§5.2). If the pick happened but its drops
cannot be identified, the outcome is `Uncertain`: NeedsAttention, nothing granted, and the order pauses.

### 5.4 Journal kinds (schema v3)

Agent D implements these names in `JournalEntryKind`, appended after the existing values. Existing kinds and rows are
untouched, and schema v2 files still load:

| Kind | Payload |
|---|---|
| `CollectionAccepted` | the full order definition |
| `CollectionTransition` | from, to, reason |
| `PickupStarted` / `PickupFinished` | source key, order; the drops |
| `TransferStarted` / `TransferFinished` | the intent; the receipt |
| `TransferResolved` | a person's resolution of an uncertain transfer: which side is true |
| `CartBaselineRecorded` | lease, cart session key, per-item counts |
| `LossRecorded` | a person accepting observed loss |
| `HandoverFinished` | hold-for-player |
| `WorldSaveMarker` | generation, world time at `WorldSaveStarted` (§5.5) |
| `CollectionRebound` (C2) | order, the new scope snapshot, the new delivery target |

Every new row also records the world time at which it was written, so the marker rule can place it before or after a
save.

Replay stays idempotent and never resolves anything itself. Truncation and sequence damage make the journal read-only
(#293).

### 5.5 Reconciliation on load and after provider loss

**The world-save marker rule** (`PICKUP_SEAM_AUDIT.md` §7.3) runs first:
- Journal rows reach disk immediately, but world effects reach disk only at the game's next save. After any crash the
  journal is expected to be ahead of the loaded world.
- On `ZNet.WorldSaveStarted`, a static action invoked on the main thread just before the save snapshot, the runtime
  appends and persists `WorldSaveMarker{generation, worldTimeSeconds}`.
- On load, the loaded save is identified by the last marker whose recorded time ≤ the loaded world time.
- Rows **after** that marker describe effects the world rolled back. They are **voided**: never credited, refunded or
  replayed.
- When net time did not advance, or no marker matches, the result is ambiguous: NeedsAttention `ReconciliationMismatch`
  with the evidence kept.
- **Load restatement (C3, ratified from agent D).** At load, when rows follow the matched save or the loaded save is
  older than the record's top, the runtime appends `WorldSaveMarker{generation = the matched generation, time = the
  loaded world time}`. A marker whose generation already exists in the chain is read as a **load**, not a save.
  Without it, a crashed session's voided rows would read as confirmed after the next session saves.

After voiding, for each non-terminal order, the ledger's expected counts are compared with the **actual** inventories:
- Worker: its persisted inventory, excluding tool holdings.
- Cart: actual counts minus the baseline.
- Open intents without receipts.

| Finding | Outcome |
|---|---|
| Everything matches | continue |
| An open intent whose effect is fully visible (the destination gained, the source lost) | the person confirms and `TransferResolved` is recorded; never auto-applied |
| Actual worker or cart count below expected | NeedsAttention `ReconciliationMismatch` (or `PlayerRemovedMaterial` when the order was not running); the person chooses "record as lost" (`LossRecorded`) |
| Actual count above expected | NeedsAttention with evidence; never silently credited |

Delivered units are never reversed by later chest changes.

### 5.6 Worker body persistence (agent D, D9)

- **Registration:** the Foreman worker prefab is registered at plugin start and is persistent.
- **Worker object fields:**
  - `tcc.worker.key` (string, `foreman/thorstein`);
  - `tcc.worker.inventory` (a vanilla `Inventory.Save` package, the same format `Container` uses);
  - `tcc.worker.revision` (int).
- **Saving:** the inventory is written in the **same synchronous call** as the pick-take or the deposit that changed
  it, before the receipt is persisted. The pick, carry and deposit then share one world snapshot.
- **The worker clone:** the inactive clone has its `CharacterDrop` removed (no loot minted on death) and its default
  and random items cleared (no gear granted on spawn). Its prefab is registered at plugin start, so a saved body is
  never deleted as an unknown prefab when a world loads.
- **Despawn and death:** refused while the body carries order material or tools, unless it is moving them to a custody
  place first. On an unavoidable death, carried items are dropped as world items in the same frame and recorded as
  SourceGround-like `Lost` evidence for the player to recover.
- **Carry budget:** the real inventory fit (`CanAddItem`, counting any remaining default slots) **and** a documented
  NPC weight budget, `Collection/WorkerCarryWeight` (default 100, i.e. 50 Stone or 50 Wood at 2.0 each). Never pick
  what cannot be carried.
- **On load:** the body is re-bound by key and its inventory loaded before any work resumes.
- **Duplicates and missing bodies:** a duplicate body means NeedsAttention `WorkerBodyDuplicated`. A missing body with
  a non-empty ledger means NeedsAttention `WorkerBodyLost`, never refunded.
- **Tools:** tool holdings (`ToolLedger`) use the same persisted inventory, which closes the lifecycle loss named in
  the Foreman audit.

## 6. Natural sources

From `PICKUP_SEAM_AUDIT.md` §4.4 (Valheim 1.0.12, bundles and decompile). The predicate is an **allowlist**: every
clause must pass, and a source that cannot be classified with evidence is not collected (D10).

| Clause | Check | Why |
|---|---|---|
| **C1 identity** | the source's network object has prefab hash `Pickable_Stone` (→ LooseStone) or `Pickable_Branch` (→ Branch) | Only the name separates `Pickable_HardRockOffspring`, which is bred from a hammer-placed rock and yields Stone. |
| **C2 shape** | exactly one `Pickable` in the object, and no `Piece`, `WearNTear`, `Plant`, `Destructible`, `ItemDrop`, `Container`, `ItemStand`, `Procreation`, `Character` or `PickableItem` anywhere in it | Rejects hoe-placed `Placeable_Stone`, crops, bushes, stands, and anything a mod adds. |
| **C2b tag** | the root is `Untagged` | The bred look-alike is tagged `spawned`: a second, independent rejection. |
| **C3 configuration and yield** | the yield prefab is `Stone` (shared name `$item_stone`) or `Wood` (`$item_wood`); `m_amount == 1`; no extra drops; `m_aggravateRange == 0`. Stone has no respawn and no hide object; Branch has a respawn and a hide object. | Pins vanilla values. A patched amount or a changed yield fails closed. Frostwood, StoneRock and Flint are rejected. |
| **C4 provenance** | no creator recorded on the source's network object | Rejects a placed variant renamed by a mod. |
| **C5 state** | not picked, enabled, `CanBePicked()` | A picked or disabled source gives nothing. |

**Site clauses**, checked at revalidation in the same tick as the pick:
- this process **owns** the source's network object (never claimed);
- the source is inside the order's `WorkScope`;
- it is not in an interior;
- it is **not inside a location** (`Location.IsInsideLocation(pos, 0)`). This excludes the start temple's stones and
  branches, which is the conservative choice recorded in D10;
- the ward answer is `Granted`, because vanilla picking never checks wards;
- the worker is within reach (≤ 2 m flat);
- the carry capacity fits the expected yield, including the world's resource rate;
- a local player exists: the game's pick path dereferences it and would throw.

**Pick, trace, take**, all in one synchronous main-thread call inside the worker's tick:
1. snapshot the live drop set;
2. `pickable.Interact(workerHumanoid, false, false)`;
3. the spawned drops are the new members whose prefab is the yield and which this process owns;
4. set `m_autoPickup = false` on each;
5. `workerHumanoid.Pickup(drop, false, false)` for each, verifying the inventory count delta.

A player's auto-pickup cannot race this: it ignores items younger than 0.5 s, and the whole sequence completes within
one call. A drop that cannot be taken gets `m_autoPickup` restored and stays in the world as an ordinary drop, which is
logged.

Excluded, by at least clause C1:
- Placeable_Stone, Pickable_HardRockOffspring, Pickable_StoneRock, Pickable_Branch_Snow, Pickable_Flint;
- every food, forage, crop, berry, ore, tar, obsidian and metal pickable;
- core stands, remains and treasure;
- `PickableItem` random pickables.

Never touched at all, because the seam only takes drops it just spawned and deposits only into the order's container:
player drops, graves, chests, piles, mine rocks, trees and logs, bushes and saplings.

- `NaturalSourceKind`: LooseStone, Branch.
- `SourceKey`: prefab, in-session network id, world-load epoch and position. Never trusted across epochs.
- `SourceObservation`: availability (Available, Exhausted, Unloaded, Inaccessible, Unknown; never merged),
  reachability, survey revision.
- `SurveySnapshot`: marks `TruncatedByBudget` and `UnloadedCells`, so "nothing found" is never claimed for ground that
  was not observed.
- `SourceReservationBook`: one order per source per world load, in memory. A reload clears it and the survey repeats.

## 7. Cancellation, invalidation and retry

| Operation | Success criterion | Deadline / retries | Cancel | Invalidated by |
|---|---|---|---|---|
| Survey | snapshot with ≥ 1 eligible source, or an honest empty/truncated/unloaded result | bounded scan budget per tick | drop the snapshot | scope change, world reload |
| Reserve source | `Reserved` | none; `HeldByAnotherOrder` → pick another | release | order end, reload |
| Walk to source | within verified pickup range | `BoundedRetry` (agent C: 3 failures, 2 s → 20 s) and a per-leg deadline | stop moving, release the reservation | scope change, source gone |
| Pick | `Picked` with the drops identified | 1 attempt per reservation; `Refused` → next source | before step 3: none needed; after: must finish taking the drops or NeedsAttention | none mid-step |
| Take drop / transfer | receipt `Completed` or `Partial` | 3 attempts on `Refused` with backoff; `Uncertain` → NeedsAttention (no retry) | never between steps 2 and 6 | none mid-step |
| Approach cart | within reach | `ApproachTimeoutSeconds`; route re-plan ≤ `MaxRecoveryAttempts` | stop | lease invalidated |
| Hitch | joint verified | `MaxHitchAttempts`, with backoff | Detach | lease, brake, player joint |
| Pull leg | final goal reached, cart still | stall: `MaxRecoveryAttempts`, then NeedsAttention | Stopping → Waiting or Detaching | any §3.3 event |
| Rendezvous wait | both present and cart still | `RendezvousTimeoutSeconds` → NeedsAttention `RendezvousTimedOut` | cancel haul `StopAndWait` | provider loss |
| Unload hold (C4) | the consumer's `Done` | consumer silent for `RendezvousTimeoutSeconds` → NeedsAttention `RendezvousTimedOut` | cancel haul: completes after `Done` or at the deadline | any §3.3 event; lost authority (§2.7) |
| Detach (C4) | joint gone, and the cart still through the executor's settle window | the settle is checked every frame together with the body and lease checks, so it cannot stall when the worker tick stops; a roll after release → NeedsAttention `UnsafeParking` | none: a started detach completes | body or cart loss, reported with that reason |
| Lost-authority hold (C4) | the cart is still on parkable ground, then released | none: Gunnar holds, motionless, until the player acts (§2.7) | player takeover, a detach or release command, the brake | a teardown path (§2.5) |
| Deliver to container | receipt | destination full → Paused `DestinationFull` (materials retained) | stop | container stale → `DestinationStale` |

## 8. Persistence summary

| Record | Where | Survives reload |
|---|---|---|
| Orders, transfers, pickups, baselines, resolutions | Foreman settlement journal (v3) | yes |
| Carried materials and tools | worker body's own persisted inventory | yes (with the world save) |
| Actor modes, source reservations, cart leases, haul phases | memory | no: the order resumes Paused after reload, the lease must be reassigned, and the survey repeats |
| Door permissions (Hulgi) | unchanged | unchanged |

## 9. Test obligations per contract

- **Gate A:** the tables in §2.1 and §4 are covered exhaustively (legal and illegal). Lease rules, idempotence,
  staleness and epoch refusal are covered, and the wire enum mirror is pinned.
- **Separate builds:** agent E adds a test that compiles the provider and consumer halves into **two separate
  assemblies** and exchanges every op through `CapabilityMap`: absent, major mismatch, provider exception, stale epoch,
  duplicate request id.
- **Fault injection:** agent D's tests inject failures at every step of §5.2 and §5.3 and replay every journal prefix.

## 10. Revision log

### C4 (2026-09-17): documentation only

From agent E and the independent review of agent A (`R-313`):
- **§2.5 and §2.7:** lost authority stops Gunnar at once, but he lets go of the cart only on parkable ground.
  Otherwise he holds it, motionless, until the player acts. CART-06 wins over the old "then detach", and
  `DECISIONS.md` D4 is amended to match.
- **§2.5:** a body that dies, unloads or turns out to be a duplicate while hitched is a teardown path.
- **§3.2 and §7:** an unload hold ends when the consumer is silent for `RendezvousTimeoutSeconds`.
- **§3.2:** `StopAndWait` on a haul that is already stopped is Accepted with no transition.
- **§7:** rows for the unload hold, the detach and the lost-authority hold.

### C3 (2026-09-17): documentation only

- **§2.4:** no doorway for carts in this slice; `CartRouteRequest.From` is the cart's position (from agent B).
- **§5.2:** the cart in-use exception, ratified with the acknowledged-transfer condition (from agent D).
- **§5.5:** the load-restatement marker, ratified (from agent D).

### C2 (2026-09-17): additive

From agents A and C:
- `CollectionAttentionReason.PausedByPlayer`;
- `HaulAttentionReason.WorkerBodyDuplicated` and `PausedByPlayer`, with `HaulWireReason.AuthorityLost`,
  `WorkerBodyDuplicated` and `PausedByPlayer`, so every attention reason has a wire twin (pinned by a test);
- `HaulCommandResult.Detail`;
- `HaulLimits.MaxParkingGradeRatio`;
- `ICustodyRuntime.WorldLoadEpoch`, `TryRecoverOrder` and `RecordRebound`, with journal kind `CollectionRebound`
  and the post-reload rebind rule (§4).

The game updated to Valheim 1.0.14 the same day. A decompile diff against the audited build found the seam types
unchanged (`EVIDENCE.md`).

### C1 (2026-09-17): initial freeze

Workers, Interop haul/1, the Teamster haul domain, collection orders, custody transfers and ports, journal kind names
and the persistence contract. The §6 predicate was added from the pickup audit before dispatch.
