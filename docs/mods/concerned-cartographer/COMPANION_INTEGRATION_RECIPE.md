# Adding a companion to the next product

How a Concerned Cat product adopts the shared Concerned Companions layer. This
is the recipe Cartographer itself followed in CC-NPC-003 through CC-NPC-005;
every file named here exists and can be read as a worked example.

**The product identity for the next mod is Concerned Foreman** (#270). Nothing
in this recipe assumes it — every name below is a parameter — but the recipe is
written so that adopting it is an afternoon rather than a project.

---

## 0. What you are adopting, and what you are not

The shared layer is **source-shared, not a package**. There is no assembly to
reference, no dependency to declare and no load order to negotiate. You add one
line to your `.csproj`:

```xml
<Compile Include="..\Shared\Companions\**\*.cs" LinkBase="Companions" />
```

and the same line to your test project. Everything under
`src/Shared/Companions` is `internal` and compiles into *your* DLL. Products
never reference each other; `tools/validate_repo.py` enforces that.

What the layer gives you: stable identity, a total and monotonic quest state
machine, atomic scope-addressed persistence with real recovery, an unlock
policy biased hard towards the player, a bounded placement planner, and dialogue
rotation with repetition suppression.

What it deliberately does **not** give you: anything that touches Unity. It has
no `using UnityEngine`, which is exactly why the whole recovery surface can be
exercised against a temp directory in unit tests rather than reasoned about.
Every game-facing part is yours to write, against the interfaces below.

---

## 1. Declare the product (≈40 lines)

One file, no game types. Cartographer's is
`src/ConcernedCartographer/Domain/Companions/CartographerCompanions.cs`.

```csharp
internal sealed class ForemanCompanions : ICompanionProduct
{
    public static readonly ProductId Product = new ProductId("concerned-foreman");
    public static readonly CompanionId Companion = new CompanionId("<slug>");
    public static readonly QuestId Introduction = new QuestId("<slug>");

    public ProductId Id => Product;
    public IReadOnlyList<CompanionDefinition> Companions => …;
    public IReadOnlyList<QuestDefinition> Quests => …;
}
```

Slugs are `[a-z0-9-]`, no leading, trailing or doubled dashes, ≤48 characters;
`IdentitySlug` rejects anything else at construction rather than at save time.

**Do not invent a companion's name or story here.** Cartographer's came from the
owner. A product without one registers a quest and no companion, or waits.

---

## 2. Implement four adapters

Each is small, each is capability-checked, and each has exactly one job.

| Adapter | Job | Cartographer's |
|---|---|---|
| Scope | world + character → `CompanionScope` | `Runtime/Companions/CompanionScopeSource.cs` |
| Legacy evidence | "has this installation been used before?" | `Runtime/Companions/CartographerLegacyProbe.cs` |
| Anchor | `IAnchorSource` — where home is | `Runtime/Companions/SpawnAnchorSource.cs` |
| Placement | `IPlacementProbe` — could something stand here? | `Runtime/Companions/WorldPlacementProbe.cs` |

The scope and anchor adapters are **copyable almost verbatim**: they use
`ZNet.GetWorldUID()`, `PlayerProfile.GetPlayerID()`,
`HaveCustomSpawnPoint()`/`GetCustomSpawnPoint()` and the world start location,
none of which is Cartographer-specific. So is the placement probe.

The legacy probe is the one you must write yourself, because only your product
knows what its own old data looks like. Two rules:

1. **Gather facts, decide separately.** Cartographer's probe reads file
   *existence* only and hands a `LegacyEvidenceFacts` struct to a pure
   `LegacyEvidenceRule`, so every combination is unit-tested without a
   filesystem.
2. **Never answer from `ListSidecarFiles()`.** It returns every file under the
   root — every product, world and character — so answering "is this an existing
   player" from it grants *your* tools on *another product's* history. Because
   the resulting grant is monotonic, that mistake is permanent and silent. Use
   `HasSidecarFor(scope)`, or the product-scoped listing overload.

---

## 3. Open one `CompanionProgress` per character

```csharp
var store = new CompanionSidecarStore(<your product's config directory>);
CompanionProgress progress = CompanionProgress.Open(
    store, scope, questId, legacyEvidence, toolsOnlyPreference);
```

That object owns the ordering rule the whole layer exists to protect:
**progress reaches disk before its presentation is removed.** You get
`IsUnlocked`, `QuestState`, `ShouldPresentCollectible`, `Advance(transition)`
and `TryRetirePresentation()`.

Three things to know, each of which cost somebody a review finding:

- `Advance` returning `Advanced` means the state moved **in memory**. Check
  `HasUnsavedChanges` before firing any one-time side effect, and show `Notice`
  if it is set.
- `TryRetirePresentation()` returning true is your signal to remove the
  collectible — and it only returns true once the flag has reached disk. If it
  returns false, leave the collectible exactly where it is.
- A damaged row in an otherwise valid sidecar **does** count as prior use and
  grants access. Only `Missing` and a clean `Loaded` do not.

---

## 4. Build the collectible and the introduction

Copyable patterns rather than shared code, because they are UI:

- `Runtime/Companions/BrokenCompassObject.cs` — a `Hoverable` + `Interactable`
  `MonoBehaviour` with **no `ZNetView`**, on a layer that vanilla already treats
  as non-solid for characters.
- `Runtime/Companions/LocalVisual.cs` — render-only copies of vanilla prefabs.
  **Reusable as-is.**
- `Map/CompanionStoryPanel.cs` — a paged, keyboard/controller-readable panel
  that releases its input block on every exit path.
- `Runtime/Companions/CompanionActor.cs` — the extraction that produces an
  animated body with no networking or AI. **Reusable as-is.**

### The rule that must not be reinterpreted

Never instantiate a live `Player` or `BaseAI` and strip components afterwards.
`BaseAI` registers itself into a process-wide static list the moment it wakes
and `ZNetView` creates a ZDO, so by the time anything could be stripped it has
already happened. Instantiate under an **inactive** holder, extract the visual
subtree, destroy the remainder — and if a networking or AI component turns out
to be *inside* the extracted subtree, **refuse the candidate** rather than
cleaning it up. That is what keeps "a finished actor has never contained one of
those" an auditable property rather than an intention.

---

## 5. Gate only what your product added

Cartographer's gate (`CartographerRuntime.AllowFeature`) covers the affordances
it invented. It does **not** cover its settings, its privacy controls, its
backup and support tooling, its console commands, the vanilla game, or anything
belonging to another mod.

Settings must stay ungated, because Settings is where Tools-only lives. Gating
it makes the gate unescapable, and an unescapable gate on a shipped utility is
the worst outcome this design can produce.

Ship **two** ways to reach the tools without meeting the companion: a visible
control on an ungated surface, and a console command. Cartographer has the
Companions section of the Settings panel and `cc_companion toolsonly on`.

---

## 6. Write the dialogue

`DialogueCatalog` + `DialogueRotation` + `DialogueContext` are shared; the lines
are yours. Cartographer's are in
`Domain/Companions/HulgiDialogue.cs` (keys and gates) and `AtlasStrings` (text),
so a translator replaces the companion in the same file as the buttons.

- Seed `DialogueRotation` with a **per-character** offset so two characters do
  not hear the same order.
- Gate any place-specific line behind `RequiredBiome`, and build the
  `DialogueContext` from the **local** character only. Another player's travels
  must never put words in your companion's mouth — a spoiler cannot be taken
  back, so `DialogueContext.Empty` is the correct failure.
- Ambient chatter **off by default**. Speaking to the companion always works.
- Deliver lines as HUD messages, not panels. "No modal interruption during
  combat, loading or death" is satisfied by never being modal.

---

## 7. What the tests must cover

Cartographer's companion tests are in `src/ConcernedCartographer.Tests/Companions/`.
The cases that matter are the ones a playtest takes an hour to reach:

fresh player · existing player on upgrade · a grant surviving its evidence
disappearing · corrupt sidecar (grants, explains, and the grant survives into
the next session) · duplicate discovery · double interaction · interrupted
introduction resumed after a reload · a second character in the same world ·
tools-only on and off again · an unloaded bed not counting as a destroyed bed ·
nowhere to place · a missing API · the visibility toggle · scene teardown.

---

## 8. What you owe before claiming it works

Nothing in the shared layer, and nothing in this recipe, is in-game evidence.
Cartographer ships `cc_companion status` and `cc_companion appearance`
specifically so the open questions get answered by observation: which prefab a
visual came from, whether the collider landed on a non-solid layer, whether
vanilla hovering ever reached the object, which animator state was used, and
what spellings this build actually puts in `Player.m_knownBiome`.

Build the equivalent for your product. A unit test total is not a screenshot.
