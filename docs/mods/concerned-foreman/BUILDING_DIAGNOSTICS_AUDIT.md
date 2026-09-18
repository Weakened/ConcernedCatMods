# Concerned Foreman — building-diagnostics audit and the CF-002 go/no-go

**Issue:** #270 (CF-001). This closes the three obligations `PROJECT.md` recorded
as outstanding: current pain signals with primary-source descriptions of the
tools that already exist, a narrow installed-game audit of the four subsystems,
and the CF-002 go/no-go test written down before CF-002 starts.

**Audited build:** Valheim 1.0.14, Steam build 25364265,
`assembly_valheim.dll` SHA-256
`f6499816...c8017fb6`. Read with `ilspycmd` 11.0.0.9375 against the installed
assembly. **Static audit only** — it reads the game, it does not launch it, and
it proves nothing about in-game behaviour. Every row below says what it still
owes in observation.

---

## 1. Does the gap exist today?

### The signal the game gives

Hovering a piece tints it on a blue → green → yellow → orange → red scale. That
is the whole of it: one colour, per piece, with no statement of *why*.

`WearNTear.GetSupportColorValue` is where that colour comes from, and it is worth
reading, because it shows how much the game already knows and does not say:

```csharp
float support = GetSupport();
GetMaterialProperties(out var maxSupport, out var minSupport, out _, out _);
if (support >= maxSupport) return -1f;          // blue: on the ground
support -= minSupport;
return Mathf.Clamp01(support / (maxSupport * 0.5f - minSupport));
```

A number is computed, then thrown away into a hue.

### Players asking why

The question "why is my roof red" is answered almost entirely by third parties.
Current community guides and Steam discussion threads exist specifically to
explain a mechanic the game does not explain
([Valheim Wiki: Building stability](https://valheim.fandom.com/wiki/Building_stability),
[Game Rant: Structure Stability Explained](https://gamerant.com/valheim-structure-stability-guide/),
[ScalaCube structural stability guide](https://scalacube.com/blog/valheim/valheim-structural-stability-guide),
[Mobalytics 1.0 building guide](https://mobalytics.gg/gamebase/guides/valheim-general-building-guide),
plus several
[Steam Community threads](https://steamcommunity.com/app/892970/discussions/0/5506189390965924527)).
A mechanic that needs a guide per site is the pain signal; two independent
current sources was the bar, and the guides plus the threads clear it.

**Honest limit:** these are pages, not a survey. They establish that the
mechanic is confusing enough to be worth explaining repeatedly. They do not
measure how many players want a tool.

### What the maintained tools already do

From the tools' own descriptions, not from summaries of them:

| Tool | What it actually shows | Status |
|---|---|---|
| [Building Health Mode](https://thunderstore.io/c/valheim/p/projjm/Building_Health_Mode/) | *"visually display the **health** of building peices around your player (in the style of a heatmap)"* — damage, not support | Marked **deprecated** on its own page |
| [BuildingHealthDisplay](https://thunderstore.io/c/valheim/p/cjayride/BuildingHealthDisplay/) | health, and optionally piece structural integrity as *"current / max (percent)"* | maintained |
| [ValheimPlus](https://valheim.thunderstore.io/package/ValheimPlus/ValheimPlus/) | precision placement and tweaking of placed objects | maintained |

So the closest existing thing shows **a percentage**. Nothing found shows
*which piece is carrying this one*, *where that chain reaches the ground*, or
*which link in it is the weak one*.

That is exactly the line `PROJECT.md` drew: proceed *"only if support-chain
analysis gives causal information substantially more useful than an existing
stability percentage."* A percentage is what exists. The question is whether the
chain is recoverable.

**It is.** Section 2.

---

## 2. The installed-game audit

Four subsystems, each with real signatures from the binary and an explicit
verdict.

### 2.1 Support — `WearNTear`

| Member | Verified shape | What it means |
|---|---|---|
| `private float m_support` | field, initialised `1f` | this piece's support, local |
| `ZDOVars.s_support` | `float` on the ZDO | the same value, replicated |
| `private float GetSupport()` | returns `GetMaxSupport()` when unowned/invalid, else `m_support`, else the ZDO float | **readable for any piece, including one this peer does not own** |
| `private void UpdateSupport()` | the model | below |
| `private void GetMaterialProperties(out float maxSupport, out float minSupport, out float horizontalLoss, out float verticalLoss)` | a `switch` on `m_materialType` | the constants, below |
| `private readonly List<Collider> m_supportColliders` | retained per piece | **the adjacency list** |
| `private readonly List<Vector3> m_supportPositions` | retained per piece | where each neighbour was |
| `private readonly List<float> m_supportValue` | retained per piece | **what each neighbour's support was** |
| `public bool m_supports` | field | whether this piece can hold anything up at all |
| `private const float c_ComTestWidth = 0.2f`, `c_ComMinAngle = 100f` | fields | the topple test, separate from support |

**The model, from `UpdateSupport`:**

1. Overlap the piece's bounds. A collider on the **terrain layer**, or any
   collider with no `WearNTear` above it, means ground: `m_support = maxSupport`
   and stop. This is the blue case.
2. Otherwise, for each neighbouring `WearNTear` with `m_supports`:
   - `d = distance(thisCOM, neighbourCOM) + 0.1` (or to its transform, whichever
     is nearer, unless `m_forceCorrectCOMCalculation`);
   - horizontal candidate: `neighbourSupport - horizontalLoss * d * neighbourSupport`;
   - for a support point **below** this piece's centre of mass, the loss is
     `Mathf.Lerp(horizontalLoss, verticalLoss, t)` where
     `t = acos(1 - |normal.y|) / (π/2)` — so straight down is pure vertical loss
     and sideways is pure horizontal loss;
   - the piece takes the **maximum** over all candidates.

**The constants**, exactly as the binary has them:

| Material | max | min | verticalLoss | horizontalLoss |
|---|---:|---:|---:|---:|
| Wood | 100 | 10 | 0.125 | 0.2 |
| HardWood | 140 | 10 | 0.1 | 0.1667 |
| Stone | 1000 | 100 | 0.125 | 1.0 |
| Iron | 1500 | 20 | 0.0769 | 0.0769 |
| Marble | 1500 | 100 | 0.125 | 0.5 |
| Ashstone | 2000 | 100 | 0.1 | 0.3333 |
| Ancient | 5000 | 100 | — | — |

The horizontal figure for Stone being `1.0` is why stone does not cantilever and
iron does: iron loses the same small fraction in both directions.

**Verdict: the chain is traceable and the weakest link is identifiable.** Each
piece retains its neighbours *and each neighbour's contributed value*, so a walk
from a selected piece to ground is a real traversal of the game's own data, and
the step with the largest drop is the weak link. This is not a nearest-neighbour
guess — it is the same list `UpdateSupport` built.

**What it owes in observation:** that `m_supportColliders` is populated and
current at the moment a player hovers a piece (it is cached and cleared by
`ClearCachedSupport`, and the cache is invalidated across the network by
`RPC_ClearCachedSupport`). A read that finds an empty list must say "not
computed yet", never "unsupported".

### 2.2 Shelter — `Player`

```csharp
public bool InShelter()
{
    if (m_coverPercentage >= 0.8f) return m_underRoof;
    return false;
}
```

Two conditions, both explicit: at least **80 % cover**, *and* under a roof.

**Verdict: "why is this station exposed" has an exact answer** — which of the
two failed, and by how much for the first.

**Answered since this audit was written (#286, PR #355):** shelter *can* be
evaluated for a point that is not the player's. `Cover.GetCoverForPoint(Vector3
startPos, out float coverPercentage, out bool underRoof, float minDistance =
0.5f)` is **public and static**, in `assembly_utils`, and `Player.UpdateCover`
is simply one of its callers — it passes `GetCenterPoint()`. Vanilla itself uses
it for a point that is not a player's in `Bed.CheckExposure`, which asks about
the bed's spawn point.

So a claim about a *station's* or a *bed's* shelter is a real measurement, not an
extrapolation from the player's. `WorldHousing` does exactly that.

**What it still owes in observation:** how often the answer changes as a
building is altered around a fixed point — the call is a sphere cast and a ring
of rays, so it is not free, and a diagnostic that re-asks it every frame would
be the unbudgeted cost this product keeps refusing elsewhere.

### 2.3 Comfort — `SE_Rested` and `Piece`

```csharp
public static int CalculateComfortLevel(bool inShelter, Vector3 position)
```

**Public, static, and takes the position rather than reading the player.** That
is the most useful single finding in this audit: comfort can be computed for a
hypothetical.

The rule, from the body:

1. base `1`;
2. `+1` if sheltered — and if not sheltered, **nothing else counts at all**;
3. `Piece.GetAllComfortPiecesInRadius(position, 10f)`, sorted by comfort
   descending;
4. each piece adds `GetComfort()`, **skipping** any whose `m_comfortGroup`
   matches the previous one's (when not `None`) or whose `m_name` matches.

`Piece.GetComfort()` returns `0` when `m_comfortObject` is set and not
`activeInHierarchy` — so an unlit or unbuilt comfort object silently contributes
nothing.

**Verdict: "why is this not counting" has five exact answers** — not sheltered,
beyond 10 m, duplicate comfort group, duplicate name, or `m_comfortObject`
inactive. And "what would this add" is answerable by calling the same public
method, which is the hypothetical intervention `PROJECT.md` asks for, without
inventing a model.

**What it owes in observation:** the radius is a sphere from a point, so comfort
is a property of *where you stand*, not of a building. Any UI must say which
point it is answering for.

### 2.4 Demolition dependency

`m_supportColliders` gives the inverse relation too: to find what a piece is
holding up, ask which pieces list it. That requires an index over loaded pieces
rather than a single read.

**Verdict: possible, and not free.** It is the one differentiator of the four
with a real cost, and it should be its own leaf rather than riding CF-002.

---

## 3. The CF-002 go/no-go test

Written before CF-002 starts, as `PROJECT.md` requires.

**CF-002 proceeds only if all four hold, on the installed build, observed:**

1. Select a placed piece and produce the **ordered chain** of pieces from it to a
   ground-contacting piece, using `m_supportColliders` and each step's
   `GetSupport()`, with every step's material and distance shown.
2. The **weakest link** in that chain — the step with the largest proportional
   drop — is named, and it is the step the material table predicts.
3. For a piece the game paints **red**, the explanation states the actual cause
   in the game's own terms: distance from the ground contact, and the material's
   loss per metre in the direction that dominates.
4. Every one of the four subsystems **refuses with a named reason** when its data
   is not available: an empty or stale `m_supportColliders`, an unowned ZDO, a
   piece outside loaded ground. A diagnostic that cannot say why it does not know
   is not shipped.

**It stops if:** the cached support data is not reliably readable at hover time,
or the chain that comes back disagrees with the colour the game paints. Either
would mean the model here is a plausible reconstruction rather than the game's
own, and `PROJECT.md`'s rule is explicit — *never present a nearest-neighbour
guess as the game's real support calculation.*

**What CF-002 must not do,** restating the product's fail-closed defaults so the
spike inherits them: read-only, no piece created, moved, damaged or repaired, no
support value written, no `ClaimOwnership`, no whole-world scan, and no
server-side anything. A diagnostic that changes a world is not a diagnostic.

---

## 4. The ten-second demo, unchanged and now grounded

> Point at a red roof tile. See its path to ground, the beam that is the weak
> link, and the predicted improvement from replacing that beam with one the
> material table says carries further.

Every clause of that is computable from section 2. The last one is the only one
that needs care: a prediction is only honest where the material table alone
determines it, and it must be labelled a prediction.

---

## 5. Non-goals

- No change to vanilla structural rules, by default or otherwise.
- No demolition, repair, placement or support granting.
- No percentage HUD. The percentage already exists in other tools and it is the
  thing this product is differentiating from.
- No claim about a piece whose support data is stale or unreadable.
- No whole-world scan; loaded, bounded neighbourhoods only.
- Nothing about the settlement runtime. That half is separately switched on and
  has its own documents.
