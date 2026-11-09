# Concerned Ladders: sloped cellar doors (CL-005 #486): design record and game-free model

**Status: slice 1 of #486, the game-free model and contract only.** Nothing in the game changes: no piece is
registered, no setting is bound, no token is added, and no file outside `Domain/Doors` and its tests calls the new
code. The model is `src/ConcernedLadders/Domain/Doors/CellarGeometry.cs` and `CellarDoors.cs`; its tests are
`src/ConcernedLadders.Tests/Doors/CellarGeometryTests.cs` and `CellarDoorsTests.cs`. Every number below is a design
default measured against the installed game's `wood_floor` (Valheim 1.0.21, read in CL-004's census), **not an
in-game observation**. Every placed-piece, interaction, ladder and multiplayer row is `OWNER GO-AROUND PENDING`
(owner playtest checklist #470).

The donor census, the matrix of the nine native door families and the reasons for every exclusion are CL-004's,
in `DOOR_DONOR_MATRIX.md`. This record reuses them and adds only what the sloped geometry changes.

## 1. What a cellar door is, and what it is not

A sloped cover over an entrance: a fixed frame (two side cheeks, a low front panel, a high rear header) and two
leaves that meet at a central seam, each hinged at its outer side and opening outward. One Use opens or closes the
pair. Its base takes a 2 m wood floor tile's place in the floor grid, so it sits in a floor beside an opening that the
player made, against a wall or foundation edge behind it.

Placing one **excavates nothing, removes no floor or wall, generates no stairs, spawns no ladder and promises no
tunnel.** CL-006 (#487, the Iron Shovel) owns digging. There is one size, 2 m; a larger dig is covered with normal
floor pieces around the entrance.

## 2. Geometry (`CellarGeometry`)

Piece-local axes are the piece's: x right, y up, z forward. The **low end is the front (-z)**, the **high end the rear
(+z)**, the side that stands against a wall. The leaves lie in the *slope frame*: u along x, v up the slope from the
front top edge, w along the slope's upward normal. The slope frame is the piece frame turned by
`Quaternion.Euler(-26.57, 0, 0)` about x at the front top edge's centre (`ToPiece`; a test reproduces it with Unity's
rotation formula).

| Quantity | Value |
|---|---|
| Outside module | 2.0 m x 2.0 m footprint; base flush with wood_floor: frame bottom y -0.0329, floor top y 0.0968 |
| Snap points | the four wood_floor corners (±1, 0, ±1), native `$hud_snappoint_corner` (the trapdoor's, shared) |
| Front height | 0.30 m above the floor top at the front edge (cover top y 0.3968) |
| Rear height | 1.30 m above the floor top at the rear edge (cover top y 1.3968) |
| Slope | rise 1 m over the 2 m module: tan = 0.5, **26.57°**; slope length over the module 2.236 m |
| Side cheeks | 0.15 m wide (x ±0.85 to ±1.0), full depth, from the frame bottom up to the slope plane |
| Front panel | 0.15 m deep (z -1.0 to -0.85), between the cheeks, top in the slope plane (0.40 to 0.47 m) |
| Rear header | 0.20 m deep (z 0.80 to 1.0), between the cheeks, top in the slope plane (1.30 to 1.40 m) |
| Opening through the floor | 1.70 m (x ±0.85) x 1.65 m (z -0.85 to 0.80) |
| Leaves | two, 0.835 m wide (hinge to seam), 1.792 m long up the slope, 0.06 m thick, top flush with the frame |
| Gaps | 0.01 m to each cheek, panel and header; 0.01 m at the seam |
| Hinge axes | up the slope (piece direction (0, 0.447, 0.894)) through each leaf's **top outer edge**, at x ±0.84 |
| Open angle | 90° about each hinge; the left leaf is the right leaf mirrored in x at the same angle |
| Clear width when open | 1.56 m (x ±0.78): the open leaves' inner faces, a leaf's thickness inside each hinge |
| Clear passage when open | x ±0.78, z -0.85 to 0.80, at every height: nothing of the frame or either open leaf is inside it |

**Why the hinge is on the top outer edge.** Every leaf point stays at or inside its hinge and no lower than its own
closed bottom face at every angle (tested every 0.5°), so the leaf never swings into its cheek and the cheek needs no
rebate, as the trapdoor's lid never swings into its frame.

**Why the leaves cannot collide.** The farthest any leaf point is from its hinge axis is its free bottom edge,
`LeafReach` = 0.837 m, less than the hinge's 0.84 m distance from the centre line. So no point of the right leaf ever
crosses x = 0 (it comes within 2.8 mm, a few degrees open) and the two sweeps are disjoint; the pair could not touch
even if the leaves were at different angles, which they never are (section 4).

**Why the leaf ends are where they are.** A leaf turns about an axis along the slope, so each of its ends stays in one
plane square to the slope, and its rear end comes nearest the rear header at its bottom rear edge, which is placed
0.01 m in front of it. Its front end clears the front panel's inner top edge by 0.01 m along z; once open it rises
above the panel.

### 2a. Sweep envelope

Per leaf, in the slope frame, the exact bounds of everything it passes through from closed to open
(`SweepInSlope`, tested as containing every pose at 0.05° steps and as tight): across, from the hinge (u 0.84) to
0.837 m inside it (u 0.0028); along the normal, from a leaf's thickness below the slope (w -0.06) to a leaf's width
above it (w 0.835); up the slope, the leaf's own length. In piece space (`SweepInPiece`):

| Bound | Value | Meaning |
|---|---|---|
| x | 0.0028 to 0.84 (right), mirrored (left) | never beside or over the cheeks |
| z, rear | **0.79** | 0.01 m in front of the rear header's inner face; a wall standing on the grid edge behind the piece, centred on it, up to 0.4 m thick, never meets a leaf |
| z, front | **-1.213** | an open leaf stands square to the slope, so its top leans **0.21 m forward of the module**, over the low end; below y 0.40 (the front top edge) nothing passes the front of the module |
| y | 0.42 to **2.03** | the open leaves' top rear corners reach 2.03 m above the snap plane |

So a piece, rail or wall built **in front of** the low end, above the front panel's height, or a roof lower than
2.03 m over the entrance, is in the sweep; the leaves stop against it (section 4). Owner go-around pending.

### 2b. Usable clearance

- **Open passage** (`PassageMargins`): a capsule of radius 0.4 m has 0.38 m to each open leaf and 0.85 m of free
  travel along the opening.
- **Planned ladder** (`PlannedLadderOrigin`, `ClimberMargins`, not wired to placement): hung inside the opening from
  the front panel's inner face, climbing side facing the rear, its top level with the snap plane, exactly as the
  trapdoor's ladder hangs from its front band. A front-lane climber of radius 0.4 m clears the front panel by 0.23 m
  (equal to the trapdoor's, tested), the rear header by 0.62 m and each open leaf by 0.38 m.
- **Closed leaves** are solid sloped boxes at 26.57°. Whether a player can stand on them without sliding is the
  game's ground-angle rule, which this record has not read; it is OWNER GO-AROUND PENDING, and the cover is not
  offered as a stair.
- **Not decided yet, for the ladder slice:** the ladder's top exit. A climber leaving the planned ladder forward meets
  the front panel, whose top is 0.40 to 0.47 m above the snap plane, before the floor in front of it. Whether the
  existing exit search finds a landing over it, or the anchor must move, is that slice's question.

## 3. Collision and frame shape

The frame parts' tops are sloped, so they are **convex prisms**, not boxes: a four-point profile in the piece's (z, y)
plane, extruded along x (`CellarPrism`). The piece adapter will build each as a mesh with a convex mesh collider from
exactly those eight corners, so visuals and collision share one shape. The leaves are boxes on their hinge pivots,
each on a kinematic body as the trapdoor's lid is. Tested: the four prisms are convex, inside the module, reach the
frame bottom, have their tops in the slope plane flush with the closed leaves, and none intrudes into the opening; a
5 x 5 x 5 lattice through each leaf at every 0.5° never enters a prism.

**Runtime convention** (pinned by a test that rebuilds every corner from it): each leaf's pivot sits on its hinge axis
half way along the leaf, under the slope transform; the right leaf turns by `Quaternion.Euler(0, 0, -angle)`, the left
by `Quaternion.Euler(0, 0, angle)`; the leaf box lies inboard of and below the pivot.

## 4. State, motion and the pair rule

- **State is native and single.** As on the trapdoor (`DOOR_DONOR_MATRIX.md` section 4), the clone keeps the donor's
  `Door`, `ZNetView`, `Piece`, `WearNTear` and animator: one ZDO `state`, written by the native `UseDoor` RPC and read
  by every client. Both leaves are driven from that one state. **No RPC, no ZDO field, no ownership claim and no write
  is added**; each client's leaf progress is a local float, as the lid's is.
- **One progress for both leaves.** The leaves reuse `HatchMotion` unchanged (same ease, same donor swing time, same
  5° checked sub-steps, same settle on load), which is valid because the open angle is the trapdoor's 90° (tested).
  `CellarPairOccupancy` makes the pair's pose occupied when either leaf's is, so **a leaf that meets something holds
  both** at the last pose clear for both, and the pair resumes together once clear (tested both ways and for each
  leaf). The leaves therefore can never be at different angles on any client: one open while the other is shut, or
  visuals open while collision is shut, cannot arise, because both visuals and colliders of both leaves come from the
  one progress.
- **Obstruction policy** is the trapdoor's: the leaves never move into a character, a dynamic body or another piece;
  they hold and retry. A 5° turn moves a leaf's free edge at most 0.073 m (the lid's 0.14 m), so the trapdoor's
  check-spacing guarantee holds with room to spare. Nothing is launched, pushed through a wall or shut in.
- **Passage and landing** are the trapdoor's rules on the one progress: a passage only fully open and staying open
  (`HatchMotion.IsPassage`), a landing only closed and staying closed (`IsLanding`).

## 5. Families, prefab IDs, names and costs (`CellarDoors`)

One cellar door per CL-004 family, in the same order. Donor, station, health, material, burn, roof wear, support,
damage modifiers, effects, ward behaviour and door fields are the donor's, checked against CL-004's pinned matrix
before cloning, exactly as for the trapdoors (`TrapdoorBlueprint.DonorMismatches`, reused). The clone's requirements
are read back against the cellar door's own cost with the trapdoor's rule (`TrapdoorBlueprint.RequirementMismatches`,
now with a cost-list overload, so the donor's own unchanged requirements would be refused).

| Donor | Station | Donor cost | **Cellar door cost (x2)** | Prefab ID | Name |
|---|---|---|---|---|---|
| `wood_door` | Workbench | 4 Wood | 8 Wood | `tcc_cellardoor_wood` | Wood Cellar Door |
| `wood_gate` | Workbench | 12 Wood | 24 Wood | `tcc_cellardoor_wood_gate` | Wood Gate Cellar Door |
| `wood_fence_gate` | Workbench | 4 Wood | 8 Wood | `tcc_cellardoor_roundpole` | Roundpole Cellar Door |
| `iron_grate` | Forge | 4 Iron | 8 Iron | `tcc_cellardoor_iron_gate` | Iron Gate Cellar Door |
| `darkwood_gate` | Forge | 16 Wood, 4 Iron, 2 Tar | 32 Wood, 8 Iron, 4 Tar | `tcc_cellardoor_darkwood` | Darkwood Cellar Door |
| `stave_gate` | Forge | 24 Frostwood, 4 Gold | 48 Frostwood, 8 Gold | `tcc_cellardoor_timberwood` | Timberwood Cellar Door |
| `piece_hexagonal_door` | Black forge | 8 YggdrasilWood, 8 Copper | 16 YggdrasilWood, 16 Copper | `tcc_cellardoor_hexagonal` | Hexagonal Cellar Door |
| `ashwood_door` | Workbench | 1 FlametalNew, 5 Blackwood | 2 FlametalNew, 10 Blackwood | `tcc_cellardoor_ashwood` | Ashwood Cellar Door |
| `flametal_gate` | Black forge | 16 FlametalNew | 32 FlametalNew | `tcc_cellardoor_flametal` | Flametal Cellar Door |

- **The cost is a geometry-related recipe change, recorded here rather than hidden**: two leaves, each about a native
  door leaf, so each requirement is doubled; the frame and cheeks come with them. The items and station are the
  donor's, so native discovery (which follows the items and the station, not the amounts) matches the donor's.
  Every requirement recovers on deconstruction.
- **Exclusions carry over unchanged** from CL-004 (`DOOR_DONOR_MATRIX.md` section 2b): the two drawbridges (special
  semantics), the wood shutter (window covering), the fixed windows and crystal wall and the cloth curtains (no door
  state), the Dvergr door and every location door (not buildable). A test pins that exactly the census's families are
  donors. No new exclusion is added.
- Prefab IDs `tcc_cellardoor_<family>` and tokens `cl_cellardoor_<family>` are a save contract from the first shipped
  build on. Package name, plugin GUID, assembly and configuration identity are unchanged.

## 6. The setting

`[CellarDoors] Enabled`, **off by default**, its own switch (`CellarDoorSettings`), not `Trapdoors/Enabled`. Not bound
in this slice; the piece adapter binds it and with it off registers nothing. The same multiplayer caveat as the
trapdoors applies (`DOOR_DONOR_MATRIX.md` section 3): the setting is local, and a host or server with it off deletes
the cellar doors other players place.

## 7. How it fits CL-006's excavation

CL-006 digs a 2 x 2 x 2 m or 4 x 4 x 4 m hole whose flat floor edges fall on whole world metres, at 0° or 90° only,
with a 1 m sloping border each side (`EXCAVATION.md` section 1). The cellar door's base is a 2 m floor tile on the same
grid, so:

- **Small dig.** Put the cellar door over the 2 x 2 m floor area at grade, with ordinary 2 m floor pieces around it
  bridging the 1 m sloping border onto untouched ground beyond it (the ring the dig leaves is 1 m wide, so those floors
  reach 1 m past the dig). The dig's 2 m depth is one ladder section, the height the planned ladder hangs.
- **Large dig.** The cellar door covers one 2 m cell of the 4 m opening; normal floor pieces cover the rest. No second
  size of cellar door is needed.
- Both are OWNER GO-AROUND PENDING; neither has been built.

## 8. Lead defaults pending owner confirmation (2026-11-09)

1. Cost: each donor requirement doubled (section 5). Higher rather than equal on purpose: a deconstruction refunds
   the recipe current at that moment, so lowering a cost later only refunds less than was paid, while raising one
   later would refund more and mint material.
2. Its own `[CellarDoors] Enabled` switch, off by default, separate from the trapdoors.
3. One 2 m module; the 0.30 m front and 1.30 m rear heights and the 26.57° slope; a 0.20 m rear header so a wall on
   the rear grid edge stays out of the sweep.
4. Leaves open 90°, square to the slope (so the leaves reuse the trapdoor's motion unchanged), leaning 0.21 m over the
   low end at the top.
5. The pair rule: one leaf blocked holds both.
6. Exclusions: CL-004's, unchanged; no family is dropped for geometry.
7. Usage tags (for the adapter slice): Building and Doors, as the trapdoors.
8. Placement snaps: the four wood_floor corners only. A snap for the rear wall, or for a foundation edge, is not added
   until the owner sees the corners in game.

## 9. Remaining #486 slices (each its own PR)

1. **Piece adapter**, behind `[CellarDoors] Enabled` (off): bind the setting; clone each donor after
   `OnVanillaPrefabsAvailable` with the trapdoor adapter's checks and strip set (reused, not copied: the strip and
   verify code will move to a shared door-piece helper); build the prisms as meshes with convex mesh colliders, the
   slope transform, the two hinge pivots with kinematic bodies and leaf boxes, hinge marks on the outer edges for the
   ghost; corner snaps; localization from `CellarDoors.English()`; icons; README row and changelog.
2. **Leaf runtime**: the trapdoor lid's runtime generalised to drive two pivots from one progress through
   `CellarPairOccupancy`, with one skinned overlap per leaf per checked pose.
3. **Ladder integration**: anchor, top exit over the front panel (section 2b), passage and landing.
4. Editable asset sources (`.blend`, materials, collider definitions, animation curves) and export notes; candidate
   provenance.

## 10. In-game rows (all OWNER GO-AROUND PENDING, #470)

None of these has been observed; this slice adds nothing to the game. Placement in a wood floor and beside stone
flooring, against a wood wall and a stone wall on the rear edge, every family; the ghost showing the high and low
ends and the hinge sides at every rotation; snaps not sinking the threshold or leaving floating edges; standing on the
closed leaves; Use from outside, from inside the entrance and from the ladder, keyboard and controller, remapped;
both leaves opening and closing together over the donor's swing time; a leaf held by a player, a creature, an item, a
cart, a rail in front of the low end and a low roof, and the pair resuming; the clear passage once open; damage,
repair, deconstruction refunds at the doubled cost, guard stones; host and client, late join, ownership change, zone
reload and restart never showing one leaf open and the other shut; CL-006 small and large digs covered as in
section 7; the setting off leaving everything as before.
