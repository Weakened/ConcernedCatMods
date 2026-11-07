# Gunnar camp routine (#461): source handoff

GunnarCampRoutine caches the shared PlacementPlanner result by verified anchor
and Camp revision. The existing figure survives range exit/re-entry; a changed
bed re-plans only presentation. A saved worker suppresses the figure immediately.
The production hot path measured zero allocated bytes across 100,000 warmed ticks
and did not re-probe. Cold plans remain bounded to the shared planner's budget.
Seat targets carry the actual free-seat attachment point, yaw and animation name;
no seat is claimed. FacingYaw is provided for interaction only. No ambient speech,
walking, door interaction, cart/cargo port or second movement controller is added.

Camp API RELEASE BLOCKER: CampRegistry and CampSnapshot are internal. The
public-surface test and NAMING_CONVENTIONS require a coordinated library release
and consumer-pin changes before exposing a new API. Those release files belong
to Claude's active work. A proposed additive facade is saved for review at
proposals/NpcCamp.cs.txt; it is NOT compiled or shipped. The production routine
accepts only IGunnarCampView, a read-only projection. No reflection, private-type
bypass or copied Camp implementation is introduced into Teamster. Tests project
the real internal Camp source (test-only, not public API provenance). No existing
or new compiled ConcernedNPC source file changes in this handoff.

Lead wiring: after the coordinated API release, maintain one Camp/routine per character/world, give Camp the shared
NpcRoleRegistry epoch, feed observed structural-piece additions/removals, resolve
the valid current respawn anchor, and pass its cached Survey view into Step.
Show/Reposition applies the returned local figure target; acknowledge a successful
build using NoteFigureShown(true). Retain means DO NOT call the old figure Refresh
with near=false (that would hide/rebuild it). Hide removes the figure and lease.
Forget both on scope change. Never use a camp update to place a worker or cart.

SPECIFIC NATIVE BLOCKER: current NpcFigureDresser has StandAt and TakeDown, but
no public local seat/idle pose operation. The shared planner supplies a seat pose,
but playing it would require an audited shared-library extension or an audited
Teamster pose port. That integration/audit belongs with Claude's active shared
validator work. No guessed animator parameter or direct engine write was added.
Standing can use StandAt; chair/stool/fire sitting and facing callbacks still need
that native pose layer and actual-game checks. #461 is not complete.

Automated: cached steady-state allocation/probe budget, retention across range,
bed re-plan, worker suppression without another probe, actual shared Camp cache,
non-structure and foreign-epoch refusal, free-seat pose transport, unknown camp,
stale epoch and facing calculation. No assertion is a claim of rendered seating.
OWNER GO-AROUND PENDING: real Camp events/anchor, safe chair/stool/fire fit and
occupied-seat yield, body exclusivity, range retention, bed change during hauling,
player-facing interaction and lifecycle cleanup on the exact integrated candidate.
