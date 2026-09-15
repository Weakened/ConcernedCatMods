# Designation and recruitment — the four explicit acts

Issue: [CF-SET-004 (#281)](https://github.com/Weakened/ConcernedCatMods/issues/281).
Parent: [#273](https://github.com/Weakened/ConcernedCatMods/issues/273).
Authority decisions this inherits: [`SETTLEMENT_AUTHORITY.md`](SETTLEMENT_AUTHORITY.md).
Graph: [`FIRST_COTTAGE_GRAPH.md`](FIRST_COTTAGE_GRAPH.md).

Everything about the installed game below was read from the **assemblies
installed on this machine**, by decompilation and metadata inspection only. No
game code was executed, no game bytes were copied, and no assets were exported.

## Audited build

| Item | Value |
|---|---|
| Game version | **1.0.12** |
| `assembly_valheim.dll` | SHA256 `27a766a8d23a7bd8b6a54fb9ad0452a96c305fb3629b39c40527c09a1c393a84`, 2 568 192 bytes |
| Method | `ilspycmd` 11.0.0.9375 decompilation of the shipped assembly |

Byte-identical to the binary [`WORKER_ACTOR_SPIKE.md`](WORKER_ACTOR_SPIKE.md)
audited, so both documents describe the same build.

---

## 1. What the leaf delivers

Four acts, each of which a person performs deliberately and can see the result
of:

| Act | Command | What it records |
|---|---|---|
| Mark the settlement | `cf_settle area <radius>` | a circle centred where you stand |
| Mark the harvest ground | `cf_settle harvest <radius>` | a second circle, which may be elsewhere |
| Designate the supply chest | `cf_settle supply` | the chest **you are looking at**, by its own identity |
| Recruit a worker | `cf_settle recruit [name]` | one ordinary labourer on the roster |

and one act that takes any of them back: `cf_settle clear <area|harvest|supply>`.

`cf_settle status` shows everything marked, everybody employed, whether the
record is writable, and any repair the journal is holding.

The decision-making is entirely game-free, in `src/Shared/Settlement`
(`Designations/`, `Recruitment/`, `Register/`, `Storage/`). Foreman supplies two
things and nothing else: the ward answer, and which chest you are looking at.

---

## 2. Nothing is designated implicitly

This is a property of the types, not a promise in a comment.

`DesignationBook` has no constructor, method or field that takes a position and
works out what was meant. A designation exists only because `Designate` was
called with an explicit kind, explicit geometry and an explicit ward answer. A
worker asking *"may I fell this tree"* gets its answer by **reading** the book;
nothing it does can write to it.

Three consequences follow, and each has a test:

- **With nothing marked, nothing is allowed.** `IsInHarvestArea` and
  `IsSupplyContainer` both answer `false` for an unmarked settlement. "Nothing
  marked" never reads as "anywhere".
- **Re-marking the same thing is idempotent**; re-marking the same *kind* a
  *different* way is **refused** and names the one that exists. Silently moving a
  player's settlement is a designation the player did not make.
- **The supply chest is chosen by looking at it.** Not by being nearest — that
  is the inference from proximity this leaf forbids, and it is also the
  behaviour that would re-point a settlement's supply at a chest somebody built
  later.

A chest is remembered by its **`ZDOID`**, not its position. `ZDO.m_uid` is a
public `ZDOID` field in this build and `ZDOID` exposes `public long UserID` and
`public uint ID`; a ZDO's id is assigned at creation and travels with the object
in the world save, which is what makes a designation re-readable after a relog.
Resolving by position would follow whatever ends up standing there after a chest
is destroyed and rebuilt.

---

## 3. The ward gate, and the refusal it forces

`PrivateArea.CheckAccess` is confirmed present in this build by signature:

```csharp
public static bool CheckAccess(Vector3 point, float radius = 0f,
                               bool flash = true, bool wardCheck = false)
```

With `wardCheck: true` it starts from *allowed* and denies only when an
**enabled** ward overlaps and `HaveLocalAccess()` is false — exactly "somebody
else's ward covers this". Overlap is

```csharp
Utils.DistanceXZ(ward.transform.position, point) < ward.m_radius + radius
```

so passing the designation's own radius asks the right question: do the two
circles meet. `flash: false` is passed deliberately — a designation check must
not make every nearby shield flash.

**The part that forces a refusal.** `CheckAccess` iterates `m_allAreas`, a static
list populated by `PrivateArea.Awake` and emptied by `OnDestroy`. It therefore
contains only wards whose objects are **loaded**. A ward on unloaded ground is
not absent from the world — it is invisible to this check, and answering
"granted" from a list that cannot see it is precisely the silent skip the
authority ADR forbids.

So `WorldDesignationSite` refuses unless the ground around the designation is
loaded, with a margin of one full zone (64 m in this build,
`ZoneSystem.m_zoneSize`) because a vanilla ward reaches 32 m from an object that
may sit in the neighbouring zone. Anything it cannot establish answers
`AreaAccess.Unavailable`, and **`Unavailable` is zero** — an adapter that threw,
forgot, or was never wired refuses by default rather than granting by default.

The same reasoning sets the maximum radius. `DesignationBook.MaxRadius` is 48 m,
chosen against the ward check rather than against taste: past that size an area
routinely reaches outside loaded ground, so a larger designation would spend
most of its life being refused as unanswerable. Refusing it up front, by size, is
the honest version of the same answer.

### What is deliberately not re-checked

Marking is a record of intent. A ward raised **after** a designation does not
retroactively un-mark ground — re-running the checks at load would mean a ward
built next door, or a world not finished loading, quietly deleted a settlement
the player still has. Legality at the instant of *acting* is CF-SET-003's gate
and is re-checked at the moment a piece is placed.

---

## 4. Taking a designation back

`cf_settle clear` is split in two: work out what would happen, then do it. A
player is shown *"this cancels 1 unfinished order and returns 20 Wood"* before
anything moves, and a clear that would cost something must be confirmed with a
second word.

The dependency rules are narrow, and each has a reason:

| Cleared | Cancels |
|---|---|
| **Supply container** | every unfinished order that **ever drew from it** — dependency is provenance, not current holdings, because an order that already spent what it drew is still an order whose only source just went away |
| **Harvest area** | orders that are actively `Gathering`, and nothing else. An order that already has what it needs is not affected by losing a source it is no longer using |
| **Settlement area** | the other two as well (they belong to it), and then every remaining unfinished order |

Two things are never touched:

- An order in **`NeedsRepair`** is left exactly as it is. Its material state is
  unknown, and tidying it away as part of clearing some ground is precisely the
  guess #273's gate 3 forbids.
- A reservation that is not **`Held`** is not refunded. It has already been
  spent, already been returned, or is the unknown half of an interrupted commit.

Refunds are written to the journal **before** the designation goes away, so a
crash mid-clear replays to a coherent state: material back in the container it
came from, then the order marked cancelled. The refund goes through the existing
`CustodyLedger`, which is keyed by request id and idempotent, so a refund cannot
be taken twice. Applying a plan a second time is refused as **stale** rather than
replayed.

**Nobody is dismissed by clearing ground.** Letting somebody go is its own
explicit act (`cf_settle dismiss`), for the same reason nothing is designated
implicitly.

---

## 5. Recruitment, and the half of the companion contract that fits

The roster record — not the creature — is what "recruited" means. A body can be
killed, despawned or lost to a zone unload, exactly as a worker's inventory can;
a roster that emptied itself when a creature vanished would make *"is this
settlement staffed?"* depend on which zones happen to be loaded.

That is the half of the companion unlock contract that fits: **access, once
granted, is monotonic and survives the actor not existing.**

The half that does **not** fit is that policy's deliberate bias towards granting
on ambiguous evidence. Settlement authority fails closed, so an unreadable roster
**refuses new recruitment** rather than assuming it. Adopting the companion
contract wholesale would have quietly inverted a safety rule, so it was adopted
in part and the part is named.

`WorkerRoster.MaxWorkers` is **1**. #273's first playable outcome is one cottage
and one resident; a second worker is a scaling question with no measurement
behind it anywhere.

### What is not joined up yet

Being on the roster and having a body in the world are **different things, and
this build does not join them**. `cf_settle recruit` writes the record;
`cf_worker spawn` puts a body somewhere. `cf_settle status` says so rather than
implying a worker is standing around when none is.

This is deliberate and not an oversight: CF-SET-002's two live-observation
criteria are still owed, and their first-play steps in
[`WORKER_ACTOR_SPIKE.md`](WORKER_ACTOR_SPIKE.md) §7 spawn a worker with only the
runtime enabled. Gating `cf_worker spawn` on a roster entry would invalidate
written steps nobody has run yet. Binding the roster to the live actor belongs
with the leaf where a worker first takes an order.

---

## 6. Persistence

Two files per settlement, under
`BepInEx/config/ConcernedCatMods/ConcernedForeman/settlements/`:

| File | Holds | Shape |
|---|---|---|
| `<world>.<settlement>.settlement-register.tsv` | what is marked, who is employed | **current state**, rewritten whole |
| `<world>.<settlement>.settlement.tsv` | what moved | **append-only history** |

They are separate because they answer different questions and fail differently.
A damaged register costs the player their markings; a damaged journal costs the
answer to "whose wood is where". Neither can make the other unreadable.

Both use the same temp-file-and-swap (`AtomicTextFile`, extracted from
`JournalStore` when this leaf needed a second copy of it), so an interrupted
write leaves either the whole old file or the whole new one.

**A damaged record is never quarantined and replaced with an empty one.** That
rule is the journal's, deliberately, and not the companion sidecar's: a companion
sidecar holds progress through a story, while this holds which chest a worker is
allowed to take from, and quietly forgetting that is how a settlement ends up
drawing from the wrong container. Unreadable, newer-schema, wrong-scope and
partially-damaged records all go **read-only** — what was read is kept and shown,
nothing new is marked, and the file is not written over.

Floats round-trip with `"R"` formatting. A designation that moved by a fraction
of a millimetre across a save would stop matching itself, and re-marking it
would stop being idempotent and start being refused as "already marked
differently".

One settlement per world, id `home`. `SettlementScope` already carries a
settlement id so a world can hold several and they stay isolated; marking one is
a first-proof bound, not a structural limit.

---

## 7. First-play steps — the live evidence this leaf owes

**Nothing below has been observed.** Every green result recorded for this leaf is
a unit test, a static check or a metadata audit. These steps are what turns the
acceptance criteria into evidence, and they are owner-gated.

**Before anything:** use a **disposable world and character** on a profile
refreshed against 1.0.12. `TCC-Dev`, `TCC-Compat`, `TCC-v1-Smoke` and
`TCC-v1-Smoke-RC2` all still log **0.221.12** from August — testing on a stale
profile will mislead. Foreman deploys to its own `TCF-Dev` profile
(`FOREMAN_DEPLOYPATH`), which does not exist yet.

Enable the runtime: `BepInEx/config/…ConcernedForeman.cfg` →
`[Settlement] SettlementRuntimeEnabled = true`. It is off by default.

| # | Do | Expect |
|---|---|---|
| 1 | `cf_settle status` before marking anything | "Nothing is marked", and it says nothing is inferred from where you stand |
| 2 | `cf_settle area 3` | refused, naming the allowed size range |
| 3 | `cf_settle area 24` standing in the open | marked, echoing the centre and radius |
| 4 | `cf_settle area 24` again, same spot | "Already marked, exactly like that… Nothing changed" |
| 5 | `cf_settle area 30` | **refused**, naming the 24 m one and telling you to clear it first |
| 6 | Build a workbench ward, or find one you do not own, and `cf_settle area 24` inside it | **refused**, naming the ward — at designation time, not at build time |
| 7 | Walk to the edge of loaded ground and `cf_settle area 48` reaching into unloaded terrain | **refused** because the check could not be made — *not* granted |
| 8 | `cf_settle harvest 20` in a forest away from the settlement | marked |
| 9 | `cf_settle supply` while looking at nothing | refused, telling you to look at a chest |
| 10 | `cf_settle supply` while looking at a chest **outside** the settlement area | refused, naming the reason |
| 11 | `cf_settle supply` while looking at a chest **inside** it | marked, showing the chest's identity |
| 12 | `cf_settle recruit` | one labourer recruited |
| 13 | `cf_settle recruit` again | "already on the roster. Nothing changed" |
| 14 | `cf_settle recruit second-hand` | refused: one worker at a time |
| 15 | **Quit to the main menu and reload the world.** `cf_settle status` | all three designations and the worker are exactly as they were |
| 16 | Destroy the designated chest, place a new one in the same spot, `cf_settle status` | the designation still names the **old** chest — it did not silently follow the new one |
| 17 | `cf_settle clear supply` | it is cleared (no orders exist yet, so it costs nothing) |
| 18 | `cf_settle clear area` | the harvest area and any supply designation go with it; the worker stays employed |
| 19 | `cf_settle dismiss` | the worker leaves the roster — a separate, deliberate act |
| 20 | Load a **different** world and `cf_settle status` | nothing marked; the first world's settlement is not inherited |

Steps 17 and 18 cannot yet show the interesting half. **No leaf creates an order
or reserves material**, so clearing currently cancels nothing. The cascade is
real and tested game-free — cancellation, one-time refund, `NeedsRepair` left
alone, stale plans refused — but the live confirmation of *"clearing the supply
container returned my wood"* has to wait for CF-SET-006 (#283). That is a gap in
the live evidence, not in the implementation, and it is named here rather than
being quietly skipped.

---

## 8. What is not covered by a test

The game-free core is exercised exhaustively. The Foreman adapter is not, and
cannot be from the game-free project: `WorldDesignationSite`, `SettlementTargets`
and `DesignationTools` all reference Unity and game types, and
`Shared.Settlement.Tests` deliberately references no product and no engine.

So these are only proved by §7:

- the ward call's arguments and the loaded-ground sampling;
- resolving the chest you are looking at, and its `ZDOID`;
- argument parsing, the confirmation gate on a costly clear, and dropping a
  pending plan when a new designation makes it stale.

The last of those is the one worth naming: if the confirmation gate were wrong,
a clear could happen a word early. The damage is bounded by the core — an
out-of-date plan is refused as stale by `ApplyUndesignation` rather than applied
— but the gate itself is adapter logic and has no automated test.
