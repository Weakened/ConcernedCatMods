# Concerned Ladders — specification v0.1

Owner request: Eren, 2026-10-24. Source baseline: `89ce8b68e05c4494e5d14165482eca8d2486c786`.
Status: development source candidate; see `STATUS.md` for evidence, not this specification.

## 1. Player outcome

Build a compact vertical route beside a wall using ordinary hammer placement.
Approach from a supported landing, interact, climb continuously up or down,
stop, reverse, and step onto a real landing without being teleported through
the floor or wall. Wood, copper and iron are the required materials. A natural
climbing animation is desirable; ordinary locomotion is an accepted fallback.

The feature is an independent product in ConcernedCatMods. It does not depend
on the settlement/NPC mods, and the owner can test it in a separate Thunderstore
profile. The last release/test step is an actual disposable-world run.

## 2. Decisions fixed for this candidate

| ID | Decision |
|---|---|
| D01 | Product name Concerned Ladders, version 0.1.0, independent DLL/GUID/package. |
| D02 | Three 2 m modules: wood, copper, iron. Same geometry and climb speed. |
| D03 | Hammer placement, native material discovery/cost/station/ward/support/repair/refund. |
| D04 | Explicit Interact mount. No auto-mount in the initial candidate. |
| D05 | Forward/backward or remapped controller input climbs; neutral holds; camera remains free. |
| D06 | Interact requests a safe landing; Jump deliberately releases. No blocked-exit teleport. |
| D07 | 1.8 m/s climb speed, no stamina charge and no climbing skill for this candidate. |
| D08 | Reuse BCL-only Shared/Ladders primitives; new narrow adapter for custom pieces. |
| D09 | Walking locomotion fallback first. No claim of authored climbing/IK until implemented and observed. |
| D10 | No NPC/carts/ships/moving-platform traversal in this work. No wall/cliff climbing. |
| D11 | All participants, including a dedicated server, need the same candidate. Exact patch version check for alpha. |
| D12 | Initial clean profile excludes Foreman. If its legacy ladder motor is present, refuse mounting with an explanation. |
| D13 | Bones are a later optional wood-and-bone appearance, not an additional blocker or progression requirement. |
| D14 | BepInExPack 5.4.2351 and Jotunn 2.30.2 are the new product's pins; preserve other products' pins. |
| D15 | No publishing, tagging, main merge or ready-to-run claim without the required evidence. |

All values above are design choices, not assertions about native Valheim values.
Late-game material/biome/boss names are not needed in owner-facing instructions.

## 3. Identity and persistence

| Item | Contract |
|---|---|
| Source | `src/ConcernedLadders/` |
| Assembly | `TheConcernedCat.ConcernedLadders.dll` |
| Plugin GUID | `com.theconcernedcat.valheim.concernedladders` |
| Thunderstore | `TheConcernedCat-ConcernedLadders` |
| Prefabs | `tcc_ladder_wood`, `tcc_ladder_copper`, `tcc_ladder_iron` |
| Clean development profile | `TCC-Ladders-Dev` |
| Config | BepInEx's plugin-GUID `.cfg`; `General/ClimbingEnabled=true` |
| Native saves | Ordinary custom build pieces; no climb sessions persisted into a save |

Prefab IDs are permanent after the first public placement. Never rename them to
clean up code. Changing the climb setting must keep the prefab registration
active. An unavailable climb adapter should leave the piece identifiable and
removable. No hidden custom RPC, no network authority takeover and no new keys
written into a vanilla player ZDO are part of this candidate.

Unlike Foreman's historical vanilla-piece idea, these are custom world objects.
Removing the mod can remove its pieces when a world is loaded/saved. Document
backup and removal with the mod still installed. Test only in disposable worlds;
do not open or edit the owner's real world or character files.

## 4. Building and artwork acceptance

| Variant | Resources per section | Station | Initial health | Structural donor |
|---|---|---|---:|---|
| Wood | 4 Wood | Workbench | 200 | `woodwall` |
| Copper | 2 Copper + 1 Wood | Forge | 400 | `woodiron_pole` |
| Iron | 2 Iron + 1 Wood | Forge | 800 | `woodiron_pole` |

Copper and iron intentionally inherit the iron pole's support behavior. The
donor's ordinary support/decay logic is retained; no guessed Copper material enum
or infinite support. These costs/health values can be tuned only after a concrete
test shows a problem; they are not copied vanilla balance claims.

Geometry: 2 m vertical span, 0.8 m visible width, fixed local endpoints `(0,0,0)`
and `(0,2,0)`, open climb face local +Z. Rails stand off the wall; rung pitch is
one third of a metre. The motor's actual capsule clearance determines the player
offset. The physical shape and visual silhouette must align at every rotation.

Six direct-child native `snappoint` transforms cover top/bottom centres and the
four corners of the native 2 m wall grid. Verify their actual usefulness and
remove a misleading snap only with a recorded placement reproduction. A snap
must not accidentally attach a run through a wall or rotated face.

Meshes are original runtime-created rails/rungs. Materials are referenced from
loaded donors by known names, then copied before modification. Three durability
visual groups are wired to WearNTear; collision must remain outside groups that
are switched on/off. No copied game meshes, textures, animation clips or DLLs are
shipped. This is a functional first art pass; its appearance is not owner-approved.

Requirements B01–B08:

1. Three unique readable hammer entries with correct icon, name and cost.
2. Supported wall placement and rotation at native build increments.
3. Two and ten stacked segments connect without seams; mixed materials work.
4. Invalid support/collision and forbidden ward placements remain invalid.
5. Real materials are charged once, repair works, dismantling refunds once.
6. New/worn/damaged visuals never resurrect a donor wall or invisible collider.
7. One cold start and world A → menu → world B do not duplicate registration.
8. Save/reload and zone reload preserve the same pieces and prefab identities.

## 5. Traverse acceptance

### Mount

Interact is deliberate and distance-limited. The player must be locally owned,
alive, available for normal movement, outside construction/teleport/attach
takeover, and standing on support. A grounded intermediate landing is allowed.
Mid-air ladder catches are excluded from this version. A whole-capsule sweep
must establish a clear route onto the climb line. Never pull a player through a
wall, lock them into a ceiling, or snap them to the far end.

### Climb and hold

The local player's processed movement actions drive vertical intent; camera yaw
and pitch do not reverse the meaning of forward/back. Remapped keyboard and
controller must both be tested. Neutral holds; reverse immediately changes
direction; no gravity creep at rest. Menus/chat/map/inventory pause climb intent.
Movement uses one owner and one bounded motor path. Colliders remain enabled.

Only exactly aligned, touching endpoint neighbors form a run. Nearby parallel
ladders, opposite wall faces, gaps and turned modules do not connect. The initial
run search must include the whole loaded chain, not just a small sphere at the
foot. The runtime caps pathological chains and reports a refusal instead of
continuing onto unknown geometry.

### Exit

At a supported top: rise enough to clear the ledge, then cross into a validated
standing destination through a clear path. Test headroom, floor lip and side
railings. Destination-only overlap is insufficient. At a blocked top, remain
attached and explain; descending and intentional release remain available.

At the bottom, require real support and capsule clearance. At an intermediate
landing, Interact may leave only through a safe checked transition. Jump is a
deliberate release, followed by ordinary falling and fall damage. Missing or
destroyed ladder/platform releases the player safely; it never grants immunity.

Revalidate the path during a multi-frame exit. A platform removed or placed
during the transfer cannot leave a player floating or embedded.

### Lifecycle and ownership

Requirements T01–T12:

1. Mount, ascend, hold, reverse, descend, both end exits, middle-landing exit.
2. Smooth mount/transfer with no endpoint teleport.
3. Continuous 10-section and mixed-material climbs.
4. Closed ceilings, walls, doors/hatches and occupied landings block traversal.
5. Proper local player ZNetView ownership; no other body movement.
6. Destruction, removal, piece movement/rotation, unload and invalid network view release.
7. Death, respawn, teleport, logout, world switch and plugin cleanup restore control.
8. The pre-existing gravity/physics/animation state is restored deliberately;
   do not blindly enable another system's disabled collider.
9. Ordinary fall tracking after let-go; no mid-air re-mount exploit.
10. Two simultaneous climbers never share mutable anchor/session state.
11. No motor ownership conflict with Foreman; don't unpatch third-party code.
12. Exceptions restore control and log a useful bounded diagnostic, without
    raw local filesystem paths or per-frame spam.

## 6. Animation and sound

The acceptance baseline is the existing walking cycle: motion while moving,
idle while holding, coherent direction/speed on reversal, no T-pose and no stuck
animation after release. Observer clients must see coherent movement. The normal
camera, equipment, emotes, attacks and movement must recover after traversal.

A custom loop is optional after traversal passes. If implemented, author an
original clip or use a legally permitted asset, document provenance, isolate it
to this player's climb state, match phase to real climb distance, blend in/out,
and retain the tested walk fallback. Never use an attack animation with gameplay
events, replace every player's controller globally, or present IK as complete
without an in-game recording. Do not add a large animation dependency by default.

## 7. Multiplayer and compatibility

Custom pieces use native build placement/network persistence and Jotunn's
network compatibility policy. The owner of each player's body drives only that
body. The piece owner is not the authority to move another player's character.
No ladder reservation, inventory transfer or shared per-ladder attachment point.

Test host and client separately, then both climbing, then late join, destruction,
disconnect/rejoin and world restart. A second player blocking the route may
cause a safe hold; no ghosting through that player. Dedicated-server behavior
needs its own run. Missing or mismatched mod must produce a clear compatibility
failure, not an unknown-prefab world or silent unmodded join.

Foreman's historical ladder hooks remain untouched. The initial safe coexistence
policy detects its Harmony motor owner and refuses this mod's mount. Turning
Foreman's ladder feature off and restarting is the supported interim remedy.
Future arbitration is a separate bounded follow-up, not permission to globally
disable or remove Foreman.

## 8. Evidence and handoff

The local continuation agents use `CLAUDE_PROMPT.md`, `CODEX_PROMPT.md`,
`ARCHITECTURE.md`, `TEST_PLAN.md`, and `PROFILE_SETUP.md` together. They may use
subagents in isolated worktrees. The lead alone integrates, builds against the
game under the existing machine-wide lock, installs and operates the test game.

GitHub issue and branch creation were refused by the integration (HTTP 403), so CL-001–003
are local work-item identifiers, not invented issue numbers. Create/link real
issues when authorized GitHub write access is available. Do not claim this source
is on main until it is observed there.

Ready for owner testing requires a real successful `scripts/verify.ps1` summary,
exact source commit and DLL/package SHA256, installed dependency versions, the
separate profile selected, an observed clean game load, and the critical single-
player matrix completed by the implementing agent. Mark every unrun row. A pure
math test, syntax check, running process, generated profile archive, or successful
file copy is not evidence of a working game.
