using System;

namespace TheConcernedCat.ConcernedLadders.Domain.Excavation;

/// <summary>Faults the terrain adapter can find that the planner cannot express (#487 slice 2). Each one refuses
/// the whole plan before anything is committed or charged.</summary>
internal enum ExcavationReadFault
{
    None = 0,

    /// <summary>A heightmap under the square does not use the 1 m whole-metre lattice the planner assumes
    /// (EXCAVATION.md claim 1). The plan would not be the hole the engine digs, so nothing is planned.</summary>
    UnsupportedLattice = 1,

    /// <summary>A heightmap under the square is waiting for its rebuild: its heights are stale, so a plan drawn
    /// from them could double count (claim 9). Transient; reading again a frame later normally clears it.</summary>
    Rebuilding = 2,

    /// <summary>A player TerrainModifier (the legacy terrain edit) touches a heightmap under the square. The final
    /// dig-limit clamp then measures from a base this adapter cannot read (claim 6), so it refuses instead of
    /// guessing it.</summary>
    LegacyTerrainModifier = 3,

    /// <summary>A terrain compiler under the square exists but its deltas could not be read (not initialised, or
    /// arrays of the wrong size). Its dig limit is unknown.</summary>
    CompilerUnreadable = 4,
}

/// <summary>Game-free rules the terrain adapter applies to what it reads (#487 slice 2). Kept here so each one is
/// tested without the game; the adapter only fetches the numbers.</summary>
internal static class ExcavationTerrainRules
{
    /// <summary>How close the live heightmap scale must be to <see cref="ExcavationGeometry.VertexSpacing"/>.</summary>
    internal const float LatticeTolerance = 0.0001f;

    /// <summary>How close a heightmap's origin must be to a whole metre for its vertices to sit on whole metres.</summary>
    internal const float OriginTolerance = 0.001f;

    /// <summary>The engine's two terrain clamps, in metres: TerrainComp clamps its level delta to +-8 and
    /// Heightmap clamps the result to its base +-8 (Heightmap.c_LevelMaxDelta).</summary>
    internal const float EngineClamp = 8f;

    /// <summary>Margin inside <see cref="EngineClamp"/> past which the final clamp may already have bound.</summary>
    internal const float FinalClampMargin = 0.01f;

    /// <summary>True when a live heightmap puts one vertex on every whole world metre, the lattice the planner
    /// draws on: spacing <see cref="ExcavationGeometry.VertexSpacing"/>, an even vertex count across (so the
    /// centre vertex sits on the origin), and an origin on whole metres. Anything else refuses (claim 1).</summary>
    internal static bool LatticeSupported(int width, float scale, float originX, float originZ)
    {
        if (width <= 0 || width % 2 != 0) return false;
        if (!LadderRules.Finite(scale) || Math.Abs(scale - ExcavationGeometry.VertexSpacing) > LatticeTolerance) return false;
        return WholeMetre(originX) && WholeMetre(originZ);
    }

    /// <summary>The base the engine's dig limit binds on at one vertex of one tile, or NaN when it cannot be known.
    ///
    /// Read from the decompiled installed game (EXCAVATION.md claim 6). The heightmap holds
    /// <c>H = clamp(Hpre + L + S, B - 8, B + 8)</c>, where Hpre is the height before the terrain compiler, L and S
    /// are the compiler's level and smooth deltas, and B is the base the final clamp uses. A square lowering to a
    /// target T sets <c>L' = clamp(L + T - H + S, -8, 8)</c>, so it reaches T only when <c>T &gt;= H - L - S - 8</c>,
    /// and the final clamp then needs <c>T &gt;= B - 8</c>. With no player TerrainModifier on the tile (the adapter
    /// refuses otherwise) B is Hpre, and while the final clamp has not bound, Hpre is <c>H - L - S</c>: the two
    /// bases are the same number, which this returns.
    ///
    /// When <c>|L + S|</c> is at the clamp, the final clamp may already have bound, H no longer tells Hpre, and the
    /// result is NaN, which the planner refuses (NotFinite). That never lets a plan through that the engine would
    /// clamp to a partial floor.</summary>
    internal static float LimitBase(float height, float levelDelta, float smoothDelta)
    {
        if (!LadderRules.Finite(height) || !LadderRules.Finite(levelDelta) || !LadderRules.Finite(smoothDelta)) return float.NaN;
        float compiled = levelDelta + smoothDelta;
        if (Math.Abs(compiled) >= EngineClamp - FinalClampMargin) return float.NaN;
        return height - compiled;
    }

    private static bool WholeMetre(float value) =>
        LadderRules.Finite(value) && Math.Abs(value - Math.Round(value)) <= OriginTolerance;
}

/// <summary>One lattice vertex as read from every loaded tile that holds it. A vertex on a tile edge exists in both
/// tiles (EXCAVATION.md claim 8) and the engine applies the operation to each tile on its own, so the vertex is
/// usable only when every copy agrees, and its dig limit is the strictest copy's.</summary>
internal struct ExcavationVertexRead
{
    private int _copies;
    private float _height;
    private float _limitBase;
    private bool _disagree;

    internal int Copies => _copies;

    /// <summary>The agreed height, or NaN when copies disagree or any copy is not finite.</summary>
    internal float Height => _copies == 0 || _disagree ? float.NaN : _height;

    /// <summary>The highest (strictest) copy's limit base, or NaN when any copy's is unknown.</summary>
    internal float LimitBase => _copies == 0 || _disagree ? float.NaN : _limitBase;

    internal void Add(float height, float limitBase)
    {
        if (!LadderRules.Finite(height) || !LadderRules.Finite(limitBase))
        {
            _disagree = true;
        }
        else if (_copies == 0)
        {
            _height = height;
            _limitBase = limitBase;
        }
        else
        {
            if (Math.Abs(height - _height) > ExcavationPlanner.LevelTolerance) _disagree = true;
            if (limitBase > _limitBase) _limitBase = limitBase;
        }
        _copies++;
    }

    /// <summary>Writes this vertex into the planner's grid: unloaded when no tile held it, otherwise its agreed values
    /// (NaN, which the planner refuses, when the copies disagree).</summary>
    internal void WriteTo(ExcavationSamples samples, int dx, int dz)
    {
        if (samples == null) throw new ArgumentNullException(nameof(samples));
        if (_copies == 0) { samples.SetUnloaded(dx, dz); return; }
        samples.Set(dx, dz, Height, LimitBase);
    }
}
