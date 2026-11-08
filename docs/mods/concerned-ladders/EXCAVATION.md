# Concerned Ladders: Iron Shovel excavation (#487, CL-006): geometry contract and terrain feasibility

**Status: design record plus a game-free planner. No shovel, preview or terrain operation exists in the
product yet.** Everything below about the engine was **read from the installed game's code**. None of it
was **observed in the running game**. Runtime measurement is **NOT RUN**. Every in-game row is **OWNER
GO-AROUND PENDING**.

## 1. Geometry contract (WORKING ASSUMPTION)

> **The issue asks for 2 × 2 m and 4 × 4 m "cubical" holes. That is read here as 2 × 2 × 2 m and
> 4 × 4 × 4 m (width × length × depth). This is a working assumption, not a confirmed owner depth
> requirement.** It lives in one place, `ExcavationGeometry.For`, so changing it is a one-line decision.

| Mode | Usable flat floor | Depth below datum | Whole changed square (border) | Wall on flat ground |
|---|---|---|---|---|
| Small | **2 × 2 m** (3 × 3 vertices) | **2 m** | 4 × 4 m (1 m sloping cell each side) | 2 m drop over 1 m, 63.4° |
| Large | **4 × 4 m** (5 × 5 vertices) | **4 m** | 6 × 6 m (1 m sloping cell each side) | 4 m drop over 1 m, 76.0° |

- **Usable floor** means the flat area, measured on the terrain surface that the game's collider is
  built from. It does not mean the selection rectangle. A 2 × 2 m floor piece fits exactly. The walls
  lean outward from the floor edge, so a vertical wall piece standing on that edge does not cut into the
  slope above it. At its foot, a wall's own thickness (a few centimetres either side of the edge) dips
  into the bottom of the slope. Pieces may overlap terrain, so this is not expected to block placement,
  but that is **NOT RUN**.
- **The walls are not vertical, and cannot be made vertical** (section 3). The border is the honest
  minimum: exactly one 1 m lattice cell on each side. It is shown separately from the floor.
- **Datum:** the highest vertex of the untouched ring, one metre outside the floor. **Floor:** one
  absolute height, the datum minus the mode's depth. The result is a level floor on sloped ground, not
  the old slope shifted down. On a slope the downhill walls are lower than the depth. The plan reports
  the lowest and highest wall height.
- **Alignment:** the floor's edges fall on whole world metres. The square follows the world axes only
  (section 3), so build pieces line up with it at a rotation of 0° or 90°. No rotated ghost will be drawn.

## 2. Installed versions (read 2026-11-08)

| Item | Version | Source |
|---|---|---|
| Valheim | 1.0.21 | `Version.CurrentVersion` in the installed `assembly_valheim.dll` (file dated 2026-10-28). The Ladders README says it was tested on 1.0.20. |
| Unity | 6000.0.75 | `UnityPlayer.dll` file version |
| BepInEx | 5.4.23.5 | `BepInEx.dll` in the build-reference profile TCC-Gunnar-Test. The package pin is BepInExPack 5.4.2351. |
| Jötunn | 2.30.2 | Product pin. The same version is in TCC-Gunnar-Test. |

## 3. What vanilla terrain modification can and cannot produce

All of this was read from the decompiled installed assembly (`ilspycmd`, local only, not committed).

**The only path used is the player tools' own.** `Attack.SpawnOnHitTerrain(point, prefab, …)` is the
pickaxe's path. It refuses unless `PrivateArea.CheckAccess(point, op.GetRadius())` passes and the point
is outside `Location.IsInsideNoBuildLocation`. It then instantiates a prefab carrying a `TerrainOp`.
`TerrainOp.Awake` finds every `Heightmap` within the op's radius and calls
`GetAndCreateTerrainCompiler().ApplyOperation(op)` on each one. That call sends the
`RPC_ApplyOperation` RPC to the tile's `TerrainComp` owner. The owner applies the operation, saves it to
its own network object (`ZDOVars.s_TCData`) and pokes the heightmap. Persistence, reload and
synchronisation all come from that path. The mod writes no heights, no ZDO and no RPC of its own.

Facts that decide the shape:

1. **The lattice.** A heightmap stores one height per vertex. `WorldToVertex` rounds `floor(v + 0.5)`.
   Tiles are the 64 m zones centred on multiples of 64 (`ZoneSystem.GetZone`), so vertices sit on whole
   world metres. *Assumption:* the in-world heightmap uses `m_width = 64` and `m_scale = 1`, which gives
   a 1 m spacing. That value comes from the prefab, not from code. The adapter must read the live
   spacing and refuse when it is not 1.
2. **The surface between vertices is flat triangles.** `RebuildCollisionMesh` builds the collider from
   the same vertices and the same split, the diagonal from (x+1, z) to (x, z+1). The preview and the
   collider therefore agree once the plan is drawn from the lattice.
3. **Vertical walls are impossible.** A wall goes from a lowered vertex to an untouched neighbour one
   metre away, so it is always at least one cell wide. Its steepest slope is depth over 1 m. A cuboid
   would need two heights at one (x, z), which a heightmap cannot hold. Overhangs, tunnels and native
   earth roofs are equally impossible.
4. **The square op follows the world axes.** `RaiseTerrain` and `LevelTerrain` loop over vertex-index
   ranges when `m_square` is set. `m_rotation` only affects paint. The hole cannot be rotated.
5. **"Lower, never raise" exists natively.** `RaiseTerrain` with a negative `m_raiseDelta` skips any
   vertex already below its target. With `m_square`, every vertex within `ceil(radius)` gets the same
   target (`op.y + delta`). The planner's single operation is therefore that square op, centred on the
   centre vertex at the datum, with radius = half extent and delta = −depth. It brings every floor
   vertex down to one absolute floor height. `LevelTerrain` is not used, because it would also raise
   deeper ground.
6. **The dig limit is 8 m.** `TerrainComp` clamps a vertex's accumulated level delta to ±8. `Heightmap`
   clamps the result to base ±8. In both, the base is the ground before player terrain ops. A plan that
   needs more refuses instead of letting the engine clamp it to a partial floor
   (`ThePlanRefusesWhereTheEngineWouldClampAndTheClampIsReal`). The adapter must read the base as the
   current height minus the compiler's level and smooth deltas. It must not use the generated height,
   because older `TerrainModifier` edits and locations shift it.
7. **Settings travel as a prefab-name hash.** `TerrainOp.Settings.Serialize` writes only the hash, and
   the owner looks it up with `ObjectDB.TryGetTerrainOp`. Every peer therefore needs the same registered
   op prefab. Each mode is one prefab with fixed radius and delta. Only the position varies. Jötunn 2.30.2
   `PrefabManager.RegisterToZNetScene` adds a custom prefab's root `TerrainOp` to `ObjectDB.m_terrainOps`
   / `m_terrainOpsByHash`. *Unconfirmed:* whether `ObjectDB.instance` exists at that moment in a game
   scene. That is an in-game check. The product already requires everyone to have the mod.
8. **Tile edges and corners.** A vertex on a tile edge exists in both tiles. `FindHeightmap(pos, r)`
   includes every tile the square touches, and each tile's owner applies the same op, so a shared edge
   vertex gets the same height in both. The plan counts those tiles: 1, 2 at an edge, 4 at a corner.
   Each tile is applied on its own. There is no cross-tile transaction, so a tile that is not loaded
   could leave half a hole. **Preflight:** every floor and ring vertex must be read from a loaded,
   full-detail heightmap, or the plan refuses (`Unloaded`).
9. **There is a stale-height double count.** An op reads `Heightmap.GetHeight`. After `Poke(1)` that
   value only refreshes on a later late-update rebuild. A second identical op that arrives before the
   rebuild therefore lowers again from the old heights and overshoots. The pickaxe has the same
   property. After a rebuild a repeat changes nothing, because the target is absolute. **Runtime rule:**
   one operation in flight, then check the result against the plan (`ExcavationPlanner.Check`). An
   overshot or unreadable result is reported, and the mod never repairs it or retries without checking.
   Two players committing in the same frame on one tile owner could still double count. That case is
   **MULTIPLAYER ENDPOINT BLOCKED**.
10. **The ward radius has a gap.** Vanilla checks access on the op radius, and that circle does not
    reach a square op's floor corners. The runtime must also call `PrivateArea.CheckAccess` with
    `ExcavationGeometry.AccessCheckRadius`, the distance to the farthest border corner, before
    instantiating anything.
11. **Objects stay.** A terrain op does not remove rocks, trees, pieces or drops. The runtime must refuse
    when such objects occupy the square, rather than leaving them floating or deleting them. It must
    also show any player-built piece that stands on the changed ground. Support and collapse stay native,
    so `Heightmap.Regenerate` clears the WearNTear support cache.
12. **Water.** The sea is a plane at `ZoneSystem.m_waterLevel` (30 m by default). A floor below it
    floods. The plan reports `Floods` / `WaterDepth` and never lowers or hides water.
13. **Paint is cosmetic, and not yet decided.** `m_paintCleared` (for example dirt) also widens
    `GetRadius()`, and with it the ward check and the set of tiles touched. Whether to paint is decided
    in the op-prefab slice. If it paints, the paint must stay inside the border square.

## 4. Planner (this slice): `src/ConcernedLadders/Domain/Excavation/`

`ExcavationPlanner.Plan(samples, centreX, centreZ, waterLevel)` uses no game code and does not allocate
(`ExcavationSamples` is a reusable fixed grid of floor + ring vertices: 25 or 49). It returns one of three
results:

- **Ready**: a plan with the floor height, the number of vertices lowered and by how much at most, the
  lowest and highest wall height, water depth, tiles, and the native op parameters. This is the only
  result that may ever be committed or charged.
- **NothingToDig**: every floor vertex is already at the floor. Running a completed plan again lands
  here, because the ring and therefore the datum are unchanged. This result is free, and no op is
  issued.
- **Refused**, for one of these reasons, checked in this order:
  - `Unloaded`;
  - `NotFinite`;
  - `FloorAlreadyDeeper`: part of the floor is below the planned floor. The op never raises ground and
    will not leave a dip it did not show. This covers a slope too steep for a level floor at that depth,
    and a shallow mode that reaches into a deeper pit;
  - `DigLimit`: within 1 cm of the 8 m clamp, or past it.

Two further behaviours:

- **Adjacent plans** of one mode share a datum on level ground and join with no ridge.
- **A plan inside a larger pit** takes the pit floor as its datum. That makes it a visible, deliberate
  new depth, not an accidental deepening.

Tests: `src/ConcernedLadders.Tests/Excavation/` (40). They measure the surface by the engine's own
triangulation, using `VanillaTerrainModel`, a test-only transcription of the source-read path. Planted
mutations were checked against them: a minimum-height datum, a removed dig-limit refusal, a removed
deeper-floor refusal, and a dropped shared tile edge each fail them.

## 5. Acceptance rows (#487)

| ID | State |
|---|---|
| BSM-01 | Versions, APIs, floor, depth, border and limits are documented here from source. Runtime geometry prototype: **NOT RUN**. Open assumptions: the depth (section 1), the 1 m spacing (3.1), Jötunn's op registration timing (3.7). |
| BSM-02 to BSM-12 | **NOT RUN**, and nothing is implemented for them yet. In-game rows: **OWNER GO-AROUND PENDING**. Multiplayer: **MULTIPLAYER ENDPOINT BLOCKED**. Controller: **CONTROLLER HARDWARE BLOCKED**. |

## 6. Next slices (each a PR of its own)

1. **Op prefabs and adapter.** Register the two `TerrainOp` prefabs, cloned from the pickaxe's own
   `m_spawnOnHitTerrain` with `m_raise`/`m_square` set and no spawn-on-placed item. Add the sampling
   adapter (live spacing check, limit base from the compiler deltas, loaded full-detail tiles only). Add
   the commit gate: ward check at `AccessCheckRadius`, no-build check, object refusal, then exactly one
   `Attack.SpawnOnHitTerrain` and one op in flight, then `Check`. Behind an off-by-default switch until
   it has been observed.
2. **Iron Shovel item.** Clone a vanilla tool prefab; ship no game assets. The recipe needs refined Iron
   at the Forge, so it becomes known through normal material discovery and Iron Scrap alone does not
   unlock it. Durability, repair and stamina follow vanilla tool conventions, and the large mode
   documents its cost. Charge once per committed Ready plan; never charge on a refusal, a no-op or a
   cancel. This slice bumps the version to 1.1.0 and adds `## Unreleased` to the CHANGELOG.
3. **Preview and input.** Draw the floor, border, floor height, depth, wall range, water and refusal
   reason, using native hints and bindings. Aiming never mutates anything. Define the reach and
   targeting rule, including use under a player-built floor and from a ladder.
