# Concerned Ladders: railings (#556)

**Status:** 40 railings, behind `[Railings] Enabled` (off by default): eight material families in five shapes (#565
removed the spiral-stair railings at the owner's request on 2026-11-11 and added a 1 m short railing). Slice 1
(PR #561) was wood, copper and iron in three slopes. Slice 2 adds the vanilla families and the newel post.
Nothing here has been seen in game. Every placed-piece row is OWNER GO-AROUND PENDING.

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
| (a 1 m gap, a landing's end) | 0 / 1 m | 0° | Short Railing |
| (corners, the ends of a run) | none | none | Newel Post |

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

## Slice 2: the vanilla families and the newel post (and #565: the short railing)

**Families** (`Domain/Families/BuildFamilies.cs`, shared with the ladders, #558). Owner, 2026-11-10: "extend the ladders
and railings in a similar fashion ... beautifully integrate with vanilla assets."

The ladders' wood, copper and iron are joined by each vanilla building family the doors follow (#555). Each is built from
the game's own pole of that material: its donor, its material, and its recipe, station and health, read from the donor at
registration so they stay the game's.

| Family | Donor | Material | Texture region | Style |
|---|---|---|---|---|
| Wood | `woodwall` | `woodwall` | tiles (seamless planks) | plain |
| Copper | `woodiron_pole` | `copper` | tiles (a colour, no texture) | decorated |
| Iron | `woodiron_pole` | `Ironbeam_mat` | u 0.8348-0.9095, v 0.0552-0.4913 (the pole's iron band; the sheet also holds wood) | detailed |
| Darkwood | `darkwood_pole` | `DarkWood_mat` | u 0.6312-0.7565, v 0.2212-0.5655 | plain |
| Ashwood | `ashwood_pole_2m` | `AshWood_mat` | u 0.4772-0.9451, v 0.0435-0.9795 | plain |
| Flametal | `Piece_flametal_beam` | `flametalbeam_mat` | u 0.4119-0.49, v 0.4155-0.8291 | detailed |
| Dvergr | `piece_dvergr_pole` | `DvergrTownPieces_Mat` | u 0.025-0.4593, v 0.0978-0.8087 | plain |
| Log | `wood_pole_log` | `logwall` | u 0.0701-0.4945, v 0.0109-0.9266 (the bark) | plain |

Each region is the donor pole's own largest UV island, read by `cl-ladders-devtools/doors/pole_islands.py`. Every one of
these textures but the plank sheet and copper's plain colour is an atlas (#548's rule), so the railing mesh maps each face
into the region (`RailingMesh.Build(..., region)`). Since slice 2 that includes iron, whose slice-1 railings stretched the
whole sheet.

**Short railing** (`RailingAngle.Short`, #565): a level railing 1 m long, with the same posts, handrail and style,
snapping end to end with the 2 m ones. The spiral-stair railings of slice 2 were removed (owner, 2026-11-11). Their IDs
never shipped.

**Newel post** (`RailingAngle.Newel`): one post, 1.2 m tall and 1.3 times the style's post width, capped (or with a finial
on the detailed style). It has a bottom snap only, for corners and the ends of a run.

There are 40 pieces: eight families in five shapes. Slice 1's nine IDs are unchanged.

## Not yet

- **Corner snapping.** A railing whose end meets a newel post snaps by the post's base only.
