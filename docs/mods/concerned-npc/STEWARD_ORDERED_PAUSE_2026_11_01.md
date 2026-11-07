# #379 Steward ordered pause and resume

## Operator path

With the existing Steward runtime opted in, as host with no peers and an active
maintenance job: `cs_steward pause`, `cs_steward status`, `cs_steward resume`.
No profile or setting is changed by this source work. The pause applies to the
current loaded session. Reload follows existing durable revalidation and safe
return-only recovery, not an automatic replay of the old feed cursor.

The status command reports the pause separately from the retained work phase.
Idle, unsafe/pending, blocked or absent plans refuse. Repeating pause is a no-op.
Each command uses the runtime's existing authority/read-only-record gate.

## One executor and unchanged public contract

NpcPlanRun.Suspend already publicly represents higher-priority suspension. It
now calls the internal arbiter's role-work/external-order rule before writing
its optional note. Generic internal arbitration uses an internal note primitive,
so there is no recursion. The seven priority types stay internal; no new public
API, package pin or major version is introduced to work around a compile error.
This is the concrete PlayerOrder-over-RequiredRoleWork pair, not seven new native
NPC activities. Existing source-linked and compiled-native public contracts must
both remain unchanged.

StewardMaintenancePlan checks that the existing run is active, unblocked and has
no in-flight custody outcome. Its optional pause note can fail without making
the previous durable resume point unsafe. UpkeepLoop retains its existing driver,
current step, phase, reservation, target, custody and carried units. Pause stops
the existing vanilla walk goal once through IStewardMotionPort.Stop; resume lets
the same walk/phase obtain a fresh goal through the original movement port.
There is no new motor, body, executor or item transfer adapter.

The loop's active clock freezes deadlines and retry/scan timing across paused
wall time, including a pause longer than the walking deadline. Native world
facts, ownership/authority, scope, journal availability and blocked-plan checks
still run before the pause branch. A safety failure follows the old safe-stop
path, clears session pause and refuses resuming the old cursor. A changed scope,
lost host/no-peers authority, unreadable state or death is never excused by pause.
Stop, attention and world-load paths clear the session flag; world load resets
its active clock and abandons the old driver as before.

## Source evidence and limitations

OrderedPauseTests drives the ACTUAL shared-driver upkeep executor at ToDepot,
Withdrawing, ToTarget, Feeding, Returning and Depositing. Tests preserve the plan
phase/work, exact target, depot/pack/fed counts, and journal bytes after the one
optional pause note. Repeated pause writes no second note, paused ticks stop once
and perform no walk or transfer, a 10000-second pause does not expire a walking
deadline, and completion fetches once and conserves units. Return/deposit cases
carry surplus from a fire the player filled while Sunniva was travelling.
Additional cases cover authority loss, unsafe pending intent and absent work,
attention and the clock's invalid/rewound inputs. Existing recovery and native
adapter tests remain required; engine behavior and CLI operation are not proved
by domain tests. The native build must compile the real command/runtime wiring.

The #372 linked/native API guard commits are included as reviewed dependencies;
this feature changes neither contract fixture. Claude's active PR/dirty files
must remain disjoint; the handoff records the current ownership snapshot.
No merge, publication, profile import or game operation by Codex.

## Fresh integrated candidate gameplay required (NOT RUN)

1. Start an opted-in real maintenance round. Pause while walking to the depot,
   after withdrawing, walking to a fire, and feeding. Confirm the command/status
   feedback and that vanilla movement stops, with the pack/target unchanged.
2. Stay paused longer than the configured walk timeout. Resume and confirm the
   same job continues, without another withdrawal or duplicated fuel.
3. Fill/close/block the destination while paused. Resume: ordinary revalidation
   must refuse, skip or return remaining fuel safely. Exercise return/deposit.
4. While paused, disable tending/runtime, change the settlement/depot, lose host
   authority or add a peer, unload the body, or relog. No stale cursor may resume
   or mutate. Re-enable only through the existing recovery requirements.
5. Pause with carried fuel, relog, inspect durable state and verify the existing
   revalidated return-only path; no blind old-feed continuation. Exercise death
   and needs-attention handling without item compensation.
6. Confirm normal unpaused tending, ownership, lifecycle, noise/performance and
   presentation/worker exclusion on the exact candidate. No gameplay GO until
   observed by the lead/owner; this branch does not operate their game.

#379 remains open for Thorstein's shared recovery adoption and remaining exact
candidate requirements. After merged source, observation-only rows may follow
#470; remaining source work cannot be labelled observations.
