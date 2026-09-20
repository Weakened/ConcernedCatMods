# Interruption, reload and death: revalidate, never duplicate

Leaf #379 (CNPC-008). This document is the design; `src/ConcernedNPC/Interruption/` is the code and
`src/ConcernedNPC.Tests/Interruption/` is the evidence. **Nothing here has been observed in game. Every gameplay
row for this program is OWNER GO-AROUND PENDING**, this one included: what is proved below is proved by unit tests
over a modelled world, not by watching an NPC be killed mid-haul.

## 1. The one absolute, and the ordering rule that buys it

No path duplicates a resource and no path silently loses one.

Everything else here follows from a single discipline: **a caller may act only on the phase the record already says
it is in.** `NpcPlanRun` enforces it - a phase change is written down and adopted only if the write succeeded, and a
write that was refused leaves the in-memory state where it was, so a caller that cannot record cannot act. The
record is therefore always at or ahead of the world, and the last written record is always a safe place to resume
from, whatever instant the process died at.

That collapses twenty-odd points inside a round into one class of point - "the record is the truth and the world may
be one action short of it" - with one answer.

## 2. Where the ordering rule is not enough

Two of the eight pipeline transitions move a player's material: reaching `Provisioned` and reaching `Reconciling`.
For those, the record being ahead is not enough on its own, because the move may have happened. So each is written
twice:

- `Intend` before the world is touched - custody becomes `Pending`;
- `Conclude` after, with what was measured - custody becomes `Clear`, or `Uncertain` when nobody can establish the
  outcome.

A record found in `Pending` is one where the answer is genuinely unknown. It is **never replayed** (that duplicates
a player's material) and **never discarded** (that deletes it). `NpcPlanState.AsRecovered` turns it into
`Uncertain`, and `Uncertain` stops the plan for a person. This is the transfer executor's add-before-remove
asymmetry raised one level: of the two possible failures, choose the one that leaves evidence.

`Unspecified` custody is treated exactly like `Pending`. A field nobody wrote is not evidence that nothing was in
flight.

**The library refuses the transition, rather than trusting the role to write the pair.** `NpcPlanRun` will not move a
plan into `Provisioned` or `Reconciling` unless this run has already written an intent and concluded it with an
established outcome, in the phase it is moving out of. An outcome nobody could establish does not count: it stops the
plan rather than buying the next step.

**And it refuses the load, not only the transition.** A second rule beside it says `Carried` only ever changes as the
recorded outcome of a movement that was announced first. Both are needed. In-phase writes skip the pending, the
uncertain and the transition rules, so with only the first rule any same-phase write could rewrite what the NPC is
carrying, in any phase, with no intent at all - and "the two transitions that move a player's material are written
twice" would have bound which phase a plan was in rather than what it was holding. Deliberately *not* bound the same
way: reservations, the route and the progress count. None of them is a player's material in transit, a reservation
name is derived so re-taking it is satisfied rather than doubled, and binding them would refuse the write that
records them.

**A movement with no recorded outcome cannot be written into an ending, and cannot have its custody laundered by a
phase change.** The pending rule used to exempt every terminal phase, so a plan could report itself `Settled` - or
`Refunded` - over a movement nobody had accounted for, and the reload called it "already ended, nothing to resume". No
document ever mentioned that exemption and three passages said the opposite, `InterruptionResponse.Refund`'s own
summary among them. It was reachable by an ordinary role: `Refund` is the answer to lost authority, which can fire in
the window between `Intend` and `Conclude`, and `Stop(Refunded)` is the obvious verb for it. Worse, one write could
both end the plan and drop the pending custody, which erases the only evidence that anything was ever in flight -
after that not even `AsRecovered` can get the question back. The one ending a pending movement may reach is
`NeedsAttention`, with the pending custody kept.

This is the one thing in this document that changed as a result of an independent review, and the correction is worth
recording. Until then `NpcPlanProgression.MovesMaterialToReach` - the only function naming the two material-moving
transitions - had **no callers at all**. Replacing its body with `false` left all 891 tests green, and a run could
walk `Reserved` to `Reconciling` with no `Intend` and no `Conclude` anywhere and reload to a resumable record. The
double write was a convention of the test fixture and this paragraph was describing the fixture. It is now a rule,
and `PlanRunTests.A_phase_reached_by_moving_material_is_refused_unless_the_movement_was_written_first` is where it is
proved.

**The limit of that enforcement, because it has one.** What a run remembers about its own concluded intent is in
memory, not on the disk. There is no durable field saying "the intent for this transition was concluded", and adding
one would put a field the library demands into a format the role owns - the one thing §3 exists to prevent. So a plan
resumed from the disk has to intend and conclude again before it may claim a material-moving phase. That costs two
writes and moves nothing, because `Conclude` records what was measured: a body that is already loaded is recorded as
already loaded, and nothing is fetched twice.

## 3. Durable plan state without owning a format or a path

The library owns the **mechanism** of persistence and never the **format** or the **path**. That is the
zero-migration promise: a pre-refactor data directory, dropped in unchanged, keeps working with no migration code
having run, and that holds only while every durable row tag, field order, schema number and file name stays with the
role that already writes it.

| Decision | Who owns it |
|---|---|
| Where the plan file is | the role - an absolute path handed to `NpcPlanJournal.TryOpen` |
| Row tags, field order, escaping, schema number | the role - `INpcPlanCodec` |
| When to write, and in what order relative to the world moving | the library - `NpcPlanRun` |
| Whole-or-not-at-all writes, and never overwriting an unreadable file | the library - `NpcSidecarFile` |
| What a reconstructed plan is allowed to believe | the library - `NpcPlanState.AsRecovered` |

`NpcPlanJournal` names no path, joins none, takes none apart and reads no field. `validate_repo.py` enforces the
mechanical half (`LIBRARY_FILE_APIS`, `LIBRARY_FORMAT_OWNERSHIP`, `LIBRARY_PATH_INSPECTION`, and the pinned two-file
plumbing allow-list); `PlanFormatBoundaryTests` enforces the rest, including that no literal in the area looks like
a row tag or a file name.

**No purpose token was added to `INpcDataPaths`.** The journal takes the path directly, so the role resolves it
however it likes and the library still has no way to write a name. That interface's "there are no purposes yet"
remains true.

**The epoch is deliberately not in the format.** A role cannot mint an `NpcWorldEpoch` - the constructor is internal
- and it does not need to: `AsRecovered` stamps `NpcWorldEpoch.Unknown`, which matches nothing, so every in-session
key a reconstructed plan holds is stale rather than dangerous. A chest id from the last load can never be acted on
as though it named the same chest.

## 4. What survives

`NpcPlanState` carries the issue's own list: plan identity (`Identity` + `JobId`), current phase, reservations,
carried inventory, target progress, assigned vehicle or cart, source and destination, custody state. Plus an attempt
count, which rises on every reconstruction and is evidence for a person rather than a limit enforced anywhere.

Reservation names are `ReservationId`s - derived from the job and the step, never minted - so a resumed plan
re-taking a hold it already has is answered `AlreadySatisfied` rather than taking it twice.

## 5. Revalidation: four answers, and the order they are read in

`NpcPlanEvidence` is everything the decision may consider; nothing reads ambient state. `CauseFor` picks **one**
cause by severity, because a reload commonly makes four of them true at once and a player is owed the worst one
rather than the first one checked: two bodies, then an unrecorded movement, then lost authority, then death, then a
missing body, then the area, then chests, then the route, then a pause, then a plain reload.

`NpcWorkInterruptionPolicy` is the first `IInterruptionPolicy` implementation. Its table, in the order it is read:

| Situation | Answer |
|---|---|
| anything uncertain, whatever else is true | needs attention |
| two bodies answer to this NPC | needs attention |
| a movement with no recorded outcome | needs attention |
| nobody said why | needs attention |
| authority is gone | refund |
| he died, holding nothing / holding something | refund / needs attention |
| his body is not there, holding nothing / holding something | refund / needs attention |
| the work area cannot be read | refund |
| the area moved | re-plan |
| a container refuses, or he cannot get there | re-plan, after a wait |
| the player paused it | carry on, after a wait |
| the world reloaded | re-plan |

Two rules sit above the table rather than inside its rows: **uncertainty always wins**, and **a stale plan never
continues** - the staleness downgrade lives in one place so that eleven rows do not each have to remember it.

Whatever is decided, `NpcPlanRecovery` changes only the phase and the note.
`NpcPlanState.CarriesTheSameWorkAs` is the question a test asks about the rest, and it is asserted for all four
responses.

`NeedsAttention` is a durable phase and is **one-way**. Nothing in this library takes a plan out of it; only a
person does, through the role's own custody resolution, and the plan that follows is a new one. That is true of
`Record` and, since the corrective round, of `Adopt` as well - see the precondition in §9, which is about the person
half of that sentence not existing yet.

`NpcPlanRun.Adopt` is the verb a recovery's decision is written through, and it enforces the rules `Record` enforces:
nothing is adopted over a terminal phase; a movement with no established outcome may be followed only by a decision
that stops the plan for a person; a state has to say whether anything is in flight; a record in no phase at all may
only be stopped for a person; the work itself has to come through unchanged; and the phase has to be the one the
decision actually leaves a plan in, which is `NpcPlanProgression.PhaseAfter` - the same function the recovery path
produced the state with, so the two cannot disagree. The first version checked only that a decision existed, and an
independent review used an invented `Replan` to clear an uncertain movement, reopen a plan that had stopped for a
person, skip five phases at once, write a plan with an unset custody field to disk, and launder a record in no phase
into a live plan. One test per refusal is in `PlanRunTests`.

It is **not** exactly the same set as `Record`'s, and the difference is in `Adopt`'s favour in the one reachable case:
a `Continue` decision over a `Pending` custody produces a same-phase note-only state that `Record` accepts and
`Adopt` refuses. Nothing is stranded - the note can still be written through `Suspend` - so this is deliberate
conservatism about the verb that exists to authorise a move, not a gap.

**A record in no phase at all is not a plan.** `NpcPlanPhase.Unspecified` is documented as "a record somebody wrote
wrong… never resumed, never continued, never counted as the start", and it used to be the one record that could not
be stopped and could be laundered live: `MayFollow` refuses `Unspecified` on both sides, so `Stop(NeedsAttention)`
was unavailable, while `CauseFor` reported it stale, the policy answered `Replan`, and `Adopt` saved it into a live
plan at `Observing` still claiming its load, its reservations and its cart. Three things now hold. `NpcPlanJournal`
answers **unreadable** rather than handing back such a plan, so a role quarantines the file and tells the player.
`NpcPlanRecovery` answers `NeedsAttention` before it asks for a body, with the state already phased so a role has
something to write. And `NpcPlanRun` refuses everything over it **except** a stop for a person.

That last one is how the `MayFollow` asymmetry was decided, and it was decided deliberately. The table keeps refusing
`Unspecified` on both sides, because the table is about the shape of the pipeline and a phase nobody set is not a
position on the line; putting the exception there would make `Unspecified → NeedsAttention` a legal pipeline edge and
grant it to any future caller of the table with no same-work guard attached. Instead `NpcPlanRun.WhyNot` makes "stop
this for a person" available above every other rule. The reason is the leaf's own thesis: a library that can hold a
state and cannot hand it to a person has a failure nobody is ever told about.

What that rule requires is `NpcPlanState.CarriesTheSameWorkAs`, and because it sits above every other rule that
predicate is the only thing between it and both pending rules, the uncertain rule, the load rule and the refusal of an
unset custody field. So the precise version of "changes nothing but the phase and the note" is: **it changes nothing
about the work** - not the load, not the custody, not the reservations, the route or the progress. Two things it does
leave loose, because `CarriesTheSameWorkAs` deliberately ignores them: the world epoch and the attempt count may be
re-stamped by a stop, and a record whose custody nobody set can be stopped past the refusal that would otherwise
catch it. Both are conservative, neither is depended on anywhere - adding either guard changes no test - and the
epoch latitude is what `Reattach` rides on, so they are written down here rather than closed.

**`AlreadyOver` is the second line of defence for an ending written over an open question.** It short-circuits before
the policy, so the policy's first row - anything uncertain is needs attention, whatever else is true - never ran for a
record that had already ended. It now answers `NeedsAttention` for any terminal record whose custody is not `Clear`.
`NpcPlanRun` refuses to write one; this answers one that exists anyway, from an older build, a hand edit, or a role
that found another way. A plan that really did finish is still reported as finished.

**And that answer can be written down, which needed one deliberate exception.** The state `AlreadyOver` hands back is
phased `NeedsAttention`, and both verbs refuse over a terminal phase - so at first the corrupt row stayed on the disk
and the same decision re-issued on every world load, a fix that reported a problem for ever and could never record
that anybody had seen it. So exactly one move out of an ending exists: to `NeedsAttention`, only while the custody is
not `Clear`, through `Stop` or through `Adopt`.

The alternative was to document the loop as permanent. This is better, and narrowly so rather than generally: a
record that says it finished while something it set in motion had no recorded outcome is not a well-formed ending in
the first place, and moving it to `NeedsAttention` is monotonic in the conservative direction - it withdraws a claim
of success and grants nothing. A plan whose custody is `Clear` is untouched, so a job that really did finish is never
reopened and "this job finished" stays distinguishable from "this job never existed". What the write does **not** do
is resolve anything: the custody is still uncertain afterwards, so the plan lands squarely in the precondition below.
It is on the record once instead of being re-decided every load.

## 6. One logical NPC, one world entity

A reconstructed plan re-attaches through `NpcRoleRegistry.TryClaimBody`. `AlreadyHeld` is the ordinary answer for a
runtime that re-asks after a reload. Nothing here constructs a body or tears one down.

Two bodies answering to one identity is **not claimed at all** - the arbiter would grant it, because it knows about
holders and kinds rather than about how many objects in the scene answer to a name, and the claim would be on an
arbitrary one of the two. And a recovery that does not authorise going on releases the body before it returns,
because a stopped plan holding a claim is a body nothing will ever release.

## 7. The arbitration ladder

Seven rungs, in the brief's order, rising:

1. camp-border stroll
2. social idle
3. background maintenance
4. required role work
5. an explicit player order
6. danger
7. lifecycle and recovery safety

Strictly higher wins; equal does not, because the running activity has a plan in progress and the arriving one does
not. An activity nobody named neither interrupts nor is interrupted: an arrival with no stated priority has made no
claim, and a running activity with no stated priority might be any rung, including the one making the record safe
again.

**Only one half of the guard that says so does any work.** This section used to claim both directions fail closed.
They do not, symmetrically: `Unspecified` is zero, which is below every rung, so `arriving > running` already refuses
an unnamed arrival. Deleting the arriving half of the guard leaves every test green; deleting the running half turns
them red. The arriving half is kept as defence against a renumbering that moved `Unspecified` off the bottom, and the
numbering it leans on is pinned by
`ActivityArbitrationTests.Unspecified_is_numbered_below_every_rung_so_the_comparison_alone_refuses_it` rather than
assumed.

An interruption preserves inventory, reservations, custody, the plan, the cart, progress, source, destination and
identity - guaranteed by there being no path in the handover that edits a plan. The cart is in that list honestly
now: every rehearsal plan used to carry an empty vehicle key, so "the cart came through" was two empty strings being
equal, and `PlanRehearsal` assigns one. `NpcPlanState.CarriesTheSameWorkAs` is also asserted **false** - over a
different load and over a different cart - because a preservation claim resting on a predicate that answers true for
everything is worth nothing.

A note saying why is attempted; its failure is reported and is not fatal, because the record was already at a safe
resume point before the note was tried. Making a boar wait for a disk would be a rule that kills NPCs to keep a diary
tidy.

## 8. The kill suite

`PlanKillTests` runs one plan over a world of three numbers - units in the source, units on the back, units in the
destination - as a script of fifteen operations, and kills the process before each of them. Sixteen cases, generated
from the script rather than listed, so adding a phase adds cases rather than quietly going untested.

**Four extra cases truncate a transfer half way**, in both of the places a load moves and in both of the ways the two
writes can fall. They assert that the load really is in two places at once, which is what the half-move parameter is
for; without that assertion the parameter is inert, and an independent review showed it was - replacing it with -1
left the original two cases green, because both of them stopped between the intent and the outcome and were therefore
the same shape the main sweep already covers sixteen times.

The shape this section used to claim - both writes landing and the record still being wrong - turns out not to exist:
`Conclude` measures, so it records the half load honestly and says that nobody could establish the outcome. That is
now two of the four cases, and what they prove is the sharper thing: **a record that agrees with the world is not
permission to go on when the record itself says the outcome could not be established.** The original assertion
forbade the agreeing case outright, so the shape its own comment described would have failed.

Asserted over every kill:

- the record on the disk is readable and complete - there is no such thing as half a plan;
- a plan off the disk is in no world and never merely pending;
- one of the four responses, with a sentence, never the value that means nobody decided;
- deciding moved nothing;
- **when the record and the world disagree about the back, the answer is needs attention** - never resumed, never
  re-planned, never refunded;
- the plan came through as the same work, with the negative control that a different load or a different cart is not
  the same work;
- one body, re-attached rather than built, and given back if the plan stopped.

Two more assertions are in the sweep and are **not** evidence about the library, which is why they are listed apart:
that nothing was gathered or delivered twice, and that the three numbers still add up to what the player owned. Both
are arithmetic properties of the rehearsal - each counter is incremented by exactly one script operation, no
operation runs twice, and every movement is one subtraction and one matching addition - and `NpcPlanRecovery` has no
inventory port to break them with. They are a tripwire for the day something gives it one, not a demonstration that
recovery conserves material.

A separate test collects the phases the kills actually land in and compares them with the pipeline, so a shortened
script fails rather than passing over four cases. It has teeth: shortening the script turns it red.

Beside the sweep there is one reconstruction test per outcome, driven by the evidence that reaches it: re-plan from
a plain reload, refund from lost authority and again from an unreadable area, needs attention from a death while
holding something, and carry-on from a pause. **Carry-on is reachable only for an interruption inside a session**,
because a plan off the disk is always stale and a stale plan never continues - which is the rule working, not a
gap. And an interrupted job is shown resuming at the step it was at, still carrying what it was carrying, rather
than gathering a second load.

## 9. What is not here, and one precondition on the role leaves

- **No caller.** Nothing in any product constructs an `NpcPlanRun` yet. The mechanism is provable and inert; the
  three role leaves (#380, #381, #382) are where it gets a call site, and "a plan survives a reload" becomes a
  statement about behaviour rather than about a test's own script on that day.
- **No custody-ledger wiring.** `NpcCustodyLedger` already reasons about transfers with the same write-ahead
  discipline and already has `CloseOpenIntents` for "a record replayed from disk". The plan journal and the ledger
  agree in shape and are not yet joined; joining them belongs with the first role that has both a plan and a
  transfer.
- **No player-facing sentence.** The reasons are written in words rather than enum names, and no product renders
  them yet.

### A named precondition on #380, #381 and #382: `NeedsAttention` has no exit

This is a precondition rather than a future-work aside, because the first role to hold a player's material through
this library inherits it on day one.

`Uncertain` stops a plan for a person, and that is the right failure direction - it leaves evidence instead of
guessing, and §2 is mostly about why. Until this round it was not even reliably the direction: a plan could be
written straight from a pending movement to `Settled` or `Refunded`, and the reload reported a refund with "nothing to
resume" - so the alternative to a dead end was not a live plan, it was a silent one. That is closed in §2 and the
precondition below is what remains. But **the person half of it is implemented nowhere.** There is no resolution
UI. `NpcCustodyLedger.CloseOpenIntents` exists and is joined to no plan. Nothing creates "the plan that follows"
that §5 promises. So a plan that reaches `NeedsAttention` stays there for the life of the save, and the only thing in
this repository that clears it is deleting the plan file by hand.

Whoever takes #380, #381 or #382 therefore has to do one of three things, and should say which in the issue:

1. bring a resolution path of its own - a role-side way for a player to say what actually happened, which then starts
   a new plan;
2. hold no material through this library until such a path exists, which keeps the whole `Uncertain` class
   unreachable;
3. accept that a job can end in a state only a file deletion clears, and say so where a player can read it.

One neighbouring dead end was closed rather than documented. `NpcPlanState.WithWorld` - §3's answer to staleness -
had zero references and zero tests, so a re-planned plan re-planned for ever: it is in no world, a stale plan never
continues, and nothing could ever say otherwise. A review observed five consecutive revalidations answering "the
world reloaded, so re-plan" against entirely healthy evidence. `NpcPlanRun.Reattach` is now its caller and
`PlanRunTests.A_re_planned_plan_re_attached_to_this_world_stops_being_stale` is its test. It changes nothing about
the work and resolves nothing about custody: it says the role has found its chests and its piles again, which is the
only claim the role is in a position to make and the library is not.
