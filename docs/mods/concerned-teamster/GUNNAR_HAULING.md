# Gunnar hauls an assigned cart (CT-NPC-002, #313)

Status: **implemented, automated-tested and adapter-audited; live Gate B pending.** Contract revision **C4**
(`docs/settlement/cart-and-collection/`). Gunnar is the Teamster's worker: he walks to a cart the player explicitly
assigned, hitches himself to it through the cart's own attach, pulls its real cargo to a destination, stops and
detaches on suitable ground.

Route planning and progress judgement are agent B's (#314) and are now wired in: the placeholder straight-line planner
is gone, and every leg is planned, refreshed and steered by `CartNavigationKit`.

## 1. Game build

| Item | Value |
|---|---|
| Designed against | Valheim 1.0.12, `assembly_valheim.dll` SHA-256 `27a766a8…c393a84` (`CART_SEAM_AUDIT.md`) |
| Re-audited against | **Valheim 1.0.14** (auto-updated 2026-09-17), Steam build 25364265, `assembly_valheim.dll` SHA-256 `F64998168A0DD37EC774816808F914ED68376BE1B9670CD05A6C2F27C8017FB6`, Unity 6000.0.75f1, Jötunn 2.29.2 |
| Audit | `pwsh ./scripts/audit-teamster-hauling-api.ps1`: **PASS**, 102 of 102 members and vanilla behaviours verified; Teamster IL clean, with exact call counts (section 8) |

## 2. Scope

**Does:** one hauling assignment for one Gunnar; an explicitly selected and confirmed hand cart; standalone legs to a
point; stop, wait, detach; every loss of control ends the haul with one reason.

**Does not:** pick the nearest cart, re-hitch after a lost joint, move a cart by anything but the joint, change any
cart's mass or physics, touch the parking brake, request or take ownership, run in multiplayer (it refuses while any
peer is connected), unload material (that is Thorstein's custody, #316/#317), or take a cart through a doorway (C3:
no cart route passes a door in this slice).

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
change; a cart mass or tuning write; `Interact`, `ClaimOwnership`, `SetOwner` or an RPC; mod data in a vanilla object.
The one network-object write is Gunnar's identity, `tcc.worker.key = teamster/gunnar`, in **his own** worker body.

**Enforced by** `tools/validate_repo.py`: CT-002/CT-026/CT-028 stay absolute everywhere, with one CT-026 allowance in
`src/ConcernedTeamster/Adapters/Workers/` for `tcc.worker.*` literal keys written in `TeamsterWorkerPrefab.cs`; and the
**worker-runtime scope audit**, which confines cart attach, detach and detach-all to that folder, mass writes to
`TeamsterWorkerBody.cs`, and bans teleport, pose, velocity, force, kinematic, gravity, collision, constraint, joint,
cart-tuning, ownership, interaction and `SetExtraMass` calls there, along with component surgery and reflection outside
the prefab factory. Compound assignments (`|=`, `&=`, `^=`) count. Mutation-checked: fourteen injected violations,
fourteen errors.

## 4. Architecture

```
Domain/Hauling/Execution (game-free, tested)          Adapters (engine)
  HaulExecutor ── ports ──► IPullerBody ◄──────────── TeamsterWorkerBody ─► TeamsterWorkerAI (BaseAI)
      │                     ICartHitchSeam ◄──────── VagonHitchSeam ─► Vagon.AttachTo / Detach
      │                     IHaulAuthority ◄──────── GunnarWorkAuthority ─► ZNet
      │                     ICartRoutePlanner   ┐
      │                     IHaulMotionMonitor  ├──► CartNavigationBridge ─► CartNavigationKit (agent B, #314)
      │                     IHaulNavigation     ┘      Planner / Motion / Recovery / Staging / Footprint
  GunnarHaulService : IHaulService ◄───────────────── GunnarHaulingRuntime (world lifecycle, census, ct_haul)
```

- **Two clocks.** Every rendered frame the runtime lets the executor read the joint's signals (`ObserveFrame`) **and
  then** follows the world and the body binding, so a lost joint, a brake, a peer or a lost cart ends control within a
  frame and always before the body it is attached to is unbound. Progress runs in Gunnar's own 20 Hz worker tick
  (`Tick`).
- **Navigation.** `IHaulNavigation` is the one narrow port beyond C1's interfaces: it hands the leased cart to the kit
  (`UseCart`, once per lease; a cart it cannot measure is never planned for), takes it back when the lease ends,
  plans from the cart's position with the puller's position as a hint, refreshes a running plan at the planner's own
  interval, and remembers every stall where it happened so the planner avoids it next time.
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
  pace; arrival is decided from distance, never from `MoveTo`'s "stopped". A live worker body the runtime does **not**
  command is halted every binding refresh: the vanilla motor never clears a movement direction by itself.
- **Identity and binding.** On world load a bounded census (`ZDOMan.GetAllZDOsWithPrefabIterative`, one step per
  frame) finds every saved body with Gunnar's key. Nothing may be spawned until it finishes. Exactly one loaded body is
  bound; one saved in unloaded ground is `NotLoaded` (never replaced); two or more are `Duplicated` and none is bound or
  destroyed automatically. **The binding never moves while a cart holds the joint** — the joint is released first,
  because releasing it needs the body it is connected to.
- **Pulling strength (D5).** The vanilla motor restores velocity whatever the body weighs, so mass is strength.
  **Finding on the installed game:** `Vagon.AttachTo` calls `SetExtraMass(m_playerExtraPullMass)` on any puller that has
  a `Character`, which sets `m_body.mass = m_originalMass + extra`, and `Detach` resets it. Gunnar's body is a
  character, so vanilla gives him the cart's extra pull mass exactly as it gives the player. Calibration therefore sets
  his **base** mass (`Rigidbody.mass` and `m_originalMass`) to the local player's `m_originalMass`, measured, and never
  adds the extra itself. It runs at binding (once a player exists) and is re-checked before every hitch; the attach
  verification expects `calibrated + m_playerExtraPullMass`.
- **Upright.** His rigidbody's rotation constraints must equal the local player's, measured at calibration.
- **Uninstalling Teamster:** retire Gunnar first (`ct_haul retire`). If you do not, the body is **destroyed by the game**
  on the next load of that world: the host finds a saved object whose prefab no longer exists and removes it
  ("Destroyed invalid prefab ZDO"). Nothing else in the world is touched, and a cart he was hitched to keeps only the
  stale `attachJoint` flag, which its owner's first update clears.

## 6. The hitch (D4)

Evaluated in the same tick as the attach call, from that tick's own fresh reads (pinned by a test that answers a
different observation to each read in the frame); the first failure is the refusal:

| # | Condition | Refusal |
|---|---|---|
| — | seam probe verified | `SeamUnavailable` |
| — | work authority granted | `NoAuthority` |
| — | lease active; the cart resolves from its key | `LeaseNotActive`, `CartGone` |
| — | Gunnar faces within 45° of the cart's heading and the cart itself is at rest (CART-03, re-checked on every attempt, not only at the handover) | waits, turning back to the cart, up to 15 s, then counts an attempt |
| 1 | a live hand cart (no `Catapult`/`SiegeMachine`), valid network view | `CartGone` |
| 2 | this client owns it (`CartAuthorityPolicy.MayMutate(GunnarHauling, …)`) | `NotOwnedHere` |
| 3 | cart body masses sum to `m_baseMass + weight × m_itemWeightMassFactor` (1 % / 0.05 kg) | `MassNotCurrent` (waits up to `MassSettleSeconds` without spending an attempt) |
| 4 | container closed, no joint, no attach flag (`InUse()`), **nobody sitting in its chair** (`Chair.IsInUse()`) | `InUse` |
| 5 | Teamster's brake not engaged on it, root body not `FreezeAll` | `Braked` |
| 6 | no cart on this client holds a joint | `OtherJointOnClient` |
| 7 | `up.y ≥ 0.5` | `NotUpright` |
| 8 | `Distance(puller + m_attachOffset, handle) ≤ 0.6 × m_detachDistance` | `OutOfReach` (back to Approaching) |
| 9 | Gunnar's body present, alive, not faulted; rigidbody non-kinematic with gravity and collisions; rotation constraints = player's; unit scale; mass = base = calibration | `PullerBodyInvalid` (recalibrates) |

Right before `AttachTo` the seam re-checks ownership and "no joint on the client" (a non-owner call would write a
replicated flag it has no authority over; the attach detaches every loaded cart). Right after it verifies that the
joint's connected body is Gunnar's rigidbody, `IsAttached()`, the attach flag, and his mass; any failure calls `Detach()`
and refuses `VerifyFailed`. Those three decisions live in `HitchSeamRules` as pure functions, with their own tests.
Transient refusals retry up to `MaxHitchAttempts` with doubling backoff, then `NeedsAttention HitchFailed`; a brake, a
tip, lost ownership or a missing seam go to their own attention at once.

**Custody of the joint.** A verified attach records the cart's key in the executor and the connected rigidbody in the
seam. Every release goes through that key — never through the lease, which may already have ended — and happens in a
`finally`, so an exception while stopping the motor cannot skip it. **Release** calls `Detach()` when the joint is
Gunnar's (by the remembered body, the bound body, or any worker body: an unbound, dying or duplicated Gunnar is still
Gunnar), when it is connected to nothing, or when no joint exists at all (clearing a stale flag exactly as vanilla's
own next update would); never when another living body holds it. A release that does not take latches **still
holding**: it is retried every frame, the body binding does not move and the body is not retired until it takes.

## 7. Lifecycle

Phases and legal transitions are C1's (`HaulPhases`); an illegal transition is refused and logged as a bug.

| Phase | Progress | Success | Deadline / retries | Cancel |
|---|---|---|---|---|
| Approaching | walk to `handle − m_attachOffset` | within reach and facing within 45° of the cart's heading | `ApproachTimeoutSeconds` → `ApproachTimedOut`; no path for 5 s counts a failure, `MaxRecoveryAttempts + 1` failures → `NoRoute` | → Ready |
| Hitching | D4 + attach + verify, same tick | joint verified → Pulling | `MaxHitchAttempts`, backoff → `HitchFailed` | → Detaching → Ready |
| Pulling | steer toward the kit's goal; refresh the plan at `PlanRefreshSeconds` | final goal within the leg's radius → Stopping | stall/wedge (from both bodies) or joint force ≥ 0.8 × break force → Recovering, and the spot is remembered | → Stopping |
| Recovering | stop, back off, replan from the cart's position | suitable plan → Pulling | `MaxRecoveryAttempts` per leg, never refilled by progress → Stopping → `Wedged`; a refused plan → its route reason | → Stopping |
| Stopping | motor stopped | cart still for `StillForSeconds` → Waiting (or Detaching / pending attention) | 15 s → `UnsafeParking` | (already stopping) |
| Waiting | hitched, holding still | — | — | detach → Detaching |
| Unloading | nothing moves; no leg accepted | consumer's Done → Waiting | `RendezvousTimeoutSeconds` with no `getHaul`/`acknowledgeWait`/`cancelHaul` naming the haul → `RendezvousTimedOut` (C4) | pending until Done, or until that deadline |
| Detaching | parking check, then `Detach()` | released and still for 2 s → Ready | checked every frame together with the body and lease checks, so it cannot stall when the worker tick stops; rolling > 0.5 m/s → `UnsafeParking` | — (a started detach completes) |

**Routes (agent B, C3).** A leg is planned for the **cart**: grade ≤ `MaxGradeRatio` 0.12, the cart's measured width
plus `SideClearanceMetres` 0.5 free on each side, `SampleSpacingMetres` 1.5 between samples, ≤ `MaxLegMetres` 64 m,
within `PathQueriesPerMinute` 12 and `ClearanceProbesPerPlan` 128; water, unsupported gaps and anything outside the
loaded area are refused. **Doors:** a cart route never passes a doorway in this slice (`ForbiddenDoor`) — a door opening
is narrower than the cart, Teamster cannot read the companions' door permissions, and opening one is an RPC Teamster
may not send. Once hitched, the planner is given Gunnar's own position as well, so the first turn is predicted from the
cart's real heading.

**Safe parking.** Ground measured by raycasts from just above the cart, ignoring moving bodies: grade ≤ **0.05** along
and across (conservative and unmeasured; the descent calibration has only flat priors), not in water, cart upright and
still. Otherwise `NeedsAttention UnsafeParking`, still hitched; the player resolves it by taking the cart.

**Loss of control** (checked every frame while hitched; motor stopped, identity in Recovering, joint released first):

| Signal | Reason | Joint | Lease |
|---|---|---|---|
| a peer connected | `OtherPeersConnected` (Paused) | **held** until the cart can be parked (§2.7) | kept |
| authority otherwise lost | `AuthorityLost` | **held** until the cart can be parked (§2.7) | ends |
| body gone, dead, faulted or not ticking for 1.5 s | `WorkerBodyLost` | released | ends |
| more than one body carries Gunnar's identity | `WorkerBodyDuplicated` | released | ends |
| the cart seam faulted (nothing about the cart can be read) | `HitchFailed` | released | kept |
| cart gone: record exists / not | `CartUnloaded` / `CartDestroyed` | released | ends |
| joint gone and ownership moved | `OwnershipLost` | released | ends |
| joint gone after straining at ≥ 0.9 of break force or detach distance | `JointBroke` | released | kept |
| joint gone and the player holds a joint or is pointing at the cart | `PlayerTookOver` | released | kept |
| joint gone and `up.y < 0.1` | `CartTipped` | released | kept |
| joint gone otherwise | `JointBroke` | released | kept |
| joint connected to nothing | `JointBroke` | released | kept |
| joint now another body's | `PlayerTookOver` / `JointBroke` | left alone | kept |
| brake engaged or root frozen | `BrakeEngaged` | released | kept |
| leaning below 0.5 (the joint is what holds the cart) | `CartTipped` | held | kept |

**Lost authority is not a teardown (C4 §2.7).** Gunnar stops at once — the motor stops, no leg is accepted, an
Unloading hold ends with a new revision — and rests in Paused (`OtherPeersConnected`) or NeedsAttention
(`AuthorityLost`) **with the cart still hitched**, because CART-06 forbids releasing a loaded cart into a roll. He lets
go the moment the cart is still, upright and on ground within `MaxParkingGradeRatio`, and the phase and the reason do
not change when he does. Anywhere else he holds it, motionless, and says so in the status and the log; the per-frame
safety checks keep running, and nothing resumes or re-hitches on its own, even when authority comes back. The hold ends
when the player takes the cart, gives a detach or release command, engages the brake, or a teardown path runs.

**Revision** increments on every phase, lease, reason or haul change, never on positions. **IHaulService**
(`CONTRACTS.md` §3.2): stale revision → Stale; no authority or worker → Unavailable; one haul per Gunnar; a leg from
Ready (new or same haul) or Waiting (same haul); a refused route answers its reason and moves nothing; planner budget →
Unavailable/NoRoute; `Transferring` only in still Waiting, `Done` only in Unloading; cancel is never refused while
Unloading and completes after Done or at the hold's deadline; `StopAndWait` on a haul that has already stopped (Ready,
Waiting, Paused, NeedsAttention) is **Accepted with no transition and no pending intent** (C4). The exact protocol
answer is `GunnarHaulService.LastCommandDetail`.

**Hold liveness (C4 §3.2).** While Unloading, every `getHaul`, `acknowledgeWait` or `cancelHaul` naming the haul
refreshes the hold; the provider adapter calls `HaulExecutor.TouchUnloadHold(haulId)` for the reads that do not already
go through the executor. After `RendezvousTimeoutSeconds` of silence the provider ends the hold itself:
Unloading → NeedsAttention `RendezvousTimedOut`, new revision, cart still hitched.

**Teardown paths** that release the joint first: setting switched off by the player, `Game.IsShuttingDown`, world
unload, plugin destroy, body retirement (only while resting), and a body that dies, unloads or turns out to be a
duplicate while hitched (released in the first frame the runtime sees it, before the body is unbound). **Known limit:**
the game's logout resets every network view before a plugin can react, so a cart's saved `attachJoint` flag may persist,
exactly as after a vanilla player logs out while pulling; the owner's first update after loading clears it.

## 8. Verification

| Check | Result |
|---|---|
| `ConcernedTeamster.Tests` (`HaulingExecution*`, `HaulingSeamRules*`, `HaulingNavigationWiring*`, `CartAuthorityPolicyTests`) | 910 pass: phase execution, every D4 precondition and the seam's own attach/verify/release rules, the hitch decided from its own frame's read, assignment refusals, signal classification, teardown ordering, joint custody (lease ended while hitched, a release that does not take, an unbound body, a lost body stopping the motor), the C4 hold and unload liveness, recovery ceilings, navigation wiring, IHaulService, lease flows. Mutation check: three rules broken failed 10 tests |
| `tools/validate_repo.py` | passes; the scoped audit mutation-checked with fourteen injected violations (nine inside the worker runtime, five outside) |
| `scripts/audit-teamster-hauling-api.ps1` on 1.0.14 | PASS: 102/102 members and behaviours (including `AttachTo`'s `DetachAll`, exact-object `connectedBody`, flag write and `SetExtraMass`; `Detach`'s owner-only clear; `CanAttach`'s distance and 0.1 tip; `SetExtraMass` = `m_originalMass + amount`; `Vagon.m_chair` and `Chair.IsInUse`); IL, **exact counts**: `Vagon::AttachTo` 1 and `Vagon::Detach` 2, only in `Adapters.Workers`; `Vagon::DetachAll` 0; `Rigidbody::set_mass` 1 and `Character::m_originalMass` store 1, only in `TeamsterWorkerBody`; `ZDO::Set(string,string)` 1, only in `TeamsterWorkerPrefab` with a `tcc.worker.` key; `set_constraints` 2, only in the brake; no forbidden call anywhere |
| `scripts/audit-teamster-navigation-api.ps1` | PASS (agent B's, 28 contracts verified) |

## 9. Development commands (`ct_haul`)

`status`, `seam` (probe, player and Gunnar body calibration, the pointed-at or leased cart's live constants), `spawn`
(only when the census found no Gunnar), `assign` (the cart you point at) then `confirm` (within 30 s), `go <x> <z>
[radius]`, `stop` (stop and wait), `detach` (stop, park, detach), `release` (the lease; parks first when hitched),
`retire` (the worker body you point at, whether or not it is the bound one; otherwise Gunnar himself, and only while
resting — a body a cart is hitched to is refused), `evidence on|off` (one log line per second). These are development
aids; the player-facing controls are agent E's (#317).

## 10. Gate B live checklist

Disposable world, character and profile only; the lead runs it. Routes are agent B's: flat-ish ground (≤ 12 % grade),
the cart's width plus half a metre clear on both sides, no doorways, ≤ 64 m per leg.

**Setup.** Build through the build lock, deploy to the isolated profile, set `[Workers] GunnarHaulingEnabled = true`.
Capture at startup: `Gunnar's hauling is ENABLED …`, `Gunnar's cart seam is available: <N> game members verified.`
(an `UNAVAILABLE … missing …` line is a blocker, copy it verbatim), `Gunnar's cart navigation is available: <N> game
members verified.`, `Gunnar's worker prefab 'CT_TeamsterWorker' is registered …` (at the main menu),
`Console commands registered: ct_haul`; on world load `a world loaded (epoch …)`, `body census: 0 saved bodies`,
`Gunnar's body: NotFound`.

**Happy path.**
1. `ct_haul status`: Authority Granted, Seam available, Prefab registered, Body NotFound.
2. `ct_haul spawn`; watch 60 s: no wandering, idle noise or twitching; status Body Bound.
3. Point at the cart, `ct_haul seam`; capture all of it. Expect Gunnar mass = base = calibrated = player base, and
   rotation constraints = the player's.
4. `ct_haul assign`, then `ct_haul confirm` → lease `gunnar-lease-1`.
5. `ct_haul evidence on`.
6. `pos` at a point ~20 m away over ground the cart fits along; back at the cart `ct_haul go <x> <z>` → Accepted, with
   the planned length and steepest grade in the log.
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
N4 a fence or narrow gap on the line → `Rejected (TooNarrow)`; something holding the cart mid-pull → two recoveries,
   `Wedged`; a doorway anywhere on the line → `Rejected (ForbiddenDoor)`.
N5 a second `go` while pulling → `Rejected … HaulBusy`; re-assigning the same cart → already Gunnar's; one joint.
N6 Use on the cart while he pulls (or grab another cart) → `PlayerTookOver`, no re-hitch; `detach` → Ready.
N7 **(C4)** authority lost mid-pull. Two ways in, and each needs a tool the dedicated profile does not have; record
   whichever cannot be run as **not run**, with the reason, rather than leaving a row nobody can fill. If neither can
   be run, the whole case is not run: nothing else in the build takes authority away under a pull.
   **The setting** (needs a BepInEx configuration manager, which this profile does not install: nothing else in it can
   change `GunnarHaulingEnabled` while the game is running, and editing the file with the game shut down tests nothing
   here, because a haul does not survive a restart). With one installed: off mid-pull on **level** ground → he stops at
   once, `AuthorityLost`, lease ended, and lets go within a second or two (`Gunnar let go of cart …`); on a **slope**
   → he stops and **keeps holding** it, status says "HOLDING the cart where he stopped", nothing rolls, and nothing
   resumes when the setting is switched back on.
   **A second client** (needs one): connecting → `Paused OtherPeersConnected`, same holding rule, lease kept; it
   leaving → Ready once he has put the cart down.
   **Whichever way in was used**, finish it by taking the cart yourself while he holds it: the hold ends with
   `PlayerTookOver`. That ending is only reachable once he is holding, so it is not a substitute for either tool.
N8 cart destroyed mid-pull → `CartDestroyed`, lease ended.
N9 walk away until the idle assigned cart unloads → `CartUnloaded`, Unassigned.
N10 assign, log out, reload → new epoch, census 1 saved body, Body Bound, no lease; `go` asks to assign; re-assign works.
N11 log out mid-pull, reload → the same cart assigns and hitches again.
N12 `spawn CT_TeamsterWorker` (the vanilla console command) → an **unkeyed** extra body: the census still says Bound and
    `go` still works, so point at the extra body and `ct_haul retire` → "That worker body was retired; Gunnar's own
    binding is unchanged." (Pointing at Gunnar himself while he is hitched must be refused.) A truly Duplicated state
    needs two keyed bodies, which `ct_haul spawn` refuses to create.
N13 Gunnar killed mid-pull → `WorkerBodyLost`, joint released **in that frame**, lease ended, and the cart is left where
    it stood. Watch for the despawn negative test of `CART_SEAM_AUDIT` §7.4: the joint must be gone before the body is.
N14 sit in the cart (a chair on the handle side), then `ct_haul go` → the leg is **accepted**: `go` asks only for a
    lease and a route the cart fits along, and never reads whether the cart is in use. The refusal comes later, at the
    hitch, and nothing announces it: you have to ask. `ct_haul status` shows `Last hitch refusal: InUse (someone is
    sitting in the cart)` while he retries with backoff, and when the attempts run out the log says
    `Gunnar needs attention: HitchFailed - <n> hitch attempts refused; last: InUse (someone is sitting in the cart)`.
    No attach at any point. Standing up before the attempts run out lets him hitch.

Evidence rows for `docs/settlement/cart-and-collection/EVIDENCE.md` stay **pending** until observed, with the build,
profile and scenario recorded.
