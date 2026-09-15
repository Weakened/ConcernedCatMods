# The first cottage — dependency graph

The bounded set of leaves that produces #273's first playable outcome, and
nothing else. **Eleven leaves.** Not a roadmap, not a backlog, and deliberately
not a fifty-ticket expansion: everything past one cottage and one resident is a
decision to take *after* this works, with evidence in hand.

Authority decisions these all inherit:
[`SETTLEMENT_AUTHORITY.md`](SETTLEMENT_AUTHORITY.md).

---

## The graph

```
CF-SET-001  contracts + journal            (game-free; blocks everything)
      │
      ├── CF-SET-002  worker actor spike   ── go/no-go: does overridden UpdateAI
      │         │                             actually stop vanilla's own behaviour?
      │         │
      │         ├── CF-SET-005  designated harvest  ─┐
      │         └── CF-SET-007  visible carrying     │
      │                                              │
      ├── CF-SET-003  placement authority  ── go/no-go: can every check we cannot
      │         │                             reimplement become a refusal?
      │         │                                     │
      │         └── CF-SET-008  staged construction ──┤
      │                                              │
      ├── CF-SET-004  settlement designation +       │
      │               recruitment                     │
      │                                              │
      └── CF-SET-006  custody: reserve/commit/refund ─┤
                                                     │
                            CF-SET-009  housing + one resident
                                          │
                            CF-SET-010  recovery + conservation under failure
                                          │
                            CF-SET-011  end-to-end capture + candidate
```

`CF-SET-002` and `CF-SET-003` are the two that can fail. Both have a written
go/no-go and a named fallback; neither is allowed to become an open-ended
investigation.

---

## The leaves

| # | Leaf | Delivers | Depends on |
|---|---|---|---|
| **001** | Contracts and journal | Game-free identity, work-order state machine, request ids, leases, and a replayable journal with an idempotent commit point. Source-shared under `src/Shared/Settlement`, exercised entirely off-game. | — |
| **002** | Worker actor spike | One worker that walks to a point and stops, on a `BaseAI` subclass whose `UpdateAI` does none of vanilla's own thinking. Budgeted pathfinding; unreachable goals defer **with a stated reason**. | 001 |
| **003** | Placement authority | Explicit ward (`PrivateArea.CheckAccess`), station (`CraftingStation.HaveBuildStationInRange`), ground/biome and cost checks, then `Player.PlacePiece` via the host. Every check that cannot be faithfully reimplemented **refuses**. | 001 |
| **004** | Designation and recruitment | The player marks one small settlement area and one harvest area, designates one supply container, and recruits one Foreman worker. All four are explicit player acts. | 001 |
| **005** | Designated harvest | Felling **only** inside the marked harvest area, through `TreeBase.Damage` so vanilla drops follow. No protected or decorative trees. No drop-table reimplementation. | 002, 004 |
| **006** | Custody | Reserve against the designated container, carry as visual evidence, commit once at placement, refund unspent exactly once. | 001, 004 |
| **007** | Visible carrying | The worker is visibly holding what it is carrying. Vanilla has no "carry" concept, so this is equipment or an attached visual — named as scope, not assumed. | 002, 006 |
| **008** | Staged construction | A player-approved cottage blueprint placed as **real vanilla pieces in a deliberate order over time**. Vanilla has no partial-build state (zero occurrences of `buildprogress`/`partialbuild`/`constructionstage` across `Player`, `Piece` and `WearNTear`), so staging can only mean ordered placement — which is the honest reading of gate 4 anyway. **No timer spawns a finished cottage.** | 003, 006 |
| **009** | Housing and one resident | The completed cottage provides measured capacity; one slot is reserved; one ordinary resident is admitted, persists across reload, and is never duplicated. Unsafe housing is invalidated safely. A small credible routine, not a dynasty simulator. | 008 |
| **010** | Recovery and conservation | Adversarial failure at **every** transition — reserve, carry, commit, cancel, place — each with a forced failure and a reload. The invariant: container + reservations + placed cost is unchanged across any sequence. | 006, 008, 009 |
| **011** | Capture and candidate | End-to-end recording of recruitment → order → real gather → visible carry → staged build → resident arrival → relog persistence, plus the negative cases. A distinctly labelled candidate. | 010 |

---

## Negative cases, owned by 010 and captured in 011

Not optional, and not a later hardening pass — each is a case the design must
already have an answer for:

insufficient supplies · unreachable work · cancellation mid-order · two
simultaneous orders · disconnect and reload at every transition · invalid
housing · no compatible authority · the designated container emptied by the
player mid-order · the harvest area cleared of trees · the settlement area
overlapping somebody's ward.

---

## What "done" means for this graph

One cottage stands, built from materials that were really gathered or really
drawn from a container the player designated, by a worker that was really seen
carrying them, and one resident lives in it after a relog. Conservation holds
across every forced failure.

Then the project stops and looks at what it has. **A hundred residents is a
scaling research question, not the next leaf** — and no measurement of any NPC
count exists anywhere yet, so nothing about scaling may be inferred from this
proof working.
