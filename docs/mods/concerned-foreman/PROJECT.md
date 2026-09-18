# Concerned Foreman

**Status:** active target. As of CF-SET-002 (#279) the product exists in the
repository: `src/ConcernedForeman`, version **0.1.0**, carrying the settlement
worker spike and nothing else. **Nothing is published, there is no tag, and no
part of it has been observed running in a game.** The diagnostics half — the
actual product promise below — has no code yet; CF-001's research obligations
are discharged in `BUILDING_DIAGNOSTICS_AUDIT.md`.

**Promise:** *Point at a building or a station and Foreman explains why it is
stable, exposed, comfortable, or about to fail.* Under #273 it also gains an
**opt-in settlement runtime** — a foreman who can explain a building can also
supervise one being built — but the diagnostic promise above is the product, and
the settlement work is a second, separately switched-on half.

---

## Identity

| Field | Value |
|---|---|
| Product folder | `src/ConcernedForeman` |
| Assembly | `TheConcernedCat.ConcernedForeman` |
| Plugin GUID | `com.theconcernedcat.valheim.concernedforeman` |
| Thunderstore | `TheConcernedCat-ConcernedForeman` |
| Issue key | `CF-` (diagnostics), `CF-SET-` (settlement runtime) |
| Label | `mod:foreman` |

**No compile-time reference to Cartographer or Teamster**, in either direction —
`tools/validate_repo.py` enforces it. Shared code arrives the way the companion
layer does: as source under `src/Shared/<Area>`, compiled into each product's own
DLL by one `<Compile Include>` line.

---

## What it does

### Causal building diagnostics (the differentiator)

- Trace a selected piece's support and dependency chain **toward the ground**,
  and name the weakest link in it.
- A structural heatmap over the pieces you are looking at.
- A demolition dependency preview: what else comes down with this.
- Hypothetical interventions — reinforce or replace *this* beam — offered **only
  where the model can justify the prediction**.
- Station shelter explanations: why a workbench is or is not covered.
- Comfort contributors, their ranges, and why a particular item is not counting.

**Ten-second demo.** Point at a red roof tile. Foreman shows its path to ground,
highlights the beam that is actually carrying it, and explains what reinforcing
that beam would change — where it can actually justify the number.

### The line this product will not cross

**Never present a nearest-neighbour guess as the game's real support
calculation.** A confident wrong answer about why a building is standing is
worse than no answer, because a player will act on it. Where the model cannot
justify a prediction it says so and offers the observation instead.

This is also CF-002's go/no-go: proceed past the spike **only if** support-chain
tracing gives causal information substantially more useful than the stability
percentage the game already shows. If it does not, the diagnostics half stops
there and says why.

---

## Non-goals

- No change to vanilla structural rules, by default or otherwise.
- No demolition, no repair, no free support, no piece mutation from a diagnostic.
- No whole-world scans. Everything is bounded to what is loaded and looked at.
- No save-file mutation, ever.
- No network authority for the diagnostics half — it is read-only and
  client-safe.
- No stability HUD percentage. The game has one; copying it is not a product.

---

## The settlement runtime (#273), and why it is a separate half

The first playable outcome in #273 is: Hulgi via the Broken Compass → **recruit
Foreman** → designate one small settlement → place one cottage order → real
materials gathered or drawn from an authorised stockpile → visible carrying and
building → one validated habitable cottage → one ordinary resident arrives and
persists.

That requires an authority Cartographer's companion layer deliberately does not
have. Hulgi is a **local-only renderer** with no `ZNetView`, no ZDO and no save
participation — which is exactly right for a personal presence and exactly wrong
for anything that must own a shared tree, chest, cottage or villager.

So the settlement runtime is a **separately opted-in subsystem with its own
authority contract**, documented in
[`SETTLEMENT_AUTHORITY.md`](SETTLEMENT_AUTHORITY.md). #264's local-only rules are
not relaxed; they simply describe a different subsystem. Installing Foreman for
its diagnostics must never require a server, and turning the settlement runtime
on is an explicit, separate choice.

Its first bounded proof is [`FIRST_COTTAGE_GRAPH.md`](FIRST_COTTAGE_GRAPH.md).

### What the settlement half will not do

- Consume arbitrary nearby chests. Only containers the player designated.
- Fell decorative or protected trees, or anything outside a marked harvest area.
- Ignore wards or ownership.
- Dismantle existing buildings.
- Conjure materials from elapsed time, or cut the same tree twice.
- Take server ownership it was not granted. Missing authority **fails closed**.
- Grow into a hundred-resident town. One cottage, one resident, and then a
  decision about whether that was worth it.

---

## Research the contract owed — done

CF-001's three outstanding obligations are discharged in
[`BUILDING_DIAGNOSTICS_AUDIT.md`](BUILDING_DIAGNOSTICS_AUDIT.md), against
Valheim 1.0.14 (Steam build 25364265):

- **Current pain signals and the existing tools**, from the tools' own pages.
  The closest maintained thing shows a **percentage**; the one that came nearest
  to a heatmap shows *health*, not support, and is marked deprecated. Nothing
  found traces a support chain or names a weak link.
- **The installed-game audit** of all four subsystems, with real signatures and
  the complete per-material support table read out of the binary.
- **The CF-002 go/no-go test**, written down before CF-002 starts.

**The gate passes.** `WearNTear` retains each piece's supporting colliders *and
each neighbour's contributed support value*, so a chain to ground is a traversal
of the game's own data rather than a reconstruction, and the weakest link is the
step with the largest proportional drop. `SE_Rested.CalculateComfortLevel(bool,
Vector3)` is public and static, so comfort is answerable for a hypothetical
without inventing a model. `Player.InShelter()` is two explicit conditions, so
"exposed" has an exact cause.

The audit also records what each subsystem still owes in **observation** — a
static read of the assembly is not evidence about a running game, and CF-002's
gate is written to stop if the cached data turns out not to be readable when a
player actually hovers a piece.

<details>
<summary>The obligations as originally recorded</summary>

CF-001 is not finished by this document. It also owes:

- **Current pain signals.** At least two independent current-year sources of
  players describing the building-stability problem, plus primary-source
  descriptions of the maintained tools that already address it. The recovered
  archive's 2026 market notes are historical and are **not** evidence about
  today.
- **A narrow installed-game audit plan** for `WearNTear`, support, shelter and
  comfort — the same shape as
  [`COMPANION_COMPATIBILITY.md`](../concerned-cartographer/COMPANION_COMPATIBILITY.md):
  real signatures from the installed binary, explicit verdicts, and a list of
  what the audit owes in observation. No invented APIs.
- **The CF-002 go/no-go test**, written down before CF-002 starts.

These are recorded here as outstanding rather than quietly dropped.

</details>
