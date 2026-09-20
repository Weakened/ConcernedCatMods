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
person does, through the role's own custody resolution, and the plan that follows is a new one.

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
again. Both directions fail closed.

An interruption preserves inventory, reservations, custody, the plan, the cart, progress, source, destination and
identity - guaranteed by there being no path in the handover that edits a plan. A note saying why is attempted; its
failure is reported and is not fatal, because the record was already at a safe resume point before the note was
tried. Making a boar wait for a disk would be a rule that kills NPCs to keep a diary tidy.

## 8. The kill suite

`PlanKillTests` runs one plan over a world of three numbers - units in the source, units on the back, units in the
destination - as a script of fifteen operations, and kills the process before each of them. Sixteen cases, generated
from the script rather than listed, so adding a phase adds cases rather than quietly going untested. Two extra cases
truncate a transfer half way, which is the one shape where both writes can land and the record still be wrong.

Asserted over every kill:

- the record on the disk is readable and complete - there is no such thing as half a plan;
- a plan off the disk is in no world and never merely pending;
- one of the four responses, with a sentence, never the value that means nobody decided;
- deciding moved nothing;
- nothing was gathered or delivered twice, counted in the world rather than in the record;
- the three numbers still add up to what the player owned;
- **when the record and the world disagree about the back, the answer is needs attention** - never resumed, never
  re-planned, never refunded;
- the plan came through as the same work;
- one body, re-attached rather than built, and given back if the plan stopped.

A separate test collects the phases the kills actually land in and compares them with the pipeline, so a shortened
script fails rather than passing over four cases.

Beside the sweep there is one reconstruction test per outcome, driven by the evidence that reaches it: re-plan from
a plain reload, refund from lost authority and again from an unreadable area, needs attention from a death while
holding something, and carry-on from a pause. **Carry-on is reachable only for an interruption inside a session**,
because a plan off the disk is always stale and a stale plan never continues - which is the rule working, not a
gap. And an interrupted job is shown resuming at the step it was at, still carrying what it was carrying, rather
than gathering a second load.

## 9. What is not here

- **No caller.** Nothing in any product constructs an `NpcPlanRun` yet. The mechanism is provable and inert; the
  three role leaves (#380, #381, #382) are where it gets a call site, and "a plan survives a reload" becomes a
  statement about behaviour rather than about a test's own script on that day.
- **No custody-ledger wiring.** `NpcCustodyLedger` already reasons about transfers with the same write-ahead
  discipline and already has `CloseOpenIntents` for "a record replayed from disk". The plan journal and the ledger
  agree in shape and are not yet joined; joining them belongs with the first role that has both a plan and a
  transfer.
- **No player-facing sentence.** The reasons are written in words rather than enum names, and no product renders
  them yet.
