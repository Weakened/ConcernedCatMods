# #334 worker ladder authority - owner decision 2026-11-01

The owner approved: "Approve scoped worker ladder traversal" in response to:
"approve an off-by-default ladder traversal controller that moves only an
explicitly ordered worker's own body, on the host with no peers, while its
vanilla ground motor is suspended?"

This is the scoped amendment required by #334. It permits only that worker's
ordered ladder traversal. It permits no cart/cargo teleport, ownership takeover,
player motor, generic worker teleport, world-save mutation or new network protocol.
Workers still require the existing opt-in host/no-peers authority and identity
hold. Ladders/NpcClimbing remains off by default. The ground motor must stop
before traversal begins and remain suspended until safe restoration on every exit.
A lost/unloaded/rebuilt link releases without teleporting to a guessed landing.
Concerned Ladders remains local-player-only; no worker is submitted to its motor.

This record is separate because Claude actively edits SETTLEMENT_AUTHORITY.md and
the settlement DECISIONS.md in #489/hammer. It is an owner decision, not an inferred
exception, and the lead can reconcile it into those documents without racing them.

Implemented source: WorkerRouteSearch adds deterministic incremental
multi-link route search with verified walk edges and ONE whole-route query budget.
Unknown walk evidence waits; it never becomes a straight-line walking promise.
Every Advance rechecks explicit-order/feature/authority/capability gates. Link
revisions are checked while planning and before returning a route. Route-leg
arrival checks horizontal AND vertical tolerance without changing the existing
ordinary ground planner (#344/#352 must not be reintroduced).

Still required for #334: Concerned Ladders runtime-only link publication per
settlement/zone, scoped worker motor/identity handoff, route execution, per-leg
failure/recovery, native audit pins/plants and actual G5 gameplay evidence. The
active validator file is not changed in this slice. No native position-write
port is enabled without that confinement. Vanilla ladder admission remains
unimplemented; this slice assumes only explicit published link contracts.
No worker climbs from this source slice alone and G5 is NOT claimed.

WorkerRouteRunner executes one leg at a time, rechecking the real authority,
explicit order, body binding, identity and link generations. Ground walking is
stopped before requesting a scoped motor lease; while climbing it never pumps
the walker. Only that worker/body-epoch lease may place a traversal position.
Supported landing is checked before a deliberate step-off; interruption/loss
never applies a landing. Failed restoration holds identity and retries cleanup
without movement, rather than releasing a suspended body to another order.
A borrowed route hold must release only itself, not its surrounding job's hold.

The existing LinkTraversal.Exit now checks generation as well as id: a retired
and rebuilt ladder cannot provide its new landing to an old traversal handle.

Tests: WorkerRouteSearchTests (multi-link graph, verified stairs, per-tick and
whole-route budgets, unknown evidence, scope/authority/capability revocation,
link retirement/rebuild, route horizon/leg bounds, height-aware arrival) and
WorkerRouteRunnerTests (real LinkTraversal, walk/motor exclusivity, cancel/loss,
restoration failure/retry, unsupported exit and direct stale-generation exit).
Together with existing traversal seam tests, the focused run has 57 passing cases.

Native integration, 2026-11-08 (#334 binding slice): implemented as
`WorkerNavMeshWalkProbe`, `WorkerClimbMotor`, `WorkerClimbRuntime` and the
game-free `WorkerClimbOrder`; recorded as DECISIONS.md D14.6 and pinned by the
#400 and #334 worker climb audits. G5 remains NOT RUN. The paragraph below is
the requirement it was built against.

Native integration STILL OPEN: IWorkerWalkProbe must supply full, height-verified
path evidence, never a horizontal-only or projected-floor success. A scoped
IWorkerRoutePort / IWorkerLadderMotor must be audited before it writes an engine
position, and wired into the owned worker update so BaseAI.MoveTo is suppressed
while ControlsRoute is true. The port must capture its own body and original
motor state; it cannot follow a mutable current-body reference across reload.
Only Concerned Ladders links published through a runtime/shared contract are
intended; no stock/piece name is guessed by this planner. No compiled reference
to Concerned Ladders is introduced. No G5 runtime result is claimed.
