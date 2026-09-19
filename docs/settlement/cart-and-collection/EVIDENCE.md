# Cart pulling and resource collection: evidence

**Statuses:**
- **implemented:** the code exists.
- **automated-tested:** a named test proves it.
- **adapter-audited:** checked against the installed game binaries.
- **observed:** seen in game, with build, profile and scenario recorded.
- **pending:** none of the above yet.

A merged PR, a running process or a passing test count is **not** gameplay acceptance. Live rows stay `pending` until
observed.

**Game build.** Every row states its build. Audits: Valheim 1.0.12 (Steam build 25253764), `assembly_valheim.dll`
SHA-256 `27a766a8d23a7bd8b6a54fb9ad0452a96c305fb3629b39c40527c09a1c393a84`. Since 2026-09-17 10:20: **Valheim 1.0.14**
(Steam build 25364265), SHA-256 `f64998168a0dd37ec774816808f914ed68376be1b9670cd05a6c2f27c8017fb6`,
`assembly_utils.dll` `201e2746…4b12dd0`, `UnityEngine.PhysicsModule.dll` unchanged. Unity 6000.0.75f1. Test profile TCC-HulgiSmoke:
BepInEx 5.4.23.5, Jötunn 2.30.0.

## Audits (read-only, 2026-09-17)

| Audit | Covers | Result |
|---|---|---|
| `CART_SEAM_AUDIT.md` | `Vagon` attach, detach, ownership and mass on 1.0.12; `Character` motor; `BaseAI` steering; Teamster brake and validator | adapter-audited: seam = direct `AttachTo`/`Detach` with D4 preconditions; `CART_INTERNALS.md` corrections listed |
| `PICKUP_SEAM_AUDIT.md` | `Pickable`, `ItemDrop`, `Inventory`, `Container` on 1.0.12; bundle enumeration of 84 `Pickable` components | adapter-audited: predicate C1–C5, pick/trace/take in one call, world-save marker rule |
| `FOREMAN_RUNTIME_AUDIT.md` | Foreman worker runtime, ledgers, capability boundary, open defects | adapter-audited: reuse map, rule conflicts R1–R13 |

The audits live in `concernedcat-handoffs/2026-09-17-gunnar-thorstein-work/audits/`, outside the repository.

**Re-verification for Valheim 1.0.14** (lead, 2026-09-17). Full `ilspycmd` decompiles of both builds were diffed.
- **31 of 630 types changed.**
- **Unchanged:** `Vagon`, `Pickable`, `ItemDrop`, `Container`, `BaseAI`, `Pathfinding`, `ZDOMan`, `ZNetScene`,
  `Location`, `PrivateArea`.
- **Changed:**
  - `Version` (1.0.12 → 1.0.14);
  - `Character` (Ashlands heat only);
  - `Humanoid` (a null check in AI attack selection);
  - `Inventory` (cheated items no longer stack with normal ones: `FindFreeStackItem` matches `m_cheated`);
  - `ZNet` (the save and logout flow around `WorldSaveStarted`, which still exists);
  - `Terminal` (one new command; entries use the same constructor shape);
  - `TerrainComp` (a private helper renamed);
  - `Piece` (an achievement parameter renamed);
  - `Player` (a new private field);
  - `Minimap`, `Attack`, `SEMan`, UI and settings types (no signature changes).

The seams do not depend on any changed member. Each agent re-runs its own API audit against the new binary.

## Contract revisions (merged)

| Rev | Commit | What |
|---|---|---|
| C1 | `bbdda45` (PR #318) | The freeze: Workers, Interop `haul/1`, the haul domain, collection orders, custody, journal kinds, persistence |
| C2 | `69744be` (PR #319) | Additive: player-pause and duplicate-body reasons with their wire twins, `HaulCommandResult.Detail`, `MaxParkingGradeRatio`, `WorldLoadEpoch`/`TryRecoverOrder`/`RecordRebound` |
| C3 | `1746455` (PR #322) | Documentation: no cart doorways this slice, the cart in-use carve-out, the load-restatement marker |
| C4 | `323edea` (PR #323) | Documentation: lost authority parks safely or holds, a dead or duplicated body is a teardown path, unload holds end on consumer silence, `StopAndWait` on a stopped haul is Accepted |

## Integration heads, builds and test deployments (lead-verified)

Every number in this table was produced by the lead on this machine, not reported by an agent. Nothing here is
gameplay evidence, and none of it is merged into `main`.

| What | Head | Checks the lead ran | Deployed |
|---|---|---|---|
| Gunnar standalone slice (#313) | `e2b75c7`, worktree `cc-integration/gunnar-e2b75c7` | Teamster 805 tests, validator passed, Release build 0 errors, adapter scan for position/velocity/force/ownership writes | Teamster `1.0.4+e2b75c7`, SHA-256 `71356D30…F16F05`, TCC-HulgiSmoke only, `[Workers] GunnarHaulingEnabled = true` |
| Agent A's non-live scope (#313) | `4ed1921` | Teamster 813 tests, hauling API audit PASS on 1.0.14 | not deployed; reviewed as `R-313-4ed1921.md` → **not merge-ready** (1 blocker, 2 majors) |
| Thorstein solo slice (#315, #316) | `be23c86`, branch `integration/thorstein-solo` | Settlement 596 tests, Foreman Release 0 errors, validator passed | Foreman `0.1.0+be23c86`, SHA-256 `3E0B8705…2006`, TCC-HulgiSmoke only, `SettlementRuntimeEnabled = true`, `WorkerCarryWeight = 40` |

Earlier deployments of the same two slices (`thorstein-7b670ae`, `thorstein-6af7fec`, `thorstein-25c075b`) are
recorded and marked superseded in `concernedcat-handoffs/2026-09-17-gunnar-thorstein-work/runtime/`.

**Independent reviews:** `reviews/R-313-4ed1921.md` (Gunnar mechanics: blocker B1 detach-first on the body-death,
unload and duplicate paths; majors M1 release-depends-on-lease and M2 untested seam decisions; the fixes are in
progress) and `reviews/R2-315-316-be23c86.md` (Thorstein slice, in progress).

## Gate A: contract and compatibility

| Req | Evidence | SHA | Result |
|---|---|---|---|
| C1 worker authority rule (D3) | `WorkContractTests.AuthorityIsGrantedOnlyToAnOptedInHostWithNobodyElseConnected` | `bbdda45` | automated-tested |
| ARCH-01/02 one job holds an identity | `WorkContractTests.OneJobHoldsAnIdentityAndHomeMayMoveItOnlyWhileResting` | `bbdda45` | automated-tested |
| CART-06 phase table | `HaulContractTests.*Phase*`, `TheDocumentedLifecycleIsLegalEndToEnd` | `bbdda45` | automated-tested |
| CART-01 one lease per worker and per cart, epoch refusal | `HaulContractTests.OneLeasePerWorkerOneLeasePerCartAndPayloadCheckedIds`, `AReloadEndsEveryLeaseAndStaleKeysNeverComeBack` | `bbdda45` | automated-tested |
| ARCH-03 wire enums by name only | `WorkContractTests.EnumsTravelAsExactNamesOnly`, `HaulContractTests.EveryPhaseTravelsUnderItsOwnName` | `bbdda45` | automated-tested |
| ARCH-03 capability map keyed by major, BCL-only | `WorkContractTests.AnEndpointIsFoundOnlyUnderItsContractMajorAndOnlyAsTheBclFunc` | `bbdda45` | automated-tested |
| GATHER-01 quotas and order shape | `WorkContractTests.OnlyStoneAndWoodAreCollectableAndQuotasAreBounded`, `AnOrderNeedsDistinctQuotasADeliveryAndAMode` | `bbdda45` | automated-tested |
| COOP-04 progress buckets | `WorkContractTests.ProgressCountsEachUnitOnceAndEstimatesNever` | `bbdda45` | automated-tested |
| GATHER-03 Hulgi joins a survey only when his own product says he is here and free | `PresenceCapabilityExchangeTests` (19, two assemblies); `SurveyParticipationTests` (28) | #317 | automated-tested |
| GATHER-03 no false credit: absent, hidden, unknown and busy each keep the survey solo | `PresenceCapabilityExchangeTests.{ANotInstalledCartographerIsSoloWithoutAsking,AHiddenCompanionIsKnownButNotHere,APlayerWhoHasNotMetHimSurveysAlone,ABusyCompanionIsNotCredited}` | #317 | automated-tested |
| GATHER-03 availability is the provider's answer, never re-derived by the consumer | `PresenceCapabilityExchangeTests.AvailabilityIsTheProvidersAnswerAndNotRederivedHere` | #317 | automated-tested |
| DATA-01 one order per source | `WorkContractTests.ASourceIsClaimedByOneOrderInOneWorldLoad` | `bbdda45` | automated-tested |
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

## Gate B2: Gunnar on the shared NPC runtime (#381)

Two rows, and they are **the first two to run of anything on this page**, because they are not about the new
feature: they are about whether the Gunnar who already exists still works now that Concerned Teamster takes his
identity from the shared NPC runtime. Both are destructive if they fail, and neither needs the collection work
that is waiting on an owner decision.

| Req | Evidence | SHA | Result |
|---|---|---|---|
| B2-1 an existing saved Gunnar survives the library adoption | live | — | pending |
| B2-2 a hold from the previous world does not survive a reload | live | — | pending |

Disposable world, character and profile only. Both rows need `[General] Enabled = true` and
`[Workers] GunnarHaulingEnabled = true` in Concerned Teamster's config, and the Concerned NPC package installed
alongside it — without it the game will refuse to load Teamster and say so in one line, which is itself the
correct behaviour and worth seeing once.

**B2-1 — an existing saved Gunnar survives the library adoption.** This is the only irreversible one on the page:
if it fails, a body is deleted with whatever is inside it, silently, on load.

1. Before installing the new build, in the test profile, load the world that already contains Gunnar's body and
   confirm with `ct_haul status` that it says **Body Bound**. Note whether he is carrying anything.
2. Quit to the desktop. Install the new build. Start the game again and load the **same** world.
3. `ct_haul status` must again say **Body Bound**, in the same place, still carrying whatever he was carrying.
4. Search the log for `Destroyed invalid prefab ZDO`. **There must be none.** One naming
   `CT_TeamsterWorker` is a failure of this row, and the fix is not to carry on — stop and report it.
5. The log must also carry, once, at start-up:
   `Gunnar is registered with the shared NPC runtime as 'teamster/gunnar'.`
   A line beginning `Gunnar was refused by the shared NPC runtime` is a failure of this row; copy it verbatim.

**B2-2 — a hold from the previous world does not survive a reload.** Gunnar's identity is now held by the shared
runtime for the life of a job, and a hold that outlived its world would leave him permanently busy with a job
whose name nothing still has — visible to a player as an NPC who refuses every order for no stated reason.

1. Assign a cart (`ct_haul assign`, then `ct_haul confirm`) and start a haul with `ct_haul go <x> <z>`.
2. **While he is pulling**, quit to the main menu.
3. Load the same world again.
4. `ct_haul status` must show him **resting**, with no haul and no lease: not Pulling, not Paused, and not
   NeedsAttention about a haul from before. Give him a fresh `ct_haul assign` / `confirm` / `go`: it must be
   **accepted**.
5. A refusal at step 4 that names a haul id from the previous session is the failure this row exists to catch.

**Watch for, in both rows and in any other Gunnar row run at the same time:** a haul that is refused where it
would previously have started. The identity hold now fails closed — no world loaded, or no registration, is a
refusal rather than a grant — and that is the intended direction, but it is the change most likely to show up as
"he just will not take the order". If it happens, the log line and `ct_haul status` at that moment are the
evidence worth capturing.

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
