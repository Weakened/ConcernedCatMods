using System;
using System.Collections.Generic;
using System.Reflection;
using TheConcernedCat.ConcernedLadders.Domain.Excavation;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Excavation;

/// <summary>Reads the terrain under one plan into <see cref="ExcavationSamples"/> (#487 slice 2). Read-only: it never
/// writes a height, a delta, a ZDO or an RPC, and it never creates a terrain compiler.
///
/// Every number comes from the game's own heightmaps, read from the decompiled installed game:
/// <list type="bullet">
/// <item>Only full-detail heightmaps: <c>Heightmap.GetAllHeightmaps()</c> is the list distant-LOD heightmaps leave
/// (IsDistantLod removes them from it).</item>
/// <item>The lattice is read live (<c>m_width</c>, <c>m_scale</c>, the transform's origin) and refused when it is not
/// the planner's 1 m whole-metre grid (claim 1).</item>
/// <item>A heightmap with a rebuild queued (<c>m_doLateUpdate</c> set by Poke) has stale heights and refuses
/// (claim 9).</item>
/// <item>The dig-limit base comes from the tile's TerrainComp level and smooth deltas
/// (<see cref="ExcavationTerrainRules.LimitBase"/>); a tile with a legacy player TerrainModifier refuses (claim 6).</item>
/// <item>A vertex on a tile edge is read from every tile that holds it and must agree (claim 8).</item>
/// </list>
/// TerrainComp's deltas and its initialised flag are private in the shipped game. A publicized build reference would
/// compile a direct read and the game would then throw (observed for a protected method in 0.2.2), so they are read
/// through reflection, found once at start-up; a missing member makes the whole feature unavailable rather than
/// guessed (<see cref="Available"/>).</summary>
internal sealed class ExcavationTerrainReader
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private readonly FieldInfo? _levelDelta;
    private readonly FieldInfo? _smoothDelta;
    private readonly FieldInfo? _initialized;
    private readonly List<Heightmap> _tiles = new List<Heightmap>(4);

    // The tiles' compiler deltas, parallel to _tiles; null where the tile has no compiler (never edited: both zero).
    private readonly List<float[]?> _levels = new List<float[]?>(4);
    private readonly List<float[]?> _smooths = new List<float[]?>(4);

    internal ExcavationTerrainReader()
    {
        _levelDelta = Field(typeof(TerrainComp), "m_levelDelta", typeof(float[]));
        _smoothDelta = Field(typeof(TerrainComp), "m_smoothDelta", typeof(float[]));
        _initialized = Field(typeof(TerrainComp), "m_initialized", typeof(bool));
        Missing = _levelDelta == null ? "TerrainComp.m_levelDelta" :
                  _smoothDelta == null ? "TerrainComp.m_smoothDelta" :
                  _initialized == null ? "TerrainComp.m_initialized" : null;
    }

    /// <summary>False when a game member this reader needs was not found; then nothing is read or committed.</summary>
    internal bool Available => Missing == null;

    /// <summary>The first member the start-up probe could not find, for the log.</summary>
    internal string? Missing { get; }

    /// <summary>Fills <paramref name="samples"/> for the square centred on (<paramref name="centreX"/>,
    /// <paramref name="centreZ"/>). Returns a fault the planner cannot express, or None; vertices nobody holds are
    /// left unloaded and copies that disagree are left NaN, which the planner refuses.</summary>
    internal ExcavationReadFault Read(ExcavationSamples samples, int centreX, int centreZ)
    {
        if (samples == null) throw new ArgumentNullException(nameof(samples));
        samples.Clear();
        if (!Available) return ExcavationReadFault.CompilerUnreadable;
        int reach = samples.Reach;
        List<Heightmap> all = Heightmap.GetAllHeightmaps();

        // The tiles under the square, checked once each.
        _tiles.Clear();
        _levels.Clear();
        _smooths.Clear();
        float spacing = ExcavationGeometry.VertexSpacing;
        var centre = new Vector3(centreX * spacing, 0f, centreZ * spacing);
        float halfSquare = reach * spacing;
        for (int i = 0; i < all.Count; i++)
        {
            Heightmap tile = all[i];
            if (tile == null || !tile.isActiveAndEnabled || tile.IsDistantLod) continue;
            if (!tile.IsPointInside(centre, halfSquare)) continue;
            ExcavationReadFault fault = CheckTile(tile);
            if (fault != ExcavationReadFault.None) return fault;
            if (!ReadCompiler(tile, out float[]? levels, out float[]? smooths)) return ExcavationReadFault.CompilerUnreadable;
            _tiles.Add(tile);
            _levels.Add(levels);
            _smooths.Add(smooths);
        }

        for (int dz = -reach; dz <= reach; dz++)
        {
            for (int dx = -reach; dx <= reach; dx++)
            {
                var point = new Vector3((centreX + dx) * spacing, 0f, (centreZ + dz) * spacing);
                var vertex = new ExcavationVertexRead();
                for (int t = 0; t < _tiles.Count; t++)
                {
                    Heightmap tile = _tiles[t];
                    if (!tile.IsPointInside(point)) continue;
                    ReadVertex(tile, _levels[t], _smooths[t], point, out float height, out float limitBase);
                    vertex.Add(height, limitBase);
                }
                vertex.WriteTo(samples, dx, dz);
            }
        }
        return ExcavationReadFault.None;
    }

    private static ExcavationReadFault CheckTile(Heightmap tile)
    {
        Vector3 origin = tile.transform.position;
        if (!ExcavationTerrainRules.LatticeSupported(tile.m_width, tile.m_scale, origin.x, origin.z)) return ExcavationReadFault.UnsupportedLattice;
        if (tile.m_doLateUpdate != 0) return ExcavationReadFault.Rebuilding;
        List<TerrainModifier> modifiers = TerrainModifier.GetAllInstances();
        for (int i = 0; i < modifiers.Count; i++)
        {
            TerrainModifier modifier = modifiers[i];
            if (modifier != null && modifier.enabled && modifier.m_playerModifiction && tile.TerrainVSModifier(modifier))
            {
                return ExcavationReadFault.LegacyTerrainModifier;
            }
        }
        return ExcavationReadFault.None;
    }

    /// <summary>The tile's compiler deltas. True with nulls when the tile has no compiler (never edited); false when it
    /// has one that cannot be read (not initialised, or arrays of the wrong size), which refuses the whole plan.</summary>
    private bool ReadCompiler(Heightmap tile, out float[]? levels, out float[]? smooths)
    {
        levels = null;
        smooths = null;
        TerrainComp compiler = TerrainComp.FindTerrainCompiler(tile.transform.position);
        if (compiler == null) return true;
        if (!(_initialized!.GetValue(compiler) is bool initialized) || !initialized) return false;
        levels = _levelDelta!.GetValue(compiler) as float[];
        smooths = _smoothDelta!.GetValue(compiler) as float[];
        int count = (tile.m_width + 1) * (tile.m_width + 1);
        return levels != null && smooths != null && levels.Length == count && smooths.Length == count;
    }

    /// <summary>One vertex of one tile: its world height and its dig-limit base, NaN where the tile has no generated
    /// height there (refused by the planner).</summary>
    private static void ReadVertex(Heightmap tile, float[]? levels, float[]? smooths, Vector3 point, out float height, out float limitBase)
    {
        tile.WorldToVertex(point, out int x, out int y);
        int pitch = tile.m_width + 1;
        height = float.NaN;
        limitBase = float.NaN;
        if (x < 0 || y < 0 || x >= pitch || y >= pitch) return;
        float local;
        try { local = tile.GetHeight(x, y); }
        catch (ArgumentOutOfRangeException) { return; } // Heights not generated yet.
        height = tile.transform.position.y + local;
        int index = y * pitch + x;
        float level = levels == null ? 0f : levels[index];
        float smooth = smooths == null ? 0f : smooths[index];
        limitBase = ExcavationTerrainRules.LimitBase(height, level, smooth);
    }

    private static FieldInfo? Field(Type type, string name, Type fieldType)
    {
        FieldInfo? field = type.GetField(name, Instance);
        return field != null && field.FieldType == fieldType ? field : null;
    }
}
