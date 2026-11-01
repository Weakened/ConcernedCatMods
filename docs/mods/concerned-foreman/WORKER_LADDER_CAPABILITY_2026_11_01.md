# #334 optional Concerned Ladders measurement capability

This addition belongs to #334's read-only publication row. It does not enable
worker traversal, grant an order/capability, move a body, choose a landing or call
the player climb controller. Player ladder input, motor and presentation are
unchanged. No product references another product at compile time.

## Runtime contract (schema 1)

Resolve the installed Concerned Ladders plugin by its existing GUID, then obtain
`TheConcernedCat.ConcernedLadders.Capabilities.WorkerLadderLinks` from that
instance's assembly. Invoke its public static method on the UNITY MAIN THREAD:

`bool TryRead(int schema, out string loadEpoch, out long generation, out string[] ids, out float[][] sections)`

Pass schema 1. Public constants Schema=1 and SectionStride=12 are also present.
Arguments/results use BCL primitives only, so a consumer needs no compiled
Concerned Ladders type. Unsupported/missing capability or false means UNKNOWN:
withdraw the prior links and refuse active traversal until revalidated. A true
empty snapshot means the loaded registry currently supplies no usable sections.
Do not silently keep old route permission on either result.

Each row has exactly 12 floats, aligned with ids at the same index:

| Index | Measurement |
|---|---|
| 0..2 | world bottom x/y/z on the ladder centreline |
| 3..5 | world top x/y/z on the ladder centreline |
| 6..7 | horizontal outward heading x/z (front face) |
| 8 | measured rung width |
| 9 | rung pitch |
| 10 | independent front collider face depth |
| 11 | independent rear collider face depth |

These are SECTION measurements, not a run and not standing/landing points.
Collect/fuse compatible consecutive sections, probe the correct approached face,
apply the worker's actual capsule stand-off and verify both endpoint support and
clearance before turning one into a TraversalLink. Geometry alone never proves
that an upstairs floor exists. Do not use a moving leaf/obstruction as a landing.
The existing route policy still requires opt-in, host/no peers, explicit order,
correct worker/body/lease and a scoped native motor with ground walking suspended.

## Identity, lifecycle and bounds

- Only actual LadderSegment registry members whose IsLive/TryGeometry accept are
  copied. Cached prefabs, placement ghosts, inactive/destroyed or invalid geometry
  do not participate. This is the existing tcc_ladder_* component lifecycle.
- The native boundary bounds the registry at 2048; the detached builder also
  refuses over-budget or duplicate samples. It never publishes a partial cap.
- The load epoch changes for a different ZNet instance or world UID, including
  relogging to the SAME world. Invalid/unavailable reads invalidate the cache;
  a later valid read mints a fresh epoch. Only a weak reference to the native
  load object is retained, so this capability cannot retain a departed world.
- IDs are instance-scoped `cl:<instance>` strings, NOT saved identities. Use the
  load epoch with them and never persist them as bed/worker/cart identity.
- Generation changes on content changes, including actual movement/depth changes
  with no OnEnable/OnDisable event. Ordering changes do not manufacture changes.
- Every returned array is detached and caller-owned. Editing it cannot alter
  cached geometry or the live ladder. No caller-owned list is retained.
- Call at a bounded worker-survey cadence, not per render tick. Maximum output
  is 2048 rows of 12 floats. No per-frame object search or I/O is added. A native API exception emits one
  actionable warning per failure type until a successful read; ordinary unloaded
  state stays quiet. False results always clear all output fields.

## Verification and remaining rows

WorkerLadderSnapshotTests compile the ACTUAL published capability with stubbed
native boundaries, plus the real domain builder. They cover detached asymmetric
measurements, geometry movement, removal, reload of the same world, unknown
availability, duplicate/over-budget/invalid inputs and schema refusal. The native
registry collector itself is compiled against the installed game by the full
controlled build; these stubs are not in-game evidence of loaded piece behavior.

Still needed for #334: native receiving/adoption into the worker's link network,
chain fusion and supported landing probes, native scoped motor and validator pins,
actual order/runtime integration, and exact-candidate G5 observations. This
capability is callable in source without installing a tick hook or changing any
player behavior. Do not mark the remaining rows complete from this publisher.
