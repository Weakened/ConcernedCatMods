# Concerned Ladders: sloped cellar doors (CL-005 #486): design record and game-free model

**Status: slices 1, 2 and 3 of #486, the game-free model, the piece adapter and the leaf runtime.** Slice 1 is the
model, `src/ConcernedLadders/Domain/Doors/CellarGeometry.cs` and `CellarDoors.cs`. Slice 2 adds the build plan
(`Domain/Doors/CellarBlueprint.cs`) and the piece adapter (`Pieces/CellarDoorPieceRegistration.cs`, through the door
helper `Pieces/DoorPieceKit.cs` it shares with the trapdoors). It registers the nine pieces behind `[CellarDoors]
Enabled`, **off by default**: with it off the adapter is never constructed and the game is unchanged. Slice 3 adds the
leaf runtime (`Runtime/CellarLeaves.cs`): both leaves now open and close together from the native door state, and hold
instead of moving into anything (section 4). **The ladders do not know cellar doors yet**: that is the next slice. The
tests are `src/ConcernedLadders.Tests/Doors/CellarGeometryTests.cs`, `CellarDoorsTests.cs`, `CellarBlueprintTests.cs`
and `CellarLeavesTests.cs`. Every number below is a design default measured against the installed game's `wood_floor`
and walls (Valheim 1.0.21, CL-004's census and section 2a's wall read), **not an in-game observation**. Every
placed-piece, interaction, ladder and multiplayer row is `OWNER GO-AROUND PENDING` (owner playtest checklist #470).

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

**The rear-wall limit, checked against the installed walls (slice 2).** The sweep ends at z 0.79. A wall standing on
the rear grid edge (z 1.0) stays out of it while it reaches less than 0.21 m toward the door, which for a wall centred
on the edge means less than 0.42 m thick. The 0.4 m figure above keeps a 1 cm margin. A read-only probe of the game's
bundles (`cl-ladders-devtools/doors/walls.py` -> `walls.json`, outside the repo) measured every wall piece's colliders
in piece z and found its bottom snap points. The reach below is the larger of a wall's two sides, so it is the worst
of its two facings:

| Wall pieces (bottom snaps at z 0, so standing on a floor edge) | Reach toward the door | Against the rear edge |
|---|---|---|
| `woodwall`, `wood_wall_half`, `wood_wall_quarter`, `wood_wall_roof`, `_roof_a`, `_roof_45`, `_roof_67_a` and the upside-down roof walls, `Piece_grausten_wall_*`, `crystal_wall_1x1` (a CL-004 exclusion), the ashwood walls not named below, iron, darkwood deco and scale walls | 0.05-0.20 m | clear by 1 cm or more |
| `wood_wall_roof_top`, `wood_wall_roof_top_45`, `ashwood_wall_beam_26` | 0.205-0.209 m | clear by 1-5 mm |
| `wood_wall_roof_top_67`, `ashwood_wall_cross_67` | 0.235-0.238 m | 2.5-2.8 cm into the rear of the closed leaves |
| `wood_wall_log`, `wood_wall_log_4x0.5` | 0.25 m | 4 cm in |
| `stave_deco_wall_2x2`, `stave_wall_2x2` and the stave crosses, `stake_wall`, `piece_dvergr_stake_wall`, `piece_dvergr_wood_wall` | 0.275-0.647 m | in |
| `stone_wall_1x1`, `stone_wall_2x1`, `stone_wall_4x2`: 1.0 m deep, **bottom snaps on its two faces (z ±0.5)** | 0 or 1.0 m | snapped to the rear corners it stands **wholly behind** the module (clear) or **wholly over its rear metre** (in the header and the leaves) |
| `piece_stakewall_blackwood`: bottom snaps at z -0.085 and 0.38, colliders to z -2.97 | up to 2.97 m | not a wall that stands on a floor edge; in or clear by its facing |

A wall that reaches in front of z 0.79 overlaps the closed leaves' rear edge. Their bottom rear edge is the rear-most
point of the sweep, at about y 1.33. Nothing is pushed and nothing moves into the wall. The header stays 0.20 m (lead
default 3), and the thicker walls are an owner question (section 8). Owner go-around pending.

**What the leaf runtime does with each wall (slice 3).** These results come from the game-free model of the runtime's
query: each leaf's box at the angle, shrunk by the 1 cm skin, through `HatchMotion` and `CellarPairOccupancy`
(`CellarLeavesTests`). The rear end of a leaf turns in a plane square to the slope, so the leaf reaches furthest back
when closed (z 0.79) and least far back when fully open (z 0.763, at the slope plane). So there are three cases, not two:

- **Reach 0.20 m or less**: the wall is never in a pose. The cover opens and closes freely (tested at 0.05 and 0.20 m).
- **Reach about 0.235-0.25 m** (`wood_wall_roof_top_67`, `ashwood_wall_cross_67`, the log walls): the wall is in the
  closed pose but not the fully open one. Asked to open in session, the cover never moves, because its first pose is in
  the wall. But `HatchMotion.Settle`, reused unchanged, takes the clear open end when the stored closed end is occupied,
  as it does for someone standing in a trapdoor's opening. So **a cover stored closed loads open on that client**. It
  then closes only until it meets the wall, about 55 degrees open, while the native state says closed (tested at 0.235
  and 0.24 m). The log walls' 0.25 m is within 0.2 mm of the open leaves' skinned query in the model, so their real
  colliders decide which way it goes. This is lead default 15 and an owner question (section 8).
- **Reach 0.275 m or more** (stave, stake and dvergr walls): the wall is in every pose. The cover settles at its stored
  closed end and never opens (tested at 0.275 and 0.4 m).

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
plane, extruded along x (`CellarPrism`). The piece adapter builds each as one mesh from exactly those eight corners
(`CellarBlueprint.Mesh`: six flat faces, four vertices each, wound so Unity's front face points out). The same mesh
serves the renderers and a static convex `MeshCollider`, so visuals and collision share one shape. Tested: each mesh's
vertices are exactly its prism's corners, every triangle faces outward with its own unit normal, the mesh is closed
and consistently wound, it is convex, and it encloses exactly the prism's volume. A face wound inward is caught. The leaves are boxes on their hinge pivots,
each on a kinematic body as the trapdoor's lid is. Tested: the four prisms are convex, inside the module, reach the
frame bottom, have their tops in the slope plane flush with the closed leaves, and none intrudes into the opening; a
5 x 5 x 5 lattice through each leaf at every 0.5° never enters a prism.

**Runtime convention** (pinned by a test that rebuilds every corner from it): each leaf's pivot sits on its hinge axis
half way along the leaf, under the slope transform; the right leaf turns by `Quaternion.Euler(0, 0, -angle)`, the left
by `Quaternion.Euler(0, 0, angle)`; the leaf box lies inboard of and below the pivot.

**What the adapter builds** (slice 2, `CellarBlueprint`, walked by `Pieces/CellarDoorPieceRegistration.cs`). The
donor's root `Piece`, `ZNetView`, `WearNTear`, `Door` and `Animator` stay. The donor's `door` leaf stays as a bare
transform for the hidden native animator. Every other donor child, and the root's own geometry, goes through the
trapdoor's strip (`DoorPieceKit.Strip`, the same code, not a copy). Under the root it adds:

- one child per prism with its static convex mesh collider (`cheek_left`, `cheek_right`, `front_panel`, `rear_header`);
- the presentation root `new`, with one mesh renderer per prism in the donor's frame material, and, where the donor has
  a worn material, `worn` in it (`DoorPieceKit.AdoptPresentation`, the trapdoor's wear wiring);
- `slope`, at the front top edge's centre and turned by `Quaternion.Euler(-26.57, 0, 0)`, holding the two pivots
  `leaf_left_hinge` and `leaf_right_hinge`. Each pivot sits at `HingeInSlope`, closed at identity, on a kinematic,
  gravity-free body, and carries:
  - its leaf's box collider and a visual box in the donor's leaf material;
  - two **hinge marks**. These are darkened plates on the leaf's top, from its outer edge 0.30 m inboard, 0.6 m either
    side of the pivot. They show the hinge sides on the ghost. They are 8 mm thick, thinner than the 1 cm cheek gap, so
    an open leaf's marks never reach the cheek (tested every 0.5 degrees). They have no collider;
- the four wood_floor corner snaps.

`Verify` refuses the clone if any collider or renderer survived that the adapter did not build, if a wear state points
outside the piece's own roots, or if the donor leaf is more than a transform (`DoorPieceKit.VerifyStripped`, shared). It
also refuses a frame collider that is not a static convex mesh with its mesh, a pivot without its kinematic body or leaf
collider, and a clone that carries the trapdoor's lid runtime. The ladders recognise a trapdoor by that runtime, so a
cellar door is an ordinary piece to the ladder code until the ladder slice. Since slice 3 it also refuses a clone
without exactly one `CellarLeaves` on its root with an accepted swing time. It refuses one where a collider the leaves'
queries ignore (`CellarBlueprint.QueryIgnores`) is missing, or whose collider count is not exactly those six.

## 4. State, motion and the pair rule

- **State is native and single.** As on the trapdoor (`DOOR_DONOR_MATRIX.md` section 4), the clone keeps the donor's
  `Door`, `ZNetView`, `Piece`, `WearNTear` and animator: one ZDO `state`, written by the native `UseDoor` RPC and read
  by every client. Both leaves are driven from that one state. **No RPC, no ZDO field, no ownership claim and no write
  is added**; each client's leaf progress is a local float, as the lid's is.
- **The leaf runtime (slice 3, `Runtime/CellarLeaves.cs`).** It is the trapdoor lid's runtime (`Runtime/TrapdoorLid.cs`)
  generalised to two pivots. It reads the native state through the piece's own `ZNetView`: once for the first pose and
  then once a frame, each after `IsValid()`. It writes nothing. It turns `slope/leaf_left_hinge` by
  `Quaternion.Euler(0, 0, angle)` and `slope/leaf_right_hinge` by `Quaternion.Euler(0, 0, -angle)` from one progress, by
  setting their transforms (never a velocity, force or `MoveRotation`). The swing time is the donor's
  (`DoorFamily.SwingSeconds`, as the family's trapdoor). A ghost or icon render has no ZDO, so it never reads or moves.
  A missing part or collision layer leaves the cover shut. A fault stops it where it is, both leaves at one angle,
  logged once per session.
- **One skinned overlap per leaf.** Each checked pose asks the left leaf, then the right only if the left was clear:
  one `Physics.OverlapBoxNonAlloc` of that leaf's own collider box at that angle, shrunk by the lid's 1 cm skin, into a
  shared 32-entry buffer (a full buffer is occupied). So there are at most two queries a pose, 64 a frame at the 32
  sub-step cap. The query layers and blocking rule are the lid's own (`TrapdoorLid.QueryMask`,
  `TrapdoorLid.BlocksByKind`, shared, not copied). Characters, carts and ships, dropped items and every building piece
  block by their layer alone. On the general layers only a dynamic body blocks. Terrain and static rock are not
  queried, so a leaf may stand into them, as a native door leaf does.
- **Exclusion by collider identity.** Each leaf's query ignores exactly the piece's six own colliders, resolved once
  from `CellarBlueprint.QueryIgnores`: the four frame prisms, the other leaf and itself. They are compared as objects,
  never by name, layer or parent. The model keeps every leaf 1 cm or more from the frame and 5.7 mm or more from the
  other leaf, and the skin shrinks each query by a further 1 cm, so none of the six should ever be inside a query.
  Ignoring them by identity means the cover does not rest on margins that thin. Unlike the lid's `IsChildOf(transform)`
  test, nothing else is ignored: anything parented under the piece by anyone else is checked.
- **One progress for both leaves.** The leaves reuse `HatchMotion` unchanged (same ease, same donor swing time, same
  5° checked sub-steps, same settle on load), which is valid because the open angle is the trapdoor's 90° (tested).
  `HatchMotion.cs` is not changed by slice 3.
  `CellarPairOccupancy` makes the pair's pose occupied when either leaf's is, so **a leaf that meets something holds
  both** at the last pose clear for both, and the pair resumes together once clear (tested opening and
  closing, with each leaf blocked in turn). The leaves therefore can never be at different angles on any client: one open while the other is shut, or
  visuals open while collision is shut, cannot arise, because both visuals and colliders of both leaves come from the
  one progress.
- **Obstruction policy** is the trapdoor's: the leaves never move into a character, a dynamic body or another piece;
  they hold and retry. A 5° turn moves a leaf's free edge at most 0.073 m (the lid's 0.14 m), so the trapdoor's
  check-spacing guarantee holds with room to spare. Nothing is launched, pushed through a wall or shut in.
- **Passage and landing** are the trapdoor's rules on the one progress: a passage only fully open and staying open
  (`HatchMotion.IsPassage`), a landing only closed and staying closed (`IsLanding`). **Nothing asks them yet.**
  `CellarLeaves` is not in the trapdoor lid's registry, so the ladder code (`TrapdoorLid.Any`, `RouteShut`,
  `MayLandOn`, the hatch anchor) is unchanged and still sees each leaf as an ordinary piece collider at its current
  pose (lead default 18). The ladder slice wires these rules.

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

`[CellarDoors] Enabled`, **off by default**, its own switch (`CellarDoorSettings`), not `Trapdoors/Enabled`. Bound in
`Plugin.Awake` since slice 2. Only when it is on is the adapter constructed and installed; its install is isolated, so a
failure there logs and leaves the ladders and the trapdoors as they were. With it off nothing is cloned, localized or
registered (a test pins the one construction inside the setting's branch). The README lists it. The same multiplayer caveat as the
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

Added by slice 2 (L6, 2026-11-09):

9. Superseded by slice 3, which drives the leaves (items 15-20). As recorded for slice 2: until the leaf runtime
   lands, the leaves stay shut whatever the native state says (section 4). The setting's text and
   the README say so. This is the narrowest option that still registers the pieces: no leaf moves without the
   occupancy checks.
10. The cellar door carries no trapdoor lid runtime, so the ladders treat it as an ordinary piece until the ladder slice.
    The clone is refused if it carries one.
11. The rear header stays 0.20 m after the wall census (section 2a). The 0.47 m, 0.50 m and thicker walls, and a stone
    wall snapped over the module's rear metre, are expected to hold the leaves shut rather than be cleared.
12. Hinge marks: two darkened 8 mm plates per leaf at its hinge edge, visual only. A texture repeats once per 2 m of
    frame face. Both are cosmetic and not compared with the donor's own mapping.
13. One mesh per prism, shared by all nine families and kept for the session (four small meshes, never unloaded).
14. Frame support: WearNTear measures a non-box collider by its world bounds (`collider.bounds`, an axis-aligned box),
    so a cellar door turned 45 degrees tests support over a slightly larger box than its prisms. This is the native rule
    for every mesh-collider piece and is not changed.

Added by slice 3 (L7, 2026-11-09):

15. The first pose is `HatchMotion.Settle`, reused unchanged for the pair: the stored end if clear, else the other end if
    clear, else the clear pose nearest the stored end, else the stored end. This is the trapdoor's tested policy (L3), so
    the leaves are never put into someone standing in the entrance at load. A consequence for walls: a wall reaching
    about 0.235-0.25 m toward the door on the rear edge is in the closed leaves but not the open ones. A cover stored
    closed beside such a wall therefore **loads open on that client and closes only until it meets the wall**, while the
    native state says closed (section 2a). Changing this would mean changing `HatchMotion` for the trapdoors too, or
    placing the closed leaves into whatever is there. Owner question: deepen the rear header, add a rear-wall snap, or
    accept it.
16. Each leaf's query ignores exactly the piece's own six colliders, by identity (section 4). Anything else blocks,
    including anything another mod parents under the piece. A cover that cannot resolve all six stays shut.
17. The query layers and the blocking rule are the trapdoor lid's (shared code). Terrain and static rock are not
    queried, as for the lid.
18. The ladder code does not know cellar doors in this slice. It sees each leaf as an ordinary piece collider at its
    current pose: the snap treats the cover as a floor (corner snaps), and a ladder's landing search may stand a climber
    on a closed leaf (normal 0.89, inside its 0.65 limit) or on an upright leaf's top edge. The leaves never move into a
    climber, because characters block. This stays until the ladder slice wires passage and landing.
19. One fault message per session for all cellar doors. A faulted cover stops where it is.
20. The swing time is the donor's (`DoorFamily.SwingSeconds`), the same as the family's trapdoor.

## 9. Remaining #486 slices (each its own PR)

Done: slice 1 (model, PR #534), slice 2 (piece adapter, PR #535, this record's section 3; the wall depths in section
2a) and slice 3 (leaf runtime, section 4; what it does with each wall in section 2a).

1. **Ladder integration**: anchor, top exit over the front panel (section 2b), passage and landing. Recognise a cellar
   door by its `CellarLeaves`: stop the snap treating the cover as a floor, refuse a landing on a leaf that is not
   closed and staying closed (including an upright leaf's top edge), and route a climb through the opening only while
   the pair is a passage (lead default 18).
2. Editable asset sources (`.blend`, materials, collider definitions, animation curves) and export notes; candidate
   provenance.

## 10. In-game rows (all OWNER GO-AROUND PENDING, #470)

None of these has been observed. Slices 2 and 3 register the pieces and drive their leaves behind the setting; nothing
has been placed in game. Slice 2 added these rows: the hammer icons; the frame meshes' faces, shading and texture
scale; the hinge marks on the ghost; a wall from section 2a's table on the rear edge; and a ladder
aimed at a cellar door, which the ladder snap treats as an ordinary floor (its corner snaps) until the ladder slice, so
it may hang from or stand at the frame's outer edge. Placement in a wood floor and beside stone
flooring, against a wood wall and a stone wall on the rear edge, every family; the ghost showing the high and low
ends and the hinge sides at every rotation; snaps not sinking the threshold or leaving floating edges; standing on the
closed leaves; Use from outside, from inside the entrance and from the ladder, keyboard and controller, remapped;
both leaves opening and closing together over the donor's swing time; a leaf held by a player, a creature, an item, a
cart, a rail in front of the low end, a low roof and a second cellar door's rear header in front of the low end, and
the pair resuming; the clear passage once open; damage,
repair, deconstruction refunds at the doubled cost, guard stones; host and client, late join, ownership change, zone
reload and restart never showing one leaf open and the other shut; CL-006 small and large digs covered as in
section 7; the setting off leaving everything as before.

Slice 3 adds these rows. The leaves open and close on Use, together and mirror images, over the donor's swing time,
for every family. Use from inside the entrance and from below. The cover holds against someone standing on it, someone
in the entrance under it closing, a cart, an item, a rail in front of the low end and a low roof, and resumes once
clear. A cover placed beside each wall class of section 2a on the rear edge, saved closed and reloaded: free (0.20 m or
less), loading open and holding part open (about 0.235-0.25 m, lead default 15), or held shut (0.275 m or more). Stored
open, it loads open; stored closed, closed. A late joiner, a second client and a zone reload see the same pose. No push
or launch of a player, creature or cart at any point. The ghost and the hammer icon stay closed. A ladder near a cellar
door, which still treats it as an ordinary piece (lead default 18). The trapdoors unchanged with the cellar doors on,
and the setting off leaving everything as on main.
