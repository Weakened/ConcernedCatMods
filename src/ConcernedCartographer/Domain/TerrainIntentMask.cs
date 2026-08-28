using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>Persistent negative terrain intent for one world
/// (DEF-v1.0-005): the set of 1 m ground cells where the player explicitly
/// terraformed (level/raise/cultivate/reset), meaning any Dirt terrain
/// paint there is a side effect and must NOT be recorded as road by the
/// passive sources (traversal, chunk recovery). A later explicit Pathen or
/// Paved operation clears the cells it covers — deliberate road building
/// always overrides the exclusion.
///
/// The mask derives only from the local player's own successful terrain
/// operations, so it can never reveal unexplored terrain. Geometry is a
/// bounded cell set: disks are rasterized to cells whose centers fall
/// inside the brush, adds/clears are idempotent, and overlapping
/// operations converge to plain set semantics. Beyond
/// <see cref="MaxCells"/> the oldest cells are evicted (degrading, for
/// those cells only, to the pre-fix behavior of trusting paint).</summary>
internal sealed class TerrainIntentMask
{
    public const float CellSizeMeters = 1f;

    /// <summary>Extra radius added around a captured brush so paint
    /// feathering at the brush edge stays inside the exclusion.</summary>
    public const float BrushMarginMeters = 1f;

    /// <summary>Hard cell cap per world (~250k m² of terraformed ground —
    /// far beyond any real base).</summary>
    public const int DefaultMaxCells = 250_000;

    private readonly HashSet<long> _cells = new();

    // Insertion order for oldest-first eviction. Lazy: cleared cells leave
    // stale entries behind that eviction simply skips.
    private readonly Queue<long> _insertionOrder = new();

    private readonly int _maxCells;

    public TerrainIntentMask(int maxCells = DefaultMaxCells)
    {
        _maxCells = Math.Max(1, maxCells);
    }

    public TerrainIntentMask(IEnumerable<(int Cx, int Cz)> cells, int maxCells = DefaultMaxCells)
        : this(maxCells)
    {
        foreach ((int cx, int cz) in cells)
        {
            long key = Pack(cx, cz);
            if (_cells.Add(key))
            {
                _insertionOrder.Enqueue(key);
            }

            if (_cells.Count > _maxCells)
            {
                EvictOldest();
            }
        }

        IsDirty = false;
    }

    public int Count => _cells.Count;

    public bool IsDirty { get; private set; }

    /// <summary>All excluded cells as cell indices, for serialization.</summary>
    public IEnumerable<(int Cx, int Cz)> Cells
    {
        get
        {
            foreach (long key in _cells)
            {
                yield return Unpack(key);
            }
        }
    }

    /// <summary>True when the world position lies in an excluded cell.</summary>
    public bool IsExcluded(float x, float z)
    {
        return _cells.Contains(Pack(CellOf(x), CellOf(z)));
    }

    /// <summary>Marks the brush footprint as explicitly-not-road. Returns
    /// the number of newly excluded cells.</summary>
    public int AddExclusion(float centerX, float centerZ, float radiusMeters)
    {
        int added = 0;
        ForEachCellInDisk(centerX, centerZ, radiusMeters, key =>
        {
            if (_cells.Add(key))
            {
                _insertionOrder.Enqueue(key);
                added++;
            }
        });

        while (_cells.Count > _maxCells)
        {
            EvictOldest();
        }

        if (added > 0)
        {
            IsDirty = true;
        }

        return added;
    }

    /// <summary>Removes exclusion under a deliberate road brush. Returns
    /// the number of cells cleared.</summary>
    public int ClearExclusion(float centerX, float centerZ, float radiusMeters)
    {
        int cleared = 0;
        ForEachCellInDisk(centerX, centerZ, radiusMeters, key =>
        {
            if (_cells.Remove(key))
            {
                cleared++;
            }
        });

        if (cleared > 0)
        {
            IsDirty = true;
        }

        return cleared;
    }

    public void MarkClean()
    {
        IsDirty = false;
    }

    private void EvictOldest()
    {
        // Skip stale queue entries left behind by ClearExclusion.
        while (_insertionOrder.Count > 0)
        {
            long key = _insertionOrder.Dequeue();
            if (_cells.Remove(key))
            {
                return;
            }
        }
    }

    private static void ForEachCellInDisk(float centerX, float centerZ, float radiusMeters, Action<long> visit)
    {
        if (radiusMeters <= 0f || float.IsNaN(radiusMeters) || float.IsInfinity(radiusMeters) ||
            float.IsNaN(centerX) || float.IsNaN(centerZ))
        {
            return;
        }

        float radiusSquared = radiusMeters * radiusMeters;
        int minCx = CellOf(centerX - radiusMeters);
        int maxCx = CellOf(centerX + radiusMeters);
        int minCz = CellOf(centerZ - radiusMeters);
        int maxCz = CellOf(centerZ + radiusMeters);
        for (int cx = minCx; cx <= maxCx; cx++)
        {
            float dx = ((cx + 0.5f) * CellSizeMeters) - centerX;
            for (int cz = minCz; cz <= maxCz; cz++)
            {
                float dz = ((cz + 0.5f) * CellSizeMeters) - centerZ;
                if ((dx * dx) + (dz * dz) <= radiusSquared)
                {
                    visit(Pack(cx, cz));
                }
            }
        }
    }

    private static int CellOf(float coordinate)
    {
        return (int)Math.Floor(coordinate / CellSizeMeters);
    }

    private static long Pack(int cx, int cz)
    {
        return ((long)cx << 32) | (uint)cz;
    }

    private static (int Cx, int Cz) Unpack(long key)
    {
        return ((int)(key >> 32), (int)key);
    }
}
