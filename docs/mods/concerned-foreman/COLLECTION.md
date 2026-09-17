# Thorstein's collection: natural loose stones and branches

Issue: [CF-NPC-004 (#315)](https://github.com/Weakened/ConcernedCatMods/issues/315). Contract revision **C1**
(`docs/settlement/cart-and-collection/CONTRACTS.md` §4–§7, `DECISIONS.md` D9–D13). Requirements GATHER-01..06 and
ARCH-02 for Foreman.

Status: **implemented, automated-tested, adapter-audited. Nothing here has been observed in game.** Every Gate C row is
pending until the lead runs the checklist in §7.

---

## 1. What it does

Thorstein accepts an explicit order (1–500 new Stone and/or Wood, a work area, a chosen chest or "hold for me"),
looks over the area himself, walks to eligible natural loose stones and branches, picks each with the game's own pick,
takes exactly the items that pick spawned, carries them within his budget, and walks each load to the chosen chest.
Only newly delivered units count. Nothing else in the world is touched.

It works only when the settlement runtime is on, in single player or as a host with nobody connected (D3), in loaded
ground, and only through agent D's custody runtime: without one every order is refused with a stated reason.

## 2. Layout

| Layer | Where | What |
|---|---|---|
| Game-free planning | `src/Shared/Settlement/Collection/Planning/` | `NaturalSourcePredicate` (C1–C5 and the site clauses as decisions over facts), `SurveyScheduler` (bounded survey, accounting), `CollectionTargetSelector` and `CarryPlanner`, `ScopeCheckpoint`, `CollectionIntake`, `SoloCollectionLoop` (the state machine), `CollectionRequestIds`, `CollectionSentences`, the ports (`CollectionPorts.cs`) |
| Adapters | `src/ConcernedForeman/Runtime/Collection/` | `NaturalSourceClassifier`, `WorldSourceSurvey`, `WorldSourcePickupPort`, `ForemanWorkerMotion` (C1 `IWorkerMotion`), `RespawnAnchorSource`, `WorkScopeBuilder` (+ read-only `SettlementDiskReader`), `CollectionWorldFacts`, bridges to `ICustodyRuntime`/`ICooperativeDelivery`, `CollectionRuntime`, `CollectCommand` |
| Worker actor | `Runtime/Settlement/ForemanWorkerAI.cs` | distance-based `WalkStatus`, job goals, `WorkTick` hook, console goals refused while a job holds him |
| Tests | `src/Shared.Settlement.Tests/Collection*.cs` | 179 tests |
| Audit | `scripts/audit-foreman-collection-api.ps1` | 80 game-member contracts + forbidden-call check on the built DLL |

The loop never moves a body itself (it asks `IWorkerMotion`, which obeys only the job holding the `ActorModeOwner`),
never writes an inventory (picks through `ISourcePickupPort`, deposits through custody's executor), and reads progress
back from the custody view every time, so a unit is never counted twice or counted from an estimate.

## 3. The order's life

States and transitions are exactly `CollectionOrderStates` (CONTRACTS.md §4); the fake custody in the tests asserts
every recorded transition against the table.

1. **Accept** (`CollectionIntake`): shape, authority, custody writable, no other active order, the identity free, the
   body present, readiness (recruited, usable issued axe **and** hammer, D12), the scope valid, the default circle
   previewed, each quota a multiple of the world's yield per pick, the chest resolvable (or, holding, the whole order
   within his carry budget), a hauler when asked. Then the identity is held (`ActorModeOwner.Enter(Surveying)`), then
   `RecordAccepted` is journaled, and only then does work start.
2. **Survey** (solo, labelled "solo survey by Thorstein"): cells of 8 m checked loaded at centre and four corners;
   candidates found by prefab hash in the scene's instance table; each classified by the predicate.
3. **Select** (`CollectionTargetSelector`): only Available, reachable, unexcluded sources of a resource still needed;
   never a pick that would exceed the remaining need, his carry room, or the chest's room beyond his load. Cost = route
   estimate + pick effort + the weighted extra walk back to the chest. Ties break by the order's resource order, then
   session id. Reserve (`SourceReservationBook`), walk.
4. **Pick** on arrival, after a checkpoint in the same tick: authority, journal, no uncertain transfer, body present,
   scope, reach ≤ 2 m, carry and return room. Then `TryPick` and a `TryTakeDrop` for every traced drop.
5. **Carry checkpoint**: when nothing more fits or is needed, walk the load to the chest and deposit each resource
   through `ITransferExecutor` (one transfer per tick, fresh request id each attempt); hold for the player in hold
   mode; hand to cooperative delivery with a hauler (and take it back on `CollectMore`).
6. **Complete** when custody says every resource is delivered (or handed over in hold mode). Release the identity.

**Pauses and attention** (one reason each; `CollectionSentences` has a player sentence for every value):

| Situation | Result |
|---|---|
| Authority lost / peer connected | Paused `NoAuthority` / `OtherPeersConnected`, before the next mutation |
| Journal not writable | Paused `JournalReadOnly` (a stop happens even unrecorded; work never resumes unrecorded) |
| Uncertain transfer in custody | NeedsAttention `TransferUncertain`; resume refused until custody resolves it |
| Tool missing or broken, not recruited | Paused at the next trip start (a load already picked is still delivered); never relocks recruitment |
| Scope redrawn / new bed | Paused `ScopeChanged`; resume refused; nothing falls back or widens |
| Scope deleted, other world load | Paused `ScopeInvalid` |
| Scope or his ground unloaded | Paused `ScopeUnloaded` |
| Chest full, or fills mid-deposit | Paused `DestinationFull`, what he carries stays with him |
| Chest unusable / stale / forbidden | Paused `DestinationUnavailable` / `DestinationStale` / `DestinationAccessDenied`; no other chest |
| Walk to the chest fails 3 times | Paused `DestinationUnavailable` |
| Nothing left to collect | Paused `SurveyIncomplete` (truncated or unknown), `ScopeUnloaded`, `SourceUnreachable`, `SourcesExhausted` or `NoEligibleSources`, never merged |
| Pick refused | That source is excluded; next source |
| Pick uncertain / drops untakeable | NeedsAttention `TransferUncertain` / `CarryFull` (drops stay as ordinary world items) |
| Deposit refused or stale | 3 attempts, 2 s then 4 s backoff, then Paused |
| Deposit uncertain | NeedsAttention `TransferUncertain`, never retried or compensated |
| His tick stops (unloaded, destroyed, faulted, not owned) | Paused `ScopeUnloaded` or `WorkerBodyLost`; NeedsAttention `WorkerBodyLost` if he carries order material |
| Player `pause` / `cancel` | Paused (by the player) / Cancelled: not a refund, carried material stays on him |

## 4. The natural-source predicate and its evidence

CONTRACTS.md §6, implemented clause for clause in `NaturalSourcePredicate`; facts read by `NaturalSourceClassifier`.
Evidence: `PICKUP_SEAM_AUDIT.md` (bundles and decompile) and `scripts/audit-foreman-collection-api.ps1`.

| Clause | Check | Rejects |
|---|---|---|
| C1 identity | network prefab hash is `Pickable_Stone` or `Pickable_Branch` | `Pickable_HardRockOffspring`, `Placeable_Stone`, `Pickable_StoneRock`, `Pickable_Branch_Snow`, `Pickable_Flint`, every food, crop, ore and treasure pickable |
| C2 shape | exactly one `Pickable`; no `Piece`, `WearNTear`, `Plant`, `Destructible`, `ItemDrop`, `Container`, `ItemStand`, `Procreation`, `Character`, `PickableItem` anywhere | a placed stone renamed by a mod; crops; anything with added semantics |
| C2b tag | root `Untagged` | the procreation-born look-alike (tagged `spawned`), independently of C1 |
| C3 yield | `Stone`/`$item_stone` or `Wood`/`$item_wood` | Frostwood, StoneRock, Flint yields |
| C3 configuration | amount 1, no extra drops, no aggravation; stone: no respawn, no hide object; branch: respawn and hide object | patched values |
| C4 provenance | no `ZDOVars.s_creator` | a placed variant renamed to an allowlisted prefab |
| C5 state | not picked, enabled, `CanBePicked()` | picked or disabled sources (recorded as **Exhausted**, not dropped) |

Site clauses at the instant of the pick: owned here (never claimed), inside the order's scope, not in an interior, not
inside a location (the start temple's stones are excluded, D10), ward `Granted` through `WorldDesignationSite`
(loaded margin, no flash, `wardCheck: true`), worker ≤ 2 m flat, carry fits the game-scaled yield, a local player
exists. The tests pin every audited look-alike, every look-alike renamed to an allowlisted prefab (still refused by an
independent clause), and each clause on its own.

**Pick, trace, take in one call** (`WorldSourcePickupPort`): `BeginPickup` persisted → snapshot `ItemDrop.s_instances`
→ `pickable.Interact(worker, false, false)` in try/catch → the new members whose prefab is the yield, owned here and
within 3 m are the traced drops, each guarded `m_autoPickup = false` → `FinishPickup` → per drop `BeginTransfer` →
`Humanoid.Pickup(drop, false, false)` → classify from the inventory count delta and whether the drop's network object
is gone → `FinishTransfer` with the actual accepted count. A drop not taken in the same frame gets `m_autoPickup` back
and stays an ordinary world item, logged. A source that spawned drops without changing, or threw, is never picked
again this session.

## 5. Parameters

| Name | Default | Evidence or reason |
|---|---|---|
| `Collection/WorkerCarryWeight` (config) | 100 (10–300) | CONTRACTS.md §5.6: 50 Stone or 50 Wood at 2.0; our budget, vanilla gives NPCs none |
| Pickup reach | 2 m flat | CONTRACTS.md §6 |
| Arrival at a source | 1.5 m | inside reach, so arriving always permits the pick |
| Arrival at the chest | 2.5 m | a chest's pivot is inside its collider; **live check** |
| Survey cell | 8 m, 32 cells/tick | zone edges through a cell count it unloaded |
| Survey discovery | 4096 entries and 16 candidates per tick, 256 sources, 20 s | bounded; beyond is `TruncatedByBudget` |
| Snapshot age | 120 s | sources respawn and players pick things up |
| Fruitless surveys | 2 | then pause with the honest reason |
| Route estimate | distance × 1.3 + climb × 2, pick effort 2 m, return weight 0.5 | heuristic; no path query spent on choosing |
| Walk retry | 3 failures, 2 s doubling to 20 s; leg deadline 90 s; TooFar/Hazardous not retried | CONTRACTS.md §7 |
| Transfer retry | 3 attempts on Refused/Stale, 2 s → 20 s; Uncertain never | CONTRACTS.md §7 |
| Readiness recheck | every trip start and ≥ every 60 s while collecting | D12 |
| Absent-worker grace | 3 s | then the order stops |
| Preview validity | 10 min, same anchor revision | GATHER-02: the default circle is shown before acceptance |

## 6. Integration notes (for the lead)

1. **Custody.** `Plugin.cs` constructs `CollectionRuntime` without a custody runtime; pass agent D's `ICustodyRuntime`
   (and `sharedEpoch:` if D mints the world-load epoch: containers and sources must use one epoch).
2. **Location keys used by collection**: SourceGround = `SourceKey.ToString()` with the load epoch; Worker =
   `foreman/thorstein` with `Guid.Empty` (the body's inventory survives reloads, D9); Destination = the container key
   with the target's epoch.
3. **The take uses `Humanoid.Pickup`, not `IInventoryPort.Add`** (the contract's step 5). D9's "inventory written in
   the same call" therefore has to hook the body inventory's `m_onChanged` (as `Container` does), or expose a persist
   call.
4. **`TryResolveContainer` is also asked while he is away from the chest** (return space before collecting more): it
   must not refuse for distance; the deposit happens within 2.5 m (+0.5 m slack).
5. **Readiness and the harvest designation are read from disk** (`SettlementDiskReader`, read-only, 5 s reuse); swap
   for the settlement runtime's live records if preferred.
6. **Body.** Found in `BaseAI.BaseAIInstances`: the body with `tcc.worker.key = foreman/thorstein`, else the single
   unkeyed spike body; two of either is a duplicate and neither is used. `SettlementRuntime.Despawn` still destroys a
   body a job holds; it should consult `ActorModeOwner.MayRetireBody`.
7. **Contract gaps** (C2 requests, also in the handoff): no API to adopt a non-terminal order after a reload; no
   `CollectionAttentionReason` for a player's pause (Unspecified is recorded).
8. **The game updated during this work**: Steam build 25364265, `assembly_valheim.dll` SHA-256
   `f64998168a0dd37ec774816808f914ed68376be1b9670cd05a6c2f27c8017fb6` (still reports 1.0.12). The audit passes all
   80 contracts against it; SPEC/EVIDENCE cite the earlier `27a766a8…`.

## 7. Gate C live checklist (pending)

Only the lead runs this, in the isolated profile, a **disposable world and character**, with a game window the owner
granted. Record the build (Steam build id and `assembly_valheim.dll` SHA-256), the profile, the candidate DLL SHA-256
and the commit.

### 7.1 Setup

1. Candidate built from an integration of this branch with agent D's custody (without custody, step 7.2.3 must answer
   "This build has no custody record…": record that as the negative check and stop).
2. Config: `[Settlement] SettlementRuntimeEnabled = true`; `[Collection] WorkerCarryWeight = 40` (20 units per trip,
   forcing several trips).
3. New world, single player, nobody connected. Walk ≥ 150 m from the start temple to Meadows/Black Forest edge with
   loose stones and branches. `F5` console with `devcommands` only if needed for placing test items; note it.
4. Build a small shelter with a fire; place and **claim a bed** (sleep once to set it current). Place a wood chest.
   Put **7 Stone** in the chest (pre-existing contents that must not count).
5. `cf_settle area 40` standing at the shelter; `cf_settle recruit thorstein`; `cf_worker spawn` (or D's body).
6. Give Thorstein an axe and a hammer through D's tool handover (command name at integration). **Record both tools'
   durability.**
7. `cf_collect status` → "Work is allowed here", Thorstein here, actor mode Resting.

### 7.2 Happy path: mixed collection, bounded carry, several trips, correct delivery

1. `cf_collect preview 30` → record: anchor "your bed" at the bed, radius 30, loaded cells, available / exhausted /
   inaccessible / unknown per kind, look-alikes rejected. Need ≥ 20 available stones and ≥ 30 branches; if fewer, move
   the shelter or reduce the order to what exists and note it.
2. Record before: chest Stone/Wood, player Stone/Wood, Thorstein carried (0/0).
3. Look at the chest; `cf_collect start 20 30` → "Accepted collect-…".
4. Watch: status shows surveying (solo survey), then collecting; he walks to a source, stops within reach, the stone
   disappears / the branch hides, **no item is visible on the ground and none reaches your inventory even standing
   next to him** (stand beside a source he is about to pick).
5. `cf_collect status` during work: carried never above 20 units; per-resource lines never double count.
6. Watch at least two walks to the chest and two returns.
7. At "completed": chest = **27 Stone** (7 + 20) and **30 Wood**; Thorstein carries 0/0; player inventory unchanged;
   `preview` shows available counts reduced by exactly the picks; branches exhausted where he picked them. Log: no
   "uncertain" and no "FAULTED" lines.
8. Axe and hammer durability **unchanged** (GATHER-06: picking wears no tool).

### 7.3 Negative cases

| # | Do | Expect |
|---|---|---|
| N1 placed stones | Hoe-place 3 `Placeable_Stone` in the circle; hammer-place two pet rocks 1 m apart and wait for offspring (optional, minutes) | `preview` counts them as look-alikes rejected; never picked; still there after an order |
| N2 player drops | Drop 10 Stone and 10 Wood from your inventory inside the circle, then order | The piles are untouched (count them after); only natural sources picked |
| N3 crops and forage | Mushrooms, berries, flint, a planted crop in the circle | Untouched |
| N4 boundary | `preview 8`; note sources just outside 8 m; order | Only sources within 8 m (horizontal) are picked |
| N5 moving player vs fixed anchor | After `start`, walk 25 m away and stand still | Picks stay inside the circle around the **bed**, not you |
| N6 anchor changes | Claim a different bed mid-order | Paused, "the work area changed"; `resume` refused; `cancel` works |
| N7 temple | Order with a circle over the start temple | Its stones and branches are inaccessible, never picked |
| N8 full chest | Fill the chest so 5 Stone fit; order 20 Stone | Delivers 5, Paused "the chest is full"; he keeps what he carries; empty the chest, `resume`, it continues |
| N9 chest in use | Open the chest while he deposits | Refused deposits retry (2 s, 4 s), then Paused with the reason; nothing lost (count) |
| N10 chest destroyed | Destroy the chest mid-order | Paused, destination unavailable; **no other chest used**; his load retained |
| N11 exhausted | Pick every branch in the circle yourself, then order 5 Wood | Paused "have been picked…", status exhausted count > 0 |
| N12 unloaded | During an order walk ≥ 200 m away until his zone unloads, return | Paused "not loaded"; `resume` continues |
| N13 source race | Pick the stone he is walking to before he arrives | Log "Pick refused … gone"; he moves on; no duplicate item anywhere |
| N14 authority | Turn `SettlementRuntimeEnabled` off mid-order | Paused before the next pick; turn on, `resume` |
| N15 hold | `cf_collect start 10 0 hold` | Ends "holding the materials for you", 10 Stone on him, chest unchanged; handover through D's control completes it |
| N16 cancel | `cf_collect cancel` mid-trip | Cancelled; carried stone stays on him (count); chest unchanged by the cancel |
| N17 console goals | `cf_worker goto x z` while he works | Refused with "pause or cancel the job first"; he keeps working |
| N18 reload | Save and quit mid-order, reload | This build does not resume the order (reported gap); carried items per D9 (D's evidence) |

### 7.4 Runtime unknowns to settle while doing the above

1. `Interact` on a host-owned stone yields exactly one traced drop per unit (log "picked 1 traced drop(s), expected 1").
2. Whether sources 30–60 m from you are owned by this process (else "not under this world's control").
3. `Game.m_resourceRate` on the test world (a quota not a multiple of the yield is refused at `start`).
4. `Humanoid.Pickup` on the worker clone: default gear slots, return value, no ghost drop after reload.
5. The ward check's 64 m loaded margin at the edge of loaded ground (sources there read unknown).
6. The 2.5 m arrival tolerance at a wood chest and a reinforced chest.
7. `Location.IsInsideLocation` true for temple stones after zone load.
