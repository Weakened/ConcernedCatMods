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

23 prefabs in the main bundle carry a `Door` component. The hammer's *Doors and Windows* usage tag (`m_usage` flag
`0x40000`) holds 15 pieces: the 12 buildable Door prefabs and three that carry no `Door` (the two grausten windows and
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
  donor, unchanged. The adapter checks the clone's health and material against the pinned matrix and refuses the
  family (logged, not registered) if the installed donor changed, instead of guessing.
- **Repair, deconstruction refunds, guard stones**: native, through the cloned `Piece`, `WearNTear` and
  `Door.m_checkGuardStone`. Nothing is added: no key, no ownership lock, no automatic closing.
- **Names**: `$cl_trapdoor_<family>` tokens, Title Case English as listed. Prefab IDs `tcc_trapdoor_<family>` are a
  save contract from the first shipped build on.
- **Usage tags**: Building and Doors for all nine, so they sit together. The Roundpole donor's Misc tag and the
  Darkwood and Timberwood donors' Wall tag are not copied (section 7).
- **Uninstall**: as with the ladders (STATUS.md, uninstall row), the game deletes a placed piece whose prefab is unknown
  on load (`Destroyed invalid prefab ZDO`). Removing the mod destroys every placed trapdoor permanently, with no refund,
  and leaves a 2 m x 2 m hole in the floor; reinstalling does not restore them. The README must say so when the pieces
  ship (slice 2).

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
side. The trapdoor's lid has one swing direction about a horizontal hinge, so it reads the animator's `state` integer
(non-zero = open, either sign) and moves over the donor's own clip length (0.5 / 1.0 / 2.0 s), keeping the hidden
animator and the lid in step so `CanInteract` still gates the next press. The animator's culling stays at
always-animate so it advances with no renderer.

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
destroyed child. Destruction children are only presentation for the native break effect and are replaced by the
hatch's own. The `LODGroup` on the root is rebuilt over the hatch renderers.

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
| Ghost | the rotation decides the hinge side; the hinge is always the piece's rear edge (shown in the ghost by the frame's hinge hardware, next slice) |

The ladder's top exit leaves forward over the front frame band onto the floor beyond it; the open lid stands behind the
climber. Placing a hatch removes no floor, digs nothing, adds no stairs and spawns no ladder.

## 6. Motion, collision and obstruction (`HatchMotion`)

- The lid's transform and its collider (a kinematic body, as native door leaves are) move together from one progress
  value, so motion and collision agree through opening, reversal and closing. The frame's colliders never move.
- **Obstruction policy: the lid never moves into anything but fixed world and the hatch itself.** The occupancy query
  covers characters, dynamic bodies (dropped items, carts, boats) and other building pieces. A frame's move is split
  into sub-steps of at most 5° of hinge turn (`SubSteps`; the lid's free edge then moves at most 0.14 m between checked
  poses, less than any capsule's diameter); at the first sub-step whose pose would overlap, the lid holds at the last
  clear one and tries again next frame. A lid with someone or something on it, or a piece built on it, stays closed
  until it is clear; a lid closing over a head waits. Nothing the query covers is launched, pushed through a wall or
  shut in. The native state and hover text keep saying what was asked. A frame step is capped at 0.05 s, so a hitch is
  slower, not a jump.
- **Clients**: each client moves its own copy of the lid against the positions it sees. Remote characters are
  interpolated, so two clients can briefly disagree about when a held lid resumes; each player's own client, which is
  authoritative for that player's collision, never moves the lid into them.
- **Ladder route**: the opening is a passage only when the lid is fully open and staying open (`IsPassage`, so no route
  is committed through a lid that was just asked to close); the lid is a landing only when closed and staying closed
  (`IsLanding`). A moving or held lid is never the top exit's destination. A closed lid
  is solid collision on the piece layer, so ascent is blocked by the existing capsule sweep.
- **Interaction from the ladder** (next slice): an attached climber whose native hover target is a door uses the door,
  and the same press does not also dismount.

## 7. Intentional differences from the donors

| Difference | Why |
|---|---|
| Horizontal hinge, one swing direction | A floor hatch opening down would block the ladder shaft. Both native open directions open the one lid. |
| Visible lid driven from the native state instead of the native clip | The donor clips rotate a vertical leaf about a vertical axis; reorienting them would also swing down. The timing is the donor's. |
| Lid holds against characters, dynamic bodies and pieces | Native doors sweep through characters; a lid rising under a standing player, an item or a cart would launch it. |
| Roundpole drops the Misc tag; Darkwood and Timberwood drop the Wall tag | All nine trapdoors sit together under Building and Doors; a floor hatch is not a wall. |

## 8. Remaining slices (each its own PR)

1. Piece adapter: clone each donor under Jötunn's inactive parent after `OnVanillaPrefabsAvailable`; apply the strip
   set and `WearNTear` reassignment of section 4; build the frame and lid procedurally from `HatchGeometry` with copied
   donor materials;
   explicit requirements checked against the clone; icons; localization; snap points; hinge hardware in the ghost.
2. Lid runtime: read the animator `state`, step `HatchMotion` with one bounded non-allocating overlap per checked
   sub-step (characters, dynamic bodies, pieces) while moving, apply the pose to transform and kinematic collider.
3. Ladder integration: hatch ladder anchor in `LadderSnapAlignment`; door Use while attached does not dismount; top exit
   refuses a non-landing lid; closed lid blocks ascent.
4. Asset sources and export notes; CL-005 cellar doors reusing this matrix.

## 9. In-game rows (all OWNER GO-AROUND PENDING, #470)

Placement beside wood and stone floors and ladder endpoints, every family; walk over a closed lid; climb through an
open one and exit; re-enter from above; open/close from above, below and from the ladder; standing-on-lid and
closing-over-head holds; damage, repair, deconstruction refunds, guard stone; host/client, late join, zone and save
reload. None has been observed.
