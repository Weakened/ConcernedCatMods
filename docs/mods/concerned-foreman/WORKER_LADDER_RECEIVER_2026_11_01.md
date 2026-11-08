# #334 receiver, section chains, landing probes and checked path

This follows the owner's scoped worker ladder grant in
WORKER_LADDER_AUTHORITY_2026_11_01.md and the optional producer contract in
WORKER_LADDER_CAPABILITY_2026_11_01.md. It finishes the independent receiver/path
source. It does NOT install a worker motor, enable NpcClimbing or bind an order.
The old ordinary ground planner and both player controllers remain untouched.

## Requirement coverage

| #334 row | New implementation | Meaningful checks | Remaining |
|---|---|---|---|
| Optional tcc_ladder detection; no product reference | ConcernedLaddersWorkerSource resolves the installed plugin assembly by GUID; ReflectedWorkerLadderSource requires exact schema 1 / stride 12 / public static BCL out signature | Missing/false/throwing/unsupported/ref-shaped providers refuse, with no last-good fallback | Integrated installed-pair observation |
| Section chains | WorkerLadderPublication owns validated copies; WorkerLadderLinkSurvey fuses exact 3 cm joints, rejects overlaps/branches/drift and bounds run height | Reversed input, stacked materials/depths, overlap, nearby-run budget and outside-area rejection | Native order/settlement designation binding |
| Both approached faces | Each run publishes front/rear independently; front uses maximum front depth, rear minimum rear depth, plus ACTUAL capsule radius and clearance | Asymmetric stand-offs and blocked rear refusal | In-game fit on actual worker rig |
| Supported landings and collision | WorkerLadderLandingProbe checks static floor under centre + four footprint edges, body clearance, mount/vertical/exit sweeps and current site policy | Thin rails, closer door over underlying floor, moving supports, other actors, foreign child objects, unloaded corners, saturated queries, lost ownership and wrong body dimensions | Real geometry/terrain/controller compatibility observations |
| Navigation generations/lifecycle | Only this receiver's links retired; unchanged checked paths retain generations, geometry/floor/body changes replace them; unknown/disabled withdraw | Other providers survive withdrawal; changed floor/interior geometry invalidates old generation; forced refresh cannot keep unavailable permission | Bind Forget on native order/world/body lifecycle |
| Traversal path is the measured path | WorkerLadderPathTraversal wraps ONLY worker LinkTraversal, using completed 3D path distance and checked mount/climb/exit vertices | Vertical ascent, platform exit, descent, reversal, corner stop, large/invalid dt and premature/double exit | Scoped native motor + identity hold and per-tick collision sweep |
| Movement authority | Receiver gating takes actual NpcClimbing, explicit-order and existing WorkAuthorityVerdict; default caps deny. Wrapper rejects Player requests | Existing WorkerRouteRunner authority/body/hold tests retained; player refusal | Lead's actual actor/order/motor binding and validator pins/plants |

## Real source APIs inspected

Installed Character.GetRadius() is NOT a safe physical capsule measurement for
all NPCs: its non-player branch multiplies collider.radius by localScale.magnitude.
GetHeight() returns the unscaled maximum of collider height and diameter. Both
were inspected read-only with ilspycmd against the installed assembly_valheim.
This source therefore reads the upright CapsuleCollider WORLD bounds instead.
It requires enabled Y-axis capsule, aligned horizontal centre and feet/root,
circular XZ bounds, valid locally owned unchanged view and finite supported size.
Unsupported/offset/tilted bodies refuse; they are never fitted by guessing.

Only the exact worker ForemanWorkerAI + nearest Humanoid + nearest ZNetView
colliders are omitted from self-collision. Other characters and foreign network
objects remain collision blockers, even if parented under the worker. The layer
set includes character/character_net/character_ghost, matching the existing CL
clearance layer names. Doors and rigidbodies are never accepted as support, and
are NOT excluded from collision. Physics hit buffers are cleared after each use.

## Bounded native costs

Off/unauthorized/unordered: retire own links immediately and perform no survey.
Enabled feed: at most one normal source/probe refresh every two seconds. Admission
has an explicit forced refresh; the native caller must use it, never treat the
cache as movement authority. Body validity/shape is checked without per-tick array
allocation. Maximum 2048 wire sections, 64 nearby sections, 8 runs / 16 face probes.
Over-budget/ambiguous publications refuse as a whole; no arbitrary subset is a
complete settlement survey. Physics is capped at 256 queries per survey, with
16-hit cast and 32-overlap buffers; saturation/exhaustion means unavailable and
withdraws the receiver's links. No scene-wide object search or per-frame pathfind.

Footprint support is intentionally conservative: edge heights must agree within
15 cm. Capsule support must be static, nearly level enough for the normal test,
inside loaded footprint zones, and accepted by the caller's current site/hazard
policy. The predicate must test permission/hazards along airborne climb space;
it must NOT demand a walking floor at every mid-ladder point. Ground support is
proved separately at the two endpoints. Do not inject an unconditional true
predicate in the native runtime; only test stubs do so to isolate boundary cases.

## Why a separate path implementation is required

Generic LinkTraversal estimates position on a straight line from entry to exit.
A real top floor can be behind the rails: using that estimate as the worker's
position would cut diagonally through the ladder. The new worker-only wrapper
uses four CHECKED points: supported foot, mount lane, vertical crest, supported
platform. It stops at a corner before moving onto the next segment, so one motor
tick cannot cut the unchecked chord across a corner. Travel speed/fraction use
actual completed path length; dt is finite and capped at 50 ms. Descent reverses
the same measured path. A premature Arrived/TurnedBack does not authorize endpoint
placement; lost/rebuilt paths and repeated exits release without guessed landings.
Generic LinkTraversal remains unchanged for other callers.

## Binding procedure for the lead

Create WorkerLadderReceiver for the CURRENT bound Thorstein body, existing worker
capabilities and real site-policy predicate. Its Network and Traversal belong
together: do not substitute plain LinkTraversal for its checked-path Traversal.
Use Network with the existing WorkerRouteSearch, then WorkerRouteRunner with that
Traversal and the correctly pinned own-body IWorkerRoutePort. Tick only under
actual current worker authority/explicit job and NpcClimbing. Call
RevalidateForAdmission immediately before entry, and Forget on cancellation,
body/zone/world change, opt-out, lost authority and teardown. Keep the actor hold
until motor restoration succeeds. Revalidate capsule sweeps on every actual
position write; a checked publication is not permission to ignore a newly moved
blocker. Caller must suppress BaseAI.MoveTo while the scoped motor owns the body.

This branch does not touch Claude's active worker caller/validator files. The
native motor, collision writes, actor handoff and real order/settlement binding
remain CODE rows, not owner-observation-only rows. They must be integrated and
proved with the required pins/plants before any worker is enabled to climb.

**Bound by #334's binding slice (2026-11-08).** `cf_worker climb here | <x> <y> <z>`
(`Runtime/Ladders/WorkerClimbRuntime.cs`) makes this receiver for the bound body
and hands it to the game-free `Domain/Ladders/WorkerClimbOrder.cs`, which holds
Thorstein's actor mode, forces the admission survey, runs `WorkerRouteSearch`
over the receiver's network with `WorkerNavMeshWalkProbe` (vanilla navigation
mesh, verified in height at both ends) as walk evidence, then `WorkerRouteRunner`
with the receiver's own checked traversal. Before every entry it forces
`RevalidateForAdmission` again and refuses an entry whose link generation moved.
`WorkerClimbMotor` is the scoped motor: the AI reports sleeping (vanilla skips
walking; `ForemanWorkerAI` neither plans nor calls `MoveTo`), the rigidbody is
kinematic and restored, and every position write follows a fresh
`IsMoveClear` capsule sweep. `Forget` runs on every order end; the plugin
supervises death, unload, a lost or unsimulated body, the switch and authority
every frame. Pins: the #400 capability audit and the #334 worker climb audit
(`tools/tests/test_foreman_worker_climb_audit.py`); every exit:
`WorkerClimbOrderTests`. The rows below are still NOT RUN.
Vanilla teleport ladders are NOT admitted by this CL-only receiver. No vanilla
interaction, player admission, animation, snap, input or movement is changed.

## NOT RUN game rows

On the exact integrated candidate: publish only placed live custom ladders (no
ghost/cache prefab); load/unload/rebuild across settlement/zone boundaries;
two faces with asymmetric colliders; two ladders separated by walking/stairs;
a floor behind the ladder, floor destruction, moving hatch/door, occupants and
new blockers mid-climb; body scale/capsule/root offsets; opt-out/authority/peer
loss; cancel, jump/drop-equivalent job interruption, cleanup and relog. Verify
one motor only, actual actor hold, no body/cart teleport, and no player motor
claiming a worker. Record actual source/package/DLL identity and performance.
These tests are not satisfied by metadata inspection or unit stubs.

### Checked admission from an approximate ground arrival

The worker-bound path wrapper never snaps an approximate ground arrival onto the lane. A native mount probe verifies the actual owned body position, current capsule, supported starting floor and a clear sweep to the measured entry. That checked prefix travels at the same bounded speed, holds at the actual starting position, and stops at the entry corner before climbing. Without a native probe, only an exact entry is admitted. Reverse release returns to the checked original start, subject to the ordinary generation and landing gate. The mount probe is not a movement writer; live motor suspension and order binding remain integration CODE rows.

All reflected failure diagnostics use the existing Shared Diagnostics `SafeFailure.Brief` scrubber; consumers must retain that source inclusion.
