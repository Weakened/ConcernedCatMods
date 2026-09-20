# Concerned NPC: the runtime the companions share

**Owner brief:** 2026-09-19. **Epic:** #371. This document is the decision record and the map. Read it before
touching anything under `src/ConcernedNPC`.

## 1. What it is, and what it must never become

Concerned NPC answers the questions that are the same for Hulgi, Thorstein, Gunnar and Sunniva: who am I after a
reload, where is camp, which chests may I use, where may I work, what is my job and in what order, what am I carrying,
and what happens when I am interrupted.

It answers **how**. The role mods decide **what**. A type in here that knows what a cart is, what resin is for, or why
anyone wants a shelter is a defect, and the validator fails the build if this package so much as names a product.

On its own it does nothing: no patch, no prefab, no command, no world state, no file of its own. It is a BepInEx
plugin only so that a consumer can declare `BepInDependency` on its GUID, which turns "you forgot to install it" into
one clear line at load rather than a null reference later in somebody's evening.

## 2. Why a package, when this repository is built to forbid one

The reconnaissance recommended **against** a package and for two new shared source areas, and its reasoning was
sound on the evidence it had: a shipped DLL that products reference is blocked in three places -
`check_cross_product_independence` forbids references between products, `validate_product` forbids a second DLL in
any ZIP, and it requires a `Plugin.cs` that a pure library would not have.

Those are this repository's own rules, and the decision to change them deliberately belongs to the owner, who asked
twice for a package that can be updated without rebuilding every mod. So the rules gained a new category rather than
an exemption:

- A **product** is a mod installed for what it does. Products still must never reference each other; the day one does,
  two release cadences become one. That rule is untouched, and it is now also **complete**: a new check fails the
  build if any ordered pair of products is missing from the audit, which was previously a hand-written list a new
  product could silently escape.
- A **library** ships no gameplay and exists so several products can share one runtime *and* one release cadence for
  it. It may be referenced. The reference carries four guarantees, enforced together in `check_library_consumers`:
  the library depends on no product; a consumer references it as a `ProjectReference` with `Private` false, so its
  DLL is never copied into a product's output and cannot reach a product's ZIP; a consumer pins it in
  `thunderstore.toml`, so the storefront installs it; and a consumer declares `BepInDependency` on its GUID, so a
  missing package is a dependency error rather than a mystery. All four or none - a stale pin or a stale dependency
  left behind after a reference is removed fails too.

Every one of those rules was proved by planting the violation and watching the validator catch it: six of six.

**What the package route costs, stated honestly.** Source sharing makes every consumer rebuild for a runtime fix, but
it also makes it impossible to ship a broken runtime to a mod that was not rebuilt against it. A shared DLL trades
that safety for the independence the owner asked for: from now on, a change to this package's public surface can break
an installed consumer. The version in `thunderstore.toml` is what protects players, so a breaking change means a
major-version bump and a pin bump in every consumer, together, in one change.

There is a sharper version of that cost, and it is worth naming rather than discovering. `NpcBodyMind` is a public
type that derives from the game's own `BaseAI`, and `TryDrive` now sits on it. So a Valheim update that changes
`BaseAI` can break an installed consumer **with no change to this library at all** - the breakage arrives from a
third party, on the player's machine, between two versions that were pinned correctly against each other. Source
sharing could not do that, because a product compiled its own copy and simply failed to build. This is the price of
the independence: it is accepted, not overlooked, and it is the reason the game-bound surface is kept as small as it
can be rather than as large as is convenient.

**What it buys beyond independence.** `src/Shared` may hold no Unity, BepInEx or Jötunn type. A library assembly may.
The game-bound half of the companion runtime - body construction, the census, presentation extraction, console
registration - has had no sanctioned home in this repository and exists as three and four near-copies because of it.
It has one now.

**Public, not internal.** Types in `src/Shared` are `internal` because each product compiles its own copy. A library's
contracts are consumed across an assembly boundary, so the registration surface is `public`. Everything that is not
part of that surface stays `internal`.

## 3. The migration rule, and the one thing that must not go wrong

**Zero data migrations.** Every durable thing in this repository is a string literal or an injected path: hand-rolled
TSV with one-character row tags, ZDO keys written as literals, prefab names as literals, data roots passed in as
constructor parameters. Nothing on disk is derived from a type, a namespace or an assembly name, which was checked key
by key and adversarially re-checked: ten of ten preservable.

So the acceptance criterion for this whole program is blunt:

> A pre-refactor data directory, dropped in unchanged, produces a recruited Hulgi, a bound Thorstein with his axe, and
> a Steward with his designations - with zero migration code having run.

**The failure that would be irreversible.** The host destroys any saved object whose prefab is not registered when a
world's objects are created. If this package ever registered worker bodies under a name of its own, or later than
Jötunn's `OnVanillaPrefabsAvailable`, the first load after the refactor would delete every existing Thorstein, Gunnar
and Steward with everything in their inventories. Silently.

Therefore the durable facts stay owned by the roles and are handed to the runtime as data:

```csharp
public readonly struct NpcBodyContract
{
    public string PrefabName { get; }    // "CF_SettlementWorker", "CT_TeamsterWorker", "CS_Steward"
    public string ZdoKeyPrefix { get; }  // "tcc.worker." or "tcc.steward." - never unified
    public NpcBodyKind Kind { get; }     // Presentation or Worker
}
```

The code is unified. The keys never are. And each role still registers its own prefab, at plugin start, forever.

## 4. One identity, one body

Two identity models exist today and they guarantee uniqueness by different means. Workers are an authored
`WorkerKey` (`product/worker`), and the body is found after a reload by scanning saved objects of that product's
prefab. Hulgi has no persisted body at all: he is a local-only figure rebuilt every session, addressed by
(product, world, character) plus a quest slug.

"One logical NPC is one world entity" is therefore true today only because the two models never meet. The rule that
a presentation body and a worker body never coexist for one identity is prose, plus a property nobody calls.

`NpcBodyArbiter` is the enforcement: one mode owner per identity, one current body kind, and a claim that refuses when
the other kind exists or a job holds the identity. It is the smallest type that makes the rule true rather than
intended, and it is tested by trying to break it.

## 5. What moves, what stays

**Moves** (each with its persistence carried verbatim): sidecar persistence and the atomic write, quest state and the
monotonic unlock policy, door geometry and permissions, camp sensing, residency and placement planning, walk
setbacks, dialogue rotation, designations and work scope, the custody ledger and transfer executor, the journal, the
worker prefab factory, the body AI, body inventory, the census, presentation extraction, and console registration.

**Stays in its product, deliberately:** the cart hitch seam, pull calibration and cart routing, because
`validate_repo.py` confines `AttachTo`/`Detach`, mass writes and ZDO writes to Teamster's own folder and proves it
with a mutation check - moving them would move a shipped safety property out of the product audited for it. Also: the
fireplace adapter, the natural-source predicate, the compass, every role's slug, paths, config keys and storefront
identity.

**Rewritten rather than moved:** the fused collection loop, from which the task runner is extracted while survey,
select, pick and deliver stay Foreman's.

**A note, not a plan: the Steward's recruitment.** An earlier draft of this section said the Steward's separate
recruitment implementation folds into the shared machine. There is no shared recruitment machine in this package,
and inventing one for a single caller so that a sentence in this document comes true would be worse than the
duplication it claims to remove - a seam with one consumer is that consumer's code with an extra indirection in
front of it. So the Steward's recruitment stays where it is. If a second role ever needs recruiting, those two
implementations are what a shared one should be derived from, and that is the point at which this becomes a leaf
with an issue behind it.

## 6. The behaviour changes that must not ride along inside a refactor

Four differences between the copies are semantic, not stylistic, and each costs an existing player something. Each
needs its own issue and its own in-game proof:

1. A correct four-way bed read replaces a two-way one, which changes which beds Hulgi will sleep in, in every existing
   save. Both predicates ship; switching Cartographer over is a separate decision.
2. A stricter container gate closes a permission hole and will make some already-working Steward depots refuse until
   a ward or privacy setting is fixed. It needs a player-facing notice that names the fix.
3. The unified census changes what "duplicated" means for each product.
4. The unified anchor changes which bed counts as home when a player has several.

## 7. A plan is an answer, and the verdict decides what happens next

Working the whole job out before acting is the point of this package, and it only pays if the answer is read
correctly. There are six answers and they are not interchangeable:

| Verdict | What it means | What the runtime does with it |
|---|---|---|
| `Planned` | An ordered plan for as much of the job as this round covers. | Walk the steps. |
| `NothingToDo` | The job is already satisfied - decided on a conclusive empty snapshot, before any target is known. | The **only** verdict a job may be reported finished on without doing anything. |
| `BudgetExhausted` | Planning ran out of its allowance. Incomplete, not impossible. | Ask again next tick. Never a reason to stop a job. |
| `ShortOfMaterial` | Understood and not provisionable: the manifest asks for more than is reachable **within one round**. | Stop and tell the player. Asking again unchanged does not help. |
| `AreaInvalid` | The work area could not be read, or does not exist. | Fail closed; nothing widens to a default. |
| `Refused` | The job cannot be worked as it was ordered: a malformed request, an identity that holds no body, an empty manifest where one was required - and also a job that asks for something that cannot be done at all, such as a single target heavier than one trip. | Stop. Mostly a programmer's problem; the oversized-target case is a player's. |

Three rules, each of which exists because it was got wrong first, and each of which cost a blocker.

**The player sentence is `Reason`, never the verdict.** `ShortOfMaterial` covers two situations whose fixes are
opposites, and they are told apart by the shortfall manifest rather than by a verdict of their own: a non-empty
shortfall means the material is not there and more must be brought, an empty one means it is there and spread
across more containers than one round opens, and must be brought together. A role that renders the verdict name
instead of the reason will tell somebody they are out of wood while they are standing on it. There is deliberately
no seventh verdict for the second case: both are terminal until a player acts, no caller branches on the
difference, and a surface is not widened for a distinction nobody makes. The day a role responds to scattered
material by consolidating it, that is the caller which justifies the member.

**Finishing is decided on the reconciliation; retrying is decided on the verdict.** A plan can execute perfectly
and still be a plan for eight trips out of twelve, so a job is reported finished only on
`JobReconciliation.IsComplete`, which asks both questions: did every step come off, and did this plan cover the
whole job.

`HasUnfinishedWork` states a fact and never a recommendation, and it is **not** the complement of `IsComplete`.
A refusal that happened after targets were accepted carries their count, so it answers true and a loop keyed only
on it spins for ever on a verdict that will never change. A refusal that happened *before* any target was accepted
- no identity, no area, a stale epoch, an unreadable area - accepted nothing and left nothing, so it answers
false, and `IsComplete` answers false too. Both false at once is a real state and it is not a contradiction: it
means no plan was ever made. There is deliberately no third boolean for it, because the verdict already says which
of the two happened, and a role that reads the verdict never has to ask. This paragraph originally claimed a
terminal refusal always leaves work outstanding; an independent review showed that is false for the four
pre-target refusals, and the code was right.

**A parameter that makes a claim never carries a default.** `leftForAnotherRound` defaulting to zero says the plan
covered the whole job; `carrying` defaulting to empty says the NPC is holding nothing this job may spend. Neither
is checkable here - what is actually held is the custody ledger's answer - so a call site that stays silent is not
omitting a detail, it is asserting something it was never asked. Both were silent once and both produced the same
failure, a job reporting itself finished with targets untouched. Two validator rules keep it that way:
`check_npc_planning_never_defaults_a_claim`, and `check_npc_planning_decides_nothing_to_do_once`, which holds the
finish verdict to a single decision site because it has had three separate ways in.

## 8. An interruption never duplicates and never loses

The full design is in [INTERRUPTION.md](INTERRUPTION.md); three things about it belong here, because they constrain
everything the role leaves do next.

**A caller may act only on the phase the record already says it is in.** `NpcPlanRun` writes each phase down and
adopts it only if the write succeeded, so the record is always at or ahead of the world and the last written record
is always a safe resume point. A caller that cannot record cannot act.

**The two transitions that move a player's material are written twice** - an intent before the world is touched and
what was measured after - **and the library refuses the transition otherwise.** `NpcPlanRun` will not move a plan
into `Provisioned` or `Reconciling` unless this run wrote the pair, in the phase it is moving out of, with an outcome
somebody could establish. A record found mid-movement is never replayed and never discarded: it becomes uncertain,
and uncertain stops the plan for a person with the evidence. That is the whole of "no path duplicates a resource and
no path silently loses one".

That sentence used to describe the test fixture rather than the library: `MovesMaterialToReach` had no callers,
replacing its body with `false` left every test green, and a run could reach `Reconciling` having written neither
half of either pair. The enforcement is per run and deliberately not durable - a durable flag would be a field this
library demanded inside a format the role owns - so a plan resumed from the disk intends and concludes again before
claiming a material-moving phase. That costs two writes and moves nothing, because concluding measures.

**The same rule binds the load and the endings.** `Carried` only ever changes as the recorded outcome of a movement
that was announced first, because in-phase writes skip the transition rule and would otherwise have been able to
rewrite what the NPC is holding in any phase with no intent at all. And a movement with no recorded outcome may not
be written into an ending at all except `NeedsAttention`, nor have its pending custody dropped by a phase change: a
plan reporting itself `Settled` or `Refunded` over an open question is a plan that silently lost or duplicated a
player's material and then reported success. `NpcPlanRecovery.AlreadyOver` answers `NeedsAttention` for any terminal
record whose custody is not `Clear`, as the second line of defence for a record written by something else.

One deliberate exception to "an ending is an ending" comes with that: a terminal record whose custody is not `Clear`
may be moved to `NeedsAttention`, and only there, so the answer can be written down once instead of being re-decided
on every load. It withdraws a claim of success and grants nothing, and a plan whose custody is `Clear` is untouched.

**A record in no phase at all is unreadable, not a plan.** `NpcPlanJournal` refuses to hand one back, the recovery
path answers `NeedsAttention` for one it is handed, and `NpcPlanRun` refuses everything over it except a stop for a
person - which is available above every other rule, for any plan that has not ended, because a library that can hold
a state and cannot hand it to a person has a failure nobody is ever told about.

**One precondition the role leaves inherit.** A plan that stops for a person has nowhere to go: there is no
resolution UI, the custody ledger's `CloseOpenIntents` is joined to no plan, and nothing creates the plan that
follows. #380, #381 and #382 each have to bring a resolution path, hold no material through this library, or say
plainly that a job can end in a state only deleting its file clears.
[INTERRUPTION.md](INTERRUPTION.md) §9 states it as a precondition rather than an aside.

**Durable plan state owns no format and no path.** The role hands in an absolute path and a codec; the library hands
over lines and takes lines back, and stamps an unknown world epoch on anything read from disk so that every
in-session key a reconstructed plan holds is stale rather than dangerous. No purpose token was added to
`INpcDataPaths` - the journal takes the path directly - so that interface's "there are no purposes yet" is still
true, and adopting this is a code move rather than a migration.

Priority and arbitration live in the same area: seven rungs, strictly-higher-wins, and an interruption that changes
the phase and the note and nothing else about the work.

## 9. The leaves

| Issue | Leaf |
|---|---|
| #372 | lifecycle and persistence, moved without losing anybody |
| #373 | camp awareness: anchor, structural cluster, perimeter |
| #374 | NPC-enabled containers, off by default |
| #375 | the work-area contract, without depending on Cartographer |
| #376 | inventory, custody and reservations with stable ids |
| #377 | the job planning pipeline: understand the whole job before acting |
| #378 | the local route planner: visibly sensible, not optimal |
| #379 | interruption, reload and death: revalidate, never duplicate |
| #380 | Thorstein builds a shelter, planned and provisioned in batches |
| #381 | Gunnar collects and hauls, in planned batches, without portals |
| #382 | Sunniva: the quest, the move-in, and one planned maintenance round |

## 10. Status

The package exists, builds and ships nothing yet, but it is **no longer consumed by nobody**: two of five products
take it today - `ConcernedSteward` and `ConcernedTeamster` both carry the `ProjectReference` and the matching
`BepInDependency`, so a missing library is a load-time dependency error rather than a null reference at the first
call. `ConcernedForeman` has not adopted it yet, and that adoption is the seam the arbiter-mediates-mode work exists
to cross.

One role has been moved onto it in part: the Steward drives `NpcJobDriver` for its maintenance round. Gunnar's
collection job exists and is not yet constructed by anything.

Interruption and recovery (#379) is the same shape: the mechanism exists and is proved, and **nothing constructs an
`NpcPlanRun` yet**. No product persists a plan today, so "a plan survives a reload" is a statement about the library
and its tests, not yet about a session. The plan journal and the custody ledger agree in shape and are not joined;
joining them belongs with the first role that has both a plan and a transfer.

Nothing in this document has been observed in game, and every gameplay row for this program is OWNER GO-AROUND
PENDING. That includes the two adoptions above: the dependency wiring is proved by the validator, not by watching an
NPC work.

**Interim:** the package icon is the Concerned Cat badge cropped from an existing product icon, where it exists at
55x58 pixels. It is soft at 256x256 and should be replaced with the owner's own logo file before any package is
published. Nothing is published from this repository without the owner saying so.
