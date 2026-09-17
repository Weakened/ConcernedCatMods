# Cart pulling and resource collection: evidence

**Statuses:**
- **implemented:** the code exists.
- **automated-tested:** a named test proves it.
- **adapter-audited:** checked against the installed game binaries.
- **observed:** seen in game, with build, profile and scenario recorded.
- **pending:** none of the above yet.

A merged PR, a running process or a passing test count is **not** gameplay acceptance. Live rows stay `pending` until
observed.

**Game build for every row:** Valheim 1.0.12 (network 40, Steam build 25253764), `assembly_valheim.dll` SHA-256
`27a766a8d23a7bd8b6a54fb9ad0452a96c305fb3629b39c40527c09a1c393a84`, Unity 6000.0.75f1. Test profile TCC-HulgiSmoke:
BepInEx 5.4.23.5, Jötunn 2.30.0.

## Audits (read-only, 2026-09-17)

| Audit | Covers | Result |
|---|---|---|
| `CART_SEAM_AUDIT.md` | `Vagon` attach, detach, ownership and mass on 1.0.12; `Character` motor; `BaseAI` steering; Teamster brake and validator | adapter-audited: seam = direct `AttachTo`/`Detach` with D4 preconditions; `CART_INTERNALS.md` corrections listed |
| `PICKUP_SEAM_AUDIT.md` | `Pickable`, `ItemDrop`, `Inventory`, `Container` on 1.0.12; bundle enumeration of 84 `Pickable` components | adapter-audited: predicate C1–C5, pick/trace/take in one call, world-save marker rule |
| `FOREMAN_RUNTIME_AUDIT.md` | Foreman worker runtime, ledgers, capability boundary, open defects | adapter-audited: reuse map, rule conflicts R1–R13 |

The audits live in `concernedcat-handoffs/2026-09-17-gunnar-thorstein-work/audits/`, outside the repository.

## Gate A: contract and compatibility

| Req | Evidence | SHA | Result |
|---|---|---|---|
| C1 worker authority rule (D3) | `WorkContractTests.AuthorityIsGrantedOnlyToAnOptedInHostWithNobodyElseConnected` | C1 commit | automated-tested |
| ARCH-01/02 one job holds an identity | `WorkContractTests.OneJobHoldsAnIdentityAndHomeMayMoveItOnlyWhileResting` | C1 commit | automated-tested |
| CART-06 phase table | `HaulContractTests.*Phase*`, `TheDocumentedLifecycleIsLegalEndToEnd` | C1 commit | automated-tested |
| CART-01 one lease per worker and per cart, epoch refusal | `HaulContractTests.OneLeasePerWorkerOneLeasePerCartAndPayloadCheckedIds`, `AReloadEndsEveryLeaseAndStaleKeysNeverComeBack` | C1 commit | automated-tested |
| ARCH-03 wire enums by name only | `WorkContractTests.EnumsTravelAsExactNamesOnly`, `HaulContractTests.EveryPhaseTravelsUnderItsOwnName` | C1 commit | automated-tested |
| ARCH-03 capability map keyed by major, BCL-only | `WorkContractTests.AnEndpointIsFoundOnlyUnderItsContractMajorAndOnlyAsTheBclFunc` | C1 commit | automated-tested |
| GATHER-01 quotas and order shape | `WorkContractTests.OnlyStoneAndWoodAreCollectableAndQuotasAreBounded`, `AnOrderNeedsDistinctQuotasADeliveryAndAMode` | C1 commit | automated-tested |
| COOP-04 progress buckets | `WorkContractTests.ProgressCountsEachUnitOnceAndEstimatesNever` | C1 commit | automated-tested |
| DATA-01 one order per source | `WorkContractTests.ASourceIsClaimedByOneOrderInOneWorldLoad` | C1 commit | automated-tested |
| Separately built products exchange payloads | #317 two-assembly test | — | pending |
| Missing or mismatched provider fails safe | #317 | — | pending |
| Hulgi and existing Teamster utilities preserved | full CC and CT suites at each integration | — | pending |

## Gate B: Gunnar alone (#313, #314)

| Req | Evidence | SHA | Result |
|---|---|---|---|
| CART-02 seam probe fails closed | #313 tests | — | pending |
| CART-02/03 approach, align, hitch an empty cart | live | — | pending |
| CART-04 loaded cart moves by the joint; mass and cargo unchanged | live | — | pending |
| CART-05 route refusals (steep, narrow, water, gap, door, unsafe stop) | #314 tests; live | — | pending |
| CART-06 stall vs wedge, bounded recovery, no oscillation | #314 tests; live | — | pending |
| CART-06 takeover, brake, authority loss, destroyed or unloaded cart, stale identity after reload | #313 tests; live | — | pending |

## Gate C: Thorstein alone (#315, #316)

| Req | Evidence | SHA | Result |
|---|---|---|---|
| GATHER-03 predicate rejects the audited look-alikes | #315 tests | — | pending |
| GATHER-04 quantity-aware selection | #315 tests | — | pending |
| GATHER-05 pick, trace, take; no duplication, no race | #315 tests; live | — | pending |
| GATHER-06 carry budget, return trips, delivery counts; unused tools unworn | #315 tests; live | — | pending |
| GATHER-02 scope never falls back; moving player vs fixed anchor | #315 tests; live | — | pending |
| DATA-02/03 executor ordering under fault injection | #316 tests | — | pending |
| DATA-03 world-save marker voiding | #316 tests; live kill test | — | pending |
| DATA-05 carried items and tools survive relog and reload | live | — | pending |

## Gate D: cooperation (#317)

| Req | Evidence | SHA | Result |
|---|---|---|---|
| COOP-02 loop, capacity checkpoint, second trip | #317 tests; live 20 Stone + 30 Wood | — | pending |
| COOP-03 provider or cart loss pauses after reconciliation | #317 tests; live | — | pending |
| COOP-04 visible controls and honest progress | #317 tests; live | — | pending |
| Pre-existing cargo accounted | #316/#317 tests; live | — | pending |

## Gate E: recovery and review

| Req | Evidence | SHA | Result |
|---|---|---|---|
| Fault injection at every mutation and persist of pickup, load, unload, tool transfer | #316 | — | pending |
| Independent exact-head review of each slice | R reports | — | pending |
| Validator, all suites, Release builds, API audits at the candidate SHA | final handoff | — | pending |
