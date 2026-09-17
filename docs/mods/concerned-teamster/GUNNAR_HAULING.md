# Gunnar hauls an assigned cart (CT-NPC-002, #313)

Status: **implemented, automated-tested and adapter-audited; live Gate B pending.** Contract revision C1
(`docs/settlement/cart-and-collection/`). Gunnar is the Teamster's worker: he walks to a cart the player explicitly
assigned, hitches himself to it through the cart's own attach, pulls its real cargo to a destination, stops and
detaches on suitable ground.

Route planning and progress judgement belong to agent B (#314). Until B lands, a clearly marked **placeholder**
planner (one clear straight segment ahead of the cart on flat open ground) and a two-body motion monitor stand in.

## 1. Game build

| Item | Value |
|---|---|
| Designed against | Valheim 1.0.12, `assembly_valheim.dll` SHA-256 `27a766a8…c393a84` (`CART_SEAM_AUDIT.md`) |
| Re-audited against | **Valheim 1.0.14** (auto-updated 2026-09-17), Steam build 25364265, `assembly_valheim.dll` SHA-256 `F64998168A0DD37EC774816808F914ED68376BE1B9670CD05A6C2F27C8017FB6`, Unity 6000.0.75f1, Jötunn 2.29.2 |
| Audit | `pwsh ./scripts/audit-teamster-hauling-api.ps1`: **PASS**, 100 of 100 members and vanilla behaviours verified; Teamster IL clean (section 8) |

## 2. Scope

**Does:** one hauling assignment for one Gunnar; an explicitly selected and confirmed hand cart; standalone legs to a
point; stop, wait, detach; every loss of control ends the haul with one reason.

**Does not:** pick the nearest cart, re-hitch after a lost joint, move a cart by anything but the joint, change any
cart's mass or physics, touch the parking brake, request or take ownership, run in multiplayer (it refuses while any
peer is connected), unload material (that is Thorstein's custody, #316/#317), or plan real routes (agent B).

## 3. Authority and the rule carve-out

The owner's brief of 2026-09-17 (`DECISIONS.md` D1, D3–D6, D14) makes one scoped exception to Teamster's
observational rules, recorded in `CLAUDE.md` and `AGENTS.md`. Inside it Gunnar may walk **his own body** with a
pathfinder, rely on cart ownership the host already holds, and call the cart's own `Vagon.AttachTo` and `Detach`.

**Switches.** `[Workers] GunnarHaulingEnabled` (default **false**), and Teamster's `[General] Enabled` must also be on.
`GunnarPullStrength = MatchPlayer` is the only supported value; `WorkerBaseCreature = Dverger`.

**Work authority** (`WorkAuthorityPolicy`, asked before every attach, motor step and lease):
opted in, a world loaded, `ZNet.IsServer()`, not dedicated, and **zero** connected peers (unknown counts as not zero).

**Cart authority.** `TeamsterFeature.GunnarHauling` is a mutation feature in `CartAuthorityPolicy`, allowed only
under `CartAuthority.Local` (`AUTHORITY_POLICY.md`). The hitch preconditions decide ownership through that policy.

**Never:** a teleport; a position, rotation, velocity or force write; a kinematic, gravity, collision or constraint
change; a cart mass write; `Interact`, `ClaimOwnership`, `SetOwner` or an RPC; mod data in a vanilla object. The one
network-object write is Gunnar's identity, `tcc.worker.key = teamster/gunnar`, in **his own** worker body.

**Enforced by** `tools/validate_repo.py`: CT-002/CT-026/CT-028 stay absolute everywhere, with one CT-026 allowance in
`src/ConcernedTeamster/Adapters/Workers/` for `tcc.worker.*` literal keys; and the **worker-runtime scope audit**,
which confines cart attach/detach to that folder, mass writes to `TeamsterWorkerBody.cs`, and bans teleport, pose,
velocity, force, kinematic, gravity, collision, constraint, joint, ownership, interaction and `SetExtraMass` calls
there. Both were mutation-checked (fourteen injected violations, fourteen errors).

## 4. Architecture

```
Domain/Hauling/Execution (game-free, tested)          Adapters/Workers (engine)
  HaulExecutor ── ports ──► IPullerBody ◄──────────── TeamsterWorkerBody ─► TeamsterWorkerAI (BaseAI)
      │                     ICartHitchSeam ◄──────── VagonHitchSeam ─► Vagon.AttachTo / Detach
      │                     IHaulAuthority ◄──────── GunnarWorkAuthority ─► ZNet
      │                     ICartRoutePlanner ◄───── StraightLinePlaceholderPlanner + StraightSegmentProbe (B replaces)
      │                     IHaulMotionMonitor ◄──── PlaceholderHaulMotionMonitor (B replaces)
  GunnarHaulService : IHaulService ◄───────────────── GunnarHaulingRuntime (world lifecycle, census, ct_haul)
```

- **Two clocks.** Every rendered frame the runtime follows the world, keeps the body binding current and lets the
  executor read the joint's signals (`ObserveFrame`), so a lost joint, a brake, a peer or a lost cart ends control
  within a frame. Progress runs in Gunnar's own 20 Hz worker tick (`Tick`).
- **One executor per world load**, with its own `CartLeaseBook` (new epoch GUID) and `ActorModeOwner`.
  `GunnarHaulService` is the stable object agent E's provider holds; its revision never goes backwards across worlds.
- **Faults.** The worker tick catches and latches; the runtime catches, latches, releases any joint and stays off.
  Telemetry and the brake are unaffected either way.

## 5. The worker body

- **Prefab `CT_TeamsterWorker`.** Built from the configured creature as an inactive clone as soon as the game's prefabs
  can be cloned (`PrefabManager.OnVanillaPrefabsAvailable`) and added as a Jötunn custom prefab, so every scene
  registers it before saved objects are created: Gunnar's saved body is never lost as an unknown prefab.
- **Stripped before any `Awake`:** every `BaseAI` (then `TeamsterWorkerAI` is added), `Tameable`, `Sadle`, `NpcTalk`
  (dereferences `MonsterAI` every frame), `CharacterDrop` (no loot), `Procreation`, `Growup`,
  `CharacterTimedDestruction`; default and random gear cleared. Faction Players, not a boss, named Gunnar, persistent.
- **Mind.** `BaseAI.UpdateAI` holds no creature behaviour on this build, so the subclass inherits none; idle sound,
  alert RPCs and broadcast messages are silenced in `Awake`. It walks only through `MoveTo`/`MoveTowards` at walking
  pace; arrival is decided from distance, never from `MoveTo`'s "stopped".
- **Identity and binding.** On world load a bounded census (`ZDOMan.GetAllZDOsWithPrefabIterative`, one step per
  frame) finds every saved body with Gunnar's key. Nothing may be spawned until it finishes. Exactly one loaded body is
  bound; one saved in unloaded ground is `NotLoaded` (never replaced); two or more are `Duplicated` and none is bound or
  destroyed automatically (`ct_haul retire` while pointing at the extra one).
- **Pulling strength (D5).** The vanilla motor restores velocity whatever the body weighs, so mass is strength.
  **Finding on the installed game:** `Vagon.AttachTo` calls `SetExtraMass(m_playerExtraPullMass)` on any puller that has
  a `Character`, which sets `m_body.mass = m_originalMass + extra`, and `Detach` resets it. Gunnar's body is a
  character, so vanilla gives him the cart's extra pull mass exactly as it gives the player. Calibration therefore sets
  his **base** mass (`Rigidbody.mass` and `m_originalMass`) to the local player's `m_originalMass`, measured, and never
  adds the extra itself. It runs at binding (once a player exists) and is re-checked before every hitch; the attach
  verification expects `calibrated + m_playerExtraPullMass`.
- **Upright.** His rigidbody's rotation constraints must equal the local player's, measured at calibration.
- **Uninstalling Teamster:** retire Gunnar first (`ct_haul retire`). Otherwise his saved body stays in the world as a
  record of an unknown prefab (the game logs "Missing prefab hash" and creates nothing).

## 6. The hitch (D4)

Evaluated in the same tick as the attach call, in this order; the first failure is the refusal:

| # | Condition | Refusal |
|---|---|---|
| — | seam probe verified | `SeamUnavailable` |
| — | work authority granted | `NoAuthority` |
| — | lease active; the cart resolves from its key | `LeaseNotActive`, `CartGone` |
| 1 | a live hand cart (no `Catapult`/`SiegeMachine`), valid network view | `CartGone` |
| 2 | this client owns it (`CartAuthorityPolicy.MayMutate(GunnarHauling, …)`) | `NotOwnedHere` |
| 3 | cart body masses sum to `m_baseMass + weight × m_itemWeightMassFactor` (1 % / 0.05 kg) | `MassNotCurrent` (waits up to `MassSettleSeconds` without spending an attempt) |
| 4 | container closed, no joint, no attach flag (`InUse()`) | `InUse` |
| 5 | Teamster's brake not engaged on it, root body not `FreezeAll` | `Braked` |
| 6 | no cart on this client holds a joint | `OtherJointOnClient` |
| 7 | `up.y ≥ 0.5` | `NotUpright` |
| 8 | `Distance(puller + m_attachOffset, handle) ≤ 0.6 × m_detachDistance` | `OutOfReach` (back to Approaching) |
| 9 | Gunnar's body present, alive, not faulted; rigidbody non-kinematic with gravity and collisions; rotation constraints = player's; unit scale; mass = base = calibration | `PullerBodyInvalid` (recalibrates) |

Right before `AttachTo` the seam re-checks ownership and "no joint on the client" (a non-owner call would write a
replicated flag it has no authority over; the attach detaches every loaded cart). Right after it verifies that the
joint's connected body is Gunnar's rigidbody, `IsAttached()`, the attach flag, and his mass; any failure calls `Detach()`
and refuses `VerifyFailed`. Transient refusals retry up to `MaxHitchAttempts` with doubling backoff, then
`NeedsAttention HitchFailed`; a brake, a tip, lost ownership or a missing seam go to their own attention at once.

**Release** calls `Detach()` only when the joint is Gunnar's or none exists (clearing a stale flag exactly as vanilla's
own next update would), never when another body holds the joint.

## 7. Lifecycle

Phases and legal transitions are C1's (`HaulPhases`); an illegal transition is refused and logged as a bug.

| Phase | Progress | Success | Deadline / retries | Cancel |
|---|---|---|---|---|
| Approaching | walk to `handle − m_attachOffset` | within reach and facing within 45° of the cart's heading | `ApproachTimeoutSeconds` → `ApproachTimedOut`; no path for 5 s counts a failure, `MaxRecoveryAttempts + 1` failures → `NoRoute` | → Ready |
| Hitching | D4 + attach + verify, same tick | joint verified → Pulling | `MaxHitchAttempts`, backoff → `HitchFailed` | → Detaching → Ready |
| Pulling | steer toward the planner's goal | final goal within the leg's radius → Stopping | stall/wedge (from both bodies) or joint force ≥ 0.8 × break force → Recovering | → Stopping |
| Recovering | stop, back off, replan from the cart's position | suitable plan → Pulling | `MaxRecoveryAttempts` per leg, never refilled by progress → Stopping → `Wedged`; a refused plan → its route reason | → Stopping |
| Stopping | motor stopped | cart still for `StillForSeconds` → Waiting (or Detaching / pending attention) | 15 s → `UnsafeParking` | (already stopping) |
| Waiting | hitched, holding still | — | — | detach → Detaching |
| Unloading | nothing moves; no leg accepted | consumer's Done → Waiting | — | pending until Done |
| Detaching | parking check, then `Detach()` | released and still for 2 s → Ready | rolling > 0.5 m/s → `UnsafeParking` | — |

**Safe parking.** Ground measured by raycasts from just above the cart, ignoring moving bodies: grade ≤ **0.05** along
and across (conservative and unmeasured; the descent calibration has only flat priors), not in water, cart upright and
still. Otherwise `NeedsAttention UnsafeParking`, still hitched; the player resolves it by taking the cart.

**Loss of control** (checked every frame while hitched; motor stopped, identity in Recovering, joint released first):

| Signal | Reason | Lease |
|---|---|---|
| a peer connected | `OtherPeersConnected` (Paused; back to Ready when alone) | kept |
| authority otherwise lost | `AuthorityLost` | ends |
| body gone, dead, faulted or not ticking for 1.5 s | `WorkerBodyLost` | ends |
| cart gone: record exists / not | `CartUnloaded` / `CartDestroyed` | ends |
| joint gone and ownership moved | `OwnershipLost` | ends |
| joint gone and the player holds a joint or is pointing at the cart | `PlayerTookOver` | kept |
| joint gone and `up.y < 0.1` | `CartTipped` | kept |
| joint gone otherwise | `JointBroke` | kept |
| joint now another body's (not released) | `PlayerTookOver` / `JointBroke` | kept |
| brake engaged or root frozen | `BrakeEngaged` | kept |
| leaning below 0.5 (joint kept, it holds the cart) | `CartTipped` | kept |

**Revision** increments on every phase, lease, reason or haul change, never on positions. **IHaulService**
(`CONTRACTS.md` §3.2): stale revision → Stale; no authority or worker → Unavailable; one haul per Gunnar; a leg from
Ready (new or same haul) or Waiting (same haul); a refused route answers its reason and moves nothing; planner budget →
Unavailable/NoRoute; `Transferring` only in still Waiting, `Done` only in Unloading; cancel is never refused while
Unloading and completes after Done; `StopAndWait` on a hitched NeedsAttention is Rejected. The exact protocol answer is
`GunnarHaulService.LastCommandDetail`.

**Teardown paths** that release the joint first: setting switched off, authority loss, a peer, `Game.IsShuttingDown`,
world unload, plugin destroy, body retirement (only while resting). **Known limit:** the game's logout resets every
network view before a plugin can react, so a cart's saved `attachJoint` flag may persist, exactly as after a vanilla
player logs out while pulling; the owner's first update after loading clears it.

## 8. Verification

| Check | Result |
|---|---|
| `ConcernedTeamster.Tests` (`HaulingExecution*`, `CartAuthorityPolicyTests`) | phase execution, every D4 precondition, assignment refusals, signal classification, teardown ordering, recovery ceilings, IHaulService, lease flows; a mutation check (three rules broken) failed 10 tests |
| `tools/validate_repo.py` | passes; scoped audit mutation-checked |
| `scripts/audit-teamster-hauling-api.ps1` on 1.0.14 | PASS: 100/100 members and behaviours (including `AttachTo`'s `DetachAll`, exact-object `connectedBody`, flag write and `SetExtraMass`; `Detach`'s owner-only clear; `CanAttach`'s distance and 0.1 tip; `SetExtraMass` = `m_originalMass + amount`); IL: `Vagon::AttachTo` 1 and `Vagon::Detach` 2, only in `Adapters.Workers`; `Rigidbody::set_mass` 1 and `Character::m_originalMass` store 1, only in `TeamsterWorkerBody`; `ZDO::Set(string,string)` 1, only in `TeamsterWorkerPrefab` with a `tcc.worker.` key; `set_constraints` only in the brake; no forbidden call anywhere |

## 9. Development commands (`ct_haul`)

`status`, `seam` (probe, player and Gunnar body calibration, the pointed-at or leased cart's live constants), `spawn`
(only when the census found no Gunnar), `assign` (the cart you point at) then `confirm` (within 30 s), `go <x> <z>
[radius]`, `stop` (stop and wait), `detach` (stop, park, detach), `release` (the lease; parks first when hitched),
`retire` (resting only; with duplicates, the one you point at), `evidence on|off` (one log line per second). These are
development aids; the player-facing controls are agent E's (#317).

## 10. Gate B live checklist

Disposable world, character and profile only; the lead runs it. Placeholder planner: **flat open ground, a straight
run ahead of the cart's handle** (≤ 64 m; ≤ 25 % between samples; ≤ 5 % at the end).

**Setup.** Build through the build lock, deploy to the isolated profile, set `[Workers] GunnarHaulingEnabled = true`.
Capture at startup: `Gunnar's hauling is ENABLED …`, `Gunnar's cart seam is available: <N> game members verified.`
(an `UNAVAILABLE … missing …` line is a blocker, copy it verbatim), `Gunnar's worker prefab 'CT_TeamsterWorker' is
registered …` (at the main menu), `Console commands registered: ct_haul`; on world load `a world loaded (epoch …)`,
`body census: 0 saved bodies`, `Gunnar's body: NotFound`.

**Happy path.**
1. `ct_haul status`: Authority Granted, Seam available, Prefab registered, Body NotFound.
2. `ct_haul spawn`; watch 60 s: no wandering, idle noise or twitching; status Body Bound.
3. Point at the cart, `ct_haul seam`; capture all of it. Expect Gunnar mass = base = calibrated = player base, and
   rotation constraints = the player's.
4. `ct_haul assign`, then `ct_haul confirm` → lease `gunnar-lease-1`.
5. `ct_haul evidence on`.
6. `pos` at a point ~20 m straight ahead of the handle; back at the cart `ct_haul go <x> <z>` → Accepted.
7. Capture `Ready -> Approaching`, the turn at the handle, `Gunnar hitched to cart … (joint verified)`,
   `-> Pulling`, and an evidence line: joint connected to Gunnar, attach flag True, owner this session, Gunnar mass
   = calibrated + playerExtraPullMass.
8. While moving: cart speed ≈ Gunnar speed (both > 0), joint force well under break force, hitch distance under detach
   distance, cart body mass = "load says".
9. Arrival: `-> Stopping`, `-> Waiting`; status Waiting, Attached yes.
10. `ct_haul detach` → `Gunnar detached from cart … on suitable ground: Released.`, then `-> Ready`; evidence: joint
    none, flag False, Gunnar mass back to calibrated, cart speed 0.
11. Loaded: a known cargo (e.g. 30 Stone + 20 Wood), repeat 6–10; cargo stacks and kg identical before and after.

**Negative cases** (capture the reply, the log and `ct_haul status`):
N1 container open → `InUse` refusals, then `HitchFailed` (closing it in time lets him hitch).
N2 brake engaged → `BrakeEngaged`, no attach, the brake stays engaged.
N3 slope → `Rejected (TooSteep|UnsafeParking)`; detaching on > 5 % → `UnsafeParking`, still hitched, nothing rolls.
N4 fence across the line → `Rejected (TooNarrow)`; something holding the cart mid-pull → two recoveries, `Wedged`.
N5 a second `go` while pulling → `Rejected … HaulBusy`; re-assigning the same cart → already Gunnar's; one joint.
N6 Use on the cart while he pulls (or grab another cart) → `PlayerTookOver`, no re-hitch; `detach` → Ready.
N7 `GunnarHaulingEnabled = false` mid-pull → lets go at once, `AuthorityLost`, lease ended; a second client connecting →
   `Paused OtherPeersConnected`, joint released, lease kept; it leaving → Ready.
N8 cart destroyed mid-pull → `CartDestroyed`, lease ended.
N9 walk away until the idle assigned cart unloads → `CartUnloaded`, Unassigned.
N10 assign, log out, reload → new epoch, census 1 saved body, Body Bound, no lease; `go` asks to assign; re-assign works.
N11 log out mid-pull, reload → the same cart assigns and hitches again.
N12 `spawn CT_TeamsterWorker` → Duplicated, `go` Unavailable; `retire` pointing at the extra one → Bound.
N13 Gunnar killed mid-pull → `WorkerBodyLost`, joint released, lease ended.

Evidence rows for `docs/settlement/cart-and-collection/EVIDENCE.md` stay **pending** until observed, with the build,
profile and scenario recorded.
