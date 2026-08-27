using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using TheConcernedCat.ConcernedCartographer.Runtime;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>The chunk-recovery observation source: incrementally scans the
/// paint masks of already-loaded, non-LOD heightmaps near the player and
/// emits road paint as ChunkRecovery observations. Bounded by design:
/// no world-file parsing, no global scan (only loaded heightmaps within
/// <see cref="ScanRadiusMeters"/>), a hard per-frame cell budget, map-fog
/// gating so unexplored regions never reveal roads, and the narrowness
/// heuristic so broad cleared areas do not become road tangles. Each
/// heightmap is scanned once per session; repaints within a session are
/// reconciliation's concern (CC-015).</summary>
internal sealed class ChunkRecoveryScanner
{
    private const float ScanRadiusMeters = 128f;

    // Minimap.IsExplored is private, and the Mono runtime enforces member
    // access at JIT even when compiling against publicized references
    // (proven by a MethodAccessException in the field). Harmony's invoker
    // builds a DynamicMethod with skipVisibility, which the runtime accepts.
    private static readonly MethodInfo? IsExploredMethod =
        AccessTools.Method(typeof(Minimap), "IsExplored", new[] { typeof(Vector3) });
    private static readonly FastInvokeHandler? IsExploredInvoker =
        IsExploredMethod is null ? null : MethodInvoker.GetHandler(IsExploredMethod);

    private readonly CartographerSettings _settings;
    private readonly ManualLogSource _log;
    private readonly List<Heightmap> _buffer = new();
    private readonly HashSet<Vector3> _scannedOrigins = new();
    private readonly object[] _isExploredArguments = new object[1];

    private Heightmap? _current;
    private int _cursor;
    private bool _disabledForSession;

    public ChunkRecoveryScanner(CartographerSettings settings, ManualLogSource log)
    {
        _settings = settings;
        _log = log;
    }

    /// <summary>Raised for every explored, path-like road-paint cell, with
    /// the cell's world position.</summary>
    public event Action<RoadKind, Vector3>? PaintObserved;

    public void Tick()
    {
        if (_disabledForSession || !_settings.RecoverLoadedChunks.Value)
        {
            return;
        }

        try
        {
            if (_current == null && !TryPickNextHeightmap())
            {
                return;
            }

            ScanCells(_settings.RecoveryBudgetCellsPerFrame.Value);
        }
        catch (Exception exception)
        {
            _disabledForSession = true;
            _log.LogError($"Chunk recovery failed and was disabled for this session: {exception}");
        }
    }

    /// <summary>Cancels the in-progress scan and forgets scanned chunks, on
    /// logout or world switch.</summary>
    public void Reset()
    {
        _current = null;
        _cursor = 0;
        _scannedOrigins.Clear();
        _buffer.Clear();
    }

    private bool TryPickNextHeightmap()
    {
        Player player = Player.m_localPlayer;
        if (player is null)
        {
            return false;
        }

        Vector3 playerPosition = player.transform.position;
        _buffer.Clear();
        Heightmap.FindHeightmap(playerPosition, ScanRadiusMeters, _buffer);

        Heightmap? nearest = null;
        float nearestSqrDistance = float.MaxValue;
        foreach (Heightmap heightmap in _buffer)
        {
            if (heightmap.IsDistantLod || _scannedOrigins.Contains(heightmap.transform.position))
            {
                continue;
            }

            float sqrDistance = (heightmap.transform.position - playerPosition).sqrMagnitude;
            if (sqrDistance < nearestSqrDistance)
            {
                nearest = heightmap;
                nearestSqrDistance = sqrDistance;
            }
        }

        if (nearest is null)
        {
            return false;
        }

        _current = nearest;
        _cursor = 0;
        return true;
    }

    private void ScanCells(int budget)
    {
        Heightmap heightmap = _current!;
        if (heightmap == null)
        {
            // The heightmap was unloaded mid-scan; drop it without marking
            // it scanned so it is revisited if it reloads.
            _current = null;
            return;
        }

        int side = heightmap.m_width + 1;
        int totalCells = side * side;
        float threshold = _settings.PaintThreshold.Value;
        Minimap minimap = Minimap.instance;
        int processed = 0;

        while (_cursor < totalCells && processed < budget)
        {
            int index = _cursor;
            _cursor++;
            processed++;

            int x = index % side;
            int y = index / side;
            if (!TryClassifyCell(heightmap, x, y, threshold, out RoadKind kind))
            {
                continue;
            }

            Vector3 world = CellToWorld(heightmap, x, y);
            if (minimap == null || !IsExplored(minimap, world))
            {
                continue;
            }

            if (!RecoveryShapeHeuristic.IsPathLike(
                    (cellX, cellY) => TryClassifyCell(heightmap, cellX, cellY, threshold, out _),
                    x,
                    y))
            {
                continue;
            }

            PaintObserved?.Invoke(kind, world);
        }

        if (_cursor >= totalCells)
        {
            _scannedOrigins.Add(heightmap.transform.position);
            _current = null;
        }
    }

    private bool IsExplored(Minimap minimap, Vector3 world)
    {
        if (IsExploredInvoker is null)
        {
            // Fail closed via Tick's catch: without the fog gate the scanner
            // must not run at all, or it would reveal unexplored regions.
            throw new MissingMethodException("Minimap.IsExplored(Vector3) was not found; chunk recovery cannot fog-gate.");
        }

        _isExploredArguments[0] = world;
        return (bool)IsExploredInvoker(minimap, _isExploredArguments);
    }

    private static bool TryClassifyCell(Heightmap heightmap, int x, int y, float threshold, out RoadKind kind)
    {
        // GetPaintMask returns black outside the mask, which never passes the
        // threshold, so window lookups across chunk seams are safely biased
        // toward "unpainted".
        Color paint = heightmap.GetPaintMask(x, y);
        if (paint.b >= threshold && paint.b > paint.r)
        {
            kind = RoadKind.Paved;
            return true;
        }

        if (paint.r >= threshold && paint.r > paint.b)
        {
            kind = RoadKind.Dirt;
            return true;
        }

        kind = default;
        return false;
    }

    private static Vector3 CellToWorld(Heightmap heightmap, int x, int y)
    {
        // Inverse of Heightmap.WorldToVertexMask: cell (x, y) covers the
        // world position offset ((x - half) * scale, (y - half) * scale)
        // from the heightmap center. Y is left at 0; nothing downstream
        // consumes elevation.
        int half = (heightmap.m_width + 1) / 2;
        float scale = heightmap.m_scale;
        return heightmap.transform.position + new Vector3((x - half) * scale, 0f, (y - half) * scale);
    }
}
