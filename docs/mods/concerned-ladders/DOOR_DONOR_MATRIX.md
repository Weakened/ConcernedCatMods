# Concerned Ladders: door donor matrix and floor-hatch design (CL-004 #485, shared with CL-005 #486)

**Read from the installed game, not from a published list. This is a design and a census, not an in-game
observation.** Every placed-piece, interaction, traversal and multiplayer row is `OWNER GO-AROUND PENDING`
(owner playtest checklist #470). The game-free model this document describes is
`src/ConcernedLadders/Domain/Doors/` and its tests are `src/ConcernedLadders.Tests/Doors/`.

## 1. Source and method

| Item | Value |
|---|---|
| Game | Valheim **1.0.21** (`Version.CurrentVersion` in the installed `assembly_valheim.dll`) |
| Prefab source | SoftRef bundle `c4210710` (756,139,651 B, modified 2026-10-28) with script bundle `86c3d76e`; SoftRef hash `26349cd2a5c8` |
| Reader | UnityPy 1.25.4 in an isolated scratch venv (owner-authorized, read-only). The bundles carry type trees, so every field below is the serialized value. Nothing was extracted, copied or committed. |
| Buildability | Membership of `_HammerPieceTable.m_pieces` (398 pieces in 1.0.21) |
| Usage tags | Each hammer piece's own `Piece.m_usage` (Doors = flag `0x40000`), read for all 398 (`usage.json`) |
| Localization | English column of the game's own localization table |
| Door behaviour | `Door` decompiled from the installed assembly (ilspycmd), see section 4 |
| Census scripts | `C:\code\cc-integration\cl-ladders-devtools\doors\` (`census.py` -> `census.json`, `floors.py` -> `floors.json`, `usage.py` -> `usage.json`, `clips.py` -> `clips.txt`; local only, outside the repository) |
| Installed overlaps | The build profile `TCC-Gunnar-Test` holds BepInExPack and Jötunn only: no building, door or input mod to report. No compatibility with any other mod is claimed. |

## 2. Census: every native prefab with a `Door`, and the hammer's Doors tag

23 prefabs in the main bundle carry a `Door` component. The hammer's Doors usage tag (`$tag_doors`, English "Doors and Windows"; `m_usage`
flag `0x40000`) holds 15 pieces: the 12 buildable Door prefabs and three that carry no `Door` (the two grausten windows and
`crystal_wall_1x1`). No hammer piece with a `Door` is missing the tag. The three cloth curtains named `hanging_door` are
in neither, and are recorded so their absence is a decision too. Each of the 29 has exactly one decision
(`DoorFamilies.Census`; a test fails if one is missing or doubled, and pins the count of each class).

### 2a. Families: one trapdoor each

All nine are in the hammer table, carry `Piece`, `ZNetView` (persistent, `m_type` 2), `WearNTear`, `LODGroup`, `Door` and
an `Animator` (culling mode 0, always animate) on the root, and animate one child `door` that carries a `Rigidbody`
with `isKinematic` set (`floors.json`) and the leaf collider. None has a key item; `m_canNotBeClosed` and `m_invertedOpenClosedText` are false;
`m_checkGuardStone` is true; `m_openEnable` is empty; `m_hoverOffset` is 0. Every requirement recovers on deconstruction.
`m_minToolTier` is 0 everywhere. All nine are `m_canRotate`, comfort 0, space requirement 0.

| Donor prefab | English (token) | Usage tags | Station | Requirements | Health | Material | Burns | Roof wear | Supports | Animator, swing | Open / close sound | Trapdoor prefab | Trapdoor name |
|---|---|---|---|---|---:|---|---|---|---|---|---|---|---|
| `wood_door` | Wood Door (`$piece_wooddoor`) | Building, Doors | Workbench | 4 Wood | 400 | Wood | yes | no | yes | door_animator, 0.5 s | sfx_door_open / sfx_door_close | `tcc_trapdoor_wood` | Wood Trapdoor |
| `wood_gate` | Wood Gate (`$piece_woodgate`) | Building, Doors | Workbench | 12 Wood | 400 | Wood | yes | no | yes | door_animator, 0.5 s | sfx_door_open / sfx_door_close | `tcc_trapdoor_wood_gate` | Wood Gate Trapdoor |
| `wood_fence_gate` | Roundpole Gate (`$piece_woodfencegate`) | Misc, Doors | Workbench | 4 Wood | 400 | Wood | yes | no | yes | door_animator, 0.5 s | sfx_door_open / sfx_door_close | `tcc_trapdoor_roundpole` | Roundpole Trapdoor |
| `iron_grate` | Iron Gate (`$piece_irongate`) | Building, Doors | Forge | 4 Iron | 2000 | Iron | no | no | yes | door_animator, 0.5 s | sfx_metalgate_open / _close | `tcc_trapdoor_iron_gate` | Iron Gate Trapdoor |
| `darkwood_gate` | Darkwood Gate (`$piece_darkwoodgate`) | Building, Wall, Doors | Forge | 16 Wood, 4 Iron, 2 Tar | 1000 | HardWood | yes | yes | yes | darkwood_gate_animator, 1.0 s | sfx_darkwood_door_open / _close | `tcc_trapdoor_darkwood` | Darkwood Trapdoor |
| `stave_gate` | Timberwood Gate (`$piece_stavegate`) | Building, Wall, Doors | Forge | 24 Frostwood, 4 Gold | 1000 | HardWood | yes | yes | yes | darkwood_gate_animator, 1.0 s | sfx_darkwood_door_open / _close | `tcc_trapdoor_timberwood` | Timberwood Trapdoor |
| `piece_hexagonal_door` | Hexagonal Gate (`$piece_hexagonalgate`) | Building, Doors | Black forge | 8 YggdrasilWood, 8 Copper | 1500 | HardWood | yes | no | **no** | darkwood_gate_animator, 1.0 s | sfx_darkwood_door_open / _close | `tcc_trapdoor_hexagonal` | Hexagonal Trapdoor |
| `ashwood_door` | Ashwood Door (`$piece_ashwood_door`) | Building, Doors | Workbench | 1 FlametalNew, 5 Blackwood | 1000 | Wood | yes | no | **no** | darkwood_gate_animator, 1.0 s | sfx_darkwood_door_open / _close | `tcc_trapdoor_ashwood` | Ashwood Trapdoor |
| `flametal_gate` | Flametal Gate (`$piece_flametalgate`) | Building, Doors | Black forge | 16 FlametalNew | 3000 | Iron | no | no | yes | Flametal_gate_animator, 2.0 s | sfx_flametalgate_door_open / _close | `tcc_trapdoor_flametal` | Flametal Trapdoor |

"Roof wear: no" is `m_noRoofWear = 1`. All nine have `m_noSupportWear = 1` and `m_triggerPrivateArea = 1`. Damage
modifiers (`HitData.DamageModifier`): every wood/hardwood donor is pierce Resistant, chop Weak, poison and spirit
Immune; the hexagonal gate is additionally pickaxe Resistant; the iron gate is lightning, poison and spirit Immune only, and ash-damage resistant;
the flametal gate is wood-like plus fire Resistant and ash-damage resistant. Place effects are `vfx_Place_wood_wall` plus
`sfx_build_hammer_wood` (metal: `sfx_build_hammer_metal`). Materials, for the art pass: door_wood/woodwall(_worn),
fence_gate_mat(_worn), metalwall/fi_village_metal, DarkWoodGate_mat/_door/_worn, TimberGate_mat(_worn),
Hexagonal_mat/HexagonalBroken_mat, ashwood_door_mat(_worn), flametalgate_mat/flametalgateBroken_mat.

The hexagonal gate and the ashwood door have `m_supports = 0`: a piece resting on those two trapdoors gets no structural
support from them, as with their donors.

### 2b. Not families, with the reason (lead defaults pending owner confirmation, 2026-11-08)

| Prefab | Class | Reason |
|---|---|---|
| `piece_drawbridge` (Timberwood Drawbridge: 24 Frostwood, 2 FlametalNew, 2 Gold; workbench; 1000 HP) | Special semantics | A 4 m x 8 m hoisted bridge (`drawbridge2_animator`, 1.5 s down / 2.2 s up). Its open state enables a crush area (`m_openEnable = DrawBridgeAOE`, an `Aoe`), with rope and weight presentation. Not a hinged leaf; Frostwood is carried by the Timberwood family. |
| `piece_drawbridge_log` (Rustic Drawbridge: 10 RoundLog, 2 Silver, 2 TrophyWolf; workbench; 1000 HP) | Special semantics | The same hoisted-bridge semantics and crush area. Round log is carried by the Roundpole family. |
| `wood_window` (Wood Shutter: 4 Wood, 2 BronzeNails; workbench; 400 HP) | Window covering | A 1 m window shutter (`window_animator`, 0.5 s), a Door in code but not a passage. Its wood is carried by the Wood family. |
| `Piece_grausten_window_2x2`, `Piece_grausten_window_4x2` | No door state | In the Doors tag (`m_usage` 0x40014) with no `Door` component: fixed windows that do not open. |
| `crystal_wall_1x1` | No door state | In the Doors tag (`m_usage` 0x40214) with no `Door` component: a fixed crystal wall. |
| `piece_cloth_hanging_door`, `_blue`, `_blue2` | No door state | Named like doors, but neither in the Doors tag nor carrying a `Door` component: no open/closed state to reuse, and cloth cannot carry a walking player. |
| `piece_dvergr_wood_door` (Dvergr Door) | Not buildable | Has a Piece recipe (8 YggdrasilWood, 8 Copper, black forge, 500 HP, inverted open/close text) but is **not** in the 1.0.21 hammer table. |
| `dvergrtown_secretdoor`, `dvergrtown_slidingdoor`, `Morkborg_gate`, `dungeon_sunkencrypt_irongate`, `dungeon_forestcrypt_door`, `MountainKit_wood_gate`, `dungeon_queen_door`, `Morkhalla_Drawbridge`, `Morkhalla_jotun_gate`, `sunken_crypt_gate` | Not buildable | Location and dungeon doors. Several need a key (`BloodGoldKey`, `DvergrKey`, `CryptKey`) or cannot be closed; none is a construction donor. |


## 3. Recipes, discovery and lifecycle decisions

- **Cost and station = the donor's, exactly** (lead default pending owner confirmation): one 2 m leaf for one 2 m lid.
  Native piece discovery follows the requirements and the station, so a trapdoor appears when its donor does. The piece
  adapter must set `PieceConfig.Requirements` explicitly from `DoorFamily.Costs` and then **check the clone's
  `Piece.m_resources` against them**: Jötunn's clone keeps donor requirements when the array is empty, so an empty array
  is never a way to clear them.
- **Health, material, burn, roof wear, support, damage modifiers, effects, ward behaviour**: inherited from the cloned
  donor, unchanged. The adapter checks the installed donor's health, material, burn, station, cost and door fields against the pinned matrix before cloning, and refuses the
  family (logged, not registered) if the installed donor changed, instead of guessing.
- **Repair, deconstruction refunds, guard stones**: native, through the cloned `Piece`, `WearNTear` and
  `Door.m_checkGuardStone`. Nothing is added: no key, no ownership lock, no automatic closing.
- **Names**: `$cl_trapdoor_<family>` tokens, Title Case English as listed. Prefab IDs `tcc_trapdoor_<family>` are a
  save contract from the first shipped build on.
- **Usage tags**: Building and Doors for all nine, so they sit together. The Roundpole donor's Misc tag and the
  Darkwood and Timberwood donors' Wall tag are not copied (section 7).
- **Uninstall**: as with the ladders (STATUS.md, uninstall row), the game deletes a placed piece whose prefab is unknown
  on load (`Destroyed invalid prefab ZDO`). Removing the mod destroys every placed trapdoor permanently, with no refund,
  and leaves a 2 m x 2 m hole in the floor; reinstalling does not restore them. The README says so beside the
  setting. The same happens to a placed trapdoor when `Trapdoors/Enabled` is turned off again, or when its family is
  refused at start-up (a changed donor), because the prefab is then not registered in that session.
- **Multiplayer: the setting must match on every machine** (found in the slice-2 review). `Trapdoors/Enabled` is a
  local setting that nothing synchronizes, and Jötunn's version check does not compare it. A host or dedicated server
  that cannot create an object takes ownership and destroys its ZDO (`ZNetScene.CreateObjectsSorted`, decompiled
  1.0.21: `Destroyed invalid prefab ZDO`), during the session, not only at the next load. So a client that turns it on
  while the host or server has it off loses every trapdoor it places, with no refund, and a client with it off sees an
  open hole with no collision where another player's trapdoor stands. Lead default pending owner confirmation:
  document this and keep the setting local; a server-synchronized setting is a decision for the owner, not taken here.
- **Off by default (slice 2)**: the pieces are registered only with `Trapdoors/Enabled` on (`TrapdoorSettings`,
  default false). With it off nothing is cloned, no token is added and the hammer menu is unchanged. Each family is
  checked and built on its own; a refused family is logged with every difference and never registered, and the other
  families and the ladders are unaffected. One attempt per session: a refused family is not retried.

## 4. What the native `Door` gives the clone, and what it does not

From the decompiled 1.0.21 `Door`:

- `Awake` takes the `ZNetView` on the same object, finds the **first `Animator` in its children**, registers the
  `UseDoor` RPC and polls `UpdateState` every 0.2 s.
- `Interact` (Use, not hold) checks `CanInteract`, then guard-stone access, then keys, then sends `UseDoor` with a
  direction: `Dot(transform.forward, userDir) < 0`. `RPC_UseDoor` (owner) writes ZDO `state` = 1 or -1 when closed,
  0 when open. Every client's `UpdateState` reads it, plays the open/close effects and sets the animator integer.
- `CanInteract` is true only while the animator's current state is tagged `open` or `closed`, so a door cannot be
  re-used mid-swing. `GetHoverText` gives the localized `[$KEY_Use] Open/Close` line and `$piece_noaccess` in a ward.

So the clone keeps `Piece`, `WearNTear`, `Door`, `ZNetView` and the donor's `Animator` with its controller on the
root. The state, its
replication to observers and late joiners, its persistence, ward behaviour, hover text and remappable Use binding are
all native. The **visible lid is ours**: native doors swing about a vertical axis, either way depending on the user's
side. The trapdoor's lid has one swing direction about a horizontal hinge, so it reads the native door state
(non-zero = open, either sign) and moves over the donor's own clip length (0.5 / 1.0 / 2.0 s), keeping the hidden
animator and the lid in step so `CanInteract` still gates the next press. The animator's culling stays at
always-animate so it advances with no renderer.

**Which copy of the state the lid reads (slice 3).** The ZDO integer `ZDOVars.s_state` itself, through the piece's own
`ZNetView`, read only, rather than the animator's `state` integer this section first named. They hold the same value:
`Door.UpdateState` copies the ZDO into the animator, but only on its 0.2 s poll and only once `Door.Awake` has run, while
the ZDO is readable from the lid's first frame, so a lid that loads with its zone or for a late joiner starts in the
stored pose instead of waiting for the poll. The native door already provides everything the lid needs: **slice 3 adds
no RPC, no ZDO field, no ownership claim and no write of any kind.** The lid's own progress is a local float on each
client.

**Strip set.** Stripping only the leaf would leave a vertical gate frame and wall snaps on the hatch. Every donor child
other than the animated `door` transform goes, and `door` keeps only its transform (the animator writes it, nothing
reads it). From `census.json`:

| Donor | Children removed besides the `door` leaf's renderers and collider |
|---|---|
| all nine | every `$hud_snappoint_*` (the vertical door's own top/bottom/mid/edge/inner/corner snaps) |
| `wood_door`, `wood_gate`, `wood_fence_gate` | `New`, `Worn` (frame meshes with box colliders), `pole_top_snow` |
| `iron_grate` | `New` (frame mesh with box collider) |
| `darkwood_gate`, `stave_gate` | five `collider` boxes, `New`, `Worn`, `beam_2m_snow`; stave also `broken`, `destruction` |
| `piece_hexagonal_door` | `collider`, `DvergrDoor_Snow`, `destruction` |
| `ashwood_door` | `Frame` (mesh collider), `Worn`, `Ashwood_door_destruction` |
| `flametal_gate` | `collider`, `flametalgateDestruction` |

`WearNTear.m_new`, `m_worn` and `m_broken` point at those `New`/`Worn`/`Frame`/`broken`/`door` children, and
`WearNTear` toggles them by health. The adapter reassigns all three to the hatch's own new/worn presentation roots (or
the one root, for donors whose three are the same object), so health display keeps working and nothing points at a
destroyed child.

**Corrected in slice 2** (a second read-only probe of the same bundles, `wearextra.py` -> `wearextra.json` beside the
census scripts, read the `WearNTear` fields `census.py` had not dumped):

- The destruction children are not only presentation: they are `WearNTear.m_fragmentRoots` on three donors
  (`flametalgateDestruction`, `destruction` on the hexagonal gate, `Ashwood_door_destruction`). The other six have an
  empty list, which is the native whole-object fragment path. The adapter sets the empty list on all nine, so no
  fragment root points at a removed child.
- `m_snow` points at the removed snow child on seven donors (`pole_top_snow`, `beam_2m_snow`, `DvergrDoor_Snow`); the
  adapter clears it with `m_snowWorn`, `m_snowBroken` and `m_wet` (null on all nine). The hatch has no snow overlay yet
  (wood_floor's own is `floor_2x2_snow`): an intentional difference until the art pass.
- `m_comOffset` is (0, 2, 0) on darkwood, timberwood and flametal: the centre of mass of a 4 m leaf. The adapter sets
  it to zero on all nine, the hatch's own centre. `m_roofCheckOffset` is 0 on all nine and on wood_floor, so it is
  inherited unchanged.
- The `LODGroup` is **removed**, not rebuilt, as on the ladders (observed in game for them): the hatch is a few boxes,
  and a rebuilt single LOD would only add an invented cull height. A real LOD set belongs to the art pass.

**What the adapter builds** (`Domain/Doors/TrapdoorBlueprint.cs`, walked by `Pieces/TrapdoorPieceRegistration.cs`;
since CL-005 slice 2 the donor read, strip, wear wiring and verification are `Pieces/DoorPieceKit.cs`, shared with the
cellar doors):
the donor's root components stay; the root gets the four frame bands as box colliders on their own children (never on
the root: the ghost's blocked-by-player test skips root colliders), one presentation root `new` with the four bands in
the donor's frame material and, where the donor has a separate worn root, a `worn` root in its worn material
(`m_worn` = `m_broken` = `worn`; for iron, hexagonal and flametal all three are `new`, as the donors' are one object);
a `lid_hinge` pivot on the hinge axis carrying a kinematic `Rigidbody`, the lid's box collider, the lid in the donor
leaf's material and two hinge straps (a darkened copy of that material) that mark the hinge side in the ghost; the
four corner snap points; `Door.m_name` and `Piece.m_name` = the trapdoor's token. The materials are found by name in
the donor's own renderers before anything is stripped. The lid's collider sits on a rigidbody, so `WearNTear` leaves it
out of support bounds, and the build ray fails when the first thing it hits is on a rigidbody: aiming at a lid places
nothing, as aiming at a native door leaf does.
Support comes from the frame bands.

## 5. Floor hatch geometry (`HatchGeometry`)

Hatch-local axes are the piece's (x right, y up, z forward). Measured against the installed `wood_floor`: corner snap
points at (±1, 0, ±1), collider top **y = 0.0968**, bottom **y = -0.0329**.

| Quantity | Value |
|---|---|
| Outside module | 2.0 m x 2.0 m, top flush with wood_floor (y 0.0968), frame as deep as wood_floor (to y -0.0329) |
| Frame | four bands 0.2 m wide: front and rear full width, sides between them |
| Aperture (frame inside) | 1.6 m x 1.6 m |
| Lid | 1.58 m x 1.58 m x 0.08 m (0.01 m gap each side), top flush |
| Hinge axis | along x through the lid's **top rear edge** (y 0.0968, z +0.79): no lid point ever passes behind the hinge or below the lid's closed bottom, so the frame needs no rebate (tested at every sampled angle) |
| Open | 90°: the lid stands upright, its front face at z +0.71, top 1.58 m above the floor |
| Clear opening when open | x ±0.8, z -0.8 to +0.71 (1.6 m x 1.51 m) |
| Snap points | the four wood_floor corners with the native `$hud_snappoint_corner` token: a hatch takes a floor tile's place beside wood floors, and sits beside stone flooring exactly as a wood floor does (stone floors snap at their own top, 0.5 m) |
| Ladder | hung inside the opening from its **front** edge, facing the hinge: section origin (0, -2, -0.87), rails' back face 0.01 m inside the frame, top level with the snap plane as `LadderEdgeSnap` hangs one from a floor; the closed lid's underside is 1.7 cm above the ladder top. A front-lane climber (lane from `ClimbPolicy.LaneDepth`) of capsule radius r clears the front band by 0.23 m, the open lid by 1.28 - 2r and the sides by 0.8 - r: 0.48 m and 0.40 m at r = 0.4 (`ClimberMargins`). |
| Passage lane | front lane only. The rear lane's capsule centre (z -0.85 - r) lies under the front frame band and the floor beyond it, so a climber on the back of the ladder meets the floor, as on any ladder hung from a floor edge. |
| Ghost | the rotation decides the hinge side; the hinge is always the piece's rear edge (shown in the ghost by the two hinge straps on the lid's rear edge, slice 2) |

The ladder's top exit leaves forward over the front frame band onto the floor beyond it; the open lid stands behind the
climber. Placing a hatch removes no floor, digs nothing, adds no stairs and spawns no ladder.

**Ladder anchor (slice 4, `HatchLadder.Anchor`, `Pieces/LadderSnapAlignment`).** Aimed at a trapdoor's **frame** with
Cycle Snap on **Auto** or **Top**, a Concerned Ladders ladder ghost is put at the anchor above, in the hatch's own rotation
(the hammer's rotation is ignored there, as it is for ladder-on-ladder placement: a turned ladder would not leave the
climber in the opening). Auto does so only when nothing solid lies under the anchor for most of a section's height
(`HatchLadder.RoomProbe`, tested from just under the frame); otherwise, and in **Bottom**, **Left side** and **Right side**,
a trapdoor is the 2 m floor tile it is to the existing floor-edge snap. The anchor is checked against the game's
placement reach to its joint (the middle of the front band's inner face) and refused, not moved, beyond it. More
sections go below it by aiming at the ladder, as on any column. The anchor equals what `LadderEdgeSnap` produces for a
floor edge on the front band's inner face (tested), so the two conventions cannot drift apart. **Aim at the frame, not
the lid:** the game's own placement ray (`Player.PieceRayTest`, decompiled 1.0.21) skips any collider that carries a body,
and the lid does (its kinematic body), so a ghost aimed at the lid is hidden by the game before this adapter runs
(independent review of 5a5e2cb, blocker 1). Making the lid itself a placement target would need its own design. The room
test does not ignore a ladder already hung in the anchor, so Auto aimed at a hatch that already has one falls back to
the floor edge; aim at that ladder to extend it. Lead default pending owner confirmation: which modes anchor, Auto's
fall-back, and frame-only aiming.

## 6. Motion, collision and obstruction (`HatchMotion`)

- The lid's transform and its collider (a kinematic body, as native door leaves are) move together from one progress
  value, so motion and collision agree through opening, reversal and closing. The frame's colliders never move.
- **Obstruction policy: the lid never moves into anything but fixed world and the hatch itself.** The occupancy query
  covers characters, dynamic bodies (dropped items, carts, boats) and other building pieces. A frame's move is split
  into sub-steps of at most 5° of hinge turn (`SubSteps`; the lid's free edge then moves at most 0.14 m between checked
  poses, less than any character capsule's diameter); at the first sub-step whose pose would overlap, the lid holds at the last
  clear one and tries again next frame. A lid with someone or something on it, or a piece built on it, stays closed
  until it is clear; a lid closing over a head waits. Nothing the query covers is launched, pushed through a wall or
  shut in. A small falling item could pass between two checked poses; anything resting on the lid touches it and is
  caught by the first sub-step. The native state and hover text keep saying what was asked. A frame step is capped at 0.05 s, so a hitch is
  slower, not a jump.
- **Implemented in slice 3** (`Runtime/TrapdoorLid.cs`, on every clone; the frame step is the game-free
  `HatchMotion.Step` and `HatchMotion.Settle`). Each checked pose is one `Physics.OverlapBoxNonAlloc` of the lid
  collider's own box at that angle, shrunk by a 1 cm skin on every side (`HatchMotion.QuerySkinMetres`) so that
  touching is not overlapping, into a 32-entry shared buffer, triggers ignored; a full buffer counts as occupied.
  Because of the skin, the lid can close at most 1 cm past a surface it touches before the next checked pose sees it;
  a body resting on a closed lid is seen by the first 5-degree sub-step everywhere beyond about 0.12 m from the hinge,
  by the second beyond about 0.06 m, and by a later one nearer still. The guarantee is the check spacing: no two checked poses are more than 5 degrees
  apart, so anything spanning more than that much of the sweep is always seen; something thinner (a small falling
  item) can pass between two checked poses.
  The hatch's own colliders never count. On the `character`, `character_net`, `character_ghost`, `character_noenv`,
  `hitbox`, `vehicle`, `item`, `piece` and `piece_nonsolid` layers any collider blocks; on `Default` and `Default_small`
  only a dynamic (non-kinematic) body does; terrain and `static_solid` are not queried, so an open lid may stand into a
  slope or rock as a native door leaf may. Every name is one the installed game's own `LayerMask.GetMask` calls use; if
  one were missing, every lid stays shut and the start-up log says so. The lid turns by setting the hinge transform,
  never by velocity or `MoveRotation`, so it imparts no push, and it only ever stops at a pose that was checked clear (to within the 1 cm skin).
  A fault stops that lid where it is and logs once.
- **The first pose** (a piece placed, loaded with its zone, or seen by a late joiner) is the stored state's end of the
  swing when that pose is clear, which is what every other client shows. Contact is not occupancy (the skin above),
  so a closed lid with an item, a creature or a player resting on it loads closed under them (independent review of
  d195449, blocker 1: without the skin it would have loaded open and dropped them). When the stored pose is really
  occupied (someone stands in the opening where a loaded-closed lid would lie), the lid starts at the other end if that
  is clear, and the checked motion carries it to the stored state once the way is clear. When both ends are occupied
  it takes the clear pose nearest the stored end, scanning every 5 degrees, and only if no pose is clear does it take
  the stored end anyway. It settles in `Start`, before the first physics step after the piece appears, so a lid stored
  open is not briefly a closed collider. Lead default pending owner confirmation.
- **What this means for the person at the hatch** (a consequence of the policy, not an observation): a player standing
  with part of their body over the opening, for example on the 0.2 m frame band at the front, is in the lid's sweep, so
  the lid waits until they step back. A wall or other piece built over the rear frame band, where the upright lid
  stands, stops the lid short of upright. Both are OWNER GO-AROUND PENDING.
- **Clients**: each client moves its own copy of the lid against the positions it sees. Remote characters are
  interpolated, so two clients can briefly disagree about when a held lid resumes; each player's own client, which is
  authoritative for that player's collision, never moves the lid into them.
- **Ladder route**: the opening is a passage only when the lid is fully open and staying open (`IsPassage`, so no route
  is committed through a lid that was just asked to close); the lid is a landing only when closed and staying closed
  (`IsLanding`). A moving or held lid is never the top exit's destination. A closed lid
  is solid collision on the piece layer, so ascent is blocked by the existing capsule sweep.
- **Implemented in slice 4.** `Runtime/TrapdoorLid` is the production caller of `IsPassage` and `IsLanding`
  (`Passage`, `Landing`), reading the native state at the moment of the question, so a Use in this frame already
  counts; a lid that has not settled, stopped on a fault, lacks its parts or has no ZDO is neither. The rules are the
  game-free `HatchLadder`:
  - *Passage.* A route crosses a hatch when the climber's capsule, widened square by its radius around the 1.6 m
    opening, spans the frame's depth over the route (`HatchLadder.Crosses`; anything unmeasurable crosses). Then it
    needs a passage (`AllowsRoute`). Touching is not crossing: a capsule standing on the frame's top, or whose head
    meets its underside, within 2 cm (`ContactSlack`, the clearance's own skin), does not cross. `LadderRuntime` asks in
    three places, each before anything moves: the **top exit** (from the climb lane up to the highest landing it would
    look for: a lane under a lid that is not a passage has no top exit, and the climber is told the trapdoor is not open
    instead of "no landing"); **every exit route** it is about to commit (`PlanExitTo`: each leg of each concrete route,
    at both its ends, over that leg's own height span, `LegSpan`, so a landing on a closed lid or beside a hatch is not
    refused because the route started lower; review of 5a5e2cb, blocker 2); and a **descending entry** from the floor
    above (a mount from below never needs the lid open: a climber can always get on and open it from the ladder; this
    refusal is reported in preference to the other face's). A ghost or icon render (no ZDO) never shuts a route. Only
    leg ends are tested, not the straight legs between them: a leg between two ends outside an opening that passes over
    it does so above the lip, where the existing capsule sweep sees the lid's current pose. A bottom step-off under a
    closed lid onto ground exactly 2 m down stays under the frame at the low route lift (with the measured 1.85 m
    capsule, 7 mm to spare); onto a floor built 2 m down (its top about 0.1 m higher) it crosses and is refused, and the
    climber is then released standing at the foot, as on any ladder whose step-off has no route, never held. A lid that
    something holds short of upright (a piece over the rear band, section 6) is never a passage, so its hatch cannot be
    climbed through; that fails closed. The "not open" notice names the last reason seen; when a top exit is refused for
    several reasons it may name the trapdoor although another reason also applied (message only).
  - *Landing.* The landing search (`LadderClearance.TrySurface`, which every top, side and bottom landing and every
    footprint point uses) treats a ray that meets a lid that is not a landing as finding no landing at that point, before
    its slope test: a lid half way through its swing (normal y 0.71 at 45 degrees) would otherwise pass that test. The
    frame is always a floor; a trapdoor whose hinge is missing, so that lid and frame cannot be told apart, never is.
  - **Lid closing on a climber (lead default pending owner confirmation, the narrowest fail-closed choice).** The lid
    never moves into the climber (slice 3: it holds against characters). A route already under way through it finishes;
    no new route starts through a lid asked to close, even while it still stands fully open. A climber under a closed
    or closing lid holds on the rungs, is told the trapdoor is not open, and can still climb down, jump or drop. Nothing
    opens or closes the lid for them, and nothing detaches them.
- **Interaction from the ladder (slice 4).** An attached climber whose native hover target is a door, a trapdoor
  included, uses the door through the game's own `Interact` (from above or below, within the game's reach, with the
  donor's own permissions, key and ward checks); the same press does not also ask the ladder for a dismount
  (`ClimbControls.UseAsksDismount`), and the game's interact animation holds the climber on the rungs (an existing
  rule: a minor action holds, it does not release). A press the ladder did take as a dismount is not also delivered to
  a door in that frame once trapdoors exist (`ClimbControls.HoldsBackInteract`): the runtime reads the hover target in
  its own update, which can run before the game's, and a lid swinging under the crosshair can change it in between.
  Remapped keyboard and controller Use bindings are the game's own (`Use`, `JoyUse`). A Use while the native door is
  mid-swing does nothing, as on the donor (its `CanInteract` gate). Unverified: the game's interact animation turns the
  player to face the door's origin (the hatch centre, behind a front-lane climber), and the ladder's hold then turns
  them back; this may look like a half-second spin (in-game row below).
- **With `Trapdoors/Enabled` off** no trapdoor prefab exists, so `TrapdoorLid.Any` is false for the whole session, and
  every ladder path asks nothing of a trapdoor and behaves exactly as before (pinned by source: every consult is behind
  `TrapdoorLid.Any`).

## 7. Intentional differences from the donors

| Difference | Why |
|---|---|
| Horizontal hinge, one swing direction | A floor hatch opening down would block the ladder shaft. Both native open directions open the one lid. |
| Visible lid driven from the native state instead of the native clip | The donor clips rotate a vertical leaf about a vertical axis; reorienting them would also swing down. The timing is the donor's. |
| Lid holds against characters, dynamic bodies and pieces | Native doors sweep through characters; a lid rising under a standing player, an item or a cart would launch it. |
| Roundpole drops the Misc tag; Darkwood and Timberwood drop the Wall tag | All nine trapdoors sit together under Building and Doors; a floor hatch is not a wall. |

## 8. Remaining slices (each its own PR)

1. Piece adapter: **code-complete in slice 2, behind `Trapdoors/Enabled` (off)**. Clones each donor under Jötunn's
   inactive parent after `OnVanillaPrefabsAvailable`; checks the donor against this matrix first (`DonorMismatches`);
   applies the strip set and `WearNTear` reassignment of section 4 with its slice-2 corrections; builds the frame and
   lid from `TrapdoorBlueprint`; sets the requirements explicitly and reads them back from the clone
   (`RequirementMismatches`, with Jötunn's `JVLmock_` prefix removed); icons; localization; snap points; hinge straps.
   With it on, a placed trapdoor's native state, sounds and hover text toggle.
2. Lid runtime: **code-complete in slice 3**, behind the same setting. `Runtime/TrapdoorLid.cs` reads the native ZDO
   `state` (section 4), steps `HatchMotion.Step` with one bounded non-allocating overlap per checked sub-step
   (characters, dynamic bodies, pieces; section 6) while moving, and turns the one `lid_hinge` transform (which carries
   the kinematic body, the lid's collider and its visuals) by `Quaternion.Euler(angle, 0, 0)`. No network change.
3. Ladder integration: **code-complete in slice 4**, behind the same setting. Hatch anchor in `LadderSnapAlignment`;
   door Use while attached does not dismount; top exit, exit routes and descending entries need a passage; the landing
   search refuses a lid that is not a landing; the closed lid's collision blocks ascent (section 6).
4. Asset sources and export notes: **recorded** in `DOOR_ASSET_SOURCES.md`, shared with CL-005. The hatch is built from
   code, with no `.blend` or bundle. The record gives the object names, pivots, rest transforms, colliders, the curve,
   materials and the rebuild steps, and `DoorAssetSourcesTests` holds it to the code. The owner's items are in that
   record's section 11. The 1.1.0 candidate provenance template is in `STATUS.md`. CL-005 cellar doors reuse
   this matrix: #486 slices 1 to 4 (the model, the piece adapter, the leaf runtime and the ladder integration) are
   recorded in `CELLAR_DOORS.md`.

## 9. In-game rows (all OWNER GO-AROUND PENDING, #470)

Placement beside wood and stone floors and ladder endpoints, every family; walk over a closed lid; climb through an
open one and exit; re-enter from above; open/close from above, below and from the ladder; standing-on-lid and
closing-over-head holds; damage, repair, deconstruction refunds, guard stone; host/client, late join, zone and save
reload. None has been observed.

Slice 4 adds these, each OWNER GO-AROUND PENDING: a ladder ghost aimed at a trapdoor's frame in Auto and Top hangs
inside the opening against the front band facing the hinge, at every build rotation of the hatch, and Bottom and the side modes
behave as on a wood floor; Auto over a hatch on the ground falls back to the floor edge; further sections extend the
column below; climbing up under a closed lid stops below it with the "not open" notice; Use on the lid from the ladder
opens it without letting go, and once it is fully open the climber climbs through and steps onto the floor in front of
it; Use on it again from the ladder or from above closes it, and a lid asked to close while the climber's head is in the
opening waits; a climber below a lid that is asked to close holds and can climb down, jump or drop; from the floor above,
an open hatch's ladder can be mounted and descended, and a closed or closing one is refused with the notice; Use while
looking away from both the ladder and the lid still asks for a safe dismount; keyboard and controller, remapped Use;
the 0.5 s, 1.0 s and 2.0 s families; whether opening the lid from the ladder turns the climber round for a moment
(the interact animation faces the hatch centre); which notice a closed hatch's descending entry shows; a ghost aimed at
the lid is hidden (the game's own ray), aimed at the frame it anchors; a lid held short of upright by a piece over the
rear band is never climbed through; two hatches side by side, exiting from an open one onto a closed one; with the
setting off, every existing ladder row behaves as on 1.0.0.

Slice 3 adds these, each OWNER GO-AROUND PENDING: Use from above and from below opens the lid over the donor's swing
time (Wood, Roundpole and Iron 0.5 s; Darkwood, Timberwood, Hexagonal and Ashwood 1.0 s; Flametal 2.0 s) and closes it
again; open, it leaves the aperture clear with no residual collider and the closed lid is walkable again after closing;
the lid does not rise while the player, another player, a creature, a dropped item or a cart stands on it, and opens
once they leave; it does not close over a head in the opening; a lid held mid-swing resumes on its own; a second Use
mid-hold reverses it; a second client sees the same lid open and close, a late joiner and a reloaded zone show an open
lid open and a closed one closed; no physics launch, no log spam, no frame-time cost visible with many hatches.

Slice 2 adds these, each OWNER GO-AROUND PENDING: with `Trapdoors/Enabled` off, the hammer menu and the startup log
show no trapdoor; with it on, the log reports `9 of 9 registered`, the nine appear once each in the Building tab under
their Title Case names with a rendered icon, the cost and station of their donor, and are
discovered when the donor is; the ghost shows the hinge straps on the rear edge and snaps at the four corners
beside wood_floor; insufficient resources or invalid placement consume nothing; a placed hatch's frame and closed lid
can be walked on, Use toggles the hover text and plays the donor's sound (and, since slice 3, moves the lid), and hammer damage,
repair, worn display and deconstruction refunds behave as the donor's.
