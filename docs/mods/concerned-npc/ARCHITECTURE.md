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

**Rewritten rather than moved:** the Steward's separate recruitment implementation, which folds into the shared
machine, and the fused collection loop, from which the task runner is extracted while survey, select, pick and
deliver stay Foreman's.

## 6. The behaviour changes that must not ride along inside a refactor

Four differences between the copies are semantic, not stylistic, and each costs an existing player something. Each
needs its own issue and its own in-game proof:

1. A correct four-way bed read replaces a two-way one, which changes which beds Hulgi will sleep in, in every existing
   save. Both predicates ship; switching Cartographer over is a separate decision.
2. A stricter container gate closes a permission hole and will make some already-working Steward depots refuse until
   a ward or privacy setting is fixed. It needs a player-facing notice that names the fix.
3. The unified census changes what "duplicated" means for each product.
4. The unified anchor changes which bed counts as home when a player has several.

## 7. The leaves

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

## 8. Status

The package exists, builds, ships nothing, and is consumed by nobody yet. No role has been moved onto it. Nothing in
this document has been observed in game, and every gameplay row for this program is OWNER GO-AROUND PENDING.

**Interim:** the package icon is the Concerned Cat badge cropped from an existing product icon, where it exists at
55x58 pixels. It is soft at 256x256 and should be replaced with the owner's own logo file before any package is
published. Nothing is published from this repository without the owner saying so.
