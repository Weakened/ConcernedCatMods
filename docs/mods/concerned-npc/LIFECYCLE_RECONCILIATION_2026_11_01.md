# #372 lifecycle/persistence reconciliation — 2026-11-01

Reviewed baseline: main 523c895ca12b61623df543e28cc589c1438e276b.
This issue branch changes API tests, the controlled gate and this record. No
shipped native source, keys, package pins, worker runtime, validator or game
profile is changed. The negative native-helper plant restores its source bytes.

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
| Public members pinned, not only type names | This branch adds PublicMemberSurfaceTests plus the reviewed 819-row fixture for 97 actually linked library types. It includes constructors, methods/operators, parameter names/types/defaults, accessor visibility, fields/enum values, interfaces, generic constraints and events. A negative case changes an existing type's parameter/default/accessor. | MET for current public-member contract: the linked 819-row guard is complemented by a mandatory native metadata guard, 868 rows / 102 exported types, including rendering helpers and real inherited interfaces |
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

**Superseded for Foreman and Steward by the 2026-11-08 addendum below**: both now build their bodies through
`NpcWorkerPrefabFactory.TrySpawnRoleBody` under a worker lease, and the validator refuses any other body creation in
them. Only Teamster's entry in this list is still current. As written on 2026-11-01, native body creation lived in:

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

The live caller/worker files and validator were then in Claude's active PR/hammer lanes,
and were not edited by this report (the addendum below records the later change). The integration handoff names this remaining work; no
new callback, reflected engine write, duplicate registry or alternate spawn path
is introduced to avoid that ownership boundary.

## Outcome

#372 remains OPEN. Its persistence blocker is resolved in current source, and the
member guards now cover both the linked public types and the compiled native
assembly. Complete body factory adoption and consumer confinement/plant proof
remain. This report does not approve an installed candidate or
substitute a source test for gameplay, compiler provenance or package identity.

## Complete compiled API contract

`NativePublicMemberSurfaceTests` reads the actual `ConcernedNPC/bin/<configuration>/net48/TheConcernedCat.ConcernedNPC.dll`
using `MetadataLoadContext`. It does not execute Unity, game, BepInEx or plugin
code. Its resolver reads assembly identities because publicized game DLL
filenames differ from the assembly names their signatures reference. No reference
binary is renamed, copied, committed or packaged by this check.

The reviewed `Fixtures/native-public-members.txt` has 868 rows covering all 102
exported types, including NpcFigureDresser, NpcWorkerDresser,
NpcCustomizationPalette, NpcKeepsake and Plugin. It also records NpcBodyMind's
actual IUpdateAI interface, which the source-linked BaseAI stub cannot expose.
Every old linked row appears in the native contract except the intentionally
more complete real NpcBodyMind interface row. Namespace escapes fail separately.
The same formatter pins methods, constructors, fields/constants, properties,
events, operators, parameter defaults and generic constraints. This contract
covers declared public members and visible type metadata, not implementation
behavior, method bodies, custom attributes or an installed consumer's execution.

`pwsh ./scripts/verify.ps1` enables this test after its native build and selects
the matching Debug/Release output; it restores the caller's environment in
finally. A missing assembly or metadata dependency refuses rather than skipping.
Plain game-free test runs visibly skip this one native test, preserving portable
CI without claiming coverage of a native binary. The source-linked checks still
run there.

Commands, from this worktree:

- Normal controlled gate: `pwsh ./scripts/verify.ps1` (under the shared build lock).
- Existing native-output recheck: `pwsh ./scripts/check-npc-native-api.ps1`.
- Negative proof: `pwsh ./scripts/test-npc-native-api-plant.ps1`. This acquires
  the same machine-wide mutex, temporarily changes NpcFigureDresser.VerifyOnce
  to accept an optional int defaulting to 17, builds through build.ps1, requires
  the exact changed signature to be rejected, restores original source bytes
  in finally, rebuilds, and requires the baseline to pass. No profile is imported
  or game operated. A failed build is never counted as a caught API violation.

For an intentional API change, review consumer/version implications first.
The failed comparison emits ACTUAL_BEGIN/ACTUAL_END contract rows; compare them
with the fixture and update only after reviewing the differences. No command
silently blesses a new binary. The full exact-head gate and plant evidence are
recorded in the integration handoff.

## 2026-11-08 addendum: body factory adoption and consumer confinement

Base `origin/main` `ffa187b`, later merged with the lead's verified chain `9d8603f` (#512, #513, #517, #521). This addendum supersedes the "Remaining native adoption/confinement work" section above
for Foreman and Steward, and, since the F2c follow-up below (base `d05c24e`, PR #528 merged), for Teamster too.

| Criterion | What now holds | State |
|---|---|---|
| Factory takes an active BodyLease and re-checks it immediately before construction | Unchanged for `TrySpawn`. New `NpcWorkerPrefabFactory.TrySpawnRoleBody` for a role's own prefab goes through the same private build: `NpcBodyBuildGate.May` (lease asked last), then the library's one instantiation of a saved body with nothing between. The census is the library's, taken in the call; the prefab must be the one the network scene knows by the contract's name; the identity is stamped in the same call and a body whose stamp fails is destroyed again. `RoleBodySpawnTests` (no lease, disposed lease, unloaded world, saved body present, uncountable world, unregistered/misnamed/absent prefab, no scene, unloaded ground, presentation lease, failed and throwing stamp). | MET for the library and for both adopted roles |
| Consumer instantiation confined, with planted violation caught | `tools/validate_repo.py` `check_npc_consumer_bodies_come_from_the_gated_build`: every product whose project references the library, whole (less obj/bin) plus every shared tree it compiles in, may spell `Instantiate` (or any `Instantiate…` name, so a newer `InstantiateAsync` is no way round it), `CreateNewZDO` or `SpawnObject` nowhere - since F2c with no pending site left (it was Teamster's prefab, pinned by spelling and count, keyed by product and path, and it went with Teamster's adoption) - and may not put a network view on an object in any spelling the audit can see (`AddComponent<ZNetView>`; the Type-taking or type-parameter form of any method whose name contains `AddComponent`, Jotunn's `GetOrAddComponent<T>` and `AddComponentCopy<T>` included; `typeof(ZNetView)` anywhere; the `GameObject` constructor that takes component types; an alias of the type; or a subclass of it; added after the 2026-11-08 re-reviews, with plants for each, because a network view on a `new GameObject` is a saved object made without the gate and no existing pin refused it); each adopted product's call into the gated build is pinned by path and count; the factory's one instantiation must follow the gate's refusal and the gate must end on the lease check and its one grant. `tools/tests/test_npc_body_factory_audit.py` plants both owner-named violations (an instantiation outside the factory; a factory that ignores a disposed lease) in several shapes, each verified to pass the validator copy without the rule. Foreman's #400 audit no longer pins an instantiation, so any in Foreman is unpinned there too. Stated limits of the text audit (in the validator beside the rule): a type parameter named against the `T`/`TName` convention, reflection, a constructor argument nested more than one parenthesis deep, an MSBuild alias, a subclass declared outside the consumer trees, and a target-typed `new(` that is not a `GameObject` declaration. | MET for all three consumers (Teamster since F2c) |
| Foreman spawns through the gated build | `ForemanWorkerPrefab.Spawn(lease, ...)` calls `TrySpawnRoleBody`; `SettlementRuntime.Spawn` gets the lease from the presentation hand-over (`ThorsteinBodies.WorkerLease`). While an order holds him the spawn is now refused (cancel the order first; a paused or recovered-paused order still holds him, and the order holding him at a spawn has no body to finish with). The `WorkerBodyLost` write-off therefore cancels before it spawns, and a cancel keeps what the record holds on him, so the loss can still be written off against the cancelled order (`FaultInjectionTests.AnOrderStoppedForALostBodyCanBeCancelledAndItsMaterialStillWrittenOff`, `CollectionRecoveryTests.AnOrderStoppedForAMissingBodyHoldsHimUntilCancelledAndThenLetsGo`, `ThorsteinBodyTests.AnOrderStoppedForAMissingBodyRefusesTheLeaseUntilItIsCancelled`); before #372 that branch built a body with no lease at all. Borrowing the order's name was tried and rejected: the arbiter's release of a body held in a job's name releases that job's mode too (a Foreman test caught it), and keeping such a lease would leave a hold nothing could release. No hand-over wired means no lease and no body. This refusal is a lead default pending owner confirmation. | MET in source |
| Steward spawns through the gated build | `StewardRuntime.EnsureBody` yields the figure, claims the worker lease, and only then calls `StewardWorkerPrefab.Spawn(claim.Lease, ...)`; a refused claim refuses the spawn and a failed build releases the hold. Before #372 the claim came after the body existed. | MET in source |
| Teamster spawns through the gated build | F2c: `TeamsterWorkerPrefab.Spawn(lease, ...)` calls `TrySpawnRoleBody` with `GunnarNpcRole.Contract` (built from the same game-free `GunnarBodyContract` his role registers) and `Stamp`, a static method in the same type, so the `tcc.worker.key` and `tcc.worker.revision` writes stay in `TeamsterWorkerPrefab` for both the source audit and the IL audit. `GunnarHaulingRuntime.Spawn` gets the lease from the presentation hand-over (`GunnarBodyLeases.WorkerLease`, the arrival guard). While a job (a haul, a collection round) holds him, the hand-over is now refused, because the arbiter grants a body then only to that job; before #372 that case built a body with no lease at all. No hand-over wired (the figure runtime failed to install) means no lease and no body. The prefab, its name, its components, its registration moment and every key are unchanged; the body a stamp could not identify is destroyed by the library, so `TeamsterWorkerPrefab.cs` no longer destroys a body (the Teamster destruction pins went 6 to 4, routed 2 to 1, body removal 1 to 0). The audit's pending site is gone and `NPC_BODY_ROLE_SPAWN_SITES` pins Teamster's call. `GunnarBodyTests`, `GunnarHandOverOrderTests`; plants in `test_npc_body_factory_audit.py`. The job-held refusal is a lead default pending owner confirmation, as Foreman's is. | MET in source |

Behaviour that changed, stated because none of it has been watched in game (OWNER GO-AROUND PENDING): a body whose
identity write fails is now destroyed by the library instead of being left standing unidentified (Steward said so in
a comment; Foreman left it standing too); and a spawn is refused, with the census's words, when Concerned NPC's own
count of saved bodies is not finished and empty, in addition to each product's own census.

What this does not do: no product mind or body component moved. `ForemanWorkerAI`, `WorkerBody`, `StewardWorkerAI`
and `StewardBody` are unchanged, as are every prefab name, key field and the registration timing. Moving the minds
onto `NpcBodyMind`/`NpcBody` (the "body AI" and "body inventory" moves in ARCHITECTURE.md section 5) is separate work
with its own behaviour to prove, and Foreman's mind was also changed by #521 (in `9d8603f`, merged here).

Review follow-ups at the #528 head, both closed by F2c: `TrySpawnRoleBody` now refuses, before it counts anything, a
contract that is not exactly the one the lease's identity registered with the arbiter that granted the lease (kind,
prefab name, key prefix and figure admission; `RoleBodySpawnTests.A_lease_never_builds_another_roles_prefab` and the
three beside it); and the validator now pins the gate call's six arguments by name and order, and the two computed
inputs (`ready`, `groundLoaded`) written immediately above it, with a plant for a constant, a dropped prefab check, a
forced ground and a swapped pair. That pin covers the private build's own inputs, not what `TrySpawn` and
`TrySpawnRoleBody` hand it (the `prefabFailure == null` readiness, the census, where the contract check stands); those
are `RoleBodySpawnTests`' and a reviewer's, as the validator says beside the rule. Still open, to be checked in game: a prefab created late by `cf_worker spawn`
(world already up) is refused if the scene does not yet know it - fail closed. Teamster builds its prefab at the main
menu, so that case should not arise for Gunnar.

With Teamster adopted, every consumer of Concerned NPC creates its bodies through the gated build and the audit holds
no exception. What #372 still names beyond that is the mind/body component migration (moving `ForemanWorkerAI`,
`StewardWorkerAI` and `TeamsterWorkerAI` onto `NpcBodyMind`/`NpcBody`), stated above as separate work.

## 2026-11-10 addendum (F2d): the first body component moved

Base `origin/main` `2233365` (#533 merged). The "body inventory" move of ARCHITECTURE.md section 5, for one role:
Thorstein's own `WorkerBody` (Concerned Foreman) is gone and his prefab carries the library's `NpcBody`, which was
derived from it line for line. Same three keys, same inventory package, same load-time equipment restore, same
vanilla death drop of exactly what he carried, so a pre-#372 saved body loads with no migration code
(`WorkerBodyTests.ABodySavedByTheCopyThisReplacesLoadsUnchangedWithNothingWritten`). Two public members were added:
`NpcBody.TryRegister` (a role that builds its own prefab says what its bodies store, before any world loads) and a
stamp-less `NpcWorkerPrefabFactory.TrySpawnRoleBody` overload (the library writes the lease's identity itself). The
library's events are shared by every consumer, so Foreman's handlers and lookup take his prefab only
(`AnotherRolesBodyWithHisKeyIsNeverTakenForHis`). The six Foreman `#400` pins that lived in `WorkerBody.cs` left with
it, and the same six calls are now pinned in `Body/NpcBody.cs` by the library census (plants in
`test_npc_figure_parts_audit.py` `NpcBodyWritesArePinned`, each passing a copy of the validator without those rows;
`test_foreman_forbidden_audit.py` refuses the old file's six calls put back at their old path).

Also closed: the F2c leftover "Teamster-side real-arbiter contract-match test"
(`GunnarBodyTests.TheArrivalLeaseIsContractedForExactlyTheContractHisBodyIsBuiltUnder`).

Still to do for #372's mind/body migration, and why each is its own change:
- **Foreman's mind** (`ForemanWorkerAI` onto `NpcBodyMind`). `NpcBodyMind` cannot express what his mind does today:
  it has no ground-motor suspension (his ladder climb overrides `IsSleeping` under a climb lease, pinned by the #334
  climb audit), its leased motor walks with `SetWalk(true)` and `Time.fixedDeltaTime` where his walks at the
  non-walk speed with the tick's `dt` (read from source, most likely a slower walk; not checked in game), and every motor command needs a live worker lease,
  which the console's `cf_worker goto`/`stop` and the job motion do not route through today. Each is a library
  change plus a behaviour change that wants its own in-game proof.
- **Steward** (`StewardBody`, `StewardWorkerAI`) and **Teamster** (`TeamsterWorkerRecord`, `TeamsterWorkerAI`). The
  body halves are near-copies and the same two members serve them; Teamster's record sits in `Adapters/Workers`
  under the #313/#381 audits, and its writes are pinned there.

Stated after the F2d review (APPROVE WITH NITS, record on the PR):
- **Exposure.** `WorkerBody` was internal to Foreman; `NpcBody` is public, so another product's *code* can now reach
  Thorstein's loaded body (its `Inventory`, `Humanoid`, `TryPersist`) through `NpcBody.FindLive` with his contract or
  by subscribing to `Loaded`/`Died`. What stands in the way is each product's own inventory and capability audits
  (Teamster #313/#381, Steward S1, Foreman #400), not the type system.
- **Death report.** `NpcBody` now raises `Died` one subscriber at a time, as `Loaded` already was, so one product's
  throwing handler no longer costs another product its record of what was dropped (fixed in review; before, a
  multicast invoke stopped at the first throw).
- **Other differences from `WorkerBody`.** A throwing `Loaded` subscriber leaves the body loaded but unbound (before:
  inert), which the tick-time `FindLive` rebinding covers; messages say "NPC body"; Foreman's error handler hears every
  product's body errors.
- **Not widened here.** Foreman's `#400` worker-ZDO token is `\bzdo\s*\.\s*Set\s*\(`, which a different receiver name
  (`_view.GetZDO().Set(`) would not spell; it was that narrow before this change, and widening it needs the shared
  Interop tree's unrelated `message.Set(` excluded. A follow-up, not a regression.

## 2026-11-10 addendum (F2e): Sunniva's body moved; Gunnar's did not

Base `origin/feat/cnpc-376-slice3` `ee319c8` (PR #545, stacked on #542). The same "body inventory" move, for the
second role: Concerned Steward's own `StewardBody` is gone and her `CS_Steward` prefab carries the library's `NpcBody`,
registered with `NpcBody.TryRegister` before the prefab is built, under the same three keys (`tcc.steward.key`,
`.inventory`, `.revision`) and the same package. A pre-#372 object written by hand loads unchanged, with nothing
written by the load (`BodyTests.A_body_saved_before_372_loads_unchanged_and_keeps_its_keys`). With two roles on it,
`NpcBody` is now the one body-persistence surface #372's "used by at least two roles" asks for.

What the move had to keep that the type used to give:
- **Only her body is retired.** `StewardRetirementPort.Retire` took a `StewardBody`; it now takes the shared `NpcBody`,
  so it refuses (`NotOurs`) any body whose prefab is not hers before it reads the network object. The view is read
  off the body handed in (`body.GetComponent<ZNetView>()`), because the library's handle is internal. The S1 audit
  pins both, and its by-reference rule now refuses any declared type (it listed `StewardBody`, so
  `out NpcBody? body` would have got past it). Three plants, each passing a copy of the validator without them.
- **A just-retired body is not found.** The copy's lookup skipped a body whose network object was already reset;
  the library's does not, so `StewardSavedBodies.FindLive` does (`A_body_whose_network_object_is_gone_is_not_found`).
- **Only her body reaches her ledger.** Her `Loaded`/`Died` handlers filter by prefab as well as key, and come off the
  library's events at plugin teardown.

Differences, stated: the library restores an item saved as equipped on load (she never equips one, so nothing for
her); a throwing `Loaded` subscriber leaves the body loaded rather than inert; messages say "NPC body"; her error
handler hears every product's body errors, as Foreman's does. No Concerned Steward source now writes a ZDO field,
drops an item or equips one directly (her look still reaches her own object through vanilla's `VisEquipment`), and
`SourceAuditTests` fails if a direct write, drop or equip reappears in it (it used to allow one file). The library
body's own null-unsafe read of a dropped stack's shared name, which her copy guarded, is a library follow-up.

**Not moved: Gunnar's `TeamsterWorkerRecord`.** It is not a near-copy, and moving it changes behaviour that an owner
decision governs:
- **Death.** `NpcBody` drops everything on every death. Gunnar's death drop is D16's own port
  (`GunnarDeathDropPort`), gated on both switches, work authority, the probe, a read inventory and a detached cart,
  and it *fails closed*: when a gate refuses, nothing drops and the loss is recorded as unresolved. Putting `NpcBody`
  on his prefab would drop on a death D16 says must not drop. The library needs a keeps mode whose death is the role's
  (no drop, no `Died`), or D16 needs an owner decision to move into the library; neither is this slice.
- **Load.** His record decides through `WorkerInventoryRecord.Decide` and counts revisions with
  `WorkerInventoryRecord.Next`; it reads no identity (his AI is told it at stamp); it unhooks the change callback in
  `OnDisable` and re-hooks it in `OnEnable`, where `NpcBody` unhooks in `OnDestroy`. Likely equal on every path that
  matters, but that needs its own proof rather than this sentence.
- **Pins.** Its `"tcc.worker."` writes sit inside the validator's Teamster network-object allowance (literal keys in
  `TeamsterWorkerPrefab.cs`), `scripts/audit-teamster-hauling-api.ps1` counts them in IL by type
  (`TeamsterWorkerRecord`), and the D16 death-drop port, the hauling runtime and the world-down trust (#376) read the
  record. Each moves with its own plant set; some of the Teamster suites run for over an hour.
