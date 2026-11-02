# #379 priority-domain repair

Base: 523c895ca12b61623df543e28cc589c1438e276b. Dedicated branch
fix/cnpc-379-priority-domain; this is independent of the #372 API-guard branch.

The internal NpcActivityArbiter previously refused only Unspecified (zero), then
compared integer priorities. An undefined arriving value 8 or int.MaxValue could
outrank LifecycleSafety. An undefined negative running value could yield to any
named activity. An enum-typed argument does not guarantee a defined value.

Both operands now have to name one of the seven supported activities before the
strict higher-priority comparison. Unknown values and zero refuse with a reason
identifying an unrecognized priority. Equal priority still refuses; the existing
seven-rung ordering and valid-plan interruption behavior are unchanged.

Meaningful regression evidence: 68 new cases fail on the original source and
pass after repair. Four extreme/adjacent undefined values are exercised in both
positions against every named activity. All 16 plan transition points are
crossed with those values; refusal must retain the same live and suspended plan,
make no codec write, record no note, and leave the real journal bytes identical.
The 58 existing arbitration cases also pass (126 focused cases total). These
compile and call the actual library policy, not a copied implementation.

Scope: one internal policy, regression tests and documentation. No public API,
version, keys, migration, motor, native spawn, product executor, validator,
profile, package or live game is changed. No new runtime authority is granted.

## Requirement coverage and remaining work

- Shared durable recovery already has real Steward and Gunnar callers; their
  existing journals and mutation ordering are not changed here.
- Seven-rung arbitration model and preservation tests exist; this repair closes
  its undefined-value admission hole.
- NpcActivityPriority, NpcActivityArbiter and NpcActivityHandover remain internal
  and have no production product caller. This source repair does not claim a
  shipped NPC now interrupts and resumes work through that policy.
- Production priority adoption and Thorstein's shared-interruption adoption
  remain CODE rows. Exposing a reviewed cross-assembly surface and binding the
  actual executor must preserve its durable state and authority. Claude owns
  currently changing caller/audit files; do not work around them with a second
  executor, duplicated priority policy or unchecked reflection.
- Exact-candidate gameplay remains unverified. Existing adopted-role game rows
  are on #470; remaining source rows cannot be represented as observations.

The handoff records the exact committed gate and patch identity. #379 remains
open. A policy regression pass is not an integrated game-build approval.
