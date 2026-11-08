# Gunnar camp routine (#461): source handoff

GunnarCampRoutine caches the shared PlacementPlanner result by verified anchor
and Camp revision. The existing figure survives range exit/re-entry; a changed
bed re-plans only presentation. A saved worker suppresses the figure immediately.
The production hot path measured zero allocated bytes across 100,000 warmed ticks
and did not re-probe. Cold plans remain bounded to the shared planner's budget.
Seat targets carry the actual free-seat attachment point, yaw and animation name;
no seat is claimed. FacingYaw is provided for interaction only. No ambient speech,
walking, door interaction, cart/cargo port or second movement controller is added.

Camp API: released inside Concerned NPC's unpublished 0.5.0 as the public facade
`Camp.NpcCamp` / `NpcCampView` / `NpcCampHome` (#461 library PR). The earlier
proposal file under proposals/ is retired; the facade is the compiled API. The
registry and snapshot stay internal. Teamster consumes only the facade: no
reflection, private-type bypass or copied Camp implementation.

Lead wiring: after the coordinated API release, maintain one Camp/routine per character/world, give Camp the shared
NpcRoleRegistry epoch, feed observed structural-piece additions/removals, resolve
the valid current respawn anchor, and pass its cached Survey view into Step.
Show/Reposition applies the returned local figure target; acknowledge a successful
build using NoteFigureShown(true). Retain means DO NOT call the old figure Refresh
with near=false (that would hide/rebuild it). Hide removes the figure and lease.
Forget both on scope change. Never use a camp update to place a worker or cart.

Native pose: `NpcFigureDresser.SitOnGround` / `StandUp` / `Sitting` pose the
dresser's own figure with the game's `emote_sit` bool (#295 S5, same library
PR), so ground and fire-side sitting need no Teamster pose port. Chair, stool
and bench seats stay out of scope until a slice watches for a player taking the
seat; no seat is claimed in any case.

Automated: cached steady-state allocation/probe budget, retention across range,
bed re-plan, worker suppression without another probe, actual shared Camp cache,
non-structure and foreign-epoch refusal, free-seat pose transport, unknown camp,
stale epoch and facing calculation. No assertion is a claim of rendered seating.
OWNER GO-AROUND PENDING: real Camp events/anchor, safe chair/stool/fire fit and
occupied-seat yield, body exclusivity, range retention, bed change during hauling,
player-facing interaction and lifecycle cleanup on the exact integrated candidate.
