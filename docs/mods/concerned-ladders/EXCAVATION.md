# Concerned Ladders: Iron Shovel excavation (#487, CL-006): geometry contract and terrain feasibility

**Status: design record, game-free planner (slice 1), the terrain operations, read-only terrain adapter and
commit gate (slice 2, section 6), and the Iron Shovel item that triggers and pays for a dig (slice 3, section 7), all
behind the off-by-default `[Excavation] Enabled` switch. No placement preview exists yet (slice 4).** With the switch
off, the only thing the product does differently is register two inert terrain operations (section 6.1); with it on,
the Iron Shovel is craftable and digs, and the `cl_excavate` console diagnostic can plan (read-only) but no longer dig. Everything below about the engine was **read from the installed game's code**. None of it
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
| BSM-03 to BSM-12 (rest) | **NOT RUN**. The preview, the reach rule and full input polish (BSM-05) are slice 4. In-game rows: **OWNER GO-AROUND PENDING**. Multiplayer: **MULTIPLAYER ENDPOINT BLOCKED**. Controller: **CONTROLLER HARDWARE BLOCKED**. |

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
`cl_excavate plan small|large` reads and describes the plan at the terrain the camera looks at, within 10 m of the
player, including what would refuse it and how many building-piece colliders stand on ground that would change. It
changes nothing and flashes no ward. In slice 2, `cl_excavate dig small|large` ran the full gate; slice 3 removed it
(7.4), so the Iron Shovel's press is the only way a dig happens. `cl_excavate status` shows the switch, the probe, the
registration and any unconfirmed dig. It is registered
through Jötunn, because Jötunn 2.30.2 reflects for the 13-parameter `Terminal.ConsoleCommand` constructor that
1.0.21 has (the #307 mismatch was Jötunn 2.29.2's). The aim ray is terrain-only. The reach and targeting rule,
including digging under a floor and from a ladder, belongs to the preview slice.

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
show in the HUD's top-left message and in the log; the later read-back result too.

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
- Attack digs the small hole, Secondary Attack the large one, until slice 4 gives the modes their own selection and hints.
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

## 8. Next slices (each a PR of its own)

1. **Preview and input.** Draw the floor, border, floor height, depth, wall range, water and refusal
   reason, using native hints and bindings, and give the two modes their own selection. Aiming never mutates
   anything. Define the reach and targeting rule, including use under a player-built floor and from a ladder.
   Decide whether a confirmation step beyond the single press is needed.
