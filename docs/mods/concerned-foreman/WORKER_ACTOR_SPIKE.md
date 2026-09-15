# Worker actor spike — the go/no-go

Issue: [CF-SET-002 (#279)](https://github.com/Weakened/ConcernedCatMods/issues/279).
Parent: [#273](https://github.com/Weakened/ConcernedCatMods/issues/273).
Authority decisions this inherits: [`SETTLEMENT_AUTHORITY.md`](SETTLEMENT_AUTHORITY.md).

Everything below was read from the **assemblies installed on this machine**, by
decompilation and metadata inspection only. No game code was executed, no game
bytes were copied, and no assets were exported.

## Audited build

| Item | Value |
|---|---|
| Game version | **1.0.12** |
| Steam build id | `25253764` (app 892970) |
| `assembly_valheim.dll` | SHA256 `27a766a8d23a7bd8b6a54fb9ad0452a96c305fb3629b39c40527c09a1c393a84`, 2 568 192 bytes |
| Method | `ilspycmd` 11.0.0.9375 decompilation of the shipped assembly |

The hash is byte-identical to the one the companion surface audit
([CC-NPC-002](../concerned-cartographer/COMPANION_COMPATIBILITY.md)) recorded,
so both documents describe the same binary.

---

## 0. The verdict

**GO** — and the question's premise turned out to be wrong in our favour.

The issue asked: *does an overridden `UpdateAI` genuinely stop vanilla wandering,
alerting and threat behaviour?* In this build the honest answer is that
**`BaseAI.UpdateAI` has none of that behaviour to stop.** All of it lives one
level down, in `MonsterAI` and `AnimalAI`. So the worker does not *suppress*
vanilla thinking — it **never inherits any**, because it derives from `BaseAI`
directly and from neither subclass.

That is a stronger guarantee than the ADR assumed, and it means the named
fallback — driving `Pathfinding.GetPath` plus `Character.SetMoveDir` from a
non-AI component — **is not needed**, and reimplemented movement is not a cost
this project has to pay.

**But the override is not the whole job.** Several vanilla behaviours run
*outside* `UpdateAI` entirely and are completely untouched by overriding it.
Section 3 lists each one and what was done about it. An implementation that had
only overridden `UpdateAI` and declared victory would have shipped a worker that
makes idle creature noises forever and can be put into an alerted state by a
stray arrow.

---

## 1. What `BaseAI.UpdateAI` actually is

```csharp
public class BaseAI : MonoBehaviour, IUpdateAI      // note: not abstract
{
    public virtual bool UpdateAI(float dt)
    {
        if (!m_nview.IsValid()) return false;
        if (!m_nview.IsOwner()) { m_alerted = m_nview.GetZDO().GetBool(ZDOVars.s_alert); return false; }
        UpdateTakeoffLanding(dt);
        if (m_jumpInterval > 0f) m_jumpTimer += dt;
        if (m_randomMoveUpdateTimer > 0f) m_randomMoveUpdateTimer -= dt;
        UpdateRegeneration(dt);
        m_timeSinceHurt += dt;
        return true;
    }
}
```

It is **an ownership gate plus housekeeping**, and its `bool` return is the gate:
`false` means "not ours, or not valid". There is no wandering, no target
selection, no alerting and no threat response anywhere in it. Note particularly
that `m_randomMoveUpdateTimer` is only *decremented* here — the thing that reads
it and actually wanders is `MonsterAI`.

By contrast `MonsterAI.UpdateAI` opens with `if (!base.UpdateAI(dt)) return false;`
and then runs the entire creature mind: `UpdateSleep`, `HuntPlayer` →
`SetAlerted(true)`, `UpdateTarget`, riding, `MoveToWater`, `DespawnInDay` →
`MoveAwayAndDespawn`, event-creature despawn, crown fear → `Flee`,
`m_fleeIfNotAlerted` → `Flee`, low-health flee, lava flee, `AvoidFire`, and so on
down to attacking.

**Consequence for the design.** `BaseAI` is `public` and **not abstract**, and
the movement primitives a worker needs are reachable from a subclass:

| Member | Visibility | Used for |
|---|---|---|
| `MoveTo(float dt, Vector3 point, float dist, bool run)` | `protected` | walking |
| `FindPath(Vector3 target)` | `protected` | asking the pathfinder |
| `HavePath(Vector3 target)` | `protected` | do we have a route |
| `StopMoving()` | `public` | stopping |
| `MoveTowards(Vector3 dir, bool run)` | `public` | direct steering |
| `m_pathAgentType` | `public` field | `Pathfinding.AgentType.Humanoid` |

This is exactly the ADR's stated reason for preferring a `BaseAI` subclass: the
worker moves with the same code vanilla creatures move with, so it looks right
and replicates right.

---

## 2. Who calls `UpdateAI`, and how often

Not `Update()`. This build drives AI centrally:

```csharp
// MonoUpdaters.FixedUpdate()
m_updateAITimer += fixedDeltaTime;
if (m_updateAITimer >= 0.05f)
{
    m_ai.UpdateAI(BaseAI.Instances, "MonoUpdaters.FixedUpdate.BaseAI", 0.05f);
    m_updateAITimer -= 0.05f;
}
```

```csharp
// MonoUpdatersExtra.UpdateAI(...)
container.AddRange(source);
foreach (IUpdateAI item in container) { item.UpdateAI(deltaTime); }
container.Clear();
```

Three facts follow, and all three shaped the implementation:

1. **A tick is 1/20 s.** Every budget number in this spike is per 0.05 s, not
   per frame.
2. **`dt` is a constant `0.05f`**, not the real elapsed time.
3. **There is no `try`/`catch` in that loop.** An exception escaping one
   `UpdateAI` aborts every creature after it in the list for that tick, *and* the
   remainder of `MonoUpdaters.FixedUpdate` — `Character`, `Aoe`, `EffectArea`,
   `RandomFlyingBird`, `MeleeWeaponTrail`. A bug in our worker would therefore
   look to a player like the whole game's AI stuttering.

Registration is via `OnEnable`/`OnDisable` into the static `BaseAI.Instances`.

**What we do about (3):** `ForemanWorkerAI.UpdateAI` wraps its entire body in a
`try`/`catch`, and on any exception latches `_faulted`, stops the body, logs
once, and returns immediately on every later tick. A broken worker becomes an
inert statue rather than a game-wide fault.

---

## 3. What overriding `UpdateAI` does **not** silence

This is the part the original question would have missed. Each row is a real
call site in the installed `BaseAI`.

| # | What runs anyway | Where | What we do |
|---|---|---|---|
| 1 | `InvokeRepeating("DoIdleSound", …)` — a creature noise every `m_idleSoundInterval` seconds, on Unity's scheduler, forever | `Awake` | `CancelInvoke("DoIdleSound")`, empty `m_idleSound`, `m_idleSoundChance = 0f` |
| 2 | `m_nview.Register("Alert", …)`, `("OnNearProjectileHit", …)`, `("SetAggravated", …)` — three RPCs any peer can drive | `Awake` | `m_canBeAlerted = false`, which makes `SetAlerted` a no-op at the source |
| 3 | `MessageHud.MessageAll(…, m_spawnMessage)` — a server-wide banner on spawn | `Awake` | `m_spawnMessage = ""` |
| 4 | `MessageHud.MessageAll(…, m_deathMessage)` | `OnDeath` | `m_deathMessage = ""` |
| 5 | `MessageHud.MessageAll(…, m_alertedMessage)`, plus the alerted effect, the animator `alert` flag, and a global boss counter | `SetAlerted` | neutralised by row 2 |
| 6 | `BaseAI.DoProjectileHitNoise(…)` is **static** and walks the private `m_instances` list, invoking `OnNearProjectileHit` on anything it considers an enemy | static | row 2 again: the RPC still arrives, `SetAlerted` still refuses |
| 7 | `HaveAlertedCreatureInRange` — other creatures observe ours through the same static list | static | harmless once ours is never alerted |

Why `m_canBeAlerted = false` rather than overriding `SetAlerted`: `RPC_Alert` is
**private and non-virtual**, so an override cannot intercept the network path. The
flag is checked inside `SetAlerted` itself —
`if (m_alerted != alert && m_canBeAlerted)` — so it closes every route at once.

`BaseAI.Awake` also hard-requires a `ZNetView`, a `Character` and a
`ZSyncAnimation`, and dereferences the ZDO. A body missing any of them throws in
`Awake`. `ForemanWorkerPrefab` checks for all of them, by name, and refuses to
build a worker prefab rather than discovering it at spawn time.

---

## 4. Two traps in the movement API

**`MoveTo` returns `true` for *stopped*, not for *arrived*.** Reading it:

```csharp
if (Utils.DistanceXZ(point, transform.position) < Mathf.Max(dist, num)) { StopMoving(); return true; }
if (!FindPath(point))                                                   { StopMoving(); return true; }
if (m_path.Count == 0)                                                  { StopMoving(); return true; }
```

Two of those three are failures. A worker that treated `MoveTo == true` as
arrival would report *"order complete"* for a destination it could not path to —
which, once orders consume material, is precisely the class of bug #273's gate 3
exists to prevent. **Arrival is decided by the planner, from distance.** The
adapter discards `MoveTo`'s return value on purpose.

**`MoveTo` calls `FindPath` itself, and `FindPath` is already throttled.**

```csharp
float num = time - m_lastFindPathTime;
if (num < 1f) return m_lastFindPathResult;
if (Vector3.Distance(target, m_lastFindPathTarget) < 1f && num < 5f) return m_lastFindPathResult;
```

So a real `Pathfinding.GetPath` happens at most once a second per AI, and at most
once per five seconds for a target that has barely moved. Our budget therefore
bounds *how often we ask*; vanilla's throttle bounds *how often the ask reaches
the pathfinder*. Both are real and the tighter one wins. This is worth stating
plainly because it means vanilla creatures are already cheap per-request — what
vanilla has **no** equivalent of is a bound on *giving up*, which is the next
section.

---

## 5. The budget, and what "defers, not spins" means

Vanilla AI retries a goal forever; nothing in `BaseAI` or `MonsterAI` ever
concludes "I cannot get there." A settlement worker must, because an order that
silently never completes is worse than one that fails loudly.

`WorkerMovementPlanner` (in `src/Shared/Settlement/Worker`, engine-free) is the
only thing permitted to authorise a path request.

| Bound | Default | Meaning |
|---|---|---|
| `MaxPathRequestsPerTick` | 1 | ≤ 20 requests/second/worker |
| `MaxPathAttemptsPerGoal` | 5 | then the goal is **deferred**, permanently |
| `RetryBackoffTicks` | 10 | 0.5 s between attempts, so the allowance is not burned in 5 consecutive ticks |
| `MaxPlanningDistance` | 64 m | checked **before** an attempt is spent, because distance is free to measure |

The site checks are budgeted too, and for the same reason. The hazard check
casts a ray and walks the burning-area list, so running it at the full 20 Hz
would be an unbudgeted per-tick cost hiding behind a leaf that claims its costs
are bounded. It is re-asked **at most once every 10 ticks (0.5 s)** and the
previous answer is held in between — far faster than a fire spreads or a tide
moves. The cached pair starts at *not loaded, hazardous*, so the first tick after
a goal is set always asks: the worker never acts on an assumed-safe default it
has not actually checked.

Worst case for one unreachable goal: **5 requests over 45 ticks (2.25 s)**, then
silence. Pinned by `AWorstCaseGoal_CostsAtMostFiveRequestsAndUnderThreeSecondsOfTicks`
so the numbers in this table cannot drift away from the code.

The deferral reasons are a closed set, and each is a sentence a player is shown:
`Unreachable`, `TooFar`, `Hazardous`, `NoAuthority`, `OutsideLoadedGround`.

**Everything fails closed.** `NoAuthority`, `OutsideLoadedGround` and `Hazardous`
are all checked *before* any path request is spent, and each is a refusal rather
than an assumption:

- authority = opted in **and** `ZNet.instance.IsServer()`; a missing `ZNet` is
  "no authority", not "probably solo";
- loaded ground = `ZoneSystem.instance.IsZoneLoaded(point)`; a missing
  `ZoneSystem` is "not loaded";
- hazard = `EffectArea.IsPointInsideArea(point, EffectArea.Type.Burning)` for
  fire, and `Floating.GetLiquidLevel(point)` minus
  `ZoneSystem.GetSolidHeight(point, out …)` for standing water; **ground that
  cannot be measured is hazardous**, because "how deep is this" has no safe
  default.

One further fail-safe: the attempt counter is spent inside the *decision*, not
when the adapter reports back. An adapter that asks the pathfinder and then
crashes, unloads or forgets to report has still spent the attempt. The worst
case of a broken adapter is a goal that defers early saying `Unreachable` —
which is the safe direction to be wrong in.

---

## 6. Why the AI swap happens on a prefab, never on a live creature

`ZNetView.Register` is `m_functions.Add(name.GetStableHashCode(), …)` — a
`Dictionary.Add`, which **throws on a duplicate key**.

`BaseAI.Awake` registers three RPC names. So spawning a vanilla creature and
*then* replacing its AI runs `BaseAI.Awake` twice against one `ZNetView` and
throws `ArgumentException` — inside a driver that does not catch (section 2).

`ForemanWorkerPrefab` therefore clones the base creature through Jotunn's
`PrefabManager.CreateClonedPrefab`, which clones into Jotunn's own **inactive**
container, so no `Awake` has run when the components are swapped. `Awake` then
runs exactly once, on a real spawned instance, with our AI already in place.

**The base creature name is data, not API.** Creature prefab names live in the
game's asset bundles, not in `assembly_valheim.dll`, so no amount of reading the
assembly can prove one exists — the same lesson the companion audit recorded
about the start-location name. It is a config value
(`Settlement/WorkerBaseCreature`), resolved at runtime, and **fails closed**: a
missing prefab, or one lacking `ZNetView`/`Character`/`ZSyncAnimation`/
`Rigidbody`/`BaseAI`, produces no worker and a message naming what was missing.

---

## 7. What is proved, and what is not

### Proved automatically, at this commit

| Claim | How |
|---|---|
| The budget is never exceeded, per tick or per goal | 35 tests in `WorkerMovementPlannerTests` |
| An unreachable goal defers with a reason and then costs nothing for 5 000 further ticks | `UnreachableGoal_DefersWithAReason_AndNeverSpinsAgain` |
| With no order, 2 000 ticks produce zero path requests and zero actions | `WithNoGoal_DoesNothing_ForeverAndFree` |
| Missing authority / unloaded ground / hazard refuse before spending anything | `RefusalsHappenBeforeAnyPathRequestIsSpent` |
| A lost adapter report cannot cause an infinite ask | `ALostAdapterReport_StillSpendsTheAttempt_…` |
| Suites and validator green | 1023 CC + 647 CT + 64 settlement, `Repository validation passed.` |

### Proved by reading the shipped binary

Sections 1–6. These are structural facts about 1.0.12, not behavioural
observations.

### NOT proved — still owed, and owner-gated

**Nothing in this leaf has been seen running in Valheim.** Specifically pending:

1. **The worker walks to a point and stops.** (#279 criterion 1)
2. **With no order it does nothing at all** — no drift, no idle noise, no
   animation twitch. (#279 criterion 2)
3. That `Dverger`, or whichever base creature is configured, actually clones and
   spawns as a usable body.
4. That other creatures and players do not treat the worker as a target — the
   faction is set to `Players`, which is a code change, not an observation.
5. That the deferral messages read sensibly in the console.

**No unit-test count in section 7.1 may be offered as evidence for any of these.**
The architectural go/no-go above is answered; the behavioural half is not, and
this document does not claim it is.

### How to observe it

In a **disposable world**, on a profile refreshed against 1.0.12 — the
`TCC-Dev`, `TCC-Compat` and `TCC-v1-Smoke*` profiles still log `0.221.12` from
August and **will mislead**:

```
1. Enable [Settlement] SettlementRuntimeEnabled = true in the config.
2. F5 →  cf_worker status       expect: authority granted, no worker
3.       cf_worker spawn        expect: a worker appears 3 m in front of you
4.  ... watch it for 60 seconds, doing nothing, while a boar walks past ...
5.       cf_worker goto <x> <z> using a point ~20 m away on flat ground
6.  ... it should walk there and stop, not overshoot or circle ...
7.       cf_worker goto <x> <z> using a point across deep water
                                expect: a deferral naming the reason
8.       cf_worker status       expect: a small, finite path-request count
9.       cf_worker despawn
```

Step 4 is the interesting one. Step 6 is the one people will look at.
