# Concerned Cartographer architecture

## Context

The first release is a client-side BepInEx/Jötunn plugin targeting .NET Framework 4.8. It samples loaded terrain beneath the local player, records only path-like paint, stores data in a mod-owned sidecar file, and renders through Jötunn map overlays.

## Components

```text
Plugin
  ├─ CartographerSettings
  └─ CartographerRuntime
       ├─ GroundPaintProbe
       ├─ RoadSurveyor
       ├─ RoadAtlas
       ├─ RoadPersistence
       └─ RoadOverlayRenderer
```

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

Runs at a configured interval, not every frame. It samples the local player's position and:

- ends the active stroke when the player is not on road paint;
- starts a stroke when road kind changes;
- rejects points closer than the minimum spacing;
- starts a new stroke when a gap is too large;
- returns only a newly created segment for incremental rendering.

### RoadAtlas

Pure in-memory domain state. It owns strokes, dirty state, and append rules. It does not touch Unity map textures or the filesystem.

### RoadPersistence

Writes a tab-separated, versioned sidecar file to:

```text
BepInEx/config/ConcernedCatMods/ConcernedCartographer/<world-uid>.roads.tsv
```

The write uses an intermediate temporary file and replacement. A malformed line is skipped with a warning instead of preventing world load.

### RoadOverlayRenderer

Creates two named Jötunn overlays:

- `Concerned Cartographer - Dirt Paths`
- `Concerned Cartographer - Paved Roads`

Jötunn renders overlays on the full map and minimap, respects fog by default, and exposes GUI toggles. The renderer never retains a `MapOverlay` reference across world loads. Full texture redraw occurs only when a map becomes available; new survey segments are drawn incrementally.

## Persistence format v1

```text
# ConcernedCartographer roads v1
<stroke-guid>\t<Dirt|Paved>\t<point-index>\t<x>\t<y>\t<z>
```

Coordinates use invariant-culture decimal formatting. The file is intentionally simple enough to inspect and recover manually.

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

- `IRoadObservationSource` can later support direct hoe-action capture and loaded-chunk scanning.
- Persistence can gain a versioned binary format after profiling, with a migration from v1 TSV.
- A network layer can exchange immutable stroke updates without changing `RoadAtlas` semantics.
- Marker editing remains a separate UI subsystem so it cannot destabilize the road atlas.
