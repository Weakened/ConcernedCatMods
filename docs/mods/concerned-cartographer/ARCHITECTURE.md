# Concerned Cartographer architecture

## Context

The first release is a client-side BepInEx/Jötunn plugin targeting .NET Framework 4.8. It samples loaded terrain beneath the local player, records only path-like paint, stores data in a mod-owned sidecar file, and renders through Jötunn map overlays.

## Components

```text
Plugin
  ├─ CartographerSettings
  └─ CartographerRuntime
       ├─ GroundPaintProbe        (game adapter)
       ├─ RoadSurveyor            (game adapter: traversal source)
       ├─ ConstructionCapture     (game adapter: construction source)
       ├─ ChunkRecoveryScanner    (game adapter: chunk-recovery source)
       ├─ RoadObservationPipeline (pure domain)
       ├─ RoadAtlas               (pure domain)
       ├─ RoadAtlasCodec          (pure domain)
       ├─ RoadPersistence         (IO adapter)
       └─ RoadOverlayRenderer     (Jötunn adapter)
```

Pure domain types live under `src/ConcernedCartographer/Domain` (`RoadPoint`,
`RoadKind`, `RoadStroke`, `RoadSegment`, `RoadSamplingRules`, `RoadObservation`,
`RoadObservationSource`, `RoadObservationPipeline`, `RoadAtlas`,
`RoadAtlasCodec`, `RoadGeometry`, `RecoveryShapeHeuristic`). They have no
Unity, BepInEx, or Jötunn dependencies and are
compiled directly into `src/ConcernedCartographer.Tests`, so stroke and
serialization rules are unit-tested without the game installed. The shipped
plugin remains a single DLL because the tests link the sources instead of
referencing a second assembly.

### Plugin

Owns BepInEx lifecycle, configuration, Jötunn map event subscription, and the Unity `Update` bridge. It contains no terrain or persistence logic.

### GroundPaintProbe

The only class allowed to interpret Valheim terrain paint internals. It:

- finds the loaded `Heightmap` around a world position;
- converts world position to a terrain vertex;
- averages a small paint-mask neighborhood;
- classifies blue-dominant paint as paved and red-dominant paint as dirt Pathen;
- returns no road when APIs are unavailable or thresholds are not met.

A Valheim update should require changes here rather than throughout the mod.

### RoadSurveyor

The **traversal observation source**. Runs at a configured interval, not every
frame; samples the local player's position, classifies the paint beneath it,
and feeds `RoadObservation`s into the pipeline. It ends its own stroke when
the player is off road paint or dead, and returns only a newly created
segment for incremental rendering.

### ConstructionCapture

The **construction observation source**: a read-only Harmony postfix on
`TerrainComp.ApplyOperation(TerrainOp)`, gated by
`Sources/CaptureConstructionActions` (default on).

Verified against Valheim 0.221.12: a successful hoe/cultivator/stonecutter
placement spawns a `TerrainOp` on the placing client (failed or cancelled
placements never spawn one), whose `Awake` calls `ApplyOperation` once per
affected heightmap. The op itself is applied by the **chunk-owner** client —
`ZNetView.InvokeRPC(string, …)` routes the `ApplyOperation` RPC to
`m_zdo.GetOwner()` only — and every other client receives results passively
through ZDO data revisions. Hooking `ApplyOperation` therefore captures
exactly the local player's own successful actions on every ownership
topology; other players' construction is chunk-recovery's job (CC-006).

`PaintType.Dirt` maps to Dirt, `PaintType.Paved` to Paved;
`Cultivate`/`Reset` are ignored here and become removal signals in
reconciliation (CC-015). Level/raise ops are **terraforming**: their dirt
paint is a side effect, so they reconcile covered other-kind ink but never
record road observations (walking on leveled ground still records via
traversal, per the v0.1 "paint counts as road" decision). Seam duplicates
(one op, two heightmaps) collapse via pipeline replay idempotency. The
postfix never mutates game state; a capture exception disables the source
for the session without touching traversal surveying. Known bounded failure:
if an absent chunk owner never applies the routed op, a dab can be inked
without a terrain change; reconciliation and recovery correct it.

### Reconciliation

Gated by `Sources/ReconcileTerrainChanges` (default on) and fed by the same
captured operations as ConstructionCapture, so it inherits the same
confirmed-success and local-player-only semantics:

- A **Dirt or Paved** op removes covered ink of the *other* kind within the
  op's own brush radius (kind changes never leave duplicate parallel
  geometry); same-kind ink stays, and suppression keeps the new dab from
  duplicating it.
- A **Cultivate or Reset** op removes covered ink of both kinds and records
  nothing.
- `RoadAtlas.RemoveCoverage` is the pure domain operation: points within the
  radius are removed, strokes split into surviving runs (the first run keeps
  the stroke's identity), the spatial index is rebuilt, and any source
  actively extending a replaced stroke starts fresh. Unrelated nearby
  geometry and other-kind ink are never touched.
- **Journal**: the first destructive change per session copies the last
  saved sidecar to `.pre-reconcile.bak` (manual recovery: delete the
  sidecar, rename the backup), and every reconciliation logs its position,
  radius, and removed-point count.
- Ink cannot be un-drawn incrementally, so removals schedule a full overlay
  rebuild, debounced to at most one per 0.5 s.

### ChunkRecoveryScanner

The **chunk-recovery observation source**, gated by
`Sources/RecoverLoadedChunks` (default on). Incrementally scans the paint
masks of loaded, non-LOD heightmaps within 128 m of the player — never the
world file, never a global scan — and emits explored, path-like road-paint
cells as ChunkRecovery observations. Bounds:

- **Budgeted and cancellable** — at most `RecoveryBudgetCellsPerFrame`
  (default 256) cells per frame; one heightmap is scanned at a time, once
  per session, and the scan state resets on logout/world switch.
- **Fog-gated** — each candidate cell is checked against
  `Minimap.IsExplored` (own or shared exploration), so unexplored map
  regions never reveal roads.
- **Shape-filtered** — `RecoveryShapeHeuristic` (pure domain, unit-tested)
  rejects cells whose 5×5 neighborhood is more than half road paint, so
  plazas, leveled bases, and broad pads do not become road tangles. Wide
  (>~2 cell) roads are deliberately not auto-recovered; traversal still
  records them when walked.
- **Order-safe chaining** — recovery observations use a tightened 2.5 m
  stroke gap so only adjacent scan cells chain; parallel roads sharing a
  scan row can never be connected. Density is simplified by the pipeline's
  minimum-spacing rule before persistence; CC-016 adds real geometry
  merging.

Cell-to-world uses the verified inverse of `Heightmap.WorldToVertexMask`;
out-of-mask lookups return black (unpainted), biasing seam windows toward
recovery. A scanner exception disables only this source for the session.

### RoadObservationPipeline

The single entry point through which every detection source feeds the atlas.
`RoadObservationSource` names the sources: `Traversal` (v0.1 behavior),
`Construction` (confirmed terrain-paint actions, CC-005), and `ChunkRecovery`
(paint recovered from loaded terrain, CC-006). The pipeline guarantees:

- **Source neutrality** — all sources share the same sampling rules and atlas
  semantics; a `RoadObservation` carries only source, kind, and position.
- **Exact-replay idempotency** — an observation replayed with identical
  coordinates never grows the atlas, even when configurable duplicate
  suppression is disabled (a 0.05 m epsilon far below the minimum spacing).
- **Source isolation** — each source builds its own stroke, so interleaved
  observations stay coherent polylines, and ending or failing one source
  never breaks another's stroke. `EndAllStrokes` runs on logout/world switch.

### RoadAtlas

Pure in-memory domain state. It owns strokes, dirty state, per-source active
strokes, and append rules. It does not touch Unity map textures or the
filesystem. Every stroke records its originating `RoadObservationSource`.

Sampling rules, in order:

1. **Duplicate suppression** — a sample within `DuplicateSuppressionMeters`
   (default 2 m, 0 disables) of already-recorded ink of the same kind is
   skipped and ends the observing source's active stroke, so re-walking a
   road never grows the atlas and never draws a connector across the covered
   stretch. Proximity is measured to recorded **segments**, not points, so a
   simplified straight stretch keeps suppressing along its whole length.
   Segments touching the newest three points of that source's active stroke
   are exempt so forward walking cannot suppress itself. A per-kind spatial
   hash grid of segment entries keeps the check O(1) per sample. Radii above
   ~3 m may also suppress tight hairpin switchbacks; the default stays below
   that.
2. **Stroke start** — no active stroke, or a road-kind change, starts a new
   correctly-typed stroke.
3. **Minimum spacing** — closer than `MinimumPointSpacingMeters` to the last
   stored point: skipped (standing still stores nothing).
4. **Maximum gap** — farther than `MaximumStrokeGapMeters`: a new stroke starts
   with no connector segment (teleports, portals, and respawns can never draw
   cross-map lines).

### Repair tools

`RoadAtlasEditor` (pure domain, unit-tested) plus the `cc_roads` console
command (registered through Jötunn's `CommandManager`). Selection is
proximity-based — every operation targets the recorded road nearest the
player, with an optional search-radius argument:

- `status`, `delete`, `kind` (Dirt↔Paved toggle keeping stroke identity),
  `hide`/`unhide` (visual only — hidden strokes keep suppressing; delete to
  allow re-recording), `split` (at the nearest interior point, junction
  shared), `join` (nearest two same-kind, same-source endpoints), `rebuild`
  (clears both kinds in a radius and resets chunk recovery so explored
  terrain re-scans with current settings), `undo`.
- **Undo** is a bounded (depth 20) stack of affected-stroke snapshots;
  every mutation funnels through `RoadAtlas.EditStrokes`, which restores the
  atlas invariants (index rebuild, dirty flag), and the once-per-session
  `.pre-reconcile.bak` snapshot also covers tool mistakes across sessions.
- Summaries always identify the target's kind, point count, and recording
  source before/after destructive edits. The tools touch only the mod's
  atlas — never terrain or world saves.

### Geometry maintenance

`RoadAtlas.PerformMaintenance` runs at load time (logged when it changes
anything) and keeps the atlas compact:

1. **Merge** — same-kind, same-source strokes whose endpoints sit within
   2.5 m are joined into one polyline (reversing fragments as needed; the
   first stroke keeps its identity). The tolerance is below every
   stroke-breaking distance — the 8 m gap rule, teleports — so merging can
   only heal fragmentation from suppression breaks and recovery scan order,
   never bridge real gaps, parallel roads, or crossings (which meet
   mid-polyline, not at endpoints). Loops never join themselves.
2. **Simplify** — Douglas-Peucker at 1.0 m horizontal tolerance, far under
   the ~11.6 m map texel, so the drawn shape is unchanged. Measured on the
   10 km synthetic atlas: 6,667 → 186 points (97%) in ~8 ms; on the real
   ModrTestWorld atlas: 1,061 → 434 points with 57 fragments healed.

Simplification is safe precisely because suppression is segment-based; with
point-based suppression a thinned straight stroke would let re-walks re-ink
its interior.

### RoadAtlasCodec

Pure serialization of the sidecar TSV format (see “Persistence format”).
`RoadPersistence` delegates all parsing/formatting here and keeps only file IO,
logging, the atomic-write dance, and the one-time v1 backup.

### RoadPersistence

Writes a tab-separated, versioned sidecar file to:

```text
BepInEx/config/ConcernedCatMods/ConcernedCartographer/<world-uid>.roads.tsv
```

The write uses an intermediate temporary file and replacement. A malformed line is skipped with a warning instead of preventing world load.

### RoadOverlayRenderer

Creates two named Jötunn overlays:

- `CC Dirt Paths`
- `CC Paved Roads`

The names are deliberately short: Jötunn's overlay toggle panel truncates long
names, and both layers must remain distinguishable after truncation.

Jötunn renders overlays on the full map and minimap, respects fog by default, and exposes GUI toggles. The renderer never retains a `MapOverlay` reference across world loads. Full texture redraw occurs only when a map becomes available; new survey segments are drawn incrementally. Single-point strokes (lone construction dabs, isolated chunk-recovery hits) render as dots of the configured line width.

## Persistence format

Current format, v3 (written since 0.2.0):

```text
# ConcernedCartographer roads v3
<stroke-guid>\t<Dirt|Paved>\t<point-index>\t<x>\t<y>\t<z>\t<source>\t<flags>\t3
```

`<source>` is the stroke's `RoadObservationSource` name (`Traversal`,
`Construction`, `ChunkRecovery`); `<flags>` is an integer bitmask (1 =
hidden by the repair tools). The trailing marker names the row format: `3`
for v3, `2` for the source-only v2 rows (a development-only intermediate),
`1` for legacy v1 rows, which have no source column:

```text
<stroke-guid>\t<Dirt|Paved>\t<point-index>\t<x>\t<y>\t<z>\t1
```

The parser accepts all row formats in one file; v1 rows load with source
`Traversal`. The writer always emits v3. Because a downgraded 0.1.0 mod would
treat newer rows as malformed and discard the file, `RoadPersistence` copies
a v1 file once to `<file>.v1.bak` before its first rewrite; deleting the new
file and renaming the backup is the manual rollback path.

Point indices must start at 0 and increase by 1 within a stroke, and a
stroke's kind and source must not change between rows. Coordinates use
invariant-culture decimal formatting. The file is intentionally simple
enough to inspect and recover manually; malformed rows are skipped with a
single warning that reports the skipped count.

## Atlas Core and Pin Workbench (v0.3)

Pure pin machinery lives in `Domain/Atlas` (namespace
`TheConcernedCat.ConcernedCartographer.Atlas`), game-free and CI-tested:

- **Identity** — `AtlasId` (`cc:pin:<guid>`), never changed by any edit.
- **Entity** — `AtlasPin`: name, namespaced icon ID, category, color,
  display size, notes, tags, status, checked, scope intent, source,
  archived, durable-deletion tombstone, position, created/modified times,
  and a monotonic per-entity revision owned by `PinStore`.
- **Store** — in-place mutation with revision bumps, tombstone
  delete/restore, higher-revision-wins upserts (idempotent replay; a stale
  writer can never resurrect a deletion), a change stream, and tombstone
  retention purging.
- **Persistence** — `<uid>.pins.tsv` snapshot plus `.journal` append file
  sharing one escaped-TSV row codec (v1): recovery parses snapshot then
  journal and resolves per identity by highest revision, so an interrupted
  write loses at most its own truncated line. Snapshots are atomic at world
  boundaries; journal rows flush on the autosave tick.
- **Operations** — `PinOperations`: move/duplicate/archive/delete/restore,
  one-step-undoable batch edits, bucketed duplicate detection, merge with
  tag union + note provenance, and bounded undo/redo whose restores always
  move revisions FORWARD (old field values under a new revision), keeping
  journal replay and future sync convergent.
- **Icon registry** — curated, append-only, namespaced IDs mapped to
  vanilla pin-type ordinals; unknown IDs render as the fallback while the
  stored identity is preserved verbatim.
- **Workbench** — `PinWorkbenchController` (pure buffer/validate/apply as
  one undo step) drives both the Jötunn-built map panel
  (`PinWorkbenchPanel`: managed edit / vanilla adopt-prompt / foreign
  read-only modes) and the `cc_pins` console.

`PinAdapter` is the only Minimap bridge: managed pins render as ordinary
saved vanilla pins (uninstall-safe by construction), adoption tracks the
existing PinData untouched, restart reconciliation matches by position+name
with single-claim semantics (no duplicates), vanilla cross-offs/deletions
are absorbed into the store, and foreign/system pins are untouchable
through every code path. Private Minimap members are reached only through
Harmony skip-visibility helpers (`MinimapReflection`).

## Lifecycle

### Plugin load

1. Bind config.
2. Construct runtime.
3. Subscribe to `MinimapManager.OnVanillaMapAvailable`.

### World/map load

1. Resolve `ZNet.instance.GetWorldUID()`.
2. Save any dirty atlas from a prior world.
3. Load the current world's sidecar data.
4. Clear/rebuild both overlays.
5. Enable surveying.

### Runtime sampling

1. Wait for the configured interval.
2. Probe terrain beneath the local player.
3. Add a sample when spacing/gap rules allow.
4. Draw only the new segment.
5. Autosave on a slow interval when dirty.

### Shutdown or world switch

1. Save dirty atlas.
2. End the active stroke.
3. Unsubscribe events.

## Performance constraints

- No global scans in `Update`.
- Reuse the small `Heightmap` list in the probe.
- No per-frame logging.
- Minimum sample interval: 0.10 seconds.
- Minimum spacing: 0.5 meters.
- Full 2048×2048 texture rebuild only on map/world initialization or explicit rebuild.
- Autosave no more frequently than every 5 seconds.

## Failure policy

- Terrain API failure: log once, disable surveying for the session, retain existing atlas rendering.
- Persistence parse failure: skip malformed rows, keep valid strokes, log row count.
- Persistence write failure: retain dirty state and retry at the next autosave/shutdown.
- Overlay unavailable: log and wait for the next map-available event.

## Future extension seams

- `RoadObservationPipeline` accepts direct hoe-action capture (CC-005) and loaded-chunk scanning (CC-006) as additional sources without atlas changes.
- Persistence can gain a versioned binary format after profiling, with a migration from the TSV formats.
- A network layer can exchange immutable stroke updates without changing `RoadAtlas` semantics.
- Marker editing remains a separate UI subsystem so it cannot destabilize the road atlas.
