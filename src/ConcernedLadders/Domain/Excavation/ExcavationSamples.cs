using System;

namespace TheConcernedCat.ConcernedLadders.Domain.Excavation;

/// <summary>The terrain under one plan: floor plus the untouched ring, one entry per lattice vertex, read
/// by the engine adapter. Fixed size and reusable, so an aiming preview re-plans without allocating.</summary>
internal sealed class ExcavationSamples
{
    private readonly float[] _height;
    private readonly float[] _limitBase;
    private readonly bool[] _loaded;

    internal ExcavationSamples(ExcavationMode mode)
    {
        Geometry = ExcavationGeometry.For(mode);
        int count = Geometry.SampleSide * Geometry.SampleSide;
        _height = new float[count];
        _limitBase = new float[count];
        _loaded = new bool[count];
    }

    internal ExcavationGeometry Geometry { get; }

    /// <summary>Distance of the outermost sampled vertex from the centre (the ring).</summary>
    internal int Reach => Geometry.RingDistance;

    /// <summary>Marks every vertex unread. A plan over a grid that was not fully refilled refuses.</summary>
    internal void Clear() => Array.Clear(_loaded, 0, _loaded.Length);

    /// <summary>Records one vertex. <paramref name="height"/> is the current world height of the surface
    /// (and collider) there. <paramref name="limitBase"/> is the base the engine's 8 m dig limit binds on:
    /// the higher of (current height minus the terrain compiler's level delta), which the level-delta clamp
    /// measures from, and the heightmap's base before player TerrainModifiers, which the final clamp
    /// measures from (EXCAVATION.md section 3, claim 6).</summary>
    internal void Set(int dx, int dz, float height, float limitBase)
    {
        int index = Index(dx, dz);
        _height[index] = height;
        _limitBase[index] = limitBase;
        _loaded[index] = true;
    }

    /// <summary>Records a vertex the adapter could not read (no loaded, full-detail heightmap covers it).</summary>
    internal void SetUnloaded(int dx, int dz) => _loaded[Index(dx, dz)] = false;

    internal bool IsLoaded(int dx, int dz) => _loaded[Index(dx, dz)];

    internal float Height(int dx, int dz) => _height[Index(dx, dz)];

    internal float LimitBase(int dx, int dz) => _limitBase[Index(dx, dz)];

    private int Index(int dx, int dz)
    {
        int reach = Reach;
        if (dx < -reach || dx > reach || dz < -reach || dz > reach)
        {
            throw new ArgumentOutOfRangeException(nameof(dx), "Offset outside the sampled square.");
        }
        return (dz + reach) * Geometry.SampleSide + dx + reach;
    }
}
