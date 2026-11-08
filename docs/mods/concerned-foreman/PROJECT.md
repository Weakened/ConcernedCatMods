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
drawn where it was put, are Hulgi's. Since the #295 lift the dresser that applies
them is Concerned NPC's `Body/Dressing/NpcFigureDresser`, shared with Sunniva and
Gunnar; Foreman keeps only his look (`ThorsteinLookPlan.Spec`) and his settings.

**What this is not yet** (as first written; the quest is S1-S2, talk S3, the
hand-over panel S4 and sitting at home S5, below). He stands at home facing you. His worker body is still
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

### Tools of the Trade, slice S1: the story record (#295)

Thorstein's introduction now has a record. It is kept per character and per world
in the shared companion sidecar (`src/Shared/Companions`, which Foreman now compiles
under `<config>/ConcernedCatMods/ConcernedForeman/companions`). The game-free
`ToolsOfTheTrade` decides what happens.

- **The two objectives are read from the real tool ledger.** "An axe" is done when
  the ledger says an axe given to him is held by him. The tools reach him only
  through the existing handover, `cf_settle give axe|hammer`: a real, selected
  instance with its tier and durability.
- **When he holds both, he joins.** He is taken on through the settlement register,
  by the same act as `cf_settle recruit thorstein` and with its checks: authority, a
  readable record, a settlement area. The story is recorded complete only after the
  register write has reached disk.
- **It fails closed.** An unresolved handover, a read-only record, no authority or no
  settlement area each waits, and says which.
- **Nothing undoes it.** A lost tool never relocks him, and a replay grants nothing.
  A finished story never re-hires a worker the player dismissed.
- **The carpenter's square (slice S2)** is Concerned NPC's shared keepsake. It lies near
  your bed while this character's introduction has not begun, with the settlement runtime
  on, for the host. Examining it does what `cf_settle quest examine` does; once the
  introduction has begun it is taken away. It is local only (no network view, not an
  item, never blocks). Its look is borrowed from the hammer, a proposal, since the game
  has no carpenter's square. `cf_settle quest` shows the objectives.

**OWNER GO-AROUND PENDING** (a disposable world, settlement runtime on, host):

1. `cf_settle quest`: "not begun". Walk to your bed: a worn carpenter's square lies
   beside it. Hover it ("Worn carpenter's square", with your use key), then use it: the
   inscription and his introduction on screen, with both objectives open. The square is
   gone within a second. Walking through its spot is never blocked.
2. Mark a settlement area, run `cf_worker spawn`, and hold an axe: `cf_settle give
   axe`. The axe objective is done and he is not yet recruited. Quit and reload:
   `cf_settle quest` still shows the axe done.
3. `cf_settle give hammer`: his completion line, "Thorstein joined your settlement",
   and `cf_settle status` shows him employed. Nothing starts on its own.
4. `cf_settle takeback axe`, then `cf_settle quest`: still complete.
5. Repeat step 1's `examine`: it grants nothing twice.

### At home from the introduction, and talking (slice S3)

- **When he appears.** His figure shows once this character has examined the square
  (stage "waiting for tools"), not only once he is on the roster. Every other condition
  is unchanged:
  - he is shown only to the host, with the settlement runtime on and `ShowAtHome` on;
  - only while he is resting, with no saved worker body;
  - only when home is known and the player is near it.
- **Talking.** The use key on him says one line. The game-free `ThorsteinTalk` chooses it.
  - While he waits, the line is about the tool he still needs, read from the ledger.
  - Once recruited, he rotates through the three #295 house-building remarks with the
    shared `DialogueRotation`, so none repeats until the others are said.
- **The talk point** is Concerned NPC's: a capsule on a non-solid layer, a child of his
  figure, which never blocks anybody.

**OWNER GO-AROUND PENDING:**
1. Examine the square: he appears near your bed. Walk into him: you are not blocked.
2. Hover him ("Thorstein", your use key, "Talk"), then use: "An axe and a hammer..." Give
   an axe (`cf_worker spawn`, `cf_settle give axe`, `cf_worker despawn`), then talk again:
   he asks for the hammer.
3. Once he has joined, talk four times: three different remarks, then they start again.

**For the owner to judge in this playtest** (left as they are on purpose):
- **Layer order.** The talk point and the carpenter's square try the `piece_nonsolid` layer
  first, then `item`. The game's build placement can land on `piece_nonsolid`, so a piece
  placed against his invisible capsule could be built onto it. `item` first would avoid
  that. Try building right next to him.
- **Talk reach.** His prompt's hover offset is 2.1, Hulgi's is 1.6. Say whether he can
  be talked to from too far away.

### Thorstein's tools panel (slice S4)

A **"His tools"** button joins the Foreman buttons (settlement runtime on). Its panel shows
Tools of the Trade's status and what the record says he holds, and offers:
- **Bring him over / Send him home:** `cf_worker spawn` and `cf_worker despawn`.
- **Give axe / Give hammer:** two clicks. The first names the exact tool in your hand, with
  its quality, durability and whether it is broken, and moves nothing (`cf_settle preview`).
  The second gives it, through the existing handover (`cf_settle give`).
- **Take back axe / Take back hammer:** `cf_settle takeback`.

Every button is a console word, so the panel can neither do nor refuse anything the console
would not. While the panel is open it refreshes twice a second using only read-only words
(`cf_settle quest view`, `cf_settle tools`), so it never recruits or saves anything on a timer.
A Give click whose preview refused (no tool in hand, or a broken one) arms nothing, and an
armed Give is dropped as soon as there is nothing left to give. Buttons with nothing to do are greyed (game-free `ThorsteinToolsPanelPlan`).
Nothing moves while a handover is unresolved.

Talking to him with both tools held now names the real hold-up: no settlement area, a
record that cannot be read or saved, missing authority, or an unresolved handover.

**OWNER GO-AROUND PENDING:**
1. Open "His tools" before the square: give and take are greyed; "Bring him over" is live.
2. Examine the square, then use "Bring him over": his figure goes and his worker body stands
   there.
3. Hold an axe and click "Give axe" once: the panel names your axe and nothing moves. Click
   again: the axe is his, the panel lists it, and your hand is empty. Do the same with a
   hammer: he joins.
4. "Take back axe": it comes back with the durability it has now.
5. With both tools given but the settlement records read-only (or with no settlement area),
   talk to him. He names that cause.

### Sitting at home (slice S5)

At rest, his figure sits on the ground near home instead of standing beside the
respawn point:
- **The spot** is chosen by the shared companion `PlacementPlanner` (Hulgi's), with
  Thorstein's own band: 1.5 to 6 m from home, at most 1.5 m above or below it.
  - It prefers a fire's warmth.
  - It refuses water, steep ground, a body-sized obstruction, another body, a bed, a
    doorway, the fire piece itself, burning ground, and a location that clears its own
    ground (the starting temple).
  - It waits rather than guesses while any of that ground is still loading.
- **The probe** is Foreman's own `ThorsteinRestProbe`, which reads physics and effect
  areas and changes nothing.
- **Doors and walls.** He never opens, closes or uses a door, and is never set down on the
  far side of a doorway or a wall from home (`ThorsteinRestBounds`). A spot is refused
  when anything solid stands between it and home at chest height (a wall, a shut door),
  or when the straight line from home passes through a doorway, open or shut. Foreman keeps no list of doors players have opened
  to companions, so every door counts as one he may not use. **Lead default pending owner
  confirmation** (2026-11-08): a bed inside a house whose door is shut still counts as
  home, so he may rest in that room. He is set down there, not walked in through the door.
- **Camp.** Concerned NPC's camp (`NpcCamp`) is fed from the player-built pieces loaded
  within 32 m of home each time he looks. Once camp is established he rests inside it.
  If that look fails, he is seated nowhere new until it works again.
- **The pose.** He faces the fire, or home when there is none. Concerned NPC's
  `NpcFigureDresser.SitOnGround` poses his own figure with the game's `emote_sit` bool.
- **He moves only** when something is built on his spot or a warmer one appears. He looks
  again every 30 s, or every 5 s while the ground around home is still loading. A fire going
  out does not get him up, and neither does you standing next to him to talk. If his spot
  is built over and nowhere else qualifies, he stands up beside the respawn point again.
- **Until a spot is found** he stands as before, and `cf_worker status` says why ("At rest: ...").

Not in this slice:
- Chairs and benches. A seat has to be given up the moment a player sits down, which needs
  watching of its own.
- Walking between spots. He is moved, not walked, because the figure has no motor.

**OWNER GO-AROUND PENDING:**
1. With no fire near your bed, come home after the introduction. He sits on the ground
   1.5-6 m from the bed, on the same floor, facing it, and not in a doorway or against the
   bed. `cf_worker status` says "At rest: sitting on the ground".
2. Build and light a campfire about 3 m from the bed and wait up to 30 s. He moves to sit
   by it, facing it ("sitting by the fire"). Let it burn out: he stays.
3. Build a wall through where he sits and wait up to 30 s. He moves to clear ground, or
   stands beside the bed if nowhere near home is left.
4. Respawn at the world start with no bed. He is not seated inside the temple stones. If
   nothing qualifies, he stands beside the start point and the status names what blocked it.
5. Talk to him while he sits, and stay beside him for over 30 s. The prompt still appears,
   walking into him is not blocked, and he does not get up and move away from you. Say
   whether the 2.1 hover offset now feels too high for a seated figure.
6. Judge the pop: when he moves to a better spot he appears there at once.
7. Build a small hut with a door 3-4 m from the bed and a campfire inside it, door shut.
   Wait up to 30 s. He does not sit by that fire: it is behind the hut's walls and door. `cf_worker
   status` counts the doorway.
8. Sleep in a bed inside a closed house (door shut). He rests in that room, never outside
   its walls (lead default pending owner confirmation, DECISIONS.md). Say whether he should
   stay outside instead, as Hulgi does.
9. Judge the seated pose itself: the figure's animator gets only `emote_sit`, not the
   `onGround` bool Foreman's resident pose also sets. If he floats, kneels or plays a fall
   pose instead of sitting, say so.

### His look while working, slice S6 (owner decision 2026-10-29, DECISIONS.md D18)

His worker body stays the `Dverger` clone, with the same prefab, the same saved data and
no migration. While `Thorstein/WorkerLook` is on (the default), Concerned NPC's
`NpcWorkerDresser` puts his own Mullet and Mustache pieces on it, locally, each time his
worker body loads. It refuses any body that is not his saved worker body. A piece that
will not attach, or is not drawn where it was put on the `Dverger` rig, is taken off and
he keeps the plain look. Nothing is tinted, painted or saved on the body itself.

**OWNER GO-AROUND PENDING:**
1. `cf_worker spawn`: within a moment, "Worker body look: hair Mullet ..., beard
   Mustache ..." in `cf_worker status`, or "plain (...)" with the reason.
2. Look at him working: the hair and beard sit on his head. A piece that floated would
   have been taken off; the status line's fit says which.
3. Quit and reload with the body saved: he is dressed again on load. Turn
   `WorkerLook` off and reload: the plain `Dverger` look, with no error.

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
