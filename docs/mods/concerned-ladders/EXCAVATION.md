# Concerned Ladders: Iron Shovel excavation (#487, CL-006): geometry contract and terrain feasibility

**Status: design record, game-free planner (slice 1), the terrain operations, read-only terrain adapter and
commit gate (slice 2, section 6), the Iron Shovel item that triggers and pays for a dig (slice 3, section 7), and the
shovel's placement preview, size selection and confirmation (slice 4, section 8), all behind the off-by-default
`[Excavation] Enabled` switch.** With the switch off, the only thing the product does differently is register two inert
terrain operations (section 6.1); with it on, the Iron Shovel is craftable, previews the hole it would dig while held, and
digs the hole shown, and the `cl_excavate` console diagnostic can plan (read-only) but not dig. Everything below about the engine was **read from the installed game's code**. None of it
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
6. **The dig limit is 8 m, measured from two different bases.** There are two clamps.
   - `RaiseTerrain` clamps the vertex's accumulated level delta to ±8. A lowering to `target` therefore
     stops at (current height − level delta) − 8: the height before the compiler's level delta, which
     includes its smooth delta and any `TerrainModifier`.
   - `ApplyToHeightmap` clamps the result to `baseHeights` ± 8. `Heightmap.ApplyModifiers` copies
     `baseHeights` lazily, just before the first *player* `TerrainModifier` runs (or after all modifiers
     when there is none), so that base excludes legacy player modifiers.

   The floor is reachable only when it is no lower than the **higher** of those two bases minus 8. The
   planner takes one limit base per vertex and the adapter must pass that maximum. A plan that needs more
   refuses instead of letting the engine clamp it to a partial floor
   (`ThePlanRefusesWhereTheEngineWouldClampAndTheClampIsReal`). The test model has one base, which
   covers the clamp arithmetic but not how the adapter derives the two bases. That derivation is
   checked in the adapter slice.
7. **Settings travel as a prefab-name hash.** `TerrainOp.Settings.Serialize` writes only the hash, and
   the owner looks it up with `ObjectDB.TryGetTerrainOp`. Every peer therefore needs the same registered
   op prefab. Each mode is one prefab with fixed radius and delta. Only the position varies. Jötunn 2.30.2
   `PrefabManager.RegisterToZNetScene` adds a custom prefab's root `TerrainOp` to `ObjectDB.m_terrainOps`
   / `m_terrainOpsByHash`. *Unconfirmed:* whether `ObjectDB.instance` exists at that moment in a game
   scene. That is an in-game check. The product already requires everyone to have the mod.
   **Resolved in slice 2 without relying on that order** (section 6.1): the ops are registered from an
   `ObjectDB.Awake` postfix, where `ObjectDB.instance` provably is the instance that just woke.
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

## 4. Planner (slice 1): `src/ConcernedLadders/Domain/Excavation/`

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

Tests: `src/ConcernedLadders.Tests/Excavation/` (42). They measure the surface by the engine's own
triangulation, using `VanillaTerrainModel`, a test-only transcription of the source-read path. Planted
mutations were checked against them: a minimum-height datum, a removed dig-limit refusal, a removed
deeper-floor refusal, and a dropped shared tile edge each fail them.

## 5. Acceptance rows (#487)

| ID | State |
|---|---|
| BSM-01 | Versions, APIs, floor, depth, border and limits are documented here from source. Runtime geometry prototype: **NOT RUN** (the Iron Shovel of section 7 digs, and `cl_excavate plan` describes a plan, for that measurement). Open assumption: the depth (section 1). The 1 m spacing (3.1) is now read live and refused otherwise; the op registration (3.7) no longer depends on Unity's Awake order. |
| BSM-02 | Implemented in slice 3 (section 7): recipe, station, discovery, equip, durability, repair, localization. **NOT RUN** in game: **OWNER GO-AROUND PENDING**. |
| BSM-06 (costs) | The single charge, the stamina and broken-tool refusals and the interruption policy are implemented and unit-tested (section 7.3). Joins, datum and recovery remain slice 2's. In game: **OWNER GO-AROUND PENDING**. |
| BSM-03 | Slice 4 (section 8.2): the preview draws the usable floor at the one floor height, the depth, the transitions and the whole changed border on the surface; a test applies the planned op through the transcribed engine path on a slope and checks the drawn border and floor against the result, for both sizes. Measured in game: **NOT RUN**, **OWNER GO-AROUND PENDING**. |
| BSM-05 | Slice 4 (sections 8.1 and 8.4): two labelled sizes, controls from the current bindings (keyboard and controller), Secondary Attack switches the size and never digs, a press digs only the preview shown for 0.2 s, menus and inventory never dig. In game: **OWNER GO-AROUND PENDING**. Controller: **CONTROLLER HARDWARE BLOCKED**. |
| BSM-12 | Slice 4 (section 8.5): at most 20 terrain reads a second, seven pooled line renderers, one text, nothing networked or saved, no per-frame log. Stall measurement in game: **NOT RUN**, **OWNER GO-AROUND PENDING**. |
| BSM-04, BSM-07 to BSM-11 (rest) | **NOT RUN**. In-game rows: **OWNER GO-AROUND PENDING**. Multiplayer: **MULTIPLAYER ENDPOINT BLOCKED**. Controller: **CONTROLLER HARDWARE BLOCKED**. |

## 6. Slice 2: terrain operations, terrain adapter and commit gate

Code: `src/ConcernedLadders/Excavation/` (game adapters) and `src/ConcernedLadders/Domain/Excavation/`
(`ExcavationTerrainRules`, `ExcavationCommit`, `ExcavationReport`: game-free and tested). Everything below was
read from the decompiled installed game (1.0.21) and Jötunn 2.30.2. **None of it has been observed in game.**

### 6.1 The two terrain operations (`ExcavationTerrainOps`)

- `tcc_excavate_small` and `tcc_excavate_large`: each a new GameObject carrying only a `TerrainOp`, built
  under an inactive container, so `TerrainOp.Awake` (which applies the op and destroys itself) runs only on a
  clone instantiated as a root. Settings: `m_raise` and `m_square` on, `m_raiseRadius` = half extent (1 or
  2 m), `m_raiseDelta` = −depth (−2 or −4 m); `m_level`, `m_smooth`, `m_paintCleared` and `m_rotation`
  **off**; no `m_spawnOnPlaced` item and no effect. So `GetRadius()` is exactly the half extent, and paint
  does not widen the ward check or the tiles touched (claim 13: **no paint**). They are built from scratch
  rather than cloned from the pickaxe's op, so they inherit no spawned item or effect.
- **Registration timing, confirmed from source rather than from Unity's Awake order.** `ObjectDB.Awake` sets
  `ObjectDB.instance` to itself and rebuilds `m_terrainOpsByHash` from its own `m_terrainOps` list; only
  `CopyOtherDB` rebuilds it otherwise, and only `FejdStartup` (the main menu) calls that. Jötunn's own path
  registers a prefab's `TerrainOp` from a `ZNetScene.Awake` postfix through `ObjectDB.instance`, and the
  relative order of those two scene objects' Awakes is not something code shows. So the ops are registered
  from Jötunn's `ItemManager.OnItemsRegistered`, which Jötunn raises from an `ObjectDB.Awake` **postfix** in
  the `main` scene: the instance exists, it is the one that just woke, and its table has just been rebuilt.
  The ops are not given to Jötunn's `PrefabManager`; no ZNetScene entry is needed, because the op is
  instantiated locally and is not networked. A hash already taken by a different op is refused and logged.
  Whether the live table maps each hash to this mod's op is **re-checked at every commit**.
- **Registered on every peer, whatever the switch says.** The tile owner deserialises the settings from the
  prefab-name hash (claim 7). A peer that had not registered them would drop a dig on a tile it owns.
  Registering is inert: nothing instantiates these prefabs except the opt-in commit gate.
- At build time the log states whether `Attack.SpawnOnHitTerrain`'s own `GetComponentInChildren<TerrainOp>()`
  sees the op on a prefab whose parent is inactive (Unity's default skips inactive objects, so it most likely does
  not). If it does not, vanilla's own ward and no-build re-check is skipped. The commit gate's own checks (6.3)
  refuse everything that re-check refuses, so nothing is lost. Which one happens is **NOT RUN**.

### 6.2 The terrain read (`ExcavationTerrainReader`, read-only)

- Only full-detail heightmaps (`Heightmap.GetAllHeightmaps()`, which distant-LOD heightmaps leave), active
  and enabled.
- **Live lattice check (claim 1):** each tile under the square must have `m_scale` = 1 (±0.0001), `m_width` = 64
  (the 64 m zone the planner counts tiles in), and an origin on whole metres; otherwise the plan refuses
  `UnsupportedLattice`.
- **Stale heights (claim 9):** a tile with `m_doLateUpdate` set (a rebuild queued by `Poke`) refuses
  `Rebuilding`.
- **Limit base (claim 6):** `ExcavationTerrainRules.LimitBase` = height − level delta − smooth delta, read from
  the tile's `TerrainComp` (no compiler means both are zero). Derivation: the heightmap holds
  `H = clamp(Hpre + L + S, B − 8, B + 8)`; a square lowering to `T` sets `L' = clamp(L + T − H + S, −8, 8)`, so
  it reaches `T` only when `T ≥ H − L − S − 8`, and the final clamp needs `T ≥ B − 8`. With no player
  `TerrainModifier` on the tile, `B = Hpre`, and while the final clamp has not bound, `Hpre = H − L − S`: the two
  bases of claim 6 are one number. Slice 1's wording "(current height − level delta)" left the smooth delta
  implicit. It must be subtracted too, and a test shows that ignoring a negative smooth delta admits a floor the
  engine clamps. Where `|L + S|` is within 1 cm of 8, the final clamp may already have bound and `Hpre` is
  unknown: the base is NaN and the plan refuses. A tile touched by an enabled player `TerrainModifier` (the legacy
  edit: `m_playerModifiction`, `Heightmap.TerrainVSModifier`) refuses `LegacyTerrainModifier`, because its `B` is
  a snapshot this adapter cannot read. A compiler that exists but is not initialised, or whose arrays are the
  wrong size, refuses `CompilerUnreadable`.
- **Shared edges (claim 8):** a vertex on a tile edge is read from every tile that holds it, and all of them must
  be loaded: an edge vertex needs 2 copies and a corner vertex 4, or it counts as unloaded, because a missing tile
  would keep its edge and leave a seam. The copies must agree within 2 cm. Before a dig, copies that disagree
  refuse `TilesDisagree`. During the read-back after a dig, the same disagreement means one tile has rebuilt and its
  neighbour not yet, so it waits (until the timeout) instead of ending the dig as unreadable. The strictest copy's
  limit base is used, because each tile is lowered on its own.
- `TerrainComp`'s deltas and initialised flag are private in the shipped game, and a publicized reference would
  compile a direct read that the game then refuses (observed for a protected method in 0.2.2). So they are read
  by reflection, found once at start-up. A missing member makes the feature unavailable.

### 6.3 The commit gate (`ExcavationRuntime`, `ExcavationGate`, `ExcavationWorldChecks`)

In this order, and each refusal changes and charges nothing: the switch; the start-up probe; this mode's op in the
live registry; the game's `TerrainOp.m_forceDisableTerrainOps`; **one in flight** (`Busy`); a fresh terrain read;
the planner (`Refused`, `NothingToDig`). Only a Ready plan that lowers something then gets the world checks:

- **Ward** at `AccessCheckRadius`, which covers every border corner (claim 10), and in the strict mode
  (`wardCheck: true`): it refuses if **any** overlapping ward does not grant access, and flashes it as the pickaxe
  does. The pickaxe's own call uses the default mode, which passes when any overlapping ward grants access, so
  widening the radius in that mode would also have pulled in more wards that grant (independent review). The strict
  mode refuses everything the pickaxe's check refuses, at either radius.
- **No-build locations:** each `Location` with `m_noBuild` is asked `IsInside(centre, AccessCheckRadius,
  buildCheck: true)`, because the game's own public test takes a point only. The location list is private, so
  it is found by reflection; if it is missing, the feature is unavailable.
- **Objects (claim 11):** one `Physics.OverlapBoxNonAlloc` over the border square (inset 5 cm), from 10 cm below
  the floor to 30 cm above the highest sampled ground, on the placement layers plus `item`, ignoring triggers
  and terrain. A cart (`Vagon`), a ship (`Ship`) or anything on a moving rigidbody refuses `Obstructed` first:
  carts and ships carry `WearNTear` too. Then any collider that does not belong to a `WearNTear` (a rock, tree, ore,
  bush, planted crop or dropped item) refuses, naming the first one. Structural building pieces do not refuse:
  digging under or beside your own floor is the core use. Their colliders on the changed ground are counted and
  shown by the read-only `plan` before any dig, and their support and collapse stay the game's (so a
  ground-supported piece can lose support and break). A full buffer refuses `ScanOverflow`.

Then, after the in-flight slot is taken, exactly one `Attack.SpawnOnHitTerrain(point, op, null, 0, null, null)`
at (centre, datum). A plain null back means the game's own check declined before instantiating anything: the
slot is freed and nothing happened. Otherwise the floor is read back every frame, and compared
through `ExcavationPlanner.Check`. Complete, Overshot or Unreadable end it at once. Pending (or a `Rebuilding`
read) waits up to 10 s, then reports `TimedOut`. (Since slice 3 the slot is taken in the press's FixedUpdate, on the
fixed clock, so the first read-back can run in the commit frame before its rebuild; it then reads `Rebuilding` or the
old heights, and both wait.) A world unload reports `Abandoned`. **Nothing is ever retried or
repaired**: the next dig is a fresh plan, from freshly read terrain, that the player asks for.

`ExcavationSourceAuditTests` pins the one `SpawnOnHitTerrain` call to `Excavation/ExcavationRuntime.cs`. In the
excavation code it refuses RPCs, height or delta writes, compiler creation, ownership changes, ZDO access,
`Instantiate`, reflection writes, damage and Jötunn's `PrefabManager`. It is a text audit, so a call reached
through a delegate is invisible to it.

### 6.4 The console harness (`cl_excavate`), read-only since slice 3

Registered only when `[Excavation] Enabled = true` (default **false**; restart after changing).
`cl_excavate plan small|large` reads and describes the plan at the terrain the camera looks at (since slice 4 by the
shovel's targeting rule, 8.3: the first thing seen must be terrain, within 5 m of the eyes), including what would refuse it and how many building-piece colliders stand on ground that would change. It
changes nothing and flashes no ward. In slice 2, `cl_excavate dig small|large` ran the full gate; slice 3 removed it
(7.4), so the Iron Shovel's press is the only way a dig happens. `cl_excavate status` shows the switch, the probe, the
registration and any unconfirmed dig. It is registered
through Jötunn, because Jötunn 2.30.2 reflects for the 13-parameter `Terminal.ConsoleCommand` constructor that
1.0.21 has (the #307 mismatch was Jötunn 2.29.2's). Since slice 4 it aims by the shovel's targeting rule (8.3), and it
asks the plugin for the current runtime on every use, so a runtime disposed by an install fault is never reached (8.7).

### 6.5 Lead defaults in this slice, pending owner confirmation

- A console harness behind the off-by-default switch is the slice's only trigger, so the adapter can be reached
  and measured before the shovel exists. Nothing is charged, because there is no item yet.
- The terrain ops register on every peer regardless of the switch (6.1).
- Non-structural objects, carts and ships in the square refuse; structural pieces are allowed, counted and shown
  before the dig (6.3). The owner should confirm that pieces losing support to a dig is acceptable.
- One dig in flight per client. A Pending result times out after 10 s and is reported, never retried.
- No paint (claim 13), so the dug floor keeps whatever ground texture it had.

### 6.6 What slice 2 does not settle

- Every in-game row stays **OWNER GO-AROUND PENDING**: the holes' measured size on flat ground, a slope, edited
  ground, a tile edge and a four-tile corner; persistence over save/reload and unload/reload; the ward flash; the
  object refusal; and whether vanilla's re-check sees the op (6.1). Multiplayer (a client digging on a tile
  another peer owns, or two clients in one frame) is **MULTIPLAYER ENDPOINT BLOCKED**. The tile owner applies
  the op with the trust vanilla gives the pickaxe: the settings come from the registered prefab, and only the
  position comes from the client.
- The obstruction scan is a box of colliders, not a footprint, so a tree whose collider leans into the square
  from outside also refuses. That is the fail-closed side.
- One in flight is per client. Two clients committing on one tile in the same frame could still double count
  (claim 9).
- Until the 1.1.0 bump (on the trapdoor branch, #523) ships, a peer running the published 1.0.0 passes the version handshake but has
  not registered the ops. A dig on a tile it owns is dropped there (with an error in its log), and the digging
  client reports `TimedOut`, which says so.

## 7. Slice 3 (this slice): the Iron Shovel item

Code: `src/ConcernedLadders/Items/IronShovelItem.cs` (registration), `src/ConcernedLadders/Excavation/ExcavationShovelInput.cs`
(the press), `ExcavationRuntime.DigWithShovel` and `Charge` (the gate and the cost), and the game-free
`Domain/Excavation/IronShovel.cs` (identity, recipe, costs, press rule, can-pay gate; tested). Everything below was
read from the decompiled installed game (1.0.21) and Jötunn 2.30.2. **None of it has been observed in game.**

### 7.1 The item

- **Registered only when `[Excavation] Enabled = true`** (default false). With it off, the item, its recipe, its
  localization, its input patch and the console diagnostic are never constructed (`TheShovelExistsOnlyBehindTheSwitch`).
- Prefab id **`tcc_iron_shovel`** (new; a save contract from now on), name token `$cl_item_iron_shovel` ("Iron
  Shovel"), with an English description built from the same numbers the gate charges.
- A Jötunn `CustomItem` clone of the vanilla **`PickaxeIron`**, read from the installed game when its prefabs are
  available. No game asset ships; no live Player or BaseAI is instantiated. The clone keeps the donor's **equip slot
  and hand** (the registration refuses a donor that is not a weapon type, because `Humanoid.GetCurrentWeapon` only
  returns weapons), **weight, durability (`m_maxDurability`) and repair**. The log line at registration states the
  donor's item type, durability and repairability, because none of those values is in code.
- **Looks:** the donor's iron pickaxe model with every material replaced by a tinted copy (cooler and darker), and an
  icon rendered from that tinted clone on graphical clients (the donor's icon on a dedicated server or on a render
  fault). This is a **placeholder**: a shovel-shaped model (for example procedural, like the metal ladders) is an
  owner question.
- **No vanilla attack.** Both attack animations are cleared, so `HavePrimaryAttack`/`HaveSecondaryAttack` are false
  and `Humanoid.StartAttack` returns before it charges stamina or durability, swings, hits or spawns. The donor's own
  `m_spawnOnHitTerrain` (the pickaxe's dig) and `m_spawnOnHit` are cleared, damage is zero, `m_buildPieces` is null,
  and `m_maxQuality` is 1 (no upgrade recipe, so no free upgrade path). The registration checks the clone has no
  attack left and refuses the item otherwise.
- **Recipe:** 10 Iron + 4 Wood at the **Forge, level 1**; repaired at the Forge. `Player.UpdateKnownRecipesList`
  learns a recipe only when `HaveRequirements(recipe, discover: true)` finds every requirement in the player's known
  materials and the station level known, so the recipe appears once refined Iron has been held, and Iron Scrap alone
  never unlocks it. Repair is the game's own: `InventoryGui` repairs an item whose recipe names the current station.
- **No skill.** A dig raises no skill (the donor's pickaxe skill type stays on the item but nothing calls RaiseSkill).

### 7.2 The press

`ExcavationShovelInput` is a Harmony prefix on `Player.PlayerAttackInput` that only observes (a void prefix; the game's
own input always runs after it). `PlayerController.FixedUpdate` sets `Character.m_attack` / `m_secondaryAttack` true for
one input step per press, from the game's own **Attack / JoyAttack** and **Secondary Attack / JoySecondaryAttack**
bindings, and false whenever chat, the console, a text input or a menu has the input, or the inventory, map, a store,
the build menu or the radial menu is open. So, for the local player with the shovel as the current weapon: one press of
Attack asks for a **2x2x2 m** hole, one press of Secondary Attack a **4x4x4 m** hole, both in one step asks for nothing,
and holding the button never asks twice. The target is slice 2's: the terrain the camera looks at, within 10 m. Results
show in the HUD's top-left message and in the log; the later read-back result too. **Superseded in slice 4 (8.1, 8.3,
8.4):** Attack digs the size the preview shows and Secondary Attack switches it; the target is the shown square, by the
game's building rule, within 5 m of the eyes.

### 7.3 Costs, and when they are charged (lead defaults pending owner confirmation)

| Mode | Stamina | Durability |
|---|---|---|
| 2x2x2 m | 20 | 4 |
| 4x4x4 m | 40 | 16 |

The large mode lowers four times the floor area to twice the depth: four times the durability, twice the stamina, so it
still fits one stamina bar. Durability is multiplied by the world's durability modifier, as every vanilla tool drain is;
stamina goes through the game's own `UseStamina`, which applies the world's stamina modifier, and the check asks for the
scaled amount.

- **Asked** after the planner has a Ready plan and before any world check (so a tired player flashes no ward), in this
  order: the shovel is the equipped weapon (the very item the press came from); the player is free to act (not mid-swing,
  dodging, staggered, knocked back or in a minor action: the states in which `Humanoid.StartAttack` refuses; and not
  teleporting); the shovel
  has the **whole** durability cost left and is not broken (a tool that would break part-way never digs); the player has
  the stamina (the stamina bar flashes, as for an attack). Each refusal says what to do and that nothing was charged.
- **Charged once**, in `ExcavationRuntime.Charge`, only after `Attack.SpawnOnHitTerrain` returned a placed operation:
  stamina first (through the game's own `UseStamina`), then durability (a plain field write that cannot throw), so a
  fault charges nothing rather than half. **No animation plays** (independent review of slice 3): the donor's swing clip
  raises the attack-trigger event, and `Humanoid.OnAttackTrigger` runs whatever attack is still stored in
  `m_currentAttack`, which nothing clears when an attack finishes, so a punch thrown before equipping the shovel would
  land a real melee hit outside the gate. A tool animation that cannot reach a stored attack is an owner question for
  slice 4.
  A refusal at any gate, `NothingToDig`, the game's own spawn refusal, or a cancelled press charges nothing.
- **Interruption policy:** once the operation is placed it has been handed to every tile owner, so the dig is paid for
  whatever the read-back later reports (Complete, Overshot, Unreadable, TimedOut, Abandoned), and it is never paid for
  again: the in-flight slot refuses a second commit until it resolves, and repeating a completed plan is `NothingToDig`.
  A timed-out dig is not refunded, because the operation may still land on a remote owner; refunding it would make it
  free.

`ExcavationSourceAuditTests` pins one `UseStamina` and one durability write in the whole product, both inside `Charge`,
called once after the spawn's refusal branch; the commit gate's only caller is the shovel press; the item's cleared
attack, terrain op, quality and damage; and that `Items/` changes nothing else.

### 7.4 Lead defaults in this slice, pending owner confirmation

- The recipe (10 Iron + 4 Wood, Forge level 1), the costs (7.3), one quality level, and the donor's durability.
- Attack digs the small hole, Secondary Attack the large one, until slice 4 gives the modes their own selection and hints
  (done: 8.1).
- **`cl_excavate dig` is removed** (not kept beside the shovel): the issue forbids excavation driven by chat, menus or
  other UI, and the console is UI. `cl_excavate plan` (read-only, now with the shovel's cost) and `status` stay behind
  the switch as diagnostics.
- The tinted pickaxe model as a placeholder look, and no animation (7.3).
- When the shovel cannot dig this session (its input patch or the start-up probe did not match the installed game), the
  item is still registered, so crafted shovels survive, but its recipe is disabled, so no one spends Iron on it.
- The item exists only while the switch is on. **Risk:** turning the switch off after crafting a shovel removes the item
  prefab, and the game discards items whose prefab it cannot find when it loads an inventory or a chest, so the shovel is
  lost. A peer with the switch off cannot show a shovel another player drops. Every player needs the same setting.
- **No version bump here.** The 1.1.0 bump and the release notes ride on the trapdoor branch (#523); until then a peer
  on the published 1.0.0 passes the handshake without the ops or the item (6.6).

### 7.5 What slice 3 does not settle (OWNER GO-AROUND PENDING)

- The recipe appears after Iron is picked up and not after Iron Scrap; crafting and repair at the Forge; equip and the
  held model; the icon; the description.
- One press digs once; holding does nothing more; the inventory, map, chat and console give no press; a controller's
  attack buttons do the same (**CONTROLLER HARDWARE BLOCKED**).
- The charge: stamina and durability fall by the table once per dig; a refused or empty dig takes nothing; a nearly
  broken shovel and a tired player refuse.
- That no animation plays, and that the press feels acceptable without one (owner question: which animation, slice 4).
- Multiplayer: **MULTIPLAYER ENDPOINT BLOCKED**.

## 8. Slice 4 (this slice): selection, preview and input

Code: `src/ConcernedLadders/Excavation/ExcavationPreview.cs` (the local visual, the HUD text and the key hints),
`ExcavationRuntime.RefreshPreview` / `Evaluate` / `DigWithShovel` (the read-only preview evaluation and the press that
confirms it), `ExcavationShovelInput` (the two buttons), and the game-free `Domain/Excavation/ExcavationPreview.cs` (press
rule, size selection, refresh bound, confirmation rule and outline shape; tested). Everything below was read from the
decompiled installed game (1.0.21) and Jötunn 2.30.2. **None of it has been observed in game.**

### 8.1 Two labelled sizes, and the controls

- While the Iron Shovel is the equipped weapon, the HUD shows, under the crosshair, three lines: the selected size
  (`Iron Shovel: 2x2x2 m (usable floor 2 x 2 m, 2 m deep)` or the 4x4x4 m one), what a press would do at the aimed
  square, and the controls.
- **Attack digs the size shown, where it is shown. Secondary Attack switches between 2x2x2 m and 4x4x4 m** and never
  digs, reads or changes anything else. Both in one input step ask for nothing (`ExcavationPreviewRules.PressFor`).
  This replaces slice 3's "Attack = small, Secondary = large", under which a press could dig a size the player was not
  looking at.
- The size is local and in memory: it starts at 2x2x2 m every session and is never saved or sent.
- **Key hints from the current bindings.** The HUD's controls line reads each button through the game's own
  `ZInput.GetBoundKeyString`, so it follows a remapped key: `Attack` / `SecondaryAttack` on keyboard and mouse,
  `JoyAttack` / `JoySecondaryAttack` while a controller is the active input (`ZInput.IsGamepadActive`). For a controller
  the game returns a TextMeshPro sprite tag (the glyph of the active layout); the HUD text is a TextMeshPro text with the
  game's message font and sprite set, which is how the game's own messages show the same tags. (By default, Secondary
  Attack is the **middle** mouse button, read from `ZInput`'s defaults, so the line matters.)
- The game's own key-hint panel also shows both buttons for the shovel on keyboard and mouse, through Jötunn's
  `KeyHintManager` (`ButtonConfig` names `Attack` and `SecondaryAttack`; Jötunn reads the bound key and rebuilds the
  hint after `ZInput.Save`). **Limit, read from Jötunn 2.30.2:** its gamepad branch places a glyph object but never sets
  its text, so no gamepad panel entry is declared. A custom hint also replaces the game's combat hints for the item, so on
  a controller the key-hint panel is empty while the shovel is held and the HUD line carries the controls. CONTROLLER
  HARDWARE BLOCKED.

### 8.2 What the preview shows

`ExcavationPreviewShape` fills a fixed set of seven lines, in world metres, from the plan and the very samples it was
planned from:

| Line | What it is |
|---|---|
| Border (1) | The untouched ring of vertices one cell outside the floor, through each vertex's sampled height: 16 points (2x2x2 m) or 24 (4x4x4 m), closed. The ring never changes and each segment is a lattice edge, so this line lies on the terrain surface and collider exactly where the change stops. Everything inside it may change; nothing on or outside it does. |
| Floor (1) | The usable interior: the floor square at the one planned floor height. |
| Corners (4) | At each floor corner, a post from the datum straight down by the depth to the floor, then the sloping transition one cell out to the ring corner on the surface. |
| Water (1) | The border square at the water surface, only when the floor would sit below it (the HUD then says by how much, and that it will flood). |

- The HUD's second line gives, for a press that would dig, the floor height, the depth below the highest edge (the
  datum), the wall range, the changed square, the cost, the flood depth, and how many building-piece colliders stand on
  ground that would change. Otherwise it gives the refusal and what to do about it, from the same words the press uses
  (`ExcavationReport.PreviewState`).
- **Colour:** green when a press would dig; amber when it would do nothing (already dug, or the last dig still being
  checked); red when it would be refused; blue for the water line.
- The lines are drawn with the built-in `Hidden/Internal-Colored` shader with its depth test off, so a floor that is still
  under the ground shows through it. If that shader is not in the build, `Sprites/Default` is used and the log says that
  lines below the ground will be hidden; if neither is, only the HUD text shows. Which one the installed build has is
  **NOT RUN**.
- A refused plan with a known floor (already deeper, dig limit) still draws, in red. A plan with no floor (unloaded or
  unreadable terrain) draws nothing, and the HUD says why.
- **Test (BSM-03, code level):** `ThePreviewMatchesTheTerrainTheOperationLeaves` applies the planned operation through the
  transcribed engine path on a slope, for both sizes, and checks that no vertex on or outside the drawn border changed,
  that every floor vertex sits at the drawn floor, and that each border segment's midpoint lies on the triangulated
  surface after the dig. Measured in game: **OWNER GO-AROUND PENDING**.

### 8.3 Grid, rotation, reach and targeting

- **Grid:** the floor's edges fall on whole world metres (section 1). **Rotation: none, recorded rather than drawn.** The
  square terrain op follows the world axes only (claim 4), so the preview never offers a rotation, and build pieces line
  up with the hole at 0° or 90°. A rotated ghost would promise a hole the terrain cannot make.
- **Targeting rule (the game's own build-placement rule, `Player.PieceRayTest`):** the camera's ray, 50 m long, against
  the placement layers (`Default`, `static_solid`, `Default_small`, `piece`, `piece_nonsolid`, `terrain`, `vehicle`),
  triggers ignored. **The first thing the ray meets must be terrain**, so the shovel never digs through a wall, a floor
  or a rock, and the HUD names what is in the way. Ground seen under a raised floor, beside a house, or through an opening
  in a floor (the trapdoor use) is fair, and structural pieces in the square do not refuse (6.3). Overhead building pieces
  are therefore not blanket-rejected; remote editing through them is. **Consequence for the owner:** a floor laid on the
  ground is always the first thing seen, so digging under it needs an opening in it (or a raised floor with room to look
  under); the issue calls basement preparation under a player-built floor a core use, and this is the narrowest reading
  of it. A third-person camera can also see ground over a low wall the body is behind, as vanilla building does.
- **Reach: 5 m from the player's eyes**, the game's own building reach (`Player.m_maxPlaceDistance` = 5). Slices 2 and 3
  used 10 m from the feet as a harness bound.
- **Ladders:** a Concerned Ladders climb permits only the building hammer while attached (`LadderRuntime.Free`), so the
  shovel neither previews a dig nor digs from a ladder: the HUD says "Step off the ladder to dig".

### 8.4 Only a deliberate press confirms

- Aiming, switching size and moving only read: `RefreshPreview` aims, reads the terrain into the preview's own sample
  grids, plans, and asks every gate question the press asks (`Evaluate` with `committing: false`), including the shovel,
  the stamina and the ward without flashing it (`WardAllowsQuietly`, `flash: false`, which in the game only reads the
  wards). It never commits, charges, spawns or flashes.
- **A press digs only what the preview shows** (`ExcavationPreviewState.Confirm` and `.Matches`): the selected size, at
  the square the outline was drawn on (never a fresh aim), only once **what is shown has not changed for 0.2 s** (the
  same size, square, gate answer and floor height: a square swept onto, a size just switched, a red outline just turned
  green or a floor that just moved restarts it) and was drawn in the last two frames. The press then reads the terrain afresh and runs the
  whole gate; a fresh Ready plan that is not the one shown (another square, size, or a floor more than 2 cm away) refuses
  as `PreviewChanged` and the next frame shows the plan as it is now. A shown refusal never becomes a dig.
- **Chat, menus, inventory, map, console, store, build menu and radial menu** never dig: the game already withholds the
  attack edges then (`PlayerController.TakeInput` / `InInventoryEtc`), the preview hides while any of them is open, and
  the press is ignored then too (`ExcavationPreview.UiOpen`). So is any free cursor: during play the game's camera keeps
  the cursor locked, keyboard and controller alike (`GameCamera.UpdateMouseCapture`), and frees it for the Ctrl+F1 cursor,
  the barber and other mods' windows, so a click on one of those never digs. Closing one of them while the Attack button is held gives
  the game's attack edge in the first step after; the preview was hidden, so the dwell refuses that press
  (`NoPreview`). The same covers equipping the shovel and pressing at once. A cancelled press (a refusal at any gate)
  changes and charges nothing (7.3).
- Repeating a press on a finished hole is `NothingToDig` (amber, free). A deeper hole there is a visible new decision:
  the 4x4x4 m size inside a 2x2x2 m pit takes the pit floor as its datum (section 4), and the preview shows it before any
  press.

### 8.5 Bounded sampling and objects (BSM-12)

- **Terrain reads:** at most one read of one square (25 or 49 vertices) every 0.05 s while the aim moves, and every 0.25 s
  while it stays put: **at most 20 reads a second, whatever the frame rate** (`ReadsPerSecondAreBoundedWhateverTheFrameRate`).
  Each read also asks one ward check and one object scan (one `OverlapBoxNonAlloc` into the existing 128-collider buffer).
  Between reads the last result is drawn again; the aim ray runs every frame.
- **Objects:** one root and exactly seven line renderers, created the first time the preview shows in a scene and reused;
  one shared material; one HUD text. Fixed point buffers (no growth). No network view, no ZDO, nothing saved, no collider
  (neither the aim ray nor the object scan can meet the preview). They are plain scene objects, so leaving the world
  destroys them with the scene and the next use builds them again; the plugin's teardown hides them but does not destroy
  them (the excavation audit allows no `Destroy` outside the op container), so a script-engine hot reload would leave one
  hidden root, label and material behind until the scene unloads. Nothing is created per shovel stroke. The material is
  created once and again only if something destroyed it.
- **Text:** the HUD text is rebuilt only when what is shown changes (at most 20 times a second), never per frame. The
  name of what blocks the aim is read only on a refresh (reading a Unity object's name allocates). The
  preview writes nothing to the log per frame; its only log lines are one-off (a missing shader, a fault).
- `cl_excavate status` reports the number of preview terrain reads since start-up, for the owner's measurement.
- Whether the largest action or repeated previews stall the game is **NOT RUN** (OWNER GO-AROUND PENDING).

### 8.6 Lead defaults in this slice, pending owner confirmation

- **Attack digs the size shown; Secondary Attack switches the size** (8.1). The size starts at 2x2x2 m each session and is
  not remembered.
- **The press confirms the preview** (8.4): what is shown must have stood unchanged for 0.2 s before a press counts; a
  changed plan refuses rather than digging what was not shown. No second confirmation step beyond the single press: the preview is the
  confirmation, and the issue asks for one deliberate shovel action.
- **Reach 5 m from the eyes** (the game's building reach) and **the first thing seen must be terrain** (8.3).
- **No digging from a ladder** (8.3).
- **No rotation** (8.3, claim 4).
- **Line look:** 5 cm lines through the terrain, green / amber / red / blue, and the HUD text under the crosshair.
- **Controller:** the HUD line carries the controller glyphs; no gamepad entry in the key-hint panel (8.1).
- **No animation still** (7.3). A tool animation that cannot reach a stored attack remains an owner question.
- **A preview fault stops excavation for the session** and disables the recipe, because a press needs the preview. When
  the press itself could not be installed, no preview is installed either, so no outline ever promises a dig.
- Floors on the ground must be opened (a trapdoor hole) to dig under them (8.3).
- On a controller the key-hint panel is empty while the shovel is held; the HUD line carries the controls (8.1).
- Not refused, and left to the owner: digging the square the player stands in (as the pickaxe does). With the 5 m reach the
  player can stand outside the 4x4x4 m border (3 m from its centre), but nothing forces it, and standing on the ground of a
  4 m hole drops the player about 4 m, where the game's fall damage begins.

### 8.7 Slice 3's follow-ups, fixed here

- **`craftable` fixed at start-up.** A fault later in the session (in the press, the read-back or the preview) now
  disables the recipe for the rest of the session (`ExcavationRuntime.Faulted` -> `IronShovelItem.DisableRecipe`). It
  switches `m_enabled` off on the one `Recipe` object Jötunn adds to every ObjectDB; the game's crafting list skips it
  (`Player.GetAvailableRecipes`), and repair, which never reads `m_enabled`, still works. Nothing turns a recipe on; the
  audit refuses any `m_enabled` write other than `= false`.
- **The console command kept a disposed runtime** (slice 2's N5). `cl_excavate` now asks the plugin for the current
  runtime on every use and answers "unavailable" once an install fault has disposed it; a disposed runtime is also
  unavailable to every caller.
- Still open: the text audit cannot see a call through a delegate; a disabled recipe gives the player no in-game hint.

### 8.8 What slice 4 does not settle (OWNER GO-AROUND PENDING)

- That the outline is visible through the terrain, readable on a slope and at night, and matches the hole dug (BSM-03:
  measured floor, depth, common flat bottom, border, on flat ground and a slope).
- That the HUD text sits under the crosshair without covering the game's own hover text, and the key-hint panel shows the
  two buttons; that a remapped key shows its new name; the controller glyphs (**CONTROLLER HARDWARE BLOCKED**).
- BSM-05: that no aiming, switching, menu, inventory, chat or console use digs; that a press right after closing the
  inventory with the button held does not dig; that Secondary Attack never digs.
- BSM-12: no stall or frame-time regression with the preview held over rough ground, and no log traffic from it.
- The targeting rule under a raised floor, through a trapdoor opening, against a wall, and the ladder refusal.
- **A cursor left free makes the shovel do nothing**, silently: while the Ctrl+F1 free cursor is on, or another mod keeps
  the cursor unlocked, no outline or HUD text shows and a press gives no message (8.4 counts a free cursor as UI). Whether
  that needs a one-line hint, and what the cursor reads while the game window has no focus (Unity's own behaviour, not
  readable from the game's code; if it is not Locked, the preview hides and the dwell restarts on refocus), are in-game
  checks.
- An aim resting on a half-metre line flips the snapped square between two neighbours, and each flip restarts the dwell
  (fail closed, and the outline visibly jumps). Whether that feels acceptable is an in-game check.
- Multiplayer: **MULTIPLAYER ENDPOINT BLOCKED**.

## 9. Next slices (each a PR of its own)

1. **In-game measurement and polish** (owner go-around): the BSM rows above, with screenshots; the preview look; the
   reach. Any finding is a linked bug.
2. **A tool animation** that cannot reach a stored attack, and a shovel-shaped model, once the owner chooses them (7.4).
3. **Packaging**: the 1.1.0 changelog entry and a candidate ZIP with its provenance are done (PR #544, `STATUS.md`
   "1.1.0 candidate provenance"); installing and smoking it, and any release, are the owner's.
