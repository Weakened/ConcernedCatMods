# Concerned Ladders: railings (#556)

**Status:** the first slice, nine railings, behind `[Railings] Enabled` (off by default). Nothing here has been seen in
game. Every placed-piece row is OWNER GO-AROUND PENDING.

## What the owner asked for (2026-11-10)

- **Pieces:** railings for stairs, and straight ones.
- **Materials:** "same material categorization": wood, copper and iron, like the ladders.
- **Detail:** wood simple, copper slightly decorated, iron even more detailed.
- **Reference photos:**
  - a wood stair with square balusters and a capped newel;
  - a wrought-iron stair with twisted bars and scrollwork;
  - a stair with twisted iron balusters with basket accents.
- **Angles:** "straight and different angles for each type of stair in the game, I believe 33 and 45 degrees".
- **Integration:** later, ladders and railings follow the vanilla material families, as the bulkheads do (#555, #558),
  integrating with vanilla assets.

## The angles are the game's stairs

Measured from the installed game's stair pieces (Valheim 1.0.21), by their snap points, with the read-only probe
`cl-ladders-devtools/doors/stairs.py`:

| Stair pieces | Rise / run | Angle | Railing |
|---|---|---|---|
| `wood_stair`, `stone_stair`, `ashwood_stair`, `blackmarble_stair` (and corners), `piece_grausten_stonestair`, `dvergrprops_wood_stair` | 1 / 2 m | 26.57° | Stair Railing |
| `wood_stepladder` | 2 / 2 m | 45° | Steep Stair Railing |
| (floors, landings) | 0 / 2 m | 0° | Railing |
| `piece_dvergr_spiralstair` (left and right) | 1 m over a quarter turn | curved | not in this slice |

No straight stair in the game is 33°, so the railings follow the two slopes the game has.

## Geometry (`Domain/Railings/RailingGeometry.cs`)

Piece space: x across, y up, z along the railing from its low end.

- **Base line.** From the bottom snap at the origin to the top snap at (0, rise, 2). A stair's bottom and top side snaps
  are the same rise and run apart: `wood_stair` (±1, 0, 1) and (±1, 1, −1); `wood_stepladder` (±0.5, 0, 1) and (±0.5, 2, −1).
  So a railing whose bottom snap meets a stair's bottom side snap, turned to run up the stair, has its top snap on the
  stair's top side snap. Tested in `TheSnapsAreSpacedAsTheStairsSideSnaps`.
- **Posts.** At both ends, 1.05 m above the base line.
- **Handrail.** Its top 0.95 m above the base line, the same everywhere along the slope (tested).
- **Bottom rail.** 0.1 m above the base line. Copper and iron have one; wood has one only on the level railing.
- **Balusters.** Vertical, evenly spaced between the posts, at most 0.16 m apart.
- **Collider.** One box 0.12 m thick, from 5 cm above the base line to the handrail, along the slope, on its own child.
  It keeps a player from walking off a stair's side, and gives the support and hammer geometry WearNTear uses.

## What each tier is made of (`RailingDesign`)

| Tier | Posts | Balusters | Decoration |
|---|---|---|---|
| Wood (simple) | 0.11 m square, capped | 0.045 m square, plain | none |
| Copper (slightly decorated) | 0.08 m, capped | 0.03 m, twisted a turn and a half | a knuckle half way up each baluster |
| Iron (most detailed) | 0.07 m, with a pointed finial | 0.022 m, twisted three turns | every fourth bar is straight and carries four C-scrolls (two pairs, back to back) |

All of it is procedural (`RailingMesh`), with no imported art:

- Each bar is a square-section prism; a twisted bar is rings turned 45° further at each step.
- Each knuckle is an octahedron, and each finial a pointed one.
- The mesh is flat-shaded, wound outward and bounded (tested). It uses the tier's borrowed material with tiling UVs, as
  the ladders do.

## Family table (`RailingFamilies`)

Every railing takes its tier's ladder's donor, material, tint, health and station (`LadderPieceRegistration`), and a test
reads them from there. So a railing reads as its tier's ladder.

| Tier | Donor | Material | Health | Station | Cost (level / sloped) |
|---|---|---|---|---|---|
| Wood | `woodwall` | `woodwall` | 200 | workbench | 2 / 3 Wood |
| Copper | `woodiron_pole` | `copper` (from the Copper item), warm tint | 400 | forge | 1 Copper + 1 Wood |
| Iron | `woodiron_pole` | `Ironbeam_mat`, cool tint | 800 | forge | 1 Iron + 1 Wood |

The prefab IDs are `tcc_railing_<wood|copper|iron>_<level|stair|steep>`, a save contract once shipped. The names are
"Wood Railing", "Wood Stair Railing", "Wood Steep Stair Railing", and so on. Costs and health are lead defaults.

## Registration (`Pieces/RailingPieceRegistration.cs`)

The ladders' path:

1. A Jötunn clone of the donor, its LODs, colliders, children, renderers and filters removed from the inactive clone only.
2. The railing mesh, in a tinted, shaded copy of the material for each wear state.
3. The collider child.
4. The bottom and top snap points.
5. The rendered icon.

No RPC, ZDO field or ownership claim is added, and no native prefab is changed. The registration is installed only with
the switch on.

## Not yet (later slices of #556, and #558)

- **More materials.** The vanilla material families beyond wood, copper and iron (Darkwood, Ashwood, Flametal, Dvergr),
  matched to the game's own pieces of those materials.
- **Curved stairs.** A railing for the spiral stair.
- **Corners.** Newel posts for corners, and corner snapping.
