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

## Ladders: superseded for players by Concerned Ladders (#436)

Player ladder climbing is owned by **Concerned Ladders** (published 1.0.0, 2026-10-27). Foreman's own player climb
(#325–#328, built in #331/#332/#335/#369) is superseded and is **off by default** under `Ladders/PlayerClimbing`, because
Concerned Ladders refuses to run while Foreman's motor is installed. With the switch off Foreman patches no ladder.
Foreman keeps only the worker-traversal seam that #334 may use later. No compile-time reference to Concerned Ladders
exists or is permitted. Details and the config-migration decision: `LADDERS.md` (top).

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

### Thorstein's two bodies (#295 step 1, #298)

Thorstein has a **worker body** and, since #295 step 1, a **presentation body**,
and never both. The rule is CLAUDE.md's, and three things enforce it:

1. **The decision** (`Domain/Npc/ThorsteinBodyPlan.cs`, tested). His worker body
   (`CF_SettlementWorker`) is persistent. It holds his issued axe and hammer and
   anything he has gathered, so a saved worker body anywhere in the world is his
   body, loaded or not, and the figure is never asked for while one exists. The
   figure is shown only when all of these hold: no saved body, he is recruited
   (`cf_settle recruit`), he is resting, the settlement runtime is on,
   `Thorstein/ShowAtHome` is on, a respawn anchor is known, and you are within
   48 m of it. It is shown on the host only, because only the host can count every
   saved body. Every unknown fails towards showing nothing.
2. **The leases** (`Domain/Npc/ThorsteinBodies.cs`, tested against the real
   library). His role contract is `NpcBodyContract.ForWorkerWithPresentation`
   (Concerned NPC 0.5.0), with the same prefab name and key prefix as before.
   Foreman holds the worker lease whenever his saved body exists and the figure's
   lease only when it does not. The arbiter refuses the second kind while the
   first is held, from any runtime in the process, and refuses the figure while
   a job holds him.
3. **The runtime** (`Runtime/Presentation/ThorsteinPresentation.cs`). The figure
   is built only after its lease is granted and is taken down before anything
   that could release that lease. `cf_worker spawn` hands over in this order:
   figure down, figure lease released, worker lease claimed, body created where
   the figure stood. `cf_worker despawn`, which still refuses while he holds
   anything, releases the worker lease, and the figure may return.

**What the figure is.** It is the game's own character, extracted dark by
Concerned NPC's `NpcPresentationBody`: no network view, no character, no AI, no
collider, and every game script removed before it was enabled. It is never saved
and nobody else sees it. It is dressed with the owner's reference (#298): the
**Mullet** hair and **Mustache** beard, resolved by the game's own UI label and
then by the documented 1.0.7 ids `Hair32` and `Beard22`. His colouring comes from
the slider readings (skin about 0.68, hair tone about 0.94, blondness about 0.22),
converted through the character screen's own palette when it was read this
session. Otherwise his hair uses a stated dark-brown fallback and his skin is the
model's own. The leather tunic and trousers are this build's proposal, not the
owner's; the reference does not lock clothes. All four slots can be overridden
in the `[Thorstein]` config section. The attachment rules, including binding a
skinned preset to the body's own bone array and taking off any piece that is not
drawn where it was put, are Hulgi's, copied from Cartographer rather than
referenced.

**What this is not yet.** There is no idle routine, no sitting, no dialogue, no
quest and no hand-over UI. He stands at home facing you. His worker body is still
the `Dverger` clone, which cannot wear Mullet or Mustache, so while he works he
looks like a dwarf. Because his issued tools live in that body, a Thorstein who has
been given a worker body stays a worker body until it is despawned. **Nothing here
has been observed in game. OWNER GO-AROUND PENDING**:

1. Opt in to the settlement runtime, then run `cf_settle recruit` with no worker
   body: he appears near your bed, facing you, with Mullet and Mustache, and
   `cf_worker status` says the lease held is "the figure at home's".
2. Walk through him: there is no collision. Walk 70 m away: he goes. Come back:
   he returns.
3. Run `cf_worker spawn`: the figure goes, and the worker body appears where he
   stood, never both at once.
4. Run `cf_worker despawn` with nothing held: the worker body goes, and the
   figure returns.
5. Quit and reload, both with a worker body and without one: there is exactly one
   of him each time.
6. Check the hair, beard and colouring against the owner's reference in daylight
   and by firelight. Read the "Look:" part of `cf_worker status` for any
   fallback or removed piece.

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
