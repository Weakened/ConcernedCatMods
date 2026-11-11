using System;

namespace TheConcernedCat.ConcernedLadders.Domain.Excavation;

/// <summary>The terrain under a player-sized dig (#551): the floor rectangle and the untouched ring one vertex outside
/// it, one entry per lattice vertex in absolute vertex coordinates, read by the engine adapter. Allocated once at the
/// largest footprint and refilled per frame, so the aiming preview re-plans without allocating.</summary>
internal sealed class DigSamples
{
    /// <summary>The widest sampled side: the largest floor plus a ring vertex on each side.</summary>
    internal const int Capacity = DigFootprint.MaximumSide + 3;

    private readonly float[] _height = new float[Capacity * Capacity];
    private readonly float[] _limitBase = new float[Capacity * Capacity];
    private readonly bool[] _loaded = new bool[Capacity * Capacity];

    /// <summary>The first sampled vertex (the ring's corner) on each axis.</summary>
    internal int OriginX { get; private set; }

    internal int OriginZ { get; private set; }

    internal int SideX { get; private set; }

    internal int SideZ { get; private set; }

    /// <summary>Starts a fill for <paramref name="footprint"/>: the floor plus its ring, every vertex unread. A plan over
    /// a grid that was not fully refilled refuses.</summary>
    internal void Begin(DigFootprint footprint)
    {
        OriginX = footprint.MinX - 1;
        OriginZ = footprint.MinZ - 1;
        SideX = footprint.VerticesX + 2;
        SideZ = footprint.VerticesZ + 2;
        Array.Clear(_loaded, 0, _loaded.Length);
    }

    /// <summary>True when (<paramref name="x"/>, <paramref name="z"/>) is in the grid <see cref="Begin"/> set up.</summary>
    internal bool Covers(int x, int z) => x >= OriginX && x < OriginX + SideX && z >= OriginZ && z < OriginZ + SideZ;

    /// <summary>Records one vertex: its current surface height, and the base the engine's 8 m dig limit binds on
    /// (EXCAVATION.md section 3, claim 6).</summary>
    internal void Set(int x, int z, float height, float limitBase)
    {
        int index = Index(x, z);
        _height[index] = height;
        _limitBase[index] = limitBase;
        _loaded[index] = true;
    }

    internal void SetUnloaded(int x, int z) => _loaded[Index(x, z)] = false;

    internal bool IsLoaded(int x, int z) => _loaded[Index(x, z)];

    internal float Height(int x, int z) => _height[Index(x, z)];

    internal float LimitBase(int x, int z) => _limitBase[Index(x, z)];

    private int Index(int x, int z)
    {
        if (!Covers(x, z)) throw new ArgumentOutOfRangeException(nameof(x), "Vertex outside the sampled rectangle.");
        return (z - OriginZ) * Capacity + (x - OriginX);
    }
}
