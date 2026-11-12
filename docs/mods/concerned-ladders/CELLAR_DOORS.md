# Concerned Ladders: sloped cellar doors (CL-005 #486): design record and game-free model

**Status: slices 1 to 4 of #486, the game-free model, the piece adapter, the leaf runtime and the ladder
integration.** Slice 1 is the
model, `src/ConcernedLadders/Domain/Doors/CellarGeometry.cs` and `CellarDoors.cs`. Slice 2 adds the build plan
(`Domain/Doors/CellarBlueprint.cs`) and the piece adapter (`Pieces/CellarDoorPieceRegistration.cs`, through the door
helper `Pieces/DoorPieceKit.cs` it shares with the trapdoors). It registers the nine pieces behind `[CellarDoors]
Enabled`, **off by default**: with it off the adapter is never constructed and the game is unchanged. Slice 3 adds the
leaf runtime (`Runtime/CellarLeaves.cs`): both leaves now open and close together from the native door state, and hold
instead of moving into anything (section 4). Slice 4 teaches the ladders the cellar door (section 2c,
`Domain/Doors/CellarLadder.cs`): a ladder hangs in its opening, a climber passes through only while both leaves are
open, a leaf is never a landing unless the pair is closed and staying closed, and the top exit and the descending entry
carry the climber over the front panel. The tests are `src/ConcernedLadders.Tests/Doors/CellarGeometryTests.cs`,
`CellarDoorsTests.cs`, `CellarBlueprintTests.cs`, `CellarLeavesTests.cs` and `CellarLadderTests.cs`. Every number below is a design default measured against the installed game's `wood_floor`
and walls (Valheim 1.0.21, CL-004's census and section 2a's wall read), **not an in-game observation**. Every
placed-piece, interaction, ladder and multiplayer row is `OWNER GO-AROUND PENDING` (owner playtest checklist #470).

The donor census, the matrix of the nine native door families and the reasons for every exclusion are CL-004's,
in `DOOR_DONOR_MATRIX.md`. This record reuses them and adds only what the sloped geometry changes.

## 1. What a cellar door is, and what it is not

A sloped cover over an entrance, shaped like a house's bulkhead cellar door: a fixed frame (two side cheeks, a low
front panel, and a rear header beam at the high end) and two leaves that meet at a central seam, each hinged at its
outer side and opening outward. One Use opens or closes the pair. Its base takes a 2 m wood floor tile's place in the
floor grid, so it sits in a floor beside an opening that the player made, its high end against a wall or foundation edge
behind it. Since #549 the high end is open below its beam, so a stair the player builds down from the low end passes
under it into whatever is dug behind (section 2d).

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
| Front height | 0.05 m above the floor top at the front edge (cover top y 0.1468), so the front panel's inner top, 0.178 m above the floor top, is a threshold a player walks over (#566; 0.2625 m and a 0.375 m sill after #549) |
| Rear height | 1.7625 m above the floor top at the rear edge (cover top y 1.8593); 1.30 m before #549 |
| Slope | rise 1.7125 m over the 2 m module: tan = 0.85625, **40.57°**; slope length over the module 2.633 m (26.57° before #549, 36.87° before #566) |
| Side cheeks | 0.15 m wide (x ±0.85 to ±1.0), full depth, from the frame bottom up to the slope plane |
| Front panel | 0.15 m deep (z -1.0 to -0.85), between the cheeks, top in the slope plane (0.15 to 0.28 m) |
| Rear header | a beam 0.20 m deep (z 0.80 to 1.0) and 0.30 m tall (`RearBeamHeight`), between the cheeks, top in the slope plane (1.69 to 1.86 m), level underside at y 1.3881 (1.2913 m above the floor top). Open below (#549); until #549 it was solid down to the floor's bottom |
| Opening through the floor | 1.70 m (x ±0.85) x 1.65 m (z -0.85 to 0.80) |
| Leaves | two, 0.835 m wide (hinge to seam), 2.0945 m long up the slope, 0.06 m thick, top flush with the frame |
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
| z, front | **-1.383** | an open leaf stands square to the slope, so its top leans **0.38 m forward of the module**, over the low end; below y 0.15 (the front top edge) nothing passes the front of the module |
| y | 0.24 to **2.28** | the open leaves' top rear corners reach 2.28 m above the snap plane |

So a piece, rail or wall built **in front of** the low end, above the front panel's height, or a roof lower than
2.28 m over the entrance, is in the sweep; the leaves stop against it (section 4). Owner go-around pending.

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
point of the sweep, at about y 1.60. Nothing is pushed and nothing moves into the wall. The header stays 0.20 m (lead
default 3), and the thicker walls are an owner question (section 8). Owner go-around pending.

**What the leaf runtime does with each wall (slice 3).** These results come from the game-free model of the runtime's
query: each leaf's box at the angle, shrunk by the 1 cm skin, through `HatchMotion`, `CellarPairOccupancy` and the
first-pose rule `CellarSettle` (`CellarLeavesTests`). The rear end of a leaf turns in a plane square to the slope, so the
leaf reaches furthest back when closed (z 0.79) and least far back when fully open (z 0.763, at the slope plane). The
skinned closed pose first meets a wall reaching about 0.223 m toward the door. So:

- **Reach about 0.22 m or less** (every wall in the first two rows of the table, including the 0.205-0.209 m roof-top
  and beam walls): the wall is never in a pose. The cover opens and closes freely (tested at 0.05, 0.20 and 0.209 m).
- **Reach more than that** (`wood_wall_roof_top_67`, `ashwood_wall_cross_67`, the log walls, and the stave, stake and
  dvergr walls): the wall is in the closed pose, and for the deeper ones in every pose. **The cover is held shut.** Asked
  to open in session, it never moves, because its first checked pose is in the wall. On load it settles shut whatever the
  stored state, because `CellarSettle` puts the closed leaves over a fixed obstruction rather than taking the open end
  (lead default 15). So a Use that stored the door open while the wall held it, followed by a save and a reload, still
  shows it shut. Tested at 0.235, 0.24, 0.25, 0.275 and 0.4 m, stored closed and stored open. The trapdoor's rule alone,
  `HatchMotion.Settle`, would load the shallower of these open and leave them part open; a test shows that too.
- **One case is not a settle.** If the cover is already open and a wall is then built into its closed pose, a close asked
  for in session closes the leaves only until they meet the wall. That is the trapdoor's obstruction policy: hold and
  retry, as for an item in the way. It is shut again from the next load. Owner go-around pending.

### 2b. Usable clearance

- **Open passage** (`PassageMargins`): a capsule of radius 0.4 m has 0.38 m to each open leaf and 0.85 m of free
  travel along the opening.
- **Planned ladder** (`PlannedLadderOrigin`, `ClimberMargins`; since slice 4 the placement anchor, section 2c): hung inside the opening from
  the front panel's inner face, climbing side facing the rear, its top level with the snap plane, exactly as the
  trapdoor's ladder hangs from its front band. A front-lane climber of radius 0.4 m clears the front panel by 0.23 m
  (equal to the trapdoor's, tested), the rear header by 0.62 m and each open leaf by 0.38 m.
- **Closed leaves** are solid sloped boxes at 40.57°. Whether a player can stand on them without sliding is the
  game's ground-angle rule, which this record has not read; it is OWNER GO-AROUND PENDING, and the cover is not
  offered as a stair.
- **The ladder's top exit** (decided in slice 4, section 2c): a climber leaving the planned ladder forward meets the
  front panel, whose top is 0.15 to 0.28 m above the snap plane, before the floor in front of it. The anchor does not
  move; the exit route rises over the panel instead.

### 2d. The high end and a stair (#549)

Owner playtest, 2026-11-10: the rear header was a solid block from the floor's bottom up to the slope, a 1.3 m wall
across the back. A player walking down from the low end met it at head height at ground level and could not pass. The
owner's reference is a house's bulkhead cellar door: the high end stands against the house, the stair goes down from
the low end toward it, and the passage continues under the high end into the basement.

So the high end is raised (rear height 1.30 to 1.7625 m, slope 26.57 to 36.87°), and the rear header is a **beam**:
0.20 m deep and 0.30 m tall under the slope, with a level underside 1.3125 m above the floor top. Between the cheeks,
everything below that underside is open, back to the module's rear edge (`TheHighEndIsOpenBelowItsBeam`). The front
height is lowered from 0.30 to 0.2625 m, so the front panel's top, 0.375 m above the floor top, stays exactly where the
ladder exit over it was measured.

**#566 (owner playtest 2026-11-11).** Stairs snapped to the cover's floor-level corners left that 0.375 m sill above their
top step, so a player could step in but not walk out. The front is now 0.05 m and the slope 40.57°, keeping the high end at
1.7625 m. The front panel's inner top is 0.178 m above the floor top, a threshold rather than a ledge: less than the
ladder's 0.3 m standard step (`SinceTheSillWasLoweredTheStandardStepClearsItAndThePanelLiftStillDoes`). The beam's
underside is 1.29 m above the floor top. The ladder's panel lift over the frame is unchanged in code and still carries a
climber over the lower panel; it is no longer the only route that does. The section 2c paragraph below describes the
#549 sill.

A 45° stair started at the front panel's inner face at floor level reaches the beam's inner face 1.65 m down. There it
has 2.94 m of headroom under the beam, and 3.14 m at the module's rear edge (`AStairDownFromTheLowEndHasHeadroomUnderTheBeam`).
The game's own stair pieces are shallower. Measured from their snap points, `wood_stair`, `stone_stair`, `ashwood_stair`,
the black marble and grausten stairs all rise 1 m over 2 m (26.57°); only `wood_stepladder` is 45°. On the shallow
stair the head clears the beam's inner face by 2.12 m. The cover generates no stair. The player builds it in their dig with ordinary pieces, as they build the dig's walls.

What did not move:

- The sweep's rear edge, z 0.79, so the rear-wall table and the leaf runtime's wall results (section 2a) are unchanged.
- The opening (1.70 x 1.65 m), the leaf width and the hinge axes.
- The ladder anchor and its margins.

What did move, and is re-derived above:

- The leaves are 2.0945 m long (since #566).
- They lean 0.38 m over the low end when open, and their top reaches 2.28 m.
- A roof over the entrance must clear 2.28 m.

The beam is a convex prism like every frame part, its underside is a face of its collider, and the closed leaves still
stop 0.01 m in front of it. In game, OWNER GO-AROUND PENDING: walking down a stair under the open cover, and the look of
the taller cover.

### 2c. Ladder integration (slice 4)

The ladder side of the cover, mirroring the trapdoor's (`DOOR_DONOR_MATRIX.md` section 5, `HatchLadder`). The rules are
game-free in `Domain/Doors/CellarLadder.cs`; the trapdoor's `HatchLadder.AllowsRoute`, `LegSpan` and
`IsLandingSurface` are reused, not copied. The ladder adapters recognise a cellar door by its `CellarLeaves` runtime,
which keeps its own registry of woken covers (`CellarLeaves.Any`), never the trapdoor lid's. **Every ladder consult is
gated by `CellarLeaves.Any`**, which is false for the whole session with `[CellarDoors] Enabled` off because no cellar
door prefab exists, so with the setting off every ladder path is exactly main's (pinned by source). Nothing on the
network is added: the passage and landing questions read the native door state through the piece's own `ZNetView`,
after `IsValid()`, as the leaves already do; no RPC, ZDO field, ownership claim or write.

- **Never a floor (placement).** A cellar door's four corner snaps are the wood floor's, so the floor-edge snap would
  read it as a 2 m floor tile and hang or stand a ladder at the outside of a frame that rises 0.3 to 1.4 m. Aimed at a
  cellar door's frame (a cheek, the front panel or the rear header), Auto and Top hang the ladder in the cover's own
  anchor: `PlannedLadderOrigin`, from the front panel's inner face, climbing side toward the rear header, in the
  cover's rotation whatever the hammer's. Auto only when there is room below (the trapdoor's room test, under the
  cover's own probe). **Bottom, Left side, Right side, and Auto with no room below are refused**: the ghost is marked
  invalid and cannot be built, never put on the cover's edge. Out of the game's placement reach is refused too. Aiming
  at a leaf hides the ghost, because the game's own placement ray skips a collider with a body (each leaf has a
  kinematic one), as on the trapdoor's lid.
- **Passage.** A route that crosses the opening is committed only while the pair is a passage: both leaves fully open
  and not asked to close (`HatchMotion.IsPassage` on the one progress). The opening is widened by the capsule's radius
  on every side; the cover's depth runs from the frame's bottom up to the slope's top at the capsule's centre, clamped
  to the opening (`CellarLadder.Crosses`), so a capsule standing on the closed leaves, or on the floor clear of the
  module, does not cross it. Each leg of a route is tested at both ends over its own height span, as for the trapdoor.
  This gates the top exit (a lane under a pair that is not a passage has no top exit at all; the closed leaves' own
  collision also stops the ascent), every exit route, and a descending entry from the floor in front. A climber is
  told **"The cellar door is not open. Open it from the ladder, or climb down."** (`cl_msg_cellarclosed`, registered
  with the cellar doors' own localization, so it exists exactly when they do).
- **Landing.** The landing search may stand a climber on a leaf only while the pair is closed and staying closed. A
  moving, held or open leaf is never a landing, including an open leaf's upright top edge, whose face, like a closed
  leaf's top, faces up the slope's normal (y 0.89) and so passes the search's slope test (y 0.65 or more); a ray that
  meets one finds no landing at that point at all. The four frame prisms are an ordinary piece. A cover whose hinge
  pivots cannot be found is never a landing. The search's own footprint test (nine points at 0.9 of the radius, 0.15
  m of step) is expected to refuse the 40.57 degree leaves anyway, since the slope rises about 0.39 m across the
  footprint, so in practice no ladder exit stands a climber on the cover.
- **Over the front panel** (as measured for the #549 sill; since #566 the panel's top is 0.15 to 0.28 m and the standard step clears it). The anchored ladder's top is the snap plane; the front panel's top was 0.36 to 0.47 m above
  it, and the floor in front is at 0.097 m. The exit search's standard lifts (6 cm, then a 0.30 m step over the higher
  end) leave the feet at about 0.42 m, below the panel's 0.47 m inner edge. The clearance query's capsule floats 5 cm
  over the feet and is swept 2 cm thinner, so on the exit (from the floor's top plus the 2 cm landing skin) the 0.30 m
  step would **pass** the query while the real body's bottom met the panel's inner edge about 5.5 cm deep; on the
  descending entry (from the floor's top) the query would meet the panel and refuse the entry outright. So a route with one end in a cover's opening and the other outside the
  opening at the floor's level (`CellarLadder.LeavesOverFrame`) tries only lifts that carry the feet at least to
  `CellarLadder.OverPanelY`, 5 cm over the panel's inner top edge (0.52 m above the snap plane, about 0.40 m over the
  floor in front): the standard lifts, each raised to that height, duplicates dropped (`CellarLadder.Lifts`). A route
  to a landing inside the opening, or down in the dig below the frame, keeps the standard lifts. The first top-exit
  candidates (0.94 and 1.34 m in front of the ladder's top at the game's 0.49 m radius) stand clear of the widened
  opening and of the panel's outer face, so the settle onto the floor is not a crossing. Every leg is still swept with
  the capsule.
- **Descending entry.** From the floor in front, the hands' aim (a line from the chest to the ladder) goes to the
  floor-lip height, 0.2 m over the run's top; in front of a cellar door that line passes through the front panel, so
  the entry would always be refused. Over a cover's frame the aim rises to `OverPanelY`, and the route takes the panel
  lifts above. It also needs the passage.
- **Use while climbing.** The cover is a native `Door`, so a hovered cover takes Use (the trapdoor's rule, unchanged):
  a climber opens and closes the leaves from the ladder without letting go, and the frame in which Use asked for a
  dismount is held back from interaction once either door family exists.

Not covered: Foreman's worker climbing (`Capabilities/WorkerLadderLinks.cs`) is unchanged, as it is for trapdoors.

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
collider, and a clone that carries the trapdoor's lid runtime. The ladders recognise a trapdoor by that runtime and a
cellar door by its `CellarLeaves` (slice 4), so a clone with both would be read as both. Since slice 3 it also refuses a clone
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
  5° checked sub-steps), which is valid because the open angle is the trapdoor's 90° (tested). `HatchMotion.cs` is
  not changed by slice 3. The first pose on load is `CellarSettle.First` (lead default 15): fail-closed against a fixed
  obstruction in the closed pose, and otherwise the trapdoor's `HatchMotion.Settle`.
  `CellarPairOccupancy` makes the pair's pose occupied when either leaf's is, so **a leaf that meets something holds
  both** at the last pose clear for both, and the pair resumes together once clear (tested opening and
  closing, with each leaf blocked in turn). The leaves therefore can never be at different angles on any client: one open while the other is shut, or
  visuals open while collision is shut, cannot arise, because both visuals and colliders of both leaves come from the
  one progress.
- **Obstruction policy** is the trapdoor's: the leaves never move into a character, a dynamic body or another piece;
  they hold and retry. A 5° turn moves a leaf's free edge at most 0.073 m (the lid's 0.14 m), so the trapdoor's
  check-spacing guarantee holds with room to spare. Nothing is launched, pushed through a wall or shut in.
- **Passage and landing** are the trapdoor's rules on the one progress: a passage only fully open and staying open
  (`HatchMotion.IsPassage`), a landing only closed and staying closed (`IsLanding`). Since slice 4 the ladder code asks
  them (section 2c) through `CellarLeaves`' own registry, `Any`, `RouteShut`, `MayLandOn` and `OverPanel`, which read
  the native state now, not as of the last frame, through one guarded helper (a cover that has not settled, has
  stopped on a fault, lacks its parts or has no ZDO is neither). `CellarLeaves` is still not in the trapdoor lid's
  registry, so the trapdoor's own ladder paths are unchanged.

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
3. One 2 m module; the 0.05 m front and 1.7625 m rear heights and the 40.57° slope (0.30 m, 1.30 m and 26.57° until
   #549; 0.2625 m and 36.87° until #566); a 0.20 m deep rear header so a wall on the rear grid edge stays out of the sweep, a 0.30 m beam open below
   since #549.
4. Leaves open 90°, square to the slope (so the leaves reuse the trapdoor's motion unchanged), leaning 0.38 m over the
   low end at the top (0.21 m until #549, 0.34 m until #566).
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
10. The cellar door carries no trapdoor lid runtime; the clone is refused if it carries one. The second half, "the
    ladders treat it as an ordinary piece until the ladder slice", is superseded by slice 4 (items 21-30).
11. The rear header stays 0.20 m after the wall census (section 2a). The 0.47 m, 0.50 m and thicker walls, and a stone
    wall snapped over the module's rear metre, are expected to hold the leaves shut rather than be cleared.
12. Hinge marks: two darkened 8 mm plates per leaf at its hinge edge, visual only. A texture repeats once per 2 m of
    frame face. Both are cosmetic and not compared with the donor's own mapping.
13. One mesh per prism, shared by all nine families and kept for the session (four small meshes, never unloaded).
14. Frame support: WearNTear measures a non-box collider by its world bounds (`collider.bounds`, an axis-aligned box),
    so a cellar door turned 45 degrees tests support over a slightly larger box than its prisms. This is the native rule
    for every mesh-collider piece and is not changed.

Added by slice 3 (L7, 2026-11-09):

15. The first pose is `CellarSettle.First`, fail-closed against fixed obstructions, with `HatchMotion` unchanged:
    - closed, whatever is stored, when something is in the closed pose but nothing that can move is: no character,
      item, cart or ship (by layer, because a client that does not own an item, cart or ship holds its body kinematic
      until ownership passes) and no dynamic body. What is there is a wall, a piece or another door's kinematic part, so
      the closed leaves overlap it and nothing moves;
    - otherwise `HatchMotion.Settle`, the trapdoor's tested policy (L3): the stored end when it is clear, and otherwise
      a pose that keeps the leaves out of whoever is there, so they are never put into someone standing in the entrance
      at load.

    The trapdoor's rule alone would load a cover beside a wall that reaches about 0.223-0.25 m toward the door (in the
    closed leaves but not the open ones) open. It would then hold it part open while the native state says closed. It
    would do the same for a cover placed beside such a wall, and for one whose Use stored open while the wall held it.
    Clients that saw the wall built and late joiners would then disagree. `CellarSettle` makes all of these shut on
    every client (independent review of 4506336, blockers 1 and 2). Two cases remain. One is the in-session case in
    section 2a. In the other, someone is in the closed pose at load while such a wall also stands. The trapdoor's rule
    then applies, so the cover loads open and closes only until it meets the wall, about 50-64 degrees open, until the
    next load without them there. Keeping the leaves out of the person comes first.
    Owner question: whether walls deeper than about 0.22 m on the rear edge should be cleared, by a deeper rear header
    or a rear-wall snap, rather than holding the cover shut.
16. Each leaf's query ignores exactly the piece's own six colliders, by identity (section 4). Anything else blocks,
    including anything another mod parents under the piece. A cover that cannot resolve all six stays shut.
17. The query layers and the blocking rule are the trapdoor lid's (shared code). Terrain and static rock are not
    queried, as for the lid.
18. Superseded by slice 4 (items 21-30). As recorded for slice 3: the ladder code did not know cellar doors, saw each
    leaf as an ordinary piece collider at its current pose, and the snap treated the cover as a floor. The leaves never
    move into a climber, because characters block; that is unchanged.
19. One fault message per session for all cellar doors. A faulted cover stops where it is.
20. The swing time is the donor's (`DoorFamily.SwingSeconds`), the same as the family's trapdoor.

Added by slice 4 (L8, 2026-11-09), section 2c:

21. The ladders recognise a cellar door by its `CellarLeaves`, in that runtime's own registry, and ask nothing of
    cellar doors while none exists (`CellarLeaves.Any`), so with the setting off they behave exactly as on main.
22. **Never a floor.** Aimed at a cellar door, only Auto (with room below) and Top place a ladder, in the cover's anchor.
    Bottom, Left side, Right side and Auto without room below are **refused** (red ghost), rather than standing or
    hanging a ladder on the cover's edge as the floor-edge snap would. This is the narrowest choice; the trapdoor's
    Auto instead falls back to its floor edge, because a trapdoor is a flat tile.
23. The anchor is slice 1's planned ladder, unchanged: hung from the front panel's inner face, its top on the snap
    plane, climbing side toward the rear header, in the cover's rotation. Placement aims at the frame only; aiming at a
    leaf hides the ghost (the game's own ray), as on the trapdoor.
24. Passage: a route that crosses the widened opening, from the frame's bottom to the slope's top at the capsule's
    centre, is committed only while both leaves are fully open and not asked to close. A route already under way
    finishes (the leaves hold against a climber they would touch); no new one starts.
25. Landing: a leaf only while the pair is closed and staying closed; never while it moves, is held or is open,
    including an upright leaf's top edge; the frame is an ordinary piece; a cover whose pivots are missing, never.
26. **Over the front panel** (the open question of section 2b): the anchor stays, and a route between the opening and
    the floor's level outside it uses only lifts that carry the feet 5 cm over the panel's inner top edge, 0.52 m above
    the snap plane. Moving the anchor up instead would leave a 2 m ladder's foot 0.47 m above a 2 m dig's floor.
27. A descending entry over the frame aims the hands over the panel, at the same height, instead of the floor-lip rule
    that would aim through it.
28. Its own notice, `cl_msg_cellarclosed`, registered with the cellar doors.
29. Use while climbing is the trapdoor's rule: a hovered cover takes it; the dismount frame is held back once either
    door family exists.
30. Several covers: a route is refused if any cover it crosses is not a passage; over several frames it rises to the
    highest of their panel heights.

Owner questions (slice 4):

- Bottom and the side modes aimed at a cellar door are refused. Should they instead stand or hang a ladder on the floor
  beside the cover?
- Auto aimed at a cellar door with no room below is refused rather than standing the ladder on the frame. Is that right?
- The exit and the entry rise about 0.5 m above the ladder's top to clear the panel. Does that look right in game?
  The panel lift also needs about 2.42 m of headroom over the floor in front (the standard step about 2.30 m), so under a
  low ceiling in front of the cover both the exit and the entry are refused.
- Still open from slice 1: whether a player can stand on the 40.57 degree leaves (the game's ground-angle rule was not
  read). The ladders do not stand a climber there in practice (item 25 and section 2c).

## 9. Remaining #486 slices (each its own PR)

Done: slice 1 (model, PR #534), slice 2 (piece adapter, PR #535, this record's section 3; the wall depths in section
2a), slice 3 (leaf runtime, PR #537, section 4; what it does with each wall in section 2a) and slice 4 (ladder
integration, section 2c).

1. Done in slice 4: the ladder integration (section 2c). With it, #486's code scope is complete.
2. Editable asset sources and export notes: **recorded** in `DOOR_ASSET_SOURCES.md`. The geometry, colliders and
   motion are code, with no `.blend` or bundle. The record gives every object name, pivot, rest transform, collider
   and prism mesh, the shared curve and each family's swing time and materials. `DoorAssetSourcesTests` holds it to
   the code. Still the owner's: whether the code counts as the editable source or the doors should be modelled in
   Blender, and per-leaf motion against the pair rule (`DOOR_ASSET_SOURCES.md` section 11). Candidate provenance: a
   1.1.0 candidate ZIP is built and recorded in `STATUS.md` ("1.1.0 candidate provenance", PR #544); install and smoke
   are OWNER GO-AROUND PENDING.

## 10. In-game rows (all OWNER GO-AROUND PENDING, #470)

None of these has been observed. Slices 2 and 3 register the pieces and drive their leaves behind the setting; nothing
has been placed in game. Slice 2 added these rows: the hammer icons; the frame meshes' faces, shading and texture
scale; the hinge marks on the ghost; a wall from section 2a's table on the rear edge; and a ladder
aimed at a cellar door (superseded by slice 4's rows below: it hangs in the opening or is refused, never on the
frame's outer edge). Placement in a wood floor and beside stone
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
clear. A cover placed beside each wall class of section 2a on the rear edge, saved closed and saved open (Use while held)
and reloaded: free (about 0.22 m or less) or held shut (deeper, lead default 15). Without a wall, stored open, it loads
open; stored closed, closed. Someone standing in the entrance under a closed cover at load: it loads open and shuts once
they have gone (unless a wall also holds the closed pose, lead default 15). A late joiner, a second client and a zone reload see the same pose, except after a close asked for in session into a
wall built while the cover was open (section 2a), which the clients present see part open and a late joiner sees shut. No push
or launch of a player, creature or cart at any point. The ghost and the hammer icon stay closed. A ladder near a cellar
door, which still treats it as an ordinary piece (lead default 18). The trapdoors unchanged with the cellar doors on,
and the setting off leaving everything as on main.

Slice 4 adds these rows, and replaces slice 2's and 3's "a ladder near a cellar door treats it as an ordinary piece".
A ladder aimed at a cellar door's frame, every rotation: in Auto over a dig and in Top it hangs in the opening from the
front panel, facing the rear header; in Bottom, Left side and Right side, and in Auto over solid ground, the ghost is
red and cannot be built; aimed at a leaf, the ghost is hidden. Climbing up under a closed cover: the climber stops
under the leaves with the cellar-door notice; Use from the ladder (keyboard, controller, remapped) opens the pair
without letting go; once open, the climber climbs out over the front panel onto the floor in front without catching on
or sticking to the panel, and the rise of about 0.5 m above the ladder's top looks right. While the pair opens or
closes: no exit, the climber holds. From the floor in front: getting onto the ladder needs the pair open, the hands
reach over the panel and the climber lowers onto the rungs; with the pair closed, the notice. No ladder exit stands a
climber on the leaves, open or closed, nor on an open leaf's top edge. Two cellar doors side by side, and one beside a
trapdoor. A roof about 2.4 m over the floor in front: the exit and entry are refused, not squeezed. Another player opening or closing the pair while someone climbs. Trapdoors on with cellar doors off, and both
off: ladders as on main.
