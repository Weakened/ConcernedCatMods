# Concerned Ladders: door asset sources, rebuild notes and provenance (CL-004 #485, CL-005 #486)

**Status (2026-11-09):** this records what the floor trapdoors and the sloped cellar doors are made of, where each part
is defined, and how to change it and rebuild it. Every number comes from the source files named below. None of it has
been seen in game; every placed-piece row is OWNER GO-AROUND PENDING (#470). `DoorAssetSourcesTests`
(`src/ConcernedLadders.Tests/Doors/DoorAssetSourcesTests.cs`) checks this file against the code: every object name,
the curve samples, the per-family table and the main transforms. If one of them changes, that test fails until this
file is updated.

The issues ask for editable sources: `.blend` files where Blender is used, materials, collider definitions, animation
curves, export notes, and candidate provenance. Section 1 says which of these exist and in what form. Sections 2 to 7
are the sources. Section 8 covers editing and rebuilding, section 9 what ships, and section 10 what is original. The
1.1.0 candidate provenance record is in `STATUS.md`. Section 11 lists what only the owner can decide or do.

## 1. What the editable sources are

**The geometry, the colliders and the motion are written in C#.** The door pieces were never modelled in Blender or
any other tool, so there is no `.blend`, FBX, Unity scene, AssetBundle or texture for them. None was left out of the
repository, either. Only when `Trapdoors/Enabled` or `CellarDoors/Enabled` is on (both are off by default), and once
Jötunn reports the game's prefabs ready (`PrefabManager.OnVanillaPrefabsAvailable`), the plugin clones each donor door
and builds every frame, lid, leaf, collider and snap point from the numbers below.

| Source file (under `src/ConcernedLadders/`) | Owns |
|---|---|
| `Domain/Doors/HatchGeometry.cs` | Trapdoor dimensions: module, frame bands, aperture, lid, hinge axis, open angle, snaps, ladder origin |
| `Domain/Doors/TrapdoorBlueprint.cs` | Trapdoor build plan: object names, the boxes in their parent space, the hinge straps, the donor materials by name |
| `Domain/Doors/CellarGeometry.cs` | Cellar door dimensions: slope, cheeks, front panel, rear header (as prisms), leaves, hinge axes, open angle |
| `Domain/Doors/CellarBlueprint.cs` | Cellar door build plan: object names, the slope transform, leaf and mark boxes, the prism meshes (vertices, normals, UVs, triangles) |
| `Domain/Doors/HatchMotion.cs` | The motion curve (`Ease`), the swing timing, sub-step and skin limits, shared by lid and leaves |
| `Domain/Doors/DoorFamilies.cs` | The nine donor families, each with its swing time (the donor animator's clip length) |
| `Domain/Doors/CellarDoors.cs` | The cellar doors' prefab IDs, names and doubled costs |
| `Pieces/DoorPieceKit.cs` | The shared builder: strip the donor, cube visuals, box colliders, snaps, hinge material, icon render |
| `Pieces/TrapdoorPieceRegistration.cs`, `Pieces/CellarDoorPieceRegistration.cs` | Walk the blueprints into a clone; build the prism meshes |
| `Runtime/TrapdoorLid.cs`, `Runtime/CellarLeaves.cs` | Turn the hinge transforms from the native door state every frame |

The climbing animation is different. It is authored in Blender, in `AnimationSources/LadderGait.blend`, and exported
to C# (see `AnimationSources/README.md`). The doors do not use that pipeline.

## 2. Units and axes

Metres, degrees and seconds. Transforms and sizes in the tables are rounded to 4 decimals (0.1 mm; the hinge's y is
0.096845, shown as 0.0968); extents and profiles are given to 6. Piece-local axes are the piece's own: x right, y up, z forward. Unity is left-handed, so a
positive rotation about +x turns +z toward −y and −z toward +y. That is why the trapdoor's lid, which hangs toward −z
from its hinge, lifts for a positive angle. The snap plane is y = 0. A wood floor's top is y = 0.096845 and its bottom
is y = −0.032905. Both were measured from the installed `wood_floor` (Valheim 1.0.21, `DOOR_DONOR_MATRIX.md`
section 5).

## 3. Trapdoor: hierarchy, pivots and rest transforms

The clone keeps the donor root's `Piece`, `ZNetView`, `WearNTear`, `Door` and `Animator`, and adds `TrapdoorLid`.
Every object the adapter builds is on the game's `piece` layer. "Rest" is the closed pose. A cube visual is Unity's built-in cube with its
collider removed, scaled to the size given. A box collider sits on its own empty child, with its centre and size set
on the collider itself.

| Path from the root | Kind | Position (parent space) | Rest rotation | Size | Notes |
|---|---|---|---|---|---|
| `door` | bare transform | donor's | donor's | none | The donor leaf, kept as a bare transform (its layer is the donor's). The hidden native animator writes it and nothing reads it (`DOOR_DONOR_MATRIX.md` section 4) |
| `new` | presentation root | origin | identity | none | `WearNTear.m_new`. Holds the four frame visuals in the donor's frame material |
| `worn` | presentation root | origin | identity | none | `m_worn` and `m_broken`, in the donor's worn material. Absent where the donor has one root: `new` then serves all three states |
| `new/frame_front`, `new/frame_rear`, `new/frame_left`, `new/frame_right` (and the same under `worn`) | cube visual | see the frame table | identity | see the frame table | No collider |
| `frame_front`, `frame_rear`, `frame_left`, `frame_right` | box collider | origin; centre on the collider | identity | see the frame table | Static. `WearNTear` measures support with these |
| `lid_hinge` | pivot, kinematic `Rigidbody` (no gravity) | (0, 0.0968, 0.79) | identity | none | The hinge axis: the lid's top rear edge. Opens by `Quaternion.Euler(angle, 0, 0)`, 0° closed to 90° open |
| `lid_hinge/lid_collider` | box collider | centre (0, −0.04, −0.79) | identity | 1.58 x 0.08 x 1.58 | Moves with the pivot |
| `lid_hinge/lid` | cube visual | (0, −0.04, −0.79) | identity | 1.58 x 0.08 x 1.58 | Donor leaf material |
| `lid_hinge/hinge_strap_left`, `lid_hinge/hinge_strap_right` | cube visual | left (−0.5, 0.006, −0.18), right (0.5, 0.006, −0.18) | identity | 0.08 x 0.012 x 0.36 | Hinge material (section 6). Marks the hinge side on the ghost. No collider |
| `$hud_snappoint_corner` (four) | snap point, tag `snappoint` | (±1, 0, ±1) | identity | none | The wood floor's own corner snaps and token |

The frame bands, from `HatchGeometry.Frame` (minimum and maximum corners; height 0.12975, centre y 0.03197):

| Band | x | y | z |
|---|---|---|---|
| `frame_front` | −1 to 1 | −0.032905 to 0.096845 | −1 to −0.8 |
| `frame_rear` | −1 to 1 | −0.032905 to 0.096845 | 0.8 to 1 |
| `frame_left` | −1 to −0.8 | −0.032905 to 0.096845 | −0.8 to 0.8 |
| `frame_right` | 0.8 to 1 | −0.032905 to 0.096845 | −0.8 to 0.8 |

The lid has a 0.01 m gap to the frame on every side and is 0.08 m thick. Its top is flush with the floor. Opened 90°,
it stands upright with its front face at z 0.71. The clear opening is then x ±0.8 by z −0.8 to 0.71. Placement, ladder
offsets and passage are in `DOOR_DONOR_MATRIX.md` sections 5 and 6.

## 4. Cellar door: hierarchy, pivots and rest transforms

The root keeps the same donor components as the trapdoor and adds `CellarLeaves`. Everything the adapter builds is on
the `piece` layer. The frame is four convex prisms, each one mesh. A frame part's renderer under `new` (and `worn`) and its
`MeshCollider` (static, `convex = true`, directly under the root) use the same mesh.

| Path from the root | Kind | Position (parent space) | Rest rotation | Size | Notes |
|---|---|---|---|---|---|
| `door` | bare transform | donor's | donor's | none | As on the trapdoor |
| `new`, `worn` | presentation roots | origin | identity | none | As on the trapdoor. Each holds one mesh renderer per prism, named as below |
| `cheek_left`, `cheek_right`, `front_panel`, `rear_header` | convex mesh collider | origin | identity | prism | See the prism table |
| `slope` | transform | (0, 0.3968, −1), the front top edge's centre | `Quaternion.Euler(-26.565, 0, 0)` | none | The slope frame: x along the slope's width, y its upward normal, z up the slope |
| `slope/leaf_left_hinge` | pivot, kinematic `Rigidbody` (no gravity) | (−0.84, 0, 1.0751) | identity | none | The left leaf's top outer edge, half way along the leaf. Opens by `Quaternion.Euler(0, 0, angle)` |
| `slope/leaf_right_hinge` | pivot, kinematic `Rigidbody` (no gravity) | (0.84, 0, 1.0751) | identity | none | Opens by `Quaternion.Euler(0, 0, -angle)`: the left leaf mirrored |
| `slope/leaf_left_hinge/leaf_left_collider`, `slope/leaf_right_hinge/leaf_right_collider` | box collider | centre left (0.4175, −0.03, 0), right (−0.4175, −0.03, 0): toward the seam | identity | 0.835 x 0.06 x 1.7924 | |
| `slope/leaf_left_hinge/leaf_left`, `slope/leaf_right_hinge/leaf_right` | cube visual | left (0.4175, −0.03, 0), right (−0.4175, −0.03, 0) | identity | 0.835 x 0.06 x 1.7924 | Donor leaf material |
| `slope/leaf_left_hinge/hinge_mark_left_front`, `slope/leaf_left_hinge/hinge_mark_left_rear`, `slope/leaf_right_hinge/hinge_mark_right_front`, `slope/leaf_right_hinge/hinge_mark_right_rear` | cube visual | x 0.15 on the left leaf, −0.15 on the right (inboard of the hinge); y 0.004; z −0.6 front, 0.6 rear | identity | 0.3 x 0.008 x 0.08 | Hinge material. Thinner than the 1 cm cheek gap. No collider |
| `$hud_snappoint_corner` (four) | snap point | (±1, 0, ±1) | identity | none | Shared with the trapdoor |

The prisms are four-point profiles in the piece's (z, y) plane, extruded along x (`CellarGeometry.Frame`):

| Prism | x | Profile (z, y), in order |
|---|---|---|
| `cheek_left` | −1 to −0.85 | (−1, −0.032905), (1, −0.032905), (1, 1.396845), (−1, 0.396845) |
| `cheek_right` | 0.85 to 1 | the same profile |
| `front_panel` | −0.85 to 0.85 | (−1, −0.032905), (−0.85, −0.032905), (−0.85, 0.471845), (−1, 0.396845) |
| `rear_header` | −0.85 to 0.85 | (0.8, −0.032905), (1, −0.032905), (1, 1.396845), (0.8, 1.296845) |

**Mesh build** (`CellarBlueprint.Mesh`). Each prism has six flat faces with four vertices each: 24 vertices and 12
triangles, every face wound outward and given its own unit normal. The two end caps take UVs (z/2, y/2). Each of the
four side faces takes (x/2, distance along its profile edge/2). So one UV unit is 2 m (`CellarBlueprint.UvMetres`), the
same on every face. Unity then recalculates bounds and tangents. The mesh is named `tcc_cellardoor_<prism>`, shared by
all nine families (only the material differs), and kept loaded with `HideFlags.DontUnloadUnusedAsset`. Tests check that
it is closed, outward-facing and convex, and that it encloses exactly the prism's volume (`CellarBlueprintTests`).

## 5. Animation curves and timing

The doors have no animation clip. Each frame, the lid runtime and the leaf runtime turn the hinge transforms from one
progress value p, which runs from 0 (closed) to 1 (open). The same p sets the visuals and the colliders, so the two
cannot disagree.

- **Time to progress: linear.** p moves by `seconds / SwingSeconds` per frame toward the native state's end
  (`HatchMotion.Proposed`). A full swing takes exactly the donor's swing time, and a reversal continues from the
  current p. One frame's step is capped at 0.05 s (`MaximumStepSeconds`).
- **Progress to angle: smoothstep.** `HatchMotion.Ease(p) = p * p * (3 - 2 * p)` and `angle = 90 * Ease(p)`
  (`HatchMotion.Angle`). The curve's steepest slope, 1.5 at p = 0.5, is pinned as `MaximumEaseSlope`. The sub-step rule
  relies on it: no two checked poses are more than 5° apart (`MaximumAngleStepDegrees`).
- **Shared.** The trapdoor's lid and both cellar leaves use this one curve (the cellar's open angle is also 90°). The
  two leaves are always at the same angle (`CellarPairOccupancy`, `CELLAR_DOORS.md` section 4).

Samples of the curve:

| p | angle (degrees) |
|---|---|
| 0.0 | 0.00 |
| 0.1 | 2.52 |
| 0.2 | 9.36 |
| 0.3 | 19.44 |
| 0.4 | 31.68 |
| 0.5 | 45.00 |
| 0.6 | 58.32 |
| 0.7 | 70.56 |
| 0.8 | 80.64 |
| 0.9 | 87.48 |
| 1.0 | 90.00 |

The swing time and materials of each family. The swing time is the donor animator's opening clip: 0.5 s for
`door_animator`, 1.0 s for `darkwood_gate_animator`, 2.0 s for `Flametal_gate_animator`. The materials are named as
in the installed game (`TrapdoorBlueprint.MaterialsFor`):

| Donor | Swing | Trapdoor prefab | Cellar door prefab | Leaf material | Frame material | Worn frame material |
|---|---|---|---|---|---|---|
| `wood_door` | 0.5 s | `tcc_trapdoor_wood` | `tcc_cellardoor_wood` | `door_wood` | `woodwall` | `woodwall_worn` |
| `wood_gate` | 0.5 s | `tcc_trapdoor_wood_gate` | `tcc_cellardoor_wood_gate` | `door_wood` | `woodwall` | `woodwall_worn` |
| `wood_fence_gate` | 0.5 s | `tcc_trapdoor_roundpole` | `tcc_cellardoor_roundpole` | `fence_gate_mat` | `fence_gate_mat` | `fence_gate_worn_mat` |
| `iron_grate` | 0.5 s | `tcc_trapdoor_iron_gate` | `tcc_cellardoor_iron_gate` | `fi_village_metal` | `metalwall` | none (one root) |
| `darkwood_gate` | 1.0 s | `tcc_trapdoor_darkwood` | `tcc_cellardoor_darkwood` | `DarkWoodGate_door` | `DarkWoodGate_mat` | `DarkWoodGate_worn` |
| `stave_gate` | 1.0 s | `tcc_trapdoor_timberwood` | `tcc_cellardoor_timberwood` | `TimberGate_mat` | `TimberGate_mat` | `TimberGate_worn_mat` |
| `piece_hexagonal_door` | 1.0 s | `tcc_trapdoor_hexagonal` | `tcc_cellardoor_hexagonal` | `Hexagonal_mat` | `Hexagonal_mat` | none (one root) |
| `ashwood_door` | 1.0 s | `tcc_trapdoor_ashwood` | `tcc_cellardoor_ashwood` | `ashwood_door_mat` | `ashwood_door_mat` | `ashwood_door_worn_mat` |
| `flametal_gate` | 2.0 s | `tcc_trapdoor_flametal` | `tcc_cellardoor_flametal` | `flametalgate_mat` | `flametalgate_mat` | none (one root) |

The swing time is kept equal to the donor's clip on purpose. The hidden native animator still plays that clip, and
`Door.CanInteract` refuses a Use until it reaches its open or closed state. A lid that moved faster or slower than the
clip would finish while Use is still refused, or still be moving once it is accepted again.

## 6. Materials, textures and icons

- **Borrowed, never copied out of the game.** The frame, worn-frame and leaf materials are the installed donor's own,
  found by name among the native donor prefab's renderers (`DoorPieceKit.DonorMaterial`); the native prefab is only
  read, and only the clone is stripped.
  They are assigned as shared materials and never changed. If one is missing, that family is refused with a log line
  rather than drawn with a substitute.
- **The hinge material** is the one material the mod creates. It is a runtime copy of the leaf material, named
  `<leaf>_tcc_hinge`, with `_Color` multiplied by 0.35 (RGB) where the shader has that property
  (`DoorPieceKit.HingeMaterial`). The shared native material is untouched.
- **Texture mapping.** The trapdoor's boxes use the built-in cube's UVs, which map the full texture once onto each
  face whatever the box's size. A 2 m frame band and its 0.13 m edge therefore show the texture at different scales.
  The cellar prisms use one UV unit per 2 m on every face (section 4). The cellar's leaves, its hinge marks and all
  trapdoor parts are built-in cubes. How either looks is OWNER GO-AROUND PENDING.
- **No texture or image file** belongs to the doors. The hammer icons are rendered at load from the finished clone
  (`RenderManager.Instance.Render(prefab, RenderManager.IsometricRotation)`, Jötunn 2.30.2). If the render fails, the
  donor's own icon is kept. Rendering is skipped in batch mode.

## 7. Collider definitions (summary)

| Piece | Collider | Shape | Moves | Counted for support |
|---|---|---|---|---|
| Trapdoor | `frame_front`, `frame_rear`, `frame_left`, `frame_right` | `BoxCollider` | no | yes (static) |
| Trapdoor | `lid_hinge/lid_collider` | `BoxCollider` on the kinematic pivot | with the lid | no (`WearNTear` and the build ray skip colliders on a body) |
| Cellar door | `cheek_left`, `cheek_right`, `front_panel`, `rear_header` | convex `MeshCollider`, the prism's own mesh | no | yes |
| Cellar door | `slope/leaf_left_hinge/leaf_left_collider`, `slope/leaf_right_hinge/leaf_right_collider` | `BoxCollider` on the kinematic pivot | with the leaf | no |

Visuals never carry a collider, and colliders never carry a renderer, so a change of wear state never changes
collision. For each checked pose, the occupancy query is that collider's own box at that angle, shrunk by 0.01 m on
every side (`HatchMotion.QuerySkinMetres`). The layers, the blocking rule and what each query ignores are in
`DOOR_DONOR_MATRIX.md` section 6 and `CELLAR_DOORS.md` section 4. The adapter refuses any clone whose colliders and
renderers differ from these (`Verify` in each registration, `DoorPieceKit.VerifyStripped`).

## 8. Editing and rebuilding (export notes)

There is no export step. To change a door:

1. Edit the constant in the source file from section 1. For example: the frame width is `HatchGeometry.FrameWidth`,
   the cellar's front height is `CellarGeometry.FrontHeight`, the curve is `HatchMotion.Ease`, a family's swing time
   is its `DoorFamilies` entry, and a mark's size is in `CellarBlueprint`.
2. Run the door tests: `dotnet test src/ConcernedLadders.Tests/ConcernedLadders.Tests.csproj -c Release --filter
   "FullyQualifiedName~Doors"` (in this repository's lanes, under the machine's build lock). **The test project does
   not build the product.** It compiles only the game-free sources (`Domain/**`, `Capabilities/WorkerLadderLinks.cs`
   and `src/Shared/Ladders/**`) on net10.0. A change under `Pieces/` or `Runtime/` is compiled only by
   `scripts/verify.ps1` (step 4); the tests read those files only as text.
   Many tests pin geometry by design. Clearance, hinge-side containment, sweep envelopes, the leaves' separation and
   the ladder margins are in `HatchGeometryTests`, `CellarGeometryTests`, `HatchLadderTests` and `CellarLadderTests`.
   Build plans and meshes are in `TrapdoorBlueprintTests` and `CellarBlueprintTests`. The curve and sub-steps are in
   `HatchMotionTests`. The hinge rotation conventions are pinned in the blueprint, geometry and runtime tests (`TrapdoorLidTests`,
   `CellarLeavesTests`). This
   document is checked by `DoorAssetSourcesTests`. A failure there is either a real conflict or a pin that has to be
   updated on purpose. Update a pin only once the new value has been shown to keep its property.
3. Update this file, `DOOR_DONOR_MATRIX.md` and `CELLAR_DOORS.md` where the change shows.
4. The gate is `scripts/verify.ps1`, under the build lock. The package is built by `scripts/package.ps1 -Product
   ConcernedLadders` (the lead runs it, under the lock). The DLL is its only built output, and that DLL rebuilds every
   door at load. Nothing else needs regenerating or exporting.
5. Changes to geometry or motion need the in-game rows again (`DOOR_DONOR_MATRIX.md` section 9, `CELLAR_DOORS.md`
   section 10).

Changing the shape of a hinge-side part needs one extra check. The rule that no lid or leaf point ever passes behind
its hinge or below its closed bottom is what lets the frame do without a rebate. `HatchGeometryTests` check it for the trapdoor at a
set of sampled angles and `CellarGeometryTests` for the cellar door every 0.5°, so a change that breaks the rule fails
them.

## 9. What ships, and what never does

The Thunderstore package holds the six standard files: `manifest.json`, `README.md`, `CHANGELOG.md`, `LICENSE`,
`icon.png` and `plugins/TheConcernedCat.ConcernedLadders.dll` (`TEST_PLAN.md` section 10;
`tools/verify_ladders_candidate.py` accepts exactly these). The doors add no file to it. No mesh, texture, material,
bundle or game binary is redistributed: the materials come from the player's own installed game at run time.
`AnimationSources/` (the climbing `.blend`) is not packaged either.

## 10. What is original, and what informed it

| Item | Origin |
|---|---|
| Trapdoor and cellar door geometry, collider shapes, meshes, UVs, hinge straps and marks, the motion curve | Original to this repository, written in the C# files of section 1 |
| Floor-grid measurements (wood floor top and bottom, corner snaps) | Measured from the installed Valheim 1.0.21 `wood_floor` (`DOOR_DONOR_MATRIX.md` section 1 names the census tools) |
| Swing times | The installed donors' animator clip lengths (census) |
| Frame, worn-frame and leaf materials | The installed game's own, borrowed at run time. Not redistributed |
| Hinge material | A runtime copy of the leaf material, darkened. Not stored |
| Icons | Rendered at load from the clone by Jötunn |
| Design references | The three photographs the owner supplied, `floor-trapdoor-open.png`, `cellar-wood-closed.png`, `cellar-metal-open.png`, with their own README. They are kept, byte for byte, at commit `b4dd194` on branch `docs/concerned-ladders-expansion-references-c18fbae0bf9e`, not on `main`. That README records that ownership and licensing were not established. They informed only the concept: a flush hatch with an edge hinge, and paired sloped leaves hinged at the outer edges. Nothing derived from them ships |

No purchased, stock or extracted mesh, texture or animation is used for the doors.

## 11. What still needs the owner

1. **Is the code the editable source?** Nothing was authored in Blender. The lead's reading, for the owner to accept
   or reject, is that the "`.blend` files for authored Blender assets" criterion therefore asks for no file, and that
   the C# sources of section 1 are the editable sources. If the owner wants the doors modelled in Blender
   instead (shaped edges, planks, hardware), that is a new design job with an import path to choose. One option is a
   Unity AssetBundle, which needs a Unity editor matching the game's version; the repository has no bundle pipeline for
   Concerned Ladders. The other is a `.blend` → mesh-data → C# generator like the climbing animation's. Either way the
   owner chooses, and the art is produced by a person with Blender. No placeholder `.blend` was committed: a file that
   is not the source would mislead whoever edits it.
2. **Per-leaf motion.** CL-005 asks that "either leaf's frame-by-frame movement" can be edited. Today both leaves follow
   one curve at one angle. That is the pair rule (lead default, `CELLAR_DOORS.md` section 4): a leaf that meets
   something holds both, so one leaf can never stand open while the other is shut. Separate per-leaf curves would mean
   giving up that rule or redefining it, so the owner has to decide. Editing the one shared curve (`HatchMotion.Ease`)
   is supported today, and it changes the trapdoor's lid as well.
3. **Looks.** The texture scale on the built-in cubes (section 6), the hinge-strap and mark contrast, and the icons all
   need to be seen in game.
4. **Candidate provenance.** Someone has to freeze the source, build the package under the lock, record the ZIP and
   DLL hashes, show that the packaged DLL is the verified one, install the package and smoke it. That is the lead's or
   the owner's job, and `STATUS.md` ("1.1.0 candidate provenance") lists every field. A 1.1.0 candidate ZIP is built and its hashes
   recorded there (PR #544); installing and smoking it are still the lead's or the owner's.
5. **The reference photographs** are on a branch, not `main` (section 10). Whether to merge them, given their
   unestablished licence, is the owner's call. They are never packaged.
