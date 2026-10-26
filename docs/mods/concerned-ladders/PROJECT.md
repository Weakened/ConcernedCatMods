# Concerned Ladders — active owner revision, 2026-10-25

**Specification, not an implementation or playtest claim.** Current readiness is
in [STATUS.md](STATUS.md). This contract includes the owner's playtest changes
and later clarifications in [OWNER_REVISION_2026_10_25.md](OWNER_REVISION_2026_10_25.md).
It replaces the previous ground-only, single-face, no-stamina/no-skill scope.
The previous 0.1.1 results are [historical evidence](STATUS_0_1_1_HISTORICAL.md).

## 1. Player outcome and fixed scope

Build compact vertical routes using **Wood Ladder**, **Copper Ladder** and
**Iron Ladder**, placed with the normal hammer. Deliberately grab from a landing
or while rising/falling, climb on either physically clear face, hold, reverse,
walk onto a usable floor, or jump toward a nearby platform. Run makes climbing
faster and spends stamina. A native Climbing skill progresses through use;
controlled downward sliding unlocks at current learned level 20.

Attempt a genuine climbing animation. A tested walking fallback is permitted
only after a documented feasibility attempt; label it honestly. Snap labels,
key hints and obstruction messages must be readable and match actual reasons.
The agents complete accessible regression tests and prepare a new importable
ZIP before asking the owner for preference feedback.

Do not add bones, more materials, wall/cliff climbing, NPC traversal, carts,
ships, moving platforms or a general animation framework in this revision.
Keep other Concerned Cat products independent and existing profiles unchanged.

## 2. Identity, version and building

| Item | Permanent contract |
|---|---|
| Product / source | Concerned Ladders / `src/ConcernedLadders/` |
| Assembly | `TheConcernedCat.ConcernedLadders.dll` |
| Plugin GUID | `com.theconcernedcat.valheim.concernedladders` |
| Thunderstore package | `TheConcernedCat-ConcernedLadders` |
| Prefabs | `tcc_ladder_wood`, `tcc_ladder_copper`, `tcc_ladder_iron` |
| Climbing skill identifier | `com.theconcernedcat.valheim.concernedladders.climbing` |
| Test profile | `TCC-Ladders-Dev` |
| Climb setting | BepInEx plugin-GUID config, `General/ClimbingEnabled` |

Use **0.2.0 or a newer appropriate numeric version**, strictly newer than the
existing local candidate. Do not reuse 0.1.1: two different 0.1.1 ZIPs were
reported previously. Synchronize plugin constant, project, TOML, generated
manifest, assembly metadata and changelog. This document does not change them.

| Visible name | Resources per 2 m section | Station | Health | Structural donor |
|---|---|---|---:|---|
| Wood Ladder | 4 Wood | Workbench | 200 | `woodwall` |
| Copper Ladder | 2 Copper + 1 Wood | Forge | 400 | `woodiron_pole` |
| Iron Ladder | 2 Iron + 1 Wood | Forge | 800 | `woodiron_pole` |

Preserve native discovery, costs, wards, support, wear, repair and refunds.
Both metal variants retain the iron-pole support class. Preserve saved prefab
identities and geometry: 2 m span, 0.8 m visible width, one-third-metre rung
pitch, endpoints `(0,0,0)` and `(0,2,0)`, and six native snap transforms.
Collision stays outside the three durability visual groups. Existing original
runtime geometry and copied materials remain the art baseline.

Current product pins are BepInExPack Valheim 5.4.2351 and Jotunn 2.30.2. Recheck
the installed game before compiling; historical tests used Valheim 1.0.20 /
Unity 6000.0.75. Preserve other products' pins. Register pieces and the skill
even when climbing is off. Climb sessions are transient. Removing the mod can
remove its pieces when a world is saved; use disposable worlds/characters.

## 3. Controls and initial balance

These are design defaults, not measurements from photographs or claims about
vanilla balance. Keep them in one policy surface; record measured tuning.

| Action / parameter | Required behavior / default |
|---|---|
| Fresh Use / Interact | Mount or catch a reachable selected ladder; never auto-catch |
| Use while attached | Swept exit to safe standing ground, or hold with a reason |
| Forward / backward / neutral | Up / down / hold, independent of camera pitch |
| Jump (default Space) | Real upward and directed departure when native jump conditions permit |
| Fresh Crouch action while attached | Drop: release immediately without a jump impulse or a landing requirement; works at zero stamina |
| Hammer while attached | Aim, select, rotate/snap, place, repair and remove through native controls; hold the anchor during building actions and focused menus |
| Run + movement | Fast climb; native binding, default Shift, including controller/toggle |
| Backward + Run, learned level < 20 | Fast descent |
| Backward + Run, learned level >= 20 | Controlled slide |
| Normal / fast / slide speeds | 1.8 / 2.43 / 4.5 m/s at full input |
| Fast / slide base stamina rate | 6 / 2 units per second of actual eligible travel |
| Normal climb / hold | No additional mod stamina charge |
| Skill stamina saving | Linear 0..25% at learned levels 0..100, fast/slide only |
| Grab reach | Initial 1.5 m chest-to-usable-rung maximum, measured in game |
| Air alignment limits | 1.0 m horizontal and 0.25 m vertical body adjustment |
| Post-Jump/Drop remount | At least 0.4 s across all these ladders AND a fresh Use edge |
| Skill XP | Native RaiseSkill factor 0.25 per completed eligible metre; IncreaseStep 1 |

Use processed game actions, current remaps and native hold/toggle sprint.
Pause intent under chat, console, map, inventory and menus. Resolve input once:
one Use press must not mount and immediately dismount. Resolve simultaneous
eligible ladder actions as Jump > Crouch/Drop > Use > travel, with one departure only.
Native hammer actions own their bindings before ladder Use-to-dismount; a
shared snap-cycle/Use binding must not detach the player. Crouch
means a fresh press of the player's actual bound action, not a raw key or an
already-toggled sneak state. A button held before mounting must not auto-drop.

## 4. Reach, midair catch and both faces

Measure from a stable capsule-relative chest/grip point to the nearest usable
rung span, including authored offsets. Use the same eligibility for hints and
final mount. The first two latest photos establish a reach complaint, not a
measured distance. Recreate those approaches and record actual dimensions
before accepting or adjusting the initial 1.5 m limit.

Native targeting wins. Any assistance is limited to our registered ladders,
inside the reach envelope and a maximum 45-degree aim cone, ranked
predictably. Preserve nearer doors/chests/actors and construction controls.
Do not increase global interaction range or scan every world object per frame.

Allow deliberate Use while rising or falling. Check ownership, available
control, reach, line of sight and the full capsule route **before** changing
velocity, gravity, fall reference, pose, stamina or XP. Rejected grabs change
none of those. A successful catch arrests the existing fall once; departure
then uses ordinary falling from the actual release height.

Keep air alignment within its separate limits. Allow reachable low-rung
hanging using the real grip height/capsule; do not hoist feet to the module
bottom or create invisible downward ladder length. Unsupported bottom limits
hold. After Jump or Drop, held Use or an adjacent ladder cannot bypass cooldown/fresh
edge. Busy/dead/teleporting/attached/debug-flight/overlay states remain gated;
airborne state alone is not a refusal.

Prefer and lock the approach face. Derive each face's stand-off from its actual
collider surface and capsule radius/skin; the original collider is asymmetric,
so negating the front offset is insufficient. Route to the **same face**.
The audited rear bug accepted negative-plane feet in the pure policy but
routed them to the front, crossing the ladder box. Fix that lane selection;
do not merely hide the message or broadly ignore the ladder's collision.
A freestanding ladder works on both faces. A wall blocks only the obstructed
route; never pull through it or switch sides with the camera. Mixed-material
seams need compatible physical lanes. Reject gaps, overlaps, incompatible
rotations and parallel-shaft shortcuts.

## 5. Jump, Drop and usable floor exits

Space/Jump must visibly hop upward and toward the intended departure direction,
including toward a side platform. Resolve movement relative to the horizontal
camera view; neutral departure uses the selected face's outward direction.
Keep collision enabled. Check an immediately blocked direction before any
fallback; native physics owns the arc. Integrate native jump force, stamina
and modifiers once through a verified adapter. Do not fake global grounding
or let the next ladder/vanilla motor tick erase the impulse.

Jump works while mounting, holding, normal/fast climbing, sliding and exiting.
At insufficient native jump stamina it still deliberately releases with
ordinary gravity. Settle preceding climb stamina before jump affordability;
never charge two jumps or trap an exhausted player.

Fresh Crouch while a ladder session is active means **Drop**. It works during
mounting, holding, normal/fast travel, sliding and an exit transfer. Release
immediately at the current safe body position, with ordinary gravity and no
upward or sideways launch. Do not require ground below, a safe landing, stamina
or an unobstructed top exit. Keep collision enabled; there is no teleport or
extra outward shove. Remove only ladder-commanded motion, including horizontal
mount/exit transfer motion, without erasing independently applied motion or
writing after ownership loss. Do not simply zero the complete velocity vector.

Drop has no stamina cost or skill award of its own. Settle any preceding earned
climb payment/progress exactly once; an exhausted player still releases.
Ordinary fall damage starts from the actual release height after an accepted
catch. Clear ladder animation/control and enforce the same 0.4 s plus fresh Use
remount guard as Jump. Consume the attached Crouch press so it does not also
toggle native sneak. Preserve pre-existing native crouch state and all normal
crouch behavior outside a ladder session. Do not replay presses made in an
overlay when it closes. Localized hints show separate bound Jump and Drop
actions. See [DROP_AND_SCAFFOLDING_ADDENDUM_2026_10_25.md](DROP_AND_SCAFFOLDING_ADDENDUM_2026_10_25.md).

A correctly placed ladder also allows walking onto the next floor without
Jump. Plan before the head hits a lip. Search bounded plausible floor heights
and inboard destinations; require full standing room, footprint support and
a swept clearance/rise/cross/settle route. Revalidate every stage. The normal
up/onto-floor/walk-away/return/down loop must work without precision timing.
Test thin/thick lips, both clear faces, rotations and intermediate landings.
A closed roof/hatch/railing still blocks; reverse, safe Use, Jump and Drop remain
responsive. Removed support is never retained as an invisible foothold.

## 6. Scaffolding and obstruction feedback

### Building from the ladder

A stationary attached ladder is usable as a scaffold. Equip/switch the hammer,
aim freely, open/close its menu, choose/rotate/snap/place pieces, repair and remove
using the game's native controls. The hammer itself does not forbid mounting
or climbing. With no travel intent, or while a native building action/menu owns
input, hold the current anchor without passive climb cost or XP. Resume travel
only from deliberate valid input after the action; never replay queued menu
movement. Hammer animations may move the arms, not the physical anchor.

Preserve native building range, materials, costs, stations, wards, support,
placement validity, collision and refunds. Do not fake grounding, enlarge reach,
disable the player's collider or bypass an invalid-placement result. Allow only
verified native hammer actions through busy/action gates, not general combat.
Focused menus continue to own input; with gameplay focus Jump and Drop still
depart deliberately. Hammer snap/menu/selection actions must not fall through
to ladder Use-to-dismount. Other controls work normally outside the session.

After placing a connected ladder extension, refresh the bounded connected run
while preserving the current face and anchor. The player can climb into the
new section without remounting; no per-frame world scan or crossing a gap,
ambiguous join or parallel shaft. Removing the active supporting ladder releases
normally; removing an unrelated piece does not. Custom climb pose/IK must allow
native hammer presentation and restore correctly afterward. See the addendum
for exact source risks and R36–R43 acceptance.

### Obstruction wording

An error blaming **another building piece** is allowed only when the relevant
query identifies an actual blocking native `Piece` other than the selected
ladder/its connected run. Record blocker identity and query stage in bounded
diagnostics. A piece behind the usable face, the ladder's own collider,
distance failure or missing support is not that reason.

Terrain and actors still physically block unsafe routes; give appropriate
terrain/occupied-path/general-clearance feedback without falsely blaming a
building. Unknown classification or query overflow refuses safely with a
neutral reason. Do not remove collision to eliminate an error. Localize
separate reach, unavailable-control, obstructed-path and no-landing messages.
Retire obsolete midair-unavailable text and repeated message spam.

## 7. Stamina, skill and level-20 slide

Select mode from current input, **current learned** level and native stamina.
Release movement to hold; release Run for normal descent; reverse to leave
slide immediately. Exhaustion falls back to normal climb/hold, never forced
detachment. Apply speed through planning and actual motion; the old 1.8 m/s
clamp must not cancel the boost while stamina is charged.

Charge once for observed eligible distance divided by full-input mode speed,
so analog input has consistent cost per metre. Budget maximum effective cost
before issuing a paid step. Keep one bounded pending step and settle actual
displacement once before another paid step or voluntary Use/Jump/Drop. Ownership
loss discards pending state without a remote write. Apply native world/status
multipliers once. No charge for blockage, holds, overlays, failed grabs or
mount/exit alignment; teleports/knockback are not climbing distance.

Register the stable skill in plugin Awake using Jotunn `SkillConfig`:
`Identifier` as above, `$cl_skill_climbing`, `$cl_skill_climbing_description`,
an original readable icon, `IncreaseStep=1`. Store `AddSkill`'s returned
`Skills.SkillType`; `SkillManager.GetSkill` returns a definition, not progress.
Read the player's native learned level; do not assign levels in production.
Use native skill UI, notifications, character save/load and death loss,
protection and world settings. No parallel save or fixed death-loss percentage.
Verify actual installed-game behavior on disposable characters.

Award native RaiseSkill once on the owner for actual normal/fast climb travel.
Carry unawarded fractional metres across ordinary mounts within one character/
world session; clear at logout or character/world change. No XP from holding,
blocking, catch, slide, jump/fall, mount/exit, correction or button spam. Native
earned within-level progress must survive normal save/load; transient
unawarded distance need not survive a full exit.

Slide requires learned level >=20.00, not a buffed level or past unlock. Test
19.99, 20.00 and above; native loss below 20 relocks subsequent slides. Bound
scaling to 0..100 and reject nonfinite input. No slide XP or base-speed skill
bonus. Saving without the mod is not promised to retain unknown skills.

## 8. Names, snapping and animation

Use **Wood Ladder**, **Copper Ladder**, **Iron Ladder** in all current English
display surfaces: hammer, hover and repair text. Preserve prefab IDs and
translation keys. Other languages use proper localized equivalents.

Preserve native hammer panel, snap modes, cycling, transforms and bound-key
hints. Inspect the current formatter first. Use native Top/Bottom and native
or product-localized corner labels; never expose `snap_bottom_left` or raw
`$` tokens. Fix the narrow label integration, not the entire panel. Localize
new skill/action/blocked hints too.

Complete [VANILLA_ANIMATION_BASELINE_2026_10_26.md](VANILLA_ANIMATION_BASELINE_2026_10_26.md)
before implementing replacement motion. Analyze genuine native metadata and
playback so sampling, cadence, blends and fluidity fit the installed game.
Use [LIMB_FRAME_REFERENCE_2026_10_26.md](LIMB_FRAME_REFERENCE_2026_10_26.md) and
its per-frame CSVs for original limb/body contact planning and capture comparison;
finalize the provisional gait/easing after the native baseline. The reference
sample count is not gameplay FPS or an instruction to quantize pose updates.
Inspect the installed rig/controller and compatible Unity asset pipeline.
Codex authors original climb/hold/slide/transfer motion from the supplied visual
references, fitted to the installed rig. Use original clips adapted for in-place
presentation or a scoped original procedural solution. Meshy purchase and stock
animation exports are outside the selected route; the GIFs are reference only.
Follow [ANIMATION_REVIEW_2026_10_26.md](ANIMATION_REVIEW_2026_10_26.md): the owner
rejected the submitted arm/leg motion and then selected original authoring.
Claude keeps gameplay authority and lead integration; Codex owns animation in
an isolated worktree. An uninvolved reviewer assesses its work. R25 must prove the
replacement on the actual character and rungs, including supported top exit.
Traversal phase/cadence follows actual signed travel; mount/catch/finish also
follow accepted transition progress. Physics owns
movement; equipment stays intact. Overrides are per character and restore
only our state on every exit/takeover. Prefer embedded original animation assets; loose
assets need package/hash/install coverage and extension of the current strict
six-file verifier before they can pass its gate. That tooling change is not a
reason by itself to defer animation. No extracted game assets ship.

Observer presentation needs a verified path; local playback alone does not
replicate. Prefer sufficient existing replicated state. Necessary new
presentation-only sync needs a documented independent design review, owner
validation, bounded values and stale/late-join cleanup before integration.
No remote motor authority, per-frame movement RPCs or persistent player-ZDO
keys. If sound custom animation cannot be completed, record the concrete
inspection/attempt or failed prototype, retain the tested walk fallback and
mark `CUSTOM_ANIMATION_DEFERRED`. This never waives the required mechanics.

## 9. Lifecycle, multiplayer and completion

One locally owned player has one temporary session. Check ownership before
physics, pose, stamina and XP writes. Destruction, moved/invalid pieces, death,
teleport, lost ownership, logout, world switch, disabled climbing and exceptions
restore control without overwriting another controller's new state. Preserve
ordinary fall damage, idle vanilla play, bounded queries and bounded logging.

All clients/server require the same candidate under product compatibility.
Test host/client, simultaneous/opposite-face use, occupied routes, late join,
reconnect, removal, restart, mismatch and dedicated operation. Missing real
endpoints are BLOCKED. Foreman's legacy motor remains an explicit conflict
refusal; do not unpatch or silently reconfigure it.

Continue the actual local branch after reconciling dirty work and
PR [#433](https://github.com/Weakened/ConcernedCatMods/pull/433), linked to issues
[#430](https://github.com/Weakened/ConcernedCatMods/issues/430),
[#431](https://github.com/Weakened/ConcernedCatMods/issues/431) and
[#432](https://github.com/Weakened/ConcernedCatMods/issues/432). Separate worker
worktrees are allowed; the lead alone integrates, builds under the existing
lock, installs and operates Valheim. Never reset ongoing work to an old bundle.

Follow [TEST_PLAN.md](TEST_PLAN.md): applicable A/B/T/L/P and R01–R43 before
owner solo readiness, plus actual M/network rows before release review.
Historical PASS rows do not transfer. Source review, artifact identity and
gameplay are separate gates. `IDENTITY_MATCH_ONLY` with
`gameplayVerified=false` and `binaryProvenanceVerified=false` are neither
gameplay acceptance nor proof of which source compiled the DLL. Import the new mod ZIP,
not the `.r2z` dependency profile.

Return full source SHA, new version, verified/package/installed DLL hashes,
current-profile startup fingerprint, final evidence, and the identical tested
ZIP in the actual Windows Downloads Known Folder. No public publishing,
release tag or main merge. Final status is `READY_FOR_OWNER_SOLO_TEST`,
`READY_FOR_RELEASE_REVIEW` or `NOT_READY` with explicit remaining rows.
