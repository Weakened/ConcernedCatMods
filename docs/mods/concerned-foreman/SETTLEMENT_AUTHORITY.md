# Settlement authority — architecture decision record

**Status:** accepted for the first cottage proof (#273). Revisit before anything
beyond one cottage and one resident.
**Scope:** the opt-in settlement runtime inside Concerned Foreman. Nothing here
changes Concerned Cartographer, Concerned Teamster, or the local-only companion
rules of #264.

---

## 0. The decision in one paragraph

Build a **small independent runtime against vanilla APIs**, take no dependency
on any third-party NPC mod, and fork nothing. Place pieces through the **host
player** with the validity, ward and cost checks reimplemented **explicitly**,
because vanilla's own checks do not live in the method that places. Drive the
worker as a **`BaseAI` subclass whose `UpdateAI` does none of vanilla's own
thinking**. Treat the worker's inventory as **visual evidence**, never as the
ledger — the ledger is a journal of reservations against a designated container,
with one idempotent commit at placement.

---

## 1. Why not integrate with, or fork, an existing settlement mod

An independent feasibility review examined the two obvious candidates against
the installed game.

**VikingSettlements** — not available, rather than risky. Its plugin class is
`internal` with no public API, events or registry; there is no 1.0.x build and
no commit since a month before Valheim 1.0; and it declares
`EveryoneMustHaveMod` with `VersionStrictness.Minor`, which would force a
server-and-every-client install on a portfolio that must stay independently
installable and client-safe. Its own construction instantiates a finished layout
in one call and its chest consumption has no ward check and no reservation — so
even a working integration could not produce the evidence #273 asks for.

**Norsemen / VikingNPC** — **unlicensed**, and deprecated. Not forkable at all,
whatever its merits. One thing is worth carrying as a *reading* rather than as
code: the shape `move → stop → look → swing a real axe → let `TreeBase.Damage`
produce vanilla drops` is right, and its resource-granting timer is the wrong
half.

**A larger fork** would inherit a pre-1.0 codebase whose every part we would keep
is a part #273 forbids. What remains after fixing them is a rewrite wearing
someone else's licence obligations.

**Carried forward from the integration option, at near-zero cost:** a
coexistence advisory. Detect `com.abjumb.vikingsettlements` by GUID, log a
warning, take no dependency and no load-order coupling — the shape Teamster's
`CompatibilityKnownMods` table already uses.

---

## 2. Why this cannot reuse #264's companion authority

| | Hulgi (#264) | Settlement worker (#273) |
|---|---|---|
| Networking | none — no `ZNetView`, no ZDO | a real networked entity, host-owned |
| Save participation | none | the world save owns the pieces it places |
| Visible to others | **no**, by construction | yes, like any creature |
| Touches shared state | never | trees, containers, pieces, housing |
| Fails by | quietly not appearing | refusing to act, with a repairable journal |

A local renderer cannot safely be the authority for a shared tree, a chest, a
cottage or a villager: two clients would each believe they had felled the same
tree. So this is a **different subsystem with a different contract**, not a
relaxation of #264. Hulgi's rules are unchanged and still apply to Hulgi.

**Consequences that follow, and are binding:**

- The settlement runtime is **off by default** and turning it on is an explicit,
  separate choice.
- Installing Foreman for its **diagnostics** must never require a server. The
  diagnostics half stays read-only and client-safe.
- Solo and local-host are the initial targets. On a dedicated server the runtime
  requires the server to be running a compatible build; **missing authority or
  an incompatible peer fails closed** — it refuses to act and says why. It never
  takes client ownership of something it was not granted.

---

## 3. Decision: who places a piece

**Chosen: the host `Player` places, and we re-check explicitly beforehand.**

The problem is a real one found in the installed 1.0.12 binary.
`Player.TryPlacePiece` runs `UpdatePlacementGhost` and switches over
`PlacementStatus` (`NoBuildZone`, `PrivateZone`, `BlockedbyPlayer`, `MoreSpace`,
`ExtensionMissingStation`, `WrongBiome`, `NeedCultivated`, `NeedDirt`,
`NotInDungeon`, `DeepSnow`, `NoSnow`, `NoTeleportArea`, `NoRayHits`, `Invalid`)
*before* delegating — and it is bound to a `Player` and its placement ghost, which
an NPC cannot drive. `Player.PlacePiece` does **none** of that: it instantiates,
sets the creator, calls `PrivateArea.Setup`, `WearNTear.OnPlaced()` and effects.
Cost is consumed separately by the caller. **So `PlacePiece` alone bypasses
validity, ward and cost** — which #273's gate 4 forbids outright.

The rejected alternatives:

- *Call `PlacePiece` and hope.* Bypasses every gate. Not an option.
- *Fully custom placement that never touches `Player`.* Then nothing about the
  result is vanilla — creator, ward setup, wear-and-tear registration and stats
  all diverge, and a piece built by Foreman would not behave like a piece built
  by hand.

So: before any placement, the runtime re-checks, explicitly and by name:

1. `PrivateArea.CheckAccess(point, radius, flash: false, wardCheck: true)` — the ward gate.
2. `CraftingStation.HaveBuildStationInRange(name, point)` — the workbench gate.
3. Ground, biome and clearance through the same `ZoneSystem` / `Heightmap`
   queries the companion placement probe already uses.
4. The real cost, from `Piece.m_resources`, consumed through
   `Player.ConsumeResources` against **reserved** custody — never from whatever
   happens to be in a nearby chest.

Then it calls `Player.PlacePiece` on the host player instance.

**Every check that cannot be faithfully reimplemented becomes a refusal, not an
assumption.** If the runtime cannot establish that a placement is legal, it does
not place and it says which check it could not make. That is the whole of gate 4
in one sentence, and the largest single piece of real work in the proof.

**Range is honest.** Placement happens only while the host player is loaded and
the settlement is inside loaded ground. There is no offscreen building. The
first proof **reports** that limit rather than hiding it.

---

## 4. Decision: what the worker is

**Chosen: a `BaseAI` subclass whose `UpdateAI` does none of vanilla's own
thinking.**

The alternative — driving `Pathfinding.GetPath(…, AgentType.Humanoid)` plus
`Character.SetMoveDir` from a non-AI component — keeps more control and
reimplements movement. The deciding argument is the opposite of #264's: here we
*want* vanilla's ownership, ZDO and replication semantics, because the worker is
supposed to be a real creature that other players can see. `BaseAI.MoveTo`,
`FindPath` and `HavePath` are `protected`, so subclassing is how a mod reaches
the movement vanilla itself uses, and a worker that moves by a reimplementation
moves visibly differently.

What the subclass must do, and what CF-SET-002 must prove:

- Override `UpdateAI` so no vanilla wandering, alerting or threat behaviour runs.
  The worker does exactly what its current order says and nothing else.
- Pathfinding requests are **budgeted**: a bounded number per tick, a bounded
  path length, and an unreachable or hazardous goal **defers with a stated
  reason** rather than retrying forever.
- No combat, no aggravation, no faction behaviour.

**This was the one decision with real residual risk**, because `BaseAI` looked
like it carried behaviour we would be switching off rather than behaviour we
never had.

**Resolved by CF-SET-002 (#279): GO, and the premise was wrong in our favour.**
`BaseAI.UpdateAI` in the installed 1.0.12 build contains *no* wandering, alerting
or threat behaviour to switch off — it is an ownership gate plus housekeeping,
and every piece of vanilla's own thinking lives one level down in `MonsterAI`
and `AnimalAI`. A worker deriving from `BaseAI` directly does not suppress
vanilla behaviour; it never inherits any. **The non-AI fallback is not needed**
and reimplemented movement is a cost this project does not have to pay.

The risk that was real, and that the spike found, is elsewhere: several vanilla
behaviours run *outside* `UpdateAI` and are untouched by overriding it — a
repeating idle sound armed in `Awake`, three registered RPCs, three server-wide
`MessageAll` broadcasts, and two static cross-AI paths. The full evidence, each
call site, and what was done about each is in
[`WORKER_ACTOR_SPIKE.md`](WORKER_ACTOR_SPIKE.md). Read it before touching the
worker.

---

## 5. Decision: where material custody commits

**Chosen: a journal of reservations against the designated container. The
worker's inventory is visual evidence, not the ledger.**

`Humanoid.GetInventory()` is ZDO-backed and can be lost on death, despawn or
zone unload. If it were the ledger, materials would vanish on events that are
not cancellations — which #273's gate 3 forbids.

The flow, and the single commit point:

```
designated container
   → RESERVE (request id + lease)        ← the only write that removes from the container
   → worker carries (visual evidence)
   → COMMIT at placement                  ← the single idempotent commit
   → placed piece
```

- **Reserve** deducts from the container and records a journal entry keyed by a
  request id. It is the only step that takes anything from a player's chest.
- **Carry** is presentation. Losing it — death, despawn, unload — loses nothing
  from the ledger; the reservation still stands and the work resumes.
- **Commit** happens in one guarded step at placement: `ConsumeResources` against
  the reserved amount, then `PlacePiece`. The request id makes it idempotent, so
  a retry after a crash cannot consume twice.
- **Cancel** returns *unspent* reservation to the container exactly once, keyed
  by the same request id.

**When the two halves of the commit disagree** — the piece placed but the
consume failed, or the reverse — the runtime **stops with a repairable journal
entry and a clear message**. It does not guess which one happened. A settlement
that halts and explains itself is recoverable; one that guesses is not.

Conservation is tested adversarially at **every** transition, with a forced
failure and a reload at each: reserve, carry, commit, cancel. The invariant is
that the sum of container + reservations + placed cost is unchanged across any
sequence of failures.

---

## 6. What this decision does not cover

Named so a successor does not assume they were considered:

- **Offscreen or away-from-base work.** Not in the first proof. Gate 6 permits it
  only behind an authoritative active simulation with honest loaded/unloaded
  behaviour, and reconciling only already-reserved claims exactly once. The first
  proof reports its range limit instead.
- **More than one resident, or more than one order at a time.** Simultaneous
  orders are a *negative* test case in the first proof, not a feature.
- **Dedicated-server operation.** Fails closed until separately tested, and
  evidence will be labelled solo / hosted / dedicated / mixed-client separately.
- **Any performance claim.** No measurement of any NPC count exists anywhere —
  not in this repository and not in either upstream project examined. Nothing
  about scaling should be inferred from this proof working.
