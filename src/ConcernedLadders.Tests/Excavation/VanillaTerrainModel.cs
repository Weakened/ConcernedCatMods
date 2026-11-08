using TheConcernedCat.ConcernedLadders.Domain.Excavation;

namespace TheConcernedCat.ConcernedLadders.Tests.Excavation;

/// <summary>A transcription, for tests only, of the installed engine's terrain path that the planner relies on
/// (assembly_valheim 1.0.21, read from the decompiled source; see EXCAVATION.md section 3). It is not a game
/// stand-in for the adapter: it exists so a test can show what the one native operation the plan names does
/// to the lattice, including the clamp and the stale-height double count the runtime must avoid.
///
/// Transcribed: TerrainComp.RaiseTerrain, square branch (every vertex within ceil(radius / scale) of the
/// op's vertex; skip when delta &lt; 0 and the target is above the vertex; add target - height to the level
/// delta; clamp the level delta to +-8), Heightmap.WorldToVertex rounding, TerrainComp.ApplyToHeightmap's
/// final clamp to base +-8, and the triangulation of Heightmap.RebuildCollisionMesh (each cell split on its
/// (x+1, z) - (x, z+1) diagonal; the collider mesh is built from the same vertices).</summary>
internal sealed class VanillaTerrainModel
{
    private readonly Func<int, int, float> _base;
    private readonly Dictionary<(int, int), float> _levelDelta = new();
    private readonly Dictionary<(int, int), float> _displayed = new();
    private readonly HashSet<(int, int)> _unloaded = new();

    internal VanillaTerrainModel(Func<int, int, float> baseHeight) => _base = baseHeight;

    internal float Base(int x, int z) => _base(x, z);

    /// <summary>Height the heightmap currently holds: what GetHeight returns to the next operation, and what the
    /// mesh and collider show. Stale between an operation and the next rebuild.</summary>
    internal float Displayed(int x, int z) => _displayed.TryGetValue((x, z), out float h) ? h : _base(x, z);

    /// <summary>Height after the next rebuild (Heightmap.Generate + TerrainComp.ApplyToHeightmap).</summary>
    internal float Resolved(int x, int z)
    {
        float b = _base(x, z);
        float level = _levelDelta.TryGetValue((x, z), out float d) ? d : 0f;
        return Math.Max(b - 8f, Math.Min(b + 8f, b + level));
    }

    internal void MarkUnloaded(int x, int z) => _unloaded.Add((x, z));

    /// <summary>Another tool's earlier edit (a hoe or pickaxe), already rebuilt.</summary>
    internal void Edit(int x, int z, float delta)
    {
        float level = (_levelDelta.TryGetValue((x, z), out float d) ? d : 0f) + delta;
        _levelDelta[(x, z)] = Math.Max(-8f, Math.Min(8f, level));
        _displayed[(x, z)] = Resolved(x, z);
    }

    /// <summary>TerrainComp.RaiseTerrain with m_square = true. Heights read are the displayed (possibly stale) ones.</summary>
    internal void SquareRaise(float opX, float opY, float opZ, float radius, float delta)
    {
        int cx = (int)Math.Floor(opX + 0.5f);
        int cz = (int)Math.Floor(opZ + 0.5f);
        int reach = (int)Math.Ceiling(radius / ExcavationGeometry.VertexSpacing);
        var updates = new List<((int, int) key, float level)>();
        for (int z = cz - reach; z <= cz + reach; z++)
        {
            for (int x = cx - reach; x <= cx + reach; x++)
            {
                float height = Displayed(x, z);
                float target = opY + delta;
                if (delta < 0f && target > height) continue;
                if (delta > 0f && target < height) continue;
                float level = (_levelDelta.TryGetValue((x, z), out float d) ? d : 0f) + (target - height);
                updates.Add(((x, z), Math.Max(-8f, Math.Min(8f, level))));
            }
        }
        foreach (var (key, level) in updates) _levelDelta[key] = level;
    }

    /// <summary>The heightmap's late rebuild after Poke.</summary>
    internal void Rebuild()
    {
        foreach ((int, int) key in _levelDelta.Keys.ToList())
        {
            _displayed[key] = Resolved(key.Item1, key.Item2);
        }
    }

    internal void Apply(in ExcavationPlan plan) =>
        SquareRaise(plan.CentreX * ExcavationGeometry.VertexSpacing, plan.OperationY,
            plan.CentreZ * ExcavationGeometry.VertexSpacing, plan.OperationRadius, plan.OperationDelta);

    /// <summary>The terrain surface (and collider) at a world point: the triangle of its cell, interpolated.</summary>
    internal float Surface(float x, float z)
    {
        int ix = (int)Math.Floor(x);
        int iz = (int)Math.Floor(z);
        float u = x - ix;
        float v = z - iz;
        float h00 = Displayed(ix, iz), h10 = Displayed(ix + 1, iz), h01 = Displayed(ix, iz + 1), h11 = Displayed(ix + 1, iz + 1);
        // Triangles (x,z)-(x,z+1)-(x+1,z) and (x+1,z)-(x,z+1)-(x+1,z+1).
        if (u + v <= 1f) return h00 + (h10 - h00) * u + (h01 - h00) * v;
        return h11 + (h01 - h11) * (1f - u) + (h10 - h11) * (1f - v);
    }

    /// <summary>What the engine adapter would read into the planner: current height and the dig-limit base.</summary>
    internal ExcavationSamples Sample(ExcavationMode mode, int cx, int cz)
    {
        var samples = new ExcavationSamples(mode);
        int reach = samples.Reach;
        for (int dz = -reach; dz <= reach; dz++)
        {
            for (int dx = -reach; dx <= reach; dx++)
            {
                if (_unloaded.Contains((cx + dx, cz + dz))) { samples.SetUnloaded(dx, dz); continue; }
                samples.Set(dx, dz, Displayed(cx + dx, cz + dz), _base(cx + dx, cz + dz));
            }
        }
        return samples;
    }
}
