# Ladders: climbing, not teleporting

**Owner brief:** 2026-09-17, "Concerned Foreman — Functional Ladder System". This document is the spec, the decision
record and the work breakdown. It belongs to Concerned Foreman; there is no separate ladder product, package or
repository.

The promise, in the owner's words:

> Valheim players should not be forced to build staircases everywhere just to move vertically.

---

## 1. What the game already has (verified, 2026-09-17, Valheim 1.0.14)

Read from the decompiled `assembly_valheim` of the installed build (25364265) and from the game's own asset manifest.
No file was modified, and nothing from the game is redistributed.

| Fact | Where | What it means here |
|---|---|---|
| A `Ladder` component exists: `Interact` sets `character.transform.position/rotation` to `m_targetPos` and returns | `Ladder.cs` (58 lines) | Vanilla ladders **teleport**. There is no climbing in the game. This is the gap the feature fills. |
| Ladder build pieces ship with the game: `Assets/GameElements/Pieces/wood_stepladder.prefab` (icon `wood_ladder.png`, localization `$piece_woodstepladder`) and `Piece_grausten_stone_ladder.prefab` (`$piece_grausten_stoneladder`) | game asset manifest (`StreamingAssets/SoftRef/manifest_extended`) | The **art already exists**, at vanilla quality, with `new`/`worn`/`broken` damage meshes. Nothing has to be modelled, textured or shipped. |
| Other ladder props: `BogWitch_Ladder.prefab`, `goblin_stepladder.prefab`, the karve's ladder meshes | same manifest | Candidates for "climbable" beyond the buildable pieces, decided by what actually carries a `Ladder` component in game (runtime audit L0). |
| Players (only players) can **wall-run**: above a 38° ground angle, a running player with speed gets `m_wallRunning`, no slippage, and the visual is tilted along the surface normal | `Character.ApplySlide`, `Character.UpdateVisual` | Vanilla already has a "body tilted against a steep surface" pose, and it is **the player's**. |
| The tilt is replicated through the character's own ZDO (`ZDOVars.s_tiltrot`), and remote characters read it back, because `CanWallRun()` is true for every player | `Character.UpdateVisual` | A climbing pose can ride **vanilla's own replicated state**. No custom RPC, no animation networking. |
| The animator is fed `tilt = dot(visual.forward, up)` every frame | `Character.UpdateVisual` | The animator already blends locomotion by body tilt. Climb animation can reuse it. |
| Footsteps have a `MotionType.Climbing`, chosen by `IsWallRunning()` | `FootStep.cs` | The game's own sound system has a climbing case. |
| Attach poses are animator bools: `attach_chair`, `attach_bed`, `attach_cook`, `attach_skin`. There is no `attach_ladder` | `Player.AttachStart`, `Chair`, `Bed`, `Barber`, `CookingStation` | A **sitting-style attach is the wrong tool** for climbing, and no ladder clip exists to switch on. Section 6 says what we do instead. |

**The honest consequence.** A brand-new hand-animated climb cycle would need an animation clip authored in Unity and
shipped in an asset bundle. This session cannot author one, and shipping ripped game animation is forbidden
(`CLAUDE.md`). So the climb pose is built from what the game already replicates and blends. Whether that looks good
enough is a **visual gate the owner judges in game** (§9, G3), not something this document may claim in advance.

---

## 2. Scope

**In scope**

1. Real climbing traversal on ladders: mount, climb up and down, stop, reverse, dismount at the bottom, cross the edge
   at the top, jump or fall away, and recover safely when the ladder disappears.
2. The game's existing ladder pieces become climbable (§3, decision L2).
3. Ladder heights that are actually useful: stacking and snapping verified in game, and a Foreman piece added only if
   the vanilla piece cannot stack (decision L3).
4. A climbing pose, matched animation speed and restrained audio.
5. Multiplayer: correct for the climber, believable for observers, with no state that vanilla does not already
   replicate.
6. A traversal abstraction the NPC framework can call, plus ladder navigation links, so Thorstein and Gunnar can
   eventually climb.
7. Configuration, tests, documentation, changelog, packaging.

**Out of scope for this feature** (each needs its own issue if wanted): rope ladders, iron or decorative ladder
variants, climbing arbitrary walls or cliffs, ladder hatches and trapdoors, mounts or carts on ladders, enemies
climbing, and any change to vanilla stairs.

---

## 3. Decisions

### L1. No new art, no asset bundle, no redistribution

The feature uses the game's own ladder prefabs, meshes, materials and sounds as they are loaded at runtime. Nothing
from the game is copied into this repository or into a package. This satisfies the brief's quality bar the only way
that is both legal and achievable here: the ladder *is* Valheim art.

### L2. Climbing is added to the ladders the game already has

The primary deliverable makes existing ladder pieces climbable instead of teleporting. Consequences, all of them good:
- **No custom prefab in anyone's world.** A world built with these ladders stays a vanilla world, and removing
  Concerned Foreman leaves every ladder standing (it goes back to teleporting).
- The recipe, damage states, repair, ward rules, support and persistence are vanilla's, already balanced and already
  correct.
- The hammer's build menu is unchanged, so there is no duplicate "ladder" entry to confuse anyone.

The vanilla `Ladder.Interact` teleport stays available behind a setting, because some players use it deliberately;
the default is to climb (`Ladders/UseTeleport = false`).

**Which pieces** (CF-LAD-004, #328). Two rules, in order, in
`src/ConcernedForeman/Domain/Ladders/LadderAdmission.cs`:

1. a **prefab name the feature knows** is admitted without measuring — the two buildable pieces, as **data** in one
   string array, so adding one after an audit is a string, and a name that is not in the loaded game simply never
   matches and can never throw;
2. anything else carrying a `Ladder` is admitted only if it **measures like a ladder**: tall enough to be worth
   climbing, narrow enough, thin enough, and standing up. A hammer-placed piece is not asked to prove it is thin as
   well, because a player put it there to go up.

The loaded game is the authority: every measurement comes off the object standing in the world, and a verdict is
reached once per prefab and remembered by name. The thresholds err towards admitting — a refused ladder still
teleports, which is vanilla and therefore safe, but it is also a promise broken. `cf_ladders here` prints the numbers
that should tighten them.

**The interaction** is one Harmony prefix on `Ladder.Interact`, in
`src/ConcernedForeman/Runtime/Ladders/LadderInteraction.cs`, under its own Harmony id. It calls
`ClimbController.TryMountByUse` and suppresses the teleport only when that returns true; it never duplicates or wraps
CF-LAD-002's two motor patches. A climber's Use never teleports them. The ladder's hover text is vanilla's.

### L3. A Foreman ladder piece only if the game's piece cannot stack

Whether `wood_stepladder` snaps end-to-end into tall runs is a fact to be **observed in game** (G2). If it does, no new
piece is built and §3 of the brief is met by the vanilla piece. If it does not, the follow-up is a Foreman piece
cloned from the vanilla prefab with added snap points — and that decision is the **owner's**, because a custom piece
makes a world depend on the mod: uninstalling would delete those ladders from the world. It will not be done silently.

**How it will be observed** (CF-LAD-004, #328). The steps below are the procedure of record; there is no fuller script
anywhere else. Run them in a disposable world, with `Ladders/Enabled = false` so the answer is about the game and not
about this mod. `cf_ladders` reads and never places, moves or damages a piece, so every placement here is yours with
the hammer.

1. `cf_ladders list` — every prefab carrying a `Ladder`, its size, and vanilla's teleport target. It reads the loaded
   scene, so a world has to be loaded for it to answer.
2. Place one `wood_stepladder` against a wall and **stand next to it**: `here` and `snaps` measure the ladders within
   their radius of where you are standing (12 m when you give none, and anything above 64 m falls back to 12), and
   say "none in range" when you are away from them. Then `cf_ladders snaps` — does the piece have snap points at its
   foot and head at all? A piece with none cannot stack, and the rest of the run is already answered.
3. Bring a second ghost to the head of the first: does it turn green, and does it snap, and at what spacing?
4. The invalid cases — no support, inside the first piece, facing the wrong way — must still show a red ghost and
   refuse. A feature that made an illegal placement legal would be a defect.
5. Hammer rotation (`Q`/`E` or the mouse wheel) must behave exactly as it does anywhere else.
6. Repeat to five pieces, then stand at the foot of the run and measure the whole of it with `cf_ladders here 24`.

**Result: not yet observed.** The table below is filled in from the game, with the build number, or it stays empty.

| Observed | Build | Do two pieces snap end to end? | Spacing | Ghost valid / invalid correct? | Rotation unaffected? |
|---|---|---|---|---|---|
| — | — | — | — | — | — |

### L4. The climb is local, input-driven and uses only state the game already replicates

- The climbing state lives on the local client and is driven by the player's own input.
- Position and rotation travel the way every character's do.
- The pose rides `ZDOVars.s_tiltrot`, which vanilla already writes and reads for players.
- **No custom RPC, no new ZDO keys, no animation streaming.**

### L5. Never a locked player

Every exit from the climb state is a one-frame restore of normal control: the ladder destroyed, unloaded or moved,
the player dead, teleported, logged out, the world unloading, the plugin destroyed, the setting turned off, or an
exception inside the controller. The failure direction is always "the player falls normally", never "the player is
stuck on a ladder that is no longer there".

### L6. The traversal seam is shared, and NPCs come after the player

The climb is expressed as a traversal seam (`CanTraverse`, `Enter`, `Traverse`, `Exit`) with the player as its first
caller, so the NPC framework can call the same thing later. Ladder endpoints are published as navigation links. If
making Thorstein path *through* those links needs a navigation redesign, that redesign becomes its own issue and the
player's ladders ship without it. Player ladders are not held hostage by AI architecture (brief §11).

### L7. Authority and safety (this feature's rule amendment)

Concerned Foreman until now installs no patch and touches no world state at load. Ladder climbing changes that, and
the limits are written down here:
- It moves **the local player's own body**, only while that player is climbing a ladder they chose to use, and only
  along that ladder. It never moves another player, another character, or a piece.
- It **never** changes a piece, a ward, terrain, a container, a recipe or any world data. It places no piece and
  destroys none.
- It is off with one setting, and off means vanilla behaviour returns exactly.
- Patches are the minimum needed (the `Ladder` interaction and the player's motor), each named in the audit.
- The NPC seam obeys the worker rules already in force: an NPC climbs only under an opted-in worker runtime, on the
  host, with no peers connected.

This is proposed as a `CLAUDE.md` / `AGENTS.md` amendment in the same change that first ships climbing code, in the
style of `docs/settlement/cart-and-collection/DECISIONS.md` D14, and it is authorised by the owner brief of
2026-09-17.

---

## 4. The player's experience (acceptance, in the owner's terms)

A player can: open the hammer, place a ladder against a tower, stack it to a useful height, walk up to it, start
climbing without fighting for the exact spot, climb up and down, stop midway, reverse, step off onto the platform at
the top, step off cleanly at the bottom, and jump away when they want. No vibration, no teleport, no physics fight,
no stuck character.

Useful for: watchtowers, castle towers, defensive walls, scaffolding, docks, raised platforms, tree houses, pits,
mining structures, roof access and compact multi-floor buildings.

---

## 5. Architecture

Game-free logic first, so it can be tested without the game; the Valheim-facing layer stays thin. This mirrors how
Foreman's settlement work is already split (`Domain` versus `Runtime`).

```text
src/ConcernedForeman/Domain/Ladders/            (no UnityEngine, fully tested)
  LadderGeometry        bottom, top, centreline, facing, climbable span, rung pitch
  LadderMount           is this character in range, on a climbable side, facing it: yes, no, why not
  ClimbTrack            progress along the ladder, speed, direction, clamped ends
  ClimbExit             which exit applies (bottom, top, jump away, lost ladder) and where it lands
  LadderStack           two pieces end to end are one climbable run
  ClimbSafety           the reasons a climb must end, and what has to be restored

src/ConcernedForeman/Runtime/Ladders/           (Valheim adapters)
  LadderSurvey          finds ladder pieces near a character, cached, no scene-wide search per frame
  ClimbController       drives the local player: mount, motion, dismount, safety recovery
  ClimbPose             body facing and tilt, through the vanilla tilt path, and animator speed
  ClimbSounds           restrained wood contact, from the game's own effects
  LadderInteraction     the patch that offers climbing where vanilla offers a teleport
  LadderNavigationLink  endpoints published for NPC navigation

src/Shared/Workers/Traversal/                   (the NPC seam, game-free)
  ILadderTraversal      CanTraverse / Enter / Traverse / Exit
  TraversalCapability   which workers may climb
```

Tests live in the existing Foreman/settlement test project. No new framework: if Foreman already has a place for
something, it goes there.

---

## 6. The climb, in detail

**Mount.** Walking into a ladder within a small, forgiving band (roughly the piece's width, on its climbable face,
moving towards it) starts the climb, with a smooth alignment to the ladder's centreline and facing — never an
instant snap. Auto-mount is configurable; `Use` also mounts.

**Motion.** Forward or up climbs up, back or down climbs down, at a controlled speed, aligned to the centreline, with
gravity and the normal motor kept out of the way while climbing. Sideways drift is prevented. Camera stays the
player's. Stamina: the decision is documented in the settings (default: climbing costs no stamina, because vanilla
charges none for walking up its own stairs; a multiplier setting exists for players who want it).

**Stop and reverse.** Releasing input stops on the ladder; the pose goes to a ladder idle; reversing direction reverses
the motion and the animation.

**Bottom exit.** Stepping off restores normal locomotion on the ground, with the character never embedded in terrain.

**Top exit.** The hard one, and where the brief's attention goes. The controller looks for usable standing space at
the top, moves the character across the edge, and blends into normal locomotion without clipping. It is tested on
several floor thicknesses and tower shapes (§9, G2).

**Pose and animation.** The climbing pose comes from vanilla's tilt path (§1): the body is turned to face the ladder
and tilted against it, the way a wall-running player already is, and the locomotion animation is driven at the climb
speed so the limbs cycle with the motion, stop when it stops and reverse when it reverses. Hands and feet land near
the rungs because the ladder's rung pitch is known; exact IK is explicitly **not** promised (brief §9's priority
order: stable climbing first, believable positioning, then animation, then IK polish). If the result does not pass the
visual gate, the fallbacks are recorded there and the owner decides; one of them is authored animation assets, which
needs a human in Unity.

**Audio.** Quiet wood contact at rung intervals, not per frame, reusing the game's own effects. Restrained by default.

---

## 7. Multiplayer

- The climber's client owns the climb. Others see the character move and lean, through the state vanilla already
  replicates.
- A second player may climb the same ladder; the system does not reserve it.
- Disconnect, reconnect, world reload and save during a climb all end in a normal standing or falling character.
- Tested with the host climbing, a client climbing, and each watching the other (§9, G4).

---

## 8. Configuration (`Ladders` section, Foreman's existing config)

| Setting | Default | Why |
|---|---|---|
| `Enabled` | `true` | Zero configuration for the default experience; one switch to return to vanilla. |
| `AutoMount` | `true` | Walking into a ladder climbs it. Off means `Use` only. |
| `ClimbSpeed` | `1.0` | Multiplier on the tuned speed. |
| `StaminaCost` | `0` per second | Vanilla charges nothing to walk up stairs. |
| `UseTeleport` | `false` | Restores vanilla's `Use` teleport for players who prefer it. |
| `NpcClimbing` | `false` | Off until NPC traversal passes its own gate. |

Nothing else, unless a gate proves it is needed.

**Bound** (CF-LAD-004, #328) in `src/ConcernedForeman/Runtime/Ladders/LadderSettings.cs`, the way Foreman binds
everything else. What the values mean, and what happens to a silly one, is in
`src/ConcernedForeman/Domain/Ladders/LadderSettingValues.cs`, which has no BepInEx in it and is tested.

| Setting | Reaches | Range |
|---|---|---|
| `Enabled` | `ClimbOptions.Enabled`, read live every frame | — |
| `AutoMount` | `ClimbOptions.AutoMount` | — |
| `ClimbSpeed` | `ClimbOptions.ClimbSpeedMultiplier` → `ClimbLimits.SpeedMultiplier` | 0.25 – 3 |
| `StaminaCost` | `ClimbOptions.StaminaPerSecond` → `ClimbLimits.StaminaPerSecond` | 0 – 10 |
| `UseTeleport` | the interaction prefix only; nothing in the domain | — |
| `NpcClimbing` | nothing yet (CF-LAD-005) | — |

Two rules hold this together. The configured ranges are **exactly** the ranges `ClimbLimits.Validate` accepts, and a
test fails if they ever drift apart — otherwise a number a config file accepts becomes a throw at the moment somebody
walks into a ladder. And a value outside them is **clamped, never thrown**: `LadderSettingValues.Sanitised` and
`ClimbOptions.ToLimits` agree, so a hand-edited `NaN` costs a player nothing.

`Enabled` has two different meanings by design, and both are "vanilla": **false at startup means no patch is installed
at all**, and false later means every patch falls straight through and a climb in progress ends on the next frame.

---

## 9. Acceptance gates

No gate may be claimed without the evidence named in it. A passing test suite is not gameplay evidence.

- **G1 Automated.** Domain tests: endpoints and geometry, mount eligibility and refusals, progress and clamping,
  stacked runs, every exit, every safety reason, settings. Plus the runtime's decision seams, faked. Full Foreman
  suite, validator and Release build green.
- **G2 Play, single player.** Mount, climb up and down, stop, reverse, both exits, jump away; a stacked run of two and
  of five; ladder against a wall, inside a tower, in a pit, over water, near a roof; ladder destroyed mid-climb;
  platform destroyed mid-climb; wrong side; off-centre approach; death while climbing; save and reload while on it;
  and whether vanilla pieces stack (decision L3).
- **G3 Look.** The owner judges the pose, the hands and feet, the transitions and the dismounts, against the brief's
  §22 list. This gate can fail the feature even when everything else passes.
- **G4 Multiplayer.** Host and client climbing and observing, placement by both, destruction, reconnect, reload.
  Needs a second client; if the owner cannot run one, the gate is recorded as **not run**, and the feature ships (if
  the owner says so) with that limitation written down, never as a pass.
- **G5 NPC.** Either a worker climbs under the opted-in runtime, or the traversal seam exists, the navigation links are
  published, and the remaining navigation work is a filed follow-up issue.
- **G6 Performance.** A tower with dozens of ladders and nobody climbing costs nothing measurable: no scene-wide
  searches, no per-frame reflection, no per-rung update loops, cached surveys, local detection only.
- **G7 Packaging.** Foreman builds, packages, documents and changelogs cleanly, with a truthful version. Nothing is
  published.

**What G3 is actually judging.** There is no ladder animation clip in the game and none is shipped, so nothing in the
climb makes a hand take hold of a rung. What the owner will see is this. The body turns to face the ladder, yaw only,
and slides along its centreline at 1.8 m/s times the speed setting; the turn and the step onto the centreline are
eased over the alignment time rather than snapped. While the climb lasts, `ClimbPose` switches on the player's own
wall-running state and hands vanilla a ground normal pointing straight out of the ladder's face, so the body is leaned
against the ladder the way a wall-running player is leaned against a slope, by vanilla's own tilt, not by an angle of
ours. The ordinary locomotion animation is driven from the climb's signed speed, so the legs cycle at climbing pace,
reverse on the way down, and stop when the climber stops. There is no IK: the arms keep their walking swing, and feet
land near the rungs only because the body passes them. Sound is one quiet wood contact per rung crossed, never more
often than once every 0.22 s. Two honest caveats for the judgement: if the log says `Ladder pose unavailable on this
Valheim build`, none of the above is installed and the climber slides up in a standing idle, which is a different
thing to judge and should be said in the verdict; and a ladder whose rungs are not named in its mesh falls back to an
assumed 0.35 m pitch, which moves the contact sounds but nothing else. The gate's question is
whether that reads as climbing to somebody watching, not whether it matches an authored climb cycle.

**Evidence** goes in the handoff folder as it is captured: the eleven captures the brief lists (§21), each an in-game
screenshot, plus video or GIF of entry, climb, stop, reverse and both exits if the pipeline allows. Screenshots from
an asset viewer are not evidence. The protected screenshot script is not touched.

---

## 10. Work breakdown

One issue per workstream, one agent per worktree, the lead integrates, exactly as the cart-and-collection work runs
(`docs/settlement/cart-and-collection/TASKS.md`). Letters are prefixed `L` so they never collide with agents A–E of
that effort.

| Issue | Workstream | Owns | Depends on |
|---|---|---|---|
| CF-LAD-001 | **L0 lead** | this document, the runtime audit of which prefabs carry `Ladder`, the domain core and its tests | — |
| CF-LAD-002 | **L-A traversal** | `ClimbController`, motion, entry and exit, safety recovery, multiplayer behaviour | L0 domain |
| CF-LAD-003 | **L-C presentation** | `ClimbPose`, animation speed, tilt through the vanilla path, audio | L0 domain, L-A seams |
| CF-LAD-004 | **L-D pieces** | which pieces are climbable, stacking and snapping observations, the interaction patch, settings, packaging | L0 audit |
| CF-LAD-005 | **L-E NPC seam** | `ILadderTraversal`, navigation links, worker capability flag, the follow-up navigation issue | L0 domain |

File allowlists are per workstream; no two agents edit the same file. The lead owns `Plugin.cs`, the settings file and
every document in this folder.

---

## 11. Risks and honest limitations

1. **The climbing look.** Built from vanilla's tilt and locomotion, not from an authored climb clip. It may not pass
   G3. Recorded here before any work starts, so nobody is surprised.
2. **IK.** Not promised. A fragile IK rig that destabilises character animation is worse than a believable pose.
3. **Multiplayer testing** needs a second client on the owner's machine or network.
4. **NPC pathfinding** through ladder links may be larger than this feature; the seam ships either way.
5. **Vanilla's teleport** is a behaviour players know. The default changes it; the setting restores it.
6. **A custom piece** would make worlds depend on the mod. Not done without the owner's explicit yes (L3).
