# Concerned Companions: shared architecture

Epic: [#264](https://github.com/Weakened/ConcernedCatMods/issues/264).
Source: `src/Shared/Companions`. Namespace: `TheConcernedCat.Companions.*`.
Every type is `internal`.

This layer is **source shared, not assembly shared**. A product adds one line to
its `.csproj` and compiles the same sources into its own DLL:

```xml
<Compile Include="..\Shared\Companions\**\*.cs" LinkBase="Companions" />
```

There is no runtime package, no new DLL in any ZIP, and no product-to-product
reference. It is also not mandatory: a product that wants none of this simply
does not add the line.

## What lives here and what does not

The shared layer owns the *machinery* of having a companion: identity, quest
progress, storage, access decisions, where to sit, and what to say next. It owns
none of the *content*: no names, no dialogue text, no appearance, no prefabs.
Those belong to the product, in its own localization and content files, which is
why two products can ship completely different characters without either one
knowing the other exists.

Nothing here touches Unity, BepInEx, or Jötunn. That is the constraint that lets
the entire layer be unit-tested with no game installed, and it is enforced by
review plus `tools/validate_repo.py`.

## Components

```text
Identity        ProductId, CompanionId, QuestId, CharacterId, WorldId, CompanionScope
Definitions     CompanionDefinition, QuestDefinition, ICompanionProduct, CompanionRegistry
Quest           QuestState, QuestTransition, QuestStateMachine
Persistence     CompanionSidecar, CompanionSidecarCodec, CompanionSidecarStore, CompanionQuestRecord
Unlock          LegacyEvidence, UnlockReason, UnlockDecision, UnlockPolicy
Placement       CompanionAnchor, IAnchorSource, IPlacementProbe, PlacementRules, PlacementPlanner
Dialogue        DialogueLine, DialogueCatalog, DialogueContext, DialogueRotation
Session         CompanionProgress   (the seam a game adapter talks to)
```

## The four decisions worth knowing

### 1. Four concerns, deliberately separated

| Concern | Type | Depends on |
|---|---|---|
| Feature access | `UnlockDecision` | persisted grant, quest state, product evidence |
| Recruitment | `QuestState` | player actions only |
| Quest presentation | `ShouldPresentCollectible` | quest state and preference |
| Actor visibility | product setting | nothing above |

Access never depends on the companion existing. Hiding them, losing a bed,
dying, or failing to render them costs the player nothing. This is why the
unlock lives in its own sidecar row rather than being read off the quest stage:
a returning player is granted access *and* still gets to meet the companion.

### 2. Access is monotonic and biased toward the player

`UnlockPolicy` resolves every uncertain input to unlocked. Wrongly unlocking
shows a returning player tools they already had; wrongly locking takes tools
away from somebody who has been using them for months because a file moved. Only
one input produces a locked result: a fresh character with no evidence of any
kind.

The first grant decided from outside the sidecar is **written down**, and later
sessions read it back instead of re-deriving it. A deleted atlas, a changed
setting, or a quarantined file therefore cannot revoke anything.

The case that made this necessary: a corrupt sidecar is quarantined, so the
*next* session finds no file at all and would see a brand new player. The
session that saw the corruption is the only one that can record what it knew.

Access is granted **per scope, not per quest**. A product may ship more than one
companion, and opening a session for a second, untouched quest must not conclude
that a player who finished the first one is new. Presentation stays per quest, so
the second companion is still introduced to someone who already has the tools.

The tools-only preference **unlocks without finishing the quest**. Turning it on
and back off in one session restores the story, which is the same outcome as
having had it on when the session opened. Finishing the introduction by skipping
it is a separate, explicit action from the story UI.

### 3. Progress reaches disk before its presentation is removed

`CompanionProgress.TryRetirePresentation` refuses while anything is unsaved. A
build that removes the collectible first and saves second will, eventually,
remove it and then fail to save, leaving a player with no collectible, no
companion, and no way back into the introduction. If the process dies between
the two steps, the retire flag is still false and the removal simply runs again
next session.

### 4. Unknown data is carried, never dropped

Sidecars record their schema version and their own scope. A file from a newer
build is read-only; a file belonging to another character or world is refused
rather than merged or overwritten; unknown rows and unknown trailing fields are
re-emitted verbatim. Running an older build once cannot erase what a newer one
recorded.

Carrying an unknown row is not sufficient on its own. A **quest stage** from a
newer build makes the whole file read-only, because otherwise this build would
write its own row for that quest *before* the carried one, and the newer build
would read the older row first and adopt it — losing exactly the progress the
carry was meant to protect. A duplicate quest row is carried rather than dropped;
it is the one place the codec could destroy data instead of preserving it.

An unreadable file that could not be moved aside also makes the sidecar
read-only. The notice promises the old file was kept, so the next save must not
be able to overwrite it.

One consequence is easy to misread, so it is stated plainly here: **a damaged
row in an otherwise valid, scope-matching sidecar does count as prior use.** A
load outcome of `LoadedWithSkippedRows` goes through
`SidecarLoadOutcomes.IndicatesPriorData`, so it reaches `UnlockPolicy` as
"companion data unreadable", grants access, and persists that grant. Only two
outcomes do not count as prior use: `Missing`, and a clean `Loaded`. This is the
player-favouring direction the whole layer is built around — a file that exists
but cannot be fully read is still a file somebody's game wrote — but it is
tighter-sounding than it is, and a successor touching `LegacyEvidence` should
know that before assuming garbage cannot earn an unlock.

### Asking the storage layer about one character

`CompanionSidecarStore.HasSidecarFor(scope)` is the call a product's
legacy-evidence probe should use. `ListSidecarFiles()` without an argument
returns every file under the root — **every product, every world, every
character** — and answering "is this an existing player" from it would grant one
product's tools on another product's history. Because the resulting grant is
monotonic, that mistake would be permanent and silent. There is a product-scoped
overload for the cases that genuinely want a whole product.

## Storage

```text
<config>/ConcernedCatMods/<Product>/<product>.<worldHex>.<characterHex>.companions.tsv
```

Tab separated, line oriented, matching the repository's other sidecars. Writes
go to a temporary file and are swapped in whole, so an interrupted save leaves
either the old complete file or the new one. Unreadable files are moved to
`.corrupt` beside themselves, never deleted.

## Placement

`PlacementPlanner` sweeps deterministic rings around the anchor: fixed radii,
fixed angles, fixed order, hard bounded at 48 probes inside a 3-10 m band.
Determinism matters more than coverage - the same world state must put the
companion in the same place every session, or they appear to wander between
logins for no visible reason.

Anchors are the claimed bed or the default spawn, and nothing else. Portals,
death spots, and wherever the player is standing are places people pass through,
not places they live.

When nothing qualifies, the planner **defers** and reports what blocked it. It
never relaxes its own rules to produce an answer.

## Adding a second product

1. Add the `Compile` item to the product's `.csproj`.
2. Implement `ICompanionProduct` with the product's own `ProductId`, companions,
   and quests. Slugs only need to be unique *within* the product.
3. Register it, open a `CompanionProgress` per character, and answer
   `LegacyEvidence` for that product's own prior data.
4. Supply a `DialogueCatalog`, an `IAnchorSource`, and an `IPlacementProbe`.

`src/ConcernedCartographer.Tests/Companions/SyntheticProducts.cs` does exactly
this twice, with deliberately identical companion and quest slugs, to prove that
product scoping is what keeps two products apart.

> **Open question for the owner.** The name and scope of the next Concerned Cat
> mod are unresolved; the original ordered list after Cartographer and Teamster
> was not recoverable. The extension contract is therefore proved with two
> *synthetic* products in tests. No product identity has been invented, and none
> should be until the owner supplies one.

---

## How Concerned Cartographer adopts it (CC-NPC-003)

The shared layer is deliberately ignorant of Unity. Everything below is the
product's own half of the wiring, and it is split along the same line the test
project draws: `Domain/Companions/**` is game-free and unit-tested,
`Runtime/Companions/**` is the adapter that touches Valheim.

| Piece | Lives in | Job |
|---|---|---|
| `CartographerCompanions` | `Domain/Companions` | The `ICompanionProduct` registration: product `concerned-cartographer`, companion `hulgi`, quest `broken-compass`. |
| `CompanionStory` / `StoryReader` | `Domain/Companions` | Four authored pages, and the clamped paging model the panel drives. |
| `CompanionFeatureGate` | `Domain/Companions` | Which of *this product's added* affordances are available, and why. |
| `LegacyEvidenceRule` | `Domain/Companions` | Turns file-existence facts into the shared layer's `LegacyEvidence` answer. |
| `CompassProximity` / `IntroductionSequence` | `Domain/Companions` | Distance behaviour with hysteresis, and the exact order of quest transitions. |
| `CompanionScopeSource` | `Runtime/Companions` | `ZNet.GetWorldUID()` + `PlayerProfile.GetPlayerID()` → `CompanionScope`. |
| `CartographerLegacyProbe` | `Runtime/Companions` | Looks for prior Cartographer sidecars; gathers facts only. |
| `SpawnAnchorSource` | `Runtime/Companions` | Claimed bed, else the world start location resolved at runtime. |
| `WorldPlacementProbe` | `Runtime/Companions` | `IPlacementProbe` over `ZoneSystem` — loaded, solid, level, dry, clear. |
| `LocalVisual` | `Runtime/Companions` | Render-only copies of vanilla prefabs. Nothing from the source ever wakes. |
| `BrokenCompassObject` | `Runtime/Companions` | `Hoverable` + `Interactable`, no `ZNetView`, non-blocking collider. |
| `CompanionDirector` | `Runtime/Companions` | Owns all of the above and answers one question to the rest of the runtime. |
| `CompanionStoryPanel` | `Map` | The paged introduction. Keyboard, controller and mouse. |
| `CompanionToolsCommand` | `Runtime` | `cc_companion` — status, tools-only, replay, visibility, where, path. |

### The gate, and why it can barely close

`CartographerRuntime.AllowFeature` sits in front of the Atlas drawer, the
marker palette, Routes, Survey, Share, Quick Pin and the Pin Workbench — the
things this mod *added*. It is not in front of Settings, Privacy, the backup
and support tooling, the console commands, the vanilla map, or anything
belonging to another mod. That is not an oversight: Settings is where
Tools-only lives, so gating it would make the gate unescapable.

The gate opens for all of these, and only the last line closes it:

- companions switched off in config;
- world or character not identifiable (a fact about our adapters, never about
  the player);
- nothing resolved yet, because a player should never watch their toolbar
  appear a second late;
- any `UnlockPolicy` grant — a finished introduction, a Tools-only preference,
  prior Cartographer data in *any* world, profile-level data, unreadable
  companion data, or an ambiguous answer;
- otherwise: a genuinely new character, in an identified world, with
  companions on, who has not yet finished or skipped the introduction.

### Render-only construction

`LocalVisual` instantiates the source prefab **underneath an inactive holder**,
copies out `sharedMesh` and `sharedMaterials`, and destroys the clone before
anything is ever enabled. No component from the source prefab runs — which is
the only workable shape, because the audit found that `BaseAI` registers itself
into a process-wide static list the moment it wakes and `ZNetView` creates a
ZDO, so "instantiate and strip afterwards" is always too late. Materials are
read through `sharedMaterials` and never written, so the vanilla asset every
other object in the world is drawn with is untouched.

### Two ways to examine, one entry point

Vanilla's hover raycast is the intended path and `BrokenCompassObject`
implements `Hoverable`/`Interactable` for it. Whether that raycast reaches a
mod-made collider on this build is one of the compatibility note's open rows,
so the director also watches the Use key directly while the player is close
*and not already hovering something real*. Both paths call one idempotent
`Examine()`, and the quest transitions underneath are idempotent too, so
triggering both on the same frame still produces exactly one introduction.

### What is still pending live observation

Nothing in this slice claims in-game evidence. `cc_companion status` prints the
facts that close those rows — which prefab the visual came from, whether the
collider landed on a non-solid layer, whether vanilla hovering has ever
reached the object, and which name the world's start location answered to.

---

## Hulgi's presence (CC-NPC-004)

### Residency: three answers, not two

`PlayerProfile.HaveCustomSpawnPoint()` reports what the *profile* remembers, not
what the *world* contains — destroy the bed and the recorded position stays
behind. So `BedValidityProbe` asks the world, and its answer has three values:

| Answer | Meaning | Effect |
|---|---|---|
| `Valid` | loaded ground, a `Bed` is there | keep the bed |
| `Unknown` | the ground is not loaded, nothing can be checked | **keep the bed** |
| `Gone` | loaded ground, no bed in it | fall back to the world start |

`Unknown` keeping the bed is the load-bearing line. Collapsing it into `Gone`
would relocate the companion to the world's starting point every time the player
walked two biomes from home — the exact wandering the whole rule exists to
prevent. Only loaded-and-empty may move him off a bed a player claimed.

`ResidencyPlanner` then decides between doing nothing, placing, rehoming and
removing. Almost every case is "do nothing": a hidden companion keeps his home,
a momentary resolution failure leaves an existing actor standing, and a home
point that jitters below `AnchorMoveTolerance` is the same home point.

### The actor is extracted, never stripped

`CompanionActor.TryExtractVisual` instantiates the source prefab under an
**inactive** holder, re-parents the animated visual subtree out of the clone,
and destroys the remainder — which is where `Player`, `ZNetView`, `Character`
and `BaseAI` live — without any of it ever being enabled.

Then it does something the ordinary version of this does not: if a networking or
AI component turns out to be *inside* the extracted subtree, the whole candidate
is **refused** and the next one is tried. It is never removed and carried on
with. That keeps a property true and auditable rather than merely intended: a
finished actor has never contained one of those components.

Colliders and rigidbodies are treated differently and deliberately so. A
collider carries no registration — nothing knows about it until something
touches it — so removing one leaves no trace, while leaving one in place would
mean a player walking into an invisible wall where their companion stands.

### The body is assembled dark

Extraction is only half of the no-wake rule, and the other half was missing
until production reported it. **Re-parenting is what wakes a subtree**: the
instant the visual transform lands under an active parent, Unity runs `Awake`
on every component inside it. The new root used to be created the ordinary way,
which is to say *active*, so the extracted body woke the moment it was
re-parented into it — and the second line of `CharacterAnimEvent.Awake` is a
dereference of `GetComponentInParent<Character>()`, which the refusal above has
just guaranteed is not there.

So it threw. Every build, for every player, since Hulgi had a body
(`CONCERNED-CARTOGRAPHER-8`, seen on 1.1.2). Worse than the log line: that
component's `OnEnable` adds it to a static list `MonoUpdaters` walks every
FixedUpdate and LateUpdate, so a half-constructed one does not merely fail once
— it sits in the game's own update loop for the rest of the session.

`CompanionActor` now creates the root **inactive**, re-parents into the dark,
removes the source character's own `MonoBehaviour`s while nothing has run, and
switches the light on afterwards. Removing is not stripping: the distinction
the whole adapter rests on is *whether the component ever woke*, and none of
these did.

The rule is a type test rather than another name list, because the problem is
not one class: a script inside a character's visual subtree is game code
written for a live character, and this figure has none. Nothing that draws him
is a `MonoBehaviour` — `Animator`, `Renderer`, `SkinnedMeshRenderer`, `LODGroup`
and `Transform` are all built-in components — so the pass cannot take away the
body, the rig or the animation. Anything Unity refuses to destroy (a
`[RequireComponent]` dependency, which it refuses by writing to the log rather
than by throwing) is disabled instead and named in the log line, so that case is
visible rather than silent. Attached hair, beards and garments go through the
same pass before they are parented on, for the same reason — except for the
cloth family, which `RemoveCloth` already disables deliberately, precisely
because Unity refuses to destroy it out loud.

### Appearance is enumerated, never assumed

The audit established that stock hair and beard preset names are serialized
values inside compressed asset bundles, not API, and refused to guess them.
`AppearanceCatalog` reads the live prefab tables at runtime, `AppearancePlan`
picks from what is really there, and the chain ends in "no item at all":

1. a preferred name (matched case-insensitively as a substring — the game's own
   spelling is precisely what could not be established);
2. any member of the same family, which still looks deliberate;
3. nothing, and the model keeps its own look.

A missing preset changes how Hulgi looks and nothing else. It cannot fail his
construction, and it certainly cannot touch the introduction or a player's
tools. `cc_companion appearance` prints what this build actually has, which is
how the audit's two open appearance rows get closed by observation.

The seated idle works the same way: every candidate animator state is checked
with `Animator.HasState` before use, and when none exists the model keeps its
default idle and the report says so.

### Furniture: detected, reported, deliberately unused

The placement probe finds chairs and asks `Chair.IsInUse()`. A free chair is
reported to the shared planner as `SeatAvailability.Unverified`, **not** `Free`
— and the planner treats `Unverified` as no seat, so the companion sits on the
ground beside the chair rather than standing inside it. `cc_companion status`
says "free seat detected nearby — furniture use is PENDING in-game evidence".

That is the honest position. The audit could only confirm that
`Chair.m_attachAnimation` is a per-prefab string field; what it contains, and
whether a companion posed on a real chair lands correctly, is unobserved. No
seat is ever claimed and no attachment message is ever sent.

### Scope staleness

Following the review: the scope is resolved once and then written to on every
transition, and the store's scope-mismatch guard only catches a *file* that
disagrees, not an in-memory scope gone stale because a world change slipped past
its hook. So every ten seconds the director compares the live world UID and
player ID against the scope the open sidecar is addressed to, and reopens if
they differ. Two accessor reads; no progress is ever written to a previous
world.
