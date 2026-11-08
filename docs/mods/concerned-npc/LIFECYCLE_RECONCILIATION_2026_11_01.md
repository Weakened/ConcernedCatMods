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

Base `origin/main` `ffa187b`. This addendum supersedes the "Remaining native adoption/confinement work" section above
for Foreman and Steward; Teamster's half is still open.

| Criterion | What now holds | State |
|---|---|---|
| Factory takes an active BodyLease and re-checks it immediately before construction | Unchanged for `TrySpawn`. New `NpcWorkerPrefabFactory.TrySpawnRoleBody` for a role's own prefab goes through the same private build: `NpcBodyBuildGate.May` (lease asked last), then the library's one instantiation of a saved body with nothing between. The census is the library's, taken in the call; the prefab must be the one the network scene knows by the contract's name; the identity is stamped in the same call and a body whose stamp fails is destroyed again. `RoleBodySpawnTests` (no lease, disposed lease, unloaded world, saved body present, uncountable world, unregistered/misnamed/absent prefab, no scene, unloaded ground, presentation lease, failed and throwing stamp). | MET for the library and for both adopted roles |
| Consumer instantiation confined, with planted violation caught | `tools/validate_repo.py` `check_npc_consumer_bodies_come_from_the_gated_build`: every product whose project references the library, whole (less obj/bin) plus every shared tree it compiles in, may spell `Instantiate`, `CreateNewZDO` or `SpawnObject` nowhere except one stated pending site (Teamster's prefab, pinned by spelling and count, keyed by product and path), and may not put a network view on an object in any spelling the audit can see (`AddComponent<ZNetView>`, the Type-taking `AddComponent(`, `AddComponent<T>` of a type parameter, an alias of the type, or a subclass of it; added after the 2026-11-08 re-review, with six plants, because a network view on a `new GameObject` is a saved object made without the gate and no existing pin refused it); each adopted product's call into the gated build is pinned by path and count; the factory's one instantiation must follow the gate's refusal and the gate must end on the lease check and its one grant. `tools/tests/test_npc_body_factory_audit.py` plants both owner-named violations (an instantiation outside the factory; a factory that ignores a disposed lease) in several shapes, each verified to pass the validator copy without the rule. Foreman's #400 audit no longer pins an instantiation, so any in Foreman is unpinned there too. | MET for Foreman and Steward; Teamster pending |
| Foreman spawns through the gated build | `ForemanWorkerPrefab.Spawn(lease, ...)` calls `TrySpawnRoleBody`; `SettlementRuntime.Spawn` gets the lease from the presentation hand-over (`ThorsteinBodies.WorkerLease`). While an order holds him the spawn is now refused (cancel the order first; a paused or recovered-paused order still holds him, and the order holding him at a spawn has no body to finish with). The `WorkerBodyLost` write-off therefore cancels before it spawns, and a cancel keeps what the record holds on him, so the loss can still be written off against the cancelled order (`FaultInjectionTests.AnOrderStoppedForALostBodyCanBeCancelledAndItsMaterialStillWrittenOff`, `CollectionRecoveryTests.AnOrderStoppedForAMissingBodyHoldsHimUntilCancelledAndThenLetsGo`, `ThorsteinBodyTests.AnOrderStoppedForAMissingBodyRefusesTheLeaseUntilItIsCancelled`); before #372 that branch built a body with no lease at all. Borrowing the order's name was tried and rejected: the arbiter's release of a body held in a job's name releases that job's mode too (a Foreman test caught it), and keeping such a lease would leave a hold nothing could release. No hand-over wired means no lease and no body. This refusal is a lead default pending owner confirmation. | MET in source |
| Steward spawns through the gated build | `StewardRuntime.EnsureBody` yields the figure, claims the worker lease, and only then calls `StewardWorkerPrefab.Spawn(claim.Lease, ...)`; a refused claim refuses the spawn and a failed build releases the hold. Before #372 the claim came after the body existed. | MET in source |
| Teamster spawns through the gated build | Not done. `TeamsterWorkerPrefab.cs` and its caller are in `Adapters/Workers`, which Lane J's open PRs #512 and #513 change. Its one instantiation is the audit's single pinned pending site. | OPEN follow-up |

Behaviour that changed, stated because none of it has been watched in game (OWNER GO-AROUND PENDING): a body whose
identity write fails is now destroyed by the library instead of being left standing unidentified (Steward said so in
a comment; Foreman left it standing too); and a spawn is refused, with the census's words, when Concerned NPC's own
count of saved bodies is not finished and empty, in addition to each product's own census.

What this does not do: no product mind or body component moved. `ForemanWorkerAI`, `WorkerBody`, `StewardWorkerAI`
and `StewardBody` are unchanged, as are every prefab name, key field and the registration timing. Moving the minds
onto `NpcBodyMind`/`NpcBody` (the "body AI" and "body inventory" moves in ARCHITECTURE.md section 5) is separate work
with its own behaviour to prove, and Foreman's mind is also in open PR #521.

Review follow-ups at this head, not done here: `TrySpawnRoleBody` does not yet check that the contract passed is the
one the lease's role registered (both callers pass their role's own static contract, and the stamped key is now read
back against the lease identity under that contract's key field, so a mismatch cannot build a body the census misses
- but a forged contract naming another prefab could); and the gate call's arguments are not pinned by the validator.
A prefab created late by `cf_worker spawn` (world already up) is refused if the scene does not yet know it - fail
closed, to be checked in game.

#372 is still OPEN on Teamster's adoption.
