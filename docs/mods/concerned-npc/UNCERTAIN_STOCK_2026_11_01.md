# #376 uncertain material availability

Base: 523c895ca12b61623df543e28cc589c1438e276b. Branch:
fix/cnpc-376-uncertain-stock. Independent of the #372 API guard and #379 priority
repair; the lead integrates these separate patches.

## Concrete failure and corrected behavior

A job reserves 40 units from a chest, then records an uncertain commit. Its
reservation is no longer Held, so ReservedIn correctly stops describing it as a
known live hold. But AvailableIn previously subtracted only Held claims: a fresh
count of 40 or 100 units then became known free stock. Neither that count nor an
exclusion of the old reservation establishes whether its previous commit spent
the units or still owes them back. A planner could treat unresolved material as
available to another step/job.

AvailableIn now returns null (unknown) if an Uncertain reservation names the exact
container, current load and material identity being queried. exceptThis never
excludes uncertain evidence. This uses the existing nullable result: zero means
a known shortfall; null means wait for reconciliation. The query does not change
any claim, move any material, compensate a failure or invent a refund.

The check compares complete material identity (name, quality, variant), container
place/key and load epoch. It does not globally hide unrelated stock. Ordinary
Held subtraction and self-exclusion still work; known Committed/Refunded claims
still release their availability. HasUncertain remains true, and automatic
Refund, Commit and RefundAllFor cannot resolve an uncertain claim.

ReservedIn still reports known Held quantities, not a guessed quantity for an
uncertain outcome. Reserve remains a journal of supplied claims, not a capacity
or movement authority. A caller must use AvailableIn/HasUncertain and its actual
reconciliation gate; bypassing those by inserting a fresh claim is not granted
permission to move items.

## Meaningful evidence

UncertainMaterialAvailabilityTests compiles and calls the real source. Before
repair, 9 cases fail and 3 controls pass. After repair, all 12 pass; combined
existing custody/reservation coverage totals 86 passing cases. Cases include:
physical stock 0/10/40/100/int.MaxValue, own-claim exclusion, repeated queries,
restated uncertain ids, terminal-state preservation and forbidden automatic
settlement. Scope controls cover other chest, other load, other material,
quality/variant and unavailable physical counts. Known settlement and ordinary
held-claim controls protect existing behavior.

The handoff records the exact committed full gate, source hash and patch hash.
There is no native or in-game observation from these tests.

## Requirement coverage and remaining code

| #376 acceptance | Current source and evidence | Status |
|---|---|---|
| Two production roles use the shared custody/reservation surface | Execution and material-book types remain internal; current product custody continues through its existing product/shared-source executor. This repair adds no product caller. | NOT MET |
| Absorb Foreman's journal-first ordering and world-save marker rule | Existing ledger/executor tests cover intent before add/remove, measured deltas and uncertainty; Foreman's native journals and marker integration remain in its own runtime. | Native adoption remains CODE |
| Conservation, interrupted/resumed and exactly-once tests | Existing tests retained; added unknown-stock query regression covers uncertainty without automatic compensation or release. | Source tests pass; not proof of two-role adoption |
| Authoritative gate | Full build.ps1 + verify.ps1 run under the shared build lock, recorded in handoff. | See exact source receipt |

Claude's active work includes Foreman custody tools/SettlementRuntime, Gunnar
worker/collection adapters, localization and the central validator. None is
edited here. The API exposure, real caller migration and planted confinement
must be coordinated with that integration; do not introduce a parallel executor
or bypass existing marker/authority behavior to avoid a file conflict.

No public API/version/key, saved format, inventory adapter, engine mutation,
profile, installed mod, bulk-testing ZIP or game session changes. #376 stays
OPEN. Remaining code cannot be transferred to #470 as gameplay-only work.
