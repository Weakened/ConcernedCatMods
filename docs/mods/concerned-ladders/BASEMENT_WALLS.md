# Basement walls (#568)

Owner playtest, 2026-11-11: "the hole walls are still not straight as a wall ... Maybe we can add premade walls like
stone basement, wooden basement etc."

## 1. Why pieces, not straighter digging

A dug side cannot be vertical. The game's terrain is a heightmap with one vertex per metre, so the ground between the
floor's edge and the untouched ground beside it slopes across one lattice cell (EXCAVATION.md section 3). No terrain
operation changes that; a wall piece standing in front of the slope is the only way to make the side read straight.

## 2. Shape

Each piece is built from boxes in its own space: x along the wall, y up from the hole's floor, z outward from the hole.

| Part | Where | Size |
|---|---|---|
| Panel | inner face on the floor's edge (z 0), from the floor up to the lip | width x (height - 0.12) x 0.2 m |
| Lip | its top at ground level (y = height), from the floor's edge outward over the sloping cell | width x 0.12 x 1 m |
| Corner cap | the diagonal cell two lips leave at a corner, its top at ground level | 1 x 0.12 x 1 m |

Sizes: 1 or 2 m wide, 1, 2 or 4 m tall, matching the Iron Shovel's ready-made cubes (#567). Wood and stone, so 12
walls and 2 corner caps, all in the hammer's Building tab.

Snap points: each wall's two floor corners and two top corners on its inner face (it snaps to the wall beside it and
the one above it) and its lip's two outer corners; the corner cap's four corners.

## 3. Native behaviour borrowed, nothing else

Each piece is a Jötunn clone of `woodwall` (wood) or `stone_wall_2x1` (stone), so health class, structural support,
damage, repair and refunds are the game's own. The donor's geometry is replaced on the inactive clone by the boxes
above, in the donor's own material (`woodwall`; `stone_mat`), with darker copies for worn and broken, a box collider
per part and the snap points. No RPC, ZDO field or ownership claim is added, and no vanilla prefab is changed.

Cost (lead default): one unit of wood or stone per two square metres of panel and lip, at least one; a corner cap
costs one. Wood at the workbench, stone at the stonecutter.

## 4. Switch

`BasementWalls` / `Enabled`, off by default. With it off nothing is cloned or localized. Turning it off again, or
uninstalling, makes the game delete placed basement walls when a world loads, with no refund.

## 5. Open questions (OWNER GO-AROUND PENDING)

- Whether `stone_mat`'s texture (64 px, possibly sampled by world position rather than UV) reads well on these boxes.
- Whether the stone wall's support class holds a 4 m panel standing on terrain.
- Whether the lip sits flush with the untouched ground in every biome's terrain, or needs a small drop.
