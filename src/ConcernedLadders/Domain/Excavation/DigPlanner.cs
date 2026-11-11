using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedLadders.Domain.Excavation;

/// <summary>Why a player-sized dig (#551) cannot be made. Unlike the fixed holes it replaces, ground already lower than
/// the floor is not a reason: it is left as it is, so a dig beside or over an earlier hole joins it.</summary>
internal enum DigRefusal
{
    None = 0,

    /// <summary>A floor or ring vertex has no loaded, full-detail terrain under it.</summary>
    Unloaded = 1,

    /// <summary>A sampled height or limit base is NaN or infinite.</summary>
    NotFinite = 2,

    /// <summary>The floor is lower than the engine lets terrain go below its unmodified surface at some vertex that
    /// would be lowered: the engine would clamp it and leave the floor uneven.</summary>
    DigLimit = 3,

    /// <summary>Some vertex would drop more than one raise can bring back (<see cref="DigUndo.RaiseStep"/>, 8 m): only on
    /// ground an earlier edit raised above its natural surface. Two raises of one vertex in one frame would read the stale
    /// height and overshoot, so such a dig could not be undone exactly; it is refused instead.</summary>
    UndoLimit = 4,
}

/// <summary>One floor vertex the dig lowers, and to what.</summary>
internal readonly struct DigVertex
{
    internal DigVertex(int x, int z, float before, float after)
    {
        X = x;
        Z = z;
        Before = before;
        After = after;
    }

    internal int X { get; }

    internal int Z { get; }

    /// <summary>The height read before the dig: what an undo restores.</summary>
    internal float Before { get; }

    /// <summary>The floor: what the dig sets and what an undo expects to find.</summary>
    internal float After { get; }
}

/// <summary>One measured dig decision (#551). Plain values, so the preview re-plans every frame without allocating; the
/// vertices themselves are listed only when committing (<see cref="DigPlanner.Lowered"/>).</summary>
internal readonly struct DigPlan
{
    internal DigPlan(DigFootprint footprint, ExcavationStatus status, DigRefusal refusal, int refusalVertices, float datum,
        int loweredVertices, float loweredVolume, int alreadyLower, float maxLowering, float minWallHeight, float waterDepth,
        int firstTileX, int tileCountX, int firstTileZ, int tileCountZ)
    {
        Footprint = footprint;
        Status = status;
        Refusal = refusal;
        RefusalVertices = refusalVertices;
        Datum = datum;
        LoweredVertices = loweredVertices;
        LoweredVolume = loweredVolume;
        AlreadyLower = alreadyLower;
        MaxLowering = maxLowering;
        MinWallHeight = minWallHeight;
        WaterDepth = waterDepth;
        FirstTileX = firstTileX;
        TileCountX = tileCountX;
        FirstTileZ = firstTileZ;
        TileCountZ = tileCountZ;
    }

    internal DigFootprint Footprint { get; }

    internal ExcavationStatus Status { get; }

    internal DigRefusal Refusal { get; }

    internal int RefusalVertices { get; }

    /// <summary>The highest vertex of the untouched ring. A dig never changes the ring, so planning the same rectangle
    /// again after it completes finds the same floor and nothing to dig.</summary>
    internal float Datum { get; }

    /// <summary>The one floor height: <see cref="Datum"/> minus the depth.</summary>
    internal float Bottom => Datum - Footprint.Depth;

    /// <summary>Floor vertices the dig lowers: what it costs.</summary>
    internal int LoweredVertices { get; }

    /// <summary>The ground the dig removes, in cubic metres: each lowered vertex's drop over its one square metre of
    /// lattice. What the dig costs (<see cref="DigCost"/>).</summary>
    internal float LoweredVolume { get; }

    /// <summary>Floor vertices already below the floor (an earlier hole), left as they are.</summary>
    internal int AlreadyLower { get; }

    internal float MaxLowering { get; }

    /// <summary>The lowest ring vertex above the floor: below the depth on a slope, negative where the ground outside is
    /// lower than the floor (the pit is open on that side).</summary>
    internal float MinWallHeight { get; }

    internal float WaterDepth { get; }

    internal bool Floods => WaterDepth > 0f;

    internal int FirstTileX { get; }

    internal int TileCountX { get; }

    internal int FirstTileZ { get; }

    internal int TileCountZ { get; }

    internal int TileCount => TileCountX * TileCountZ;
}

/// <summary>Game-free planning of a player-sized dig (#551): which floor vertices change, to what one height, and every
/// reason to refuse, decided before anything is committed or charged. The engine adapter samples the terrain into
/// <see cref="DigSamples"/> and, for a Ready plan, issues one single-vertex lowering per vertex in
/// <see cref="Lowered"/>.</summary>
internal static class DigPlanner
{
    internal static DigPlan Plan(DigSamples samples, DigFootprint footprint, float? waterLevel)
    {
        if (samples == null) throw new ArgumentNullException(nameof(samples));
        if (samples.OriginX != footprint.MinX - 1 || samples.OriginZ != footprint.MinZ - 1 ||
            samples.SideX != footprint.VerticesX + 2 || samples.SideZ != footprint.VerticesZ + 2)
            throw new ArgumentException("Samples for another rectangle.", nameof(samples));
        ExcavationPlanner.TileRange(footprint.MinX, footprint.MaxX, out int firstTileX, out int tileCountX);
        ExcavationPlanner.TileRange(footprint.MinZ, footprint.MaxZ, out int firstTileZ, out int tileCountZ);

        int unloaded = 0, notFinite = 0;
        float datum = float.NegativeInfinity, lowestRing = float.PositiveInfinity;
        for (int z = footprint.MinZ - 1; z <= footprint.MaxZ + 1; z++)
        {
            for (int x = footprint.MinX - 1; x <= footprint.MaxX + 1; x++)
            {
                if (!samples.IsLoaded(x, z)) { unloaded++; continue; }
                float height = samples.Height(x, z);
                if (!LadderRules.Finite(height) || !LadderRules.Finite(samples.LimitBase(x, z))) { notFinite++; continue; }
                if (footprint.ContainsFloor(x, z)) continue;
                if (height > datum) datum = height;
                if (height < lowestRing) lowestRing = height;
            }
        }

        if (unloaded > 0) return Refused(footprint, DigRefusal.Unloaded, unloaded, firstTileX, tileCountX, firstTileZ, tileCountZ);
        if (notFinite > 0) return Refused(footprint, DigRefusal.NotFinite, notFinite, firstTileX, tileCountX, firstTileZ, tileCountZ);

        float bottom = datum - footprint.Depth;
        int lowered = 0, lower = 0, limited = 0, unrestorable = 0;
        float maxLowering = 0f, volume = 0f;
        for (int z = footprint.MinZ; z <= footprint.MaxZ; z++)
        {
            for (int x = footprint.MinX; x <= footprint.MaxX; x++)
            {
                float height = samples.Height(x, z);
                if (height < bottom - ExcavationPlanner.LevelTolerance) { lower++; continue; }
                if (height <= bottom + ExcavationPlanner.LevelTolerance) continue;
                if (bottom < samples.LimitBase(x, z) - ExcavationPlanner.MaximumDigBelowBase + ExcavationPlanner.DigLimitMargin) limited++;
                if (height - bottom > DigUndo.RaiseStep - ExcavationPlanner.DigLimitMargin) unrestorable++;
                lowered++;
                maxLowering = Math.Max(maxLowering, height - bottom);
                volume += height - bottom;
            }
        }

        if (limited > 0) return Refused(footprint, DigRefusal.DigLimit, limited, firstTileX, tileCountX, firstTileZ, tileCountZ, datum);
        if (unrestorable > 0) return Refused(footprint, DigRefusal.UndoLimit, unrestorable, firstTileX, tileCountX, firstTileZ, tileCountZ, datum);
        float waterDepth = waterLevel.HasValue && LadderRules.Finite(waterLevel.Value) ? waterLevel.Value - bottom : 0f;
        return new DigPlan(footprint, lowered == 0 ? ExcavationStatus.NothingToDig : ExcavationStatus.Ready, DigRefusal.None, 0,
            datum, lowered, volume, lower, maxLowering, lowestRing - bottom, waterDepth, firstTileX, tileCountX, firstTileZ, tileCountZ);
    }

    /// <summary>The vertices a Ready plan lowers, with the height each had before: the dig's batch and its undo record.
    /// Allocates; called once, on commit. Empty for any plan that is not Ready.</summary>
    internal static IReadOnlyList<DigVertex> Lowered(in DigPlan plan, DigSamples samples)
    {
        if (plan.Status != ExcavationStatus.Ready) return Array.Empty<DigVertex>();
        DigFootprint footprint = plan.Footprint;
        var lowered = new List<DigVertex>(plan.LoweredVertices);
        for (int z = footprint.MinZ; z <= footprint.MaxZ; z++)
        {
            for (int x = footprint.MinX; x <= footprint.MaxX; x++)
            {
                float height = samples.Height(x, z);
                if (height > plan.Bottom + ExcavationPlanner.LevelTolerance) lowered.Add(new DigVertex(x, z, height, plan.Bottom));
            }
        }

        return lowered;
    }

    /// <summary>Whether a committed dig has arrived, read back: every lowered vertex at the floor. Overshot and Unreadable
    /// are reported, never repaired by issuing more operations against heights that may be stale.</summary>
    internal static ExcavationProgress Progress(DigRecord record, Func<int, int, float?> read)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        if (read == null) throw new ArgumentNullException(nameof(read));
        bool pending = false;
        foreach (DigVertex vertex in record.Vertices)
        {
            float? height = read(vertex.X, vertex.Z);
            if (!height.HasValue || !LadderRules.Finite(height.Value)) return ExcavationProgress.Unreadable;
            if (height.Value < vertex.After - ExcavationPlanner.LevelTolerance) return ExcavationProgress.Overshot;
            if (height.Value > vertex.After + ExcavationPlanner.LevelTolerance) pending = true;
        }

        return pending ? ExcavationProgress.Pending : ExcavationProgress.Complete;
    }

    /// <summary>The height a vertex will have once a Ready plan is applied: the floor where it is above the floor,
    /// otherwise unchanged (and unchanged everywhere outside the floor).</summary>
    internal static float PlannedHeight(in DigPlan plan, DigSamples samples, int x, int z)
    {
        float height = samples.Height(x, z);
        if (plan.Status != ExcavationStatus.Ready || !plan.Footprint.ContainsFloor(x, z)) return height;
        return height > plan.Bottom + ExcavationPlanner.LevelTolerance ? plan.Bottom : height;
    }

    private static DigPlan Refused(DigFootprint footprint, DigRefusal refusal, int vertices, int firstTileX, int tileCountX,
        int firstTileZ, int tileCountZ, float datum = float.NaN) =>
        new DigPlan(footprint, ExcavationStatus.Refused, refusal, vertices, datum, 0, 0f, 0, 0f, float.NaN, 0f,
            firstTileX, tileCountX, firstTileZ, tileCountZ);
}
