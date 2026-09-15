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
