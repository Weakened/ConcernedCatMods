# #372 lifecycle/persistence reconciliation — 2026-11-01

Reviewed baseline: main 523c895ca12b61623df543e28cc589c1438e276b.
This issue branch changes tests and this record only. No native source, keys,
package pins, worker runtime, validator or game profile is changed.

## Requirement coverage before closure

| Current owner requirement | Current implementation and meaningful evidence | State |
|---|---|---|
| One lifecycle used by two roles | Foreman, Steward and Gunnar use NpcRoleRegistry and the single identity arbiter. BodyArbiterTests/BodyCensusTests refuse both body kinds at once and duplicate saved bodies. BUT each product still has its own native worker prefab/spawn implementation, below. | PARTIAL; registration alone does not finish the body-factory move |
| One persistence surface used by two roles | NpcPlanJournal owns NpcSidecarFile, opens it in TryOpen, loads and writes through it. GunnarTourPlan.Open and StewardMaintenancePlan.OnWorldLoaded are real consumers. GunnarCollectionRuntime opens the tour plan; StewardRuntime constructs and loads the maintenance plan. | MET in production source; the September zero-callers finding is stale |
| Existing keys unchanged or interrupted migration | BodyGoldenFixtureTests pin the old fields/prefix and read pre-refactor saved bodies. No migration is introduced by this branch. Shared persistence keeps caller-owned absolute paths and codecs. | Source evidence met; no new migration to test |
| Never two bodies for one identity | BodyArbiterTests and census duplication tests, including disposed/world-expired leases; the actual library factory checks the lease last through NpcBodyBuildGate. | MET inside the library; consumer bypass remains below |
| Factory takes an active BodyLease and checks immediately before construction | NpcWorkerPrefabFactory.TrySpawn takes BodyLease and calls NpcBodyBuildGate.May before its single Instantiate; BodyInstantiationAuditTests pin the last active check and refusal ordering. | MET for that library factory; NOT enforced on legacy product spawning |
| Consumer instantiation confined, with planted violation caught | check_library_consumers_do_not_bypass_the_arbiter refuses ActorModeOwner, not Instantiate. Library engine-write census and BodyInstantiationAuditTests scan the library. Foreman's runtime audit pins its existing product-local Instantiate. Neither is a general consumer body-factory confinement rule. | NOT MET; do not close on a differently scoped audit |
| Public type namespace escape fixed | PublicSurfaceTests checks exported global stubs separately and compares every non-stub/non-test exported type, not just the library namespace prefix. | Already met |
| Public members pinned, not only type names | This branch adds PublicMemberSurfaceTests plus the reviewed 819-row fixture for 97 actually linked library types. It includes constructors, methods/operators, parameter names/types/defaults, accessor visibility, fields/enum values, interfaces, generic constraints and events. A negative case changes an existing type's parameter/default/accessor. | Improved; rendering-only sources excluded by the test project still require native-assembly member coverage |
| Authoritative gate green | Exact committed issue gate is recorded in the shared handoff, not inferred from an old main gate. | See handoff receipt |

## Production persistence trace

- `src/ConcernedNPC/Interruption/NpcPlanJournal.cs`: private NpcSidecarFile,
  TryOpen calls NpcSidecarFile.TryAt. Load and writes use that same file.
- `src/ConcernedTeamster/Domain/Collection/GunnarTourPlan.cs`: NpcPlanJournal
  field and real TryOpen, then shared plan recovery/run. GunnarCollectionRuntime
  owns the plan, builds its current-world path and calls Open.
- `src/ConcernedSteward/Domain/Upkeep/StewardMaintenancePlan.cs`: NpcPlanJournal
  field and TryOpen/Load. StewardRuntime constructs it and calls OnWorldLoaded.
- INpcDataPaths' refusal of unnamed purposes does not negate this explicit-path
  public persistence surface. A declared root alone would still be insufficient.

Relevant real tests: SidecarPersistenceTests (pre-refactor bytes, interrupted
write, atomic replacement and failed commit); PlanJournalTests; the two consumers'
plan/recovery tests. These are storage/recovery tests, not gameplay captures.

## Remaining native adoption/confinement work

Current native body creation still lives in:

- `src/ConcernedForeman/Runtime/Settlement/ForemanWorkerPrefab.cs`: Spawn takes
  position/rotation and directly instantiates its prefab, with no BodyLease input.
- `src/ConcernedSteward/Runtime/StewardWorkerPrefab.cs`: same product-local spawn.
- `src/ConcernedTeamster/Adapters/Workers/TeamsterWorkerPrefab.cs`: same local spawn.

Do not merely allow-list these calls and claim the shared-factory criterion.
Carry the correct current job/presentation-worker lease to a common factory;
recheck it last, preserve the exact prefab/key/registration timing, and refuse a
missing/disposed lease. Then confine native body instantiation in consumers and
plant both the outside-factory bypass and disposed-token violation. Keep GUI,
keepsake, item-drop and render-harvest creation distinct from live body creation;
a blanket ban over every Instantiate/AddComponent would break valid product UI.

The live caller/worker files and validator are in Claude's active PR/hammer lanes.
They are not edited here. The integration handoff names this remaining work; no
new callback, reflected engine write, duplicate registry or alternate spawn path
is introduced to avoid that ownership boundary.

## Outcome

#372 remains OPEN. Its persistence blocker is resolved in current source, and the
member guard now catches changes within the linked public types. Complete body
factory adoption, consumer confinement/plant proof and rendering-helper public
member coverage remain. This report does not approve an installed candidate or
substitute a source test for gameplay, compiler provenance or package identity.
