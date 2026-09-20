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
thinking**. Keep the ledger in a journal — for building material, reservations
against a designated container with one idempotent commit at placement; for
gathered material and issued tools, one custody place per unit, reconciled
against the worker's own persisted inventory (§5a).

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
| Save participation | none | the world save owns the pieces it places, and the worker body's own object (§5a) |
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

Then it calls `Player.PlacePiece` on the host player instance. Its signature,
read out of the installed assembly's own metadata rather than inferred (#380):

```csharp
void Player.PlacePiece(Piece piece, Vector3 pos, Quaternion rot,
                       bool doAttack, bool cheated)
```

Three facts about it, each of which was an open question and none of which
should have to be rediscovered:

- **The position and the rotation are parameters.** They are not read from
  `m_placementGhost`. That was the real risk in choosing this method: the
  shipped build flow is `UpdatePlacementGhost` then `TryPlacePiece`, the ghost
  is the local player's own aiming object, and an NPC has no way to aim it — so
  had the position come from there, a call would have built the wall wherever
  the player happened to be looking. It does not. An NPC can aim a placement.
- **Both booleans are false, and neither by default.** `doAttack` makes the
  *player* swing, which is the human build animation and has no business firing
  because an NPC put a wall up twenty metres away. `cheated` marks the piece as
  conjured, and it is the one flag this runtime exists not to set: the material
  came out of a player's container through custody, so the piece is not cheated
  and must not say it is.
- **It returns `void`.** There is no success to read back. Whether a piece is
  standing is answered by looking at the site afterwards — the same way it is
  answered for a piece the player built — which is why construction progress is
  read from the world rather than remembered from what was asked for.

**What the ghost also judges, and what this runtime does about it.** Vanilla's
placement ghost weighs constraints the piece itself declares — biome, cultivated
ground, tilting surfaces, ceiling- and floor-only pieces, dungeons, deep snow,
teleport areas, connection requirements, space requirements. Two of them are
reimplemented exactly, because the game asks them as a plain yes/no about a
point: **dungeons**, through `Character.InInterior(point)`, and **biome**,
through `Heightmap.FindBiome(point)` against the piece's own `m_onlyInBiome`
mask. Vanilla's own no-build zone, `Location.IsInsideNoBuildLocation(point)`, is
a gate in its own right. **Every other constraint a piece declares is a
refusal**, naming the constraint, and a permission that is off (`m_enabled`,
`m_allowedInDeepSnow`) counts as much as a prohibition that is on. So no piece
this runtime cannot judge is ever placed, and the rule is a check rather than a
property of whichever pieces a blueprint happens to use.

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

**Scope of this section (corrected 2026-09-17, #316).** It is about **building
material for a cottage**: what a reservation against the designated container
means, and where it commits. **Gathered material and issued tools are
different**, and §5a states the difference. Where the two disagree below, §5a
is the current build.

**Chosen: a journal of reservations against the designated container. For this
flow the worker's inventory is evidence, not the ledger.**

An unmodified `Humanoid`'s inventory is **not saved at all**: `m_inventory` is a
plain field with no save or load, rebuilt on every instantiation, so a relog, a
zone unload or a despawn destroys whatever it holds. The original wording here
("ZDO-backed and can be lost") understated it. Either way the conclusion stands
for this flow: if that inventory were the ledger, materials would vanish on
events that are not cancellations — which #273's gate 3 forbids. A worker body
that carries gathered material or an issued tool therefore persists its own
inventory in **its own** network object (§5a, D9).

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

## 5a. Addendum: gathered material and issued tools (#316)

Collection (#315) gave the worker something §5 never had: **real items he holds
for hours, across reloads**. The full model, the recovery commands and the live
checklist are in [`CUSTODY_AND_RECOVERY.md`](CUSTODY_AND_RECOVERY.md); this
section records only what it changes about the decisions above.

**Places, not evidence.** Each unit an order has taken is in exactly one place —
`SourceGround`, `Worker`, `Cart`, `Destination`, `Player`, `Lost` — recorded in
the same journal. The journal is still the ledger, but the worker's inventory is
now one of the places it accounts for, compared against the record on every
load, not a picture of one.

**The worker body keeps its own inventory (D9).** Its own network object holds
`tcc.worker.key`, `tcc.worker.inventory` (vanilla's `Inventory.Save` package)
and `tcc.worker.revision`, written from the inventory's own change callback, in
the same call as the change. This is mod data in a **mod-created** object; no
vanilla object is ever written to. It is what makes a relog, a zone unload and a
despawn refusal honest, and it is the one reason the §2 table's "save
participation" row now reads "the pieces it places, **and the worker body's own
object**".

**Carrying is not free of consequence.** §5's "losing it loses nothing from the
ledger" is true of a *reservation*; it is not true of gathered material:

- **Death** drops everything he carries through vanilla's own drop, and the
  record moves it to `Lost` with the place, for a person to pick up. Nothing is
  refunded or re-granted.
- **Despawn** is refused while a job holds him, and again while he carries
  anything or the record says he holds tools or material.
- **Zone unload** loses nothing: the body is re-bound by key and loads its
  stored inventory before any work resumes.

**The journal is reconciled against the world.** Journal rows reach disk at
once, world effects only at the next world save, so `ZNet.WorldSaveStarted`
writes a world-save marker and every load decides which rows the loaded world
contains; rows the world rolled back are voided and never replayed. What
survives is then compared with the actual inventories, and every difference
waits for a person with the command that answers it. Uninstalling the mod is the
one case the record cannot absorb by itself, which is why "Release everything"
comes before removing it.

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
