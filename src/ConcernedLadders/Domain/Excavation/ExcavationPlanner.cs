using System;

namespace TheConcernedCat.ConcernedLadders.Domain.Excavation;

internal enum ExcavationStatus
{
    /// <summary>Valid, and the floor needs lowering: the only state a deliberate action may commit.</summary>
    Ready = 0,

    /// <summary>Valid, but every floor vertex is already at the planned floor: nothing to do, nothing to charge.</summary>
    NothingToDig = 1,

    /// <summary>Invalid; see <see cref="ExcavationPlan.Refusal"/>. Nothing may be committed or charged.</summary>
    Refused = 2,
}

internal enum ExcavationRefusal
{
    None = 0,

    /// <summary>A floor or ring vertex has no loaded, full-detail terrain under it.</summary>
    Unloaded = 1,

    /// <summary>A sampled height or limit base is NaN or infinite.</summary>
    NotFinite = 2,

    /// <summary>Part of the floor is already lower than the planned floor. Lowering never raises ground, so the
    /// floor could not come out level; the plan refuses instead of leaving a dip the preview did not promise.</summary>
    FloorAlreadyDeeper = 3,

    /// <summary>The planned floor is lower than the engine lets terrain go below its unmodified surface there.
    /// The engine would clamp, leaving a partial floor; the plan refuses instead.</summary>
    DigLimit = 4,
}

internal enum ExcavationProgress
{
    /// <summary>Every floor vertex is at the planned floor.</summary>
    Complete = 0,

    /// <summary>Some floor vertex is still above the planned floor (the operation has not arrived or not rebuilt).</summary>
    Pending = 1,

    /// <summary>Some floor vertex is below the planned floor. A committed plan had none (it refuses otherwise), so
    /// something lowered it further: a second operation applied against stale heights, or another edit.</summary>
    Overshot = 2,

    /// <summary>The terrain could not be read; the outcome is unknown and must not be retried blind.</summary>
    Unreadable = 3,
}

/// <summary>One measured excavation decision. Plain values only, so a preview can re-plan every frame
/// without allocating.</summary>
internal readonly struct ExcavationPlan
{
    internal ExcavationPlan(ExcavationGeometry geometry, int centreX, int centreZ, ExcavationStatus status,
        ExcavationRefusal refusal, int refusalVertices, float datum, int loweredVertices, float maxLowering,
        float minWallHeight, float maxWallHeight, float waterDepth, int firstTileX, int tileCountX, int firstTileZ,
        int tileCountZ)
    {
        Geometry = geometry;
        CentreX = centreX;
        CentreZ = centreZ;
        Status = status;
        Refusal = refusal;
        RefusalVertices = refusalVertices;
        Datum = datum;
        LoweredVertices = loweredVertices;
        MaxLowering = maxLowering;
        MinWallHeight = minWallHeight;
        MaxWallHeight = maxWallHeight;
        WaterDepth = waterDepth;
        FirstTileX = firstTileX;
        TileCountX = tileCountX;
        FirstTileZ = firstTileZ;
        TileCountZ = tileCountZ;
    }

    internal ExcavationGeometry Geometry { get; }
    internal ExcavationMode Mode => Geometry.Mode;
    internal int CentreX { get; }
    internal int CentreZ { get; }
    internal ExcavationStatus Status { get; }
    internal ExcavationRefusal Refusal { get; }

    /// <summary>How many vertices caused <see cref="Refusal"/>.</summary>
    internal int RefusalVertices { get; }

    /// <summary>Highest vertex of the untouched ring. The operation never changes the ring, so planning the same
    /// square again after it completes finds the same datum, the same floor, and nothing to dig.</summary>
    internal float Datum { get; }

    /// <summary>The one absolute floor height: datum minus the mode's depth.</summary>
    internal float Bottom => Datum - Geometry.Depth;

    internal int LoweredVertices { get; }
    internal float MaxLowering { get; }

    /// <summary>Lowest ring vertex above the floor. Below the mode's depth on a slope; negative where the ground
    /// outside is lower than the floor (the pit is open on that side).</summary>
    internal float MinWallHeight { get; }

    /// <summary>Highest ring vertex above the floor; equals the mode's depth by construction.</summary>
    internal float MaxWallHeight { get; }

    /// <summary>How far the floor would sit below the water surface; zero or less means it stays above it.</summary>
    internal float WaterDepth { get; }
    internal bool Floods => WaterDepth > 0f;

    /// <summary>Terrain tiles (zones) holding a floor vertex. A vertex on a tile edge belongs to both tiles, and the
    /// engine operation writes it in both, so a square that touches an edge spans two, and a corner four.</summary>
    internal int FirstTileX { get; }
    internal int TileCountX { get; }
    internal int FirstTileZ { get; }
    internal int TileCountZ { get; }
    internal int TileCount => TileCountX * TileCountZ;

    /// <summary>Engine operation, centred on the centre vertex at the datum: a square lower-only raise with this
    /// radius and this (negative) delta targets exactly <see cref="Bottom"/> on every floor vertex.</summary>
    internal float OperationY => Datum;
    internal float OperationDelta => -Geometry.Depth;
    internal float OperationRadius => Geometry.HalfExtent * ExcavationGeometry.VertexSpacing;

    internal float FloorMinX => (CentreX - Geometry.HalfExtent) * ExcavationGeometry.VertexSpacing;
    internal float FloorMaxX => (CentreX + Geometry.HalfExtent) * ExcavationGeometry.VertexSpacing;
    internal float FloorMinZ => (CentreZ - Geometry.HalfExtent) * ExcavationGeometry.VertexSpacing;
    internal float FloorMaxZ => (CentreZ + Geometry.HalfExtent) * ExcavationGeometry.VertexSpacing;
    internal float BorderMinX => (CentreX - Geometry.RingDistance) * ExcavationGeometry.VertexSpacing;
    internal float BorderMaxX => (CentreX + Geometry.RingDistance) * ExcavationGeometry.VertexSpacing;
    internal float BorderMinZ => (CentreZ - Geometry.RingDistance) * ExcavationGeometry.VertexSpacing;
    internal float BorderMaxZ => (CentreZ + Geometry.RingDistance) * ExcavationGeometry.VertexSpacing;
}

/// <summary>Game-free excavation planning (#487): which vertices change, to what one height, and every reason
/// to refuse, decided before anything is committed or charged. The engine adapter only samples terrain into
/// <see cref="ExcavationSamples"/> and, for a Ready plan, issues the one native operation the plan names.</summary>
internal static class ExcavationPlanner
{
    /// <summary>A vertex within this of the floor counts as at the floor.</summary>
    internal const float LevelTolerance = 0.02f;

    /// <summary>The engine clamps a player terrain edit to this far below (and above) the unmodified surface:
    /// TerrainComp clamps its level delta to +-8 and Heightmap clamps the result to base +-8.</summary>
    internal const float MaximumDigBelowBase = 8f;

    /// <summary>Margin inside the engine's clamp, so a plan the engine would clamp by a rounding error refuses.</summary>
    internal const float DigLimitMargin = 0.01f;

    private const int TileSize = 64;
    private const int TileHalf = 32;

    internal static ExcavationPlan Plan(ExcavationSamples samples, int centreX, int centreZ, float? waterLevel)
    {
        if (samples == null) throw new ArgumentNullException(nameof(samples));
        ExcavationGeometry geometry = samples.Geometry;
        int half = geometry.HalfExtent;
        int ring = geometry.RingDistance;
        TileRange(centreX - half, centreX + half, out int firstTileX, out int tileCountX);
        TileRange(centreZ - half, centreZ + half, out int firstTileZ, out int tileCountZ);

        int unloaded = 0;
        int notFinite = 0;
        float datum = float.NegativeInfinity;
        float lowestRing = float.PositiveInfinity;
        for (int dz = -ring; dz <= ring; dz++)
        {
            for (int dx = -ring; dx <= ring; dx++)
            {
                if (!samples.IsLoaded(dx, dz)) { unloaded++; continue; }
                float height = samples.Height(dx, dz);
                if (!LadderRules.Finite(height) || !LadderRules.Finite(samples.LimitBase(dx, dz))) { notFinite++; continue; }
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != ring) continue;
                if (height > datum) datum = height;
                if (height < lowestRing) lowestRing = height;
            }
        }

        if (unloaded > 0) return Refused(geometry, centreX, centreZ, ExcavationRefusal.Unloaded, unloaded, firstTileX, tileCountX, firstTileZ, tileCountZ);
        if (notFinite > 0) return Refused(geometry, centreX, centreZ, ExcavationRefusal.NotFinite, notFinite, firstTileX, tileCountX, firstTileZ, tileCountZ);

        float bottom = datum - geometry.Depth;
        int lowered = 0;
        int deeper = 0;
        int limited = 0;
        float maxLowering = 0f;
        for (int dz = -half; dz <= half; dz++)
        {
            for (int dx = -half; dx <= half; dx++)
            {
                float height = samples.Height(dx, dz);
                if (height < bottom - LevelTolerance) { deeper++; continue; }
                if (height <= bottom) continue;
                // The engine lowers every floor vertex above the target, including one within tolerance, so each of
                // them is checked against the dig limit; only a real change counts as lowered.
                if (bottom < samples.LimitBase(dx, dz) - MaximumDigBelowBase + DigLimitMargin) limited++;
                if (height <= bottom + LevelTolerance) continue;
                lowered++;
                maxLowering = Math.Max(maxLowering, height - bottom);
            }
        }

        if (deeper > 0) return Refused(geometry, centreX, centreZ, ExcavationRefusal.FloorAlreadyDeeper, deeper, firstTileX, tileCountX, firstTileZ, tileCountZ, datum);
        if (limited > 0) return Refused(geometry, centreX, centreZ, ExcavationRefusal.DigLimit, limited, firstTileX, tileCountX, firstTileZ, tileCountZ, datum);

        float waterDepth = waterLevel.HasValue && LadderRules.Finite(waterLevel.Value) ? waterLevel.Value - bottom : 0f;
        return new ExcavationPlan(geometry, centreX, centreZ,
            lowered == 0 ? ExcavationStatus.NothingToDig : ExcavationStatus.Ready, ExcavationRefusal.None, 0, datum,
            lowered, maxLowering, lowestRing - bottom, datum - bottom, waterDepth, firstTileX, tileCountX, firstTileZ,
            tileCountZ);
    }

    /// <summary>The height a vertex will have once a Ready plan is applied: the floor where it is above the floor,
    /// otherwise unchanged. Outside the floor, nothing changes.</summary>
    internal static float PlannedHeight(in ExcavationPlan plan, ExcavationSamples samples, int dx, int dz)
    {
        float height = samples.Height(dx, dz);
        int half = plan.Geometry.HalfExtent;
        if (plan.Status != ExcavationStatus.Ready || Math.Abs(dx) > half || Math.Abs(dz) > half) return height;
        return height > plan.Bottom ? plan.Bottom : height;
    }

    /// <summary>Compares terrain read after a commit with the plan. Overshot and Unreadable must be reported, never
    /// repaired by issuing another operation: a second lowering against stale heights is what overshoots.</summary>
    internal static ExcavationProgress Check(in ExcavationPlan plan, ExcavationSamples after)
    {
        if (after == null) throw new ArgumentNullException(nameof(after));
        if (after.Geometry.Mode != plan.Mode) throw new ArgumentException("Samples for another mode.", nameof(after));
        // A refused plan has no floor (a refusal before the datum leaves it NaN, and every comparison with NaN is
        // false), so there is nothing to compare against: the outcome is unknown, never Complete.
        if (plan.Status == ExcavationStatus.Refused || !LadderRules.Finite(plan.Bottom)) return ExcavationProgress.Unreadable;
        int half = plan.Geometry.HalfExtent;
        bool pending = false;
        bool overshot = false;
        for (int dz = -half; dz <= half; dz++)
        {
            for (int dx = -half; dx <= half; dx++)
            {
                // One unreadable vertex makes the whole outcome unknown, whatever the others show.
                if (!after.IsLoaded(dx, dz)) return ExcavationProgress.Unreadable;
                float height = after.Height(dx, dz);
                if (!LadderRules.Finite(height)) return ExcavationProgress.Unreadable;
                if (height < plan.Bottom - LevelTolerance) overshot = true;
                else if (height > plan.Bottom + LevelTolerance) pending = true;
            }
        }
        if (overshot) return ExcavationProgress.Overshot;
        return pending ? ExcavationProgress.Pending : ExcavationProgress.Complete;
    }

    /// <summary>Tiles holding a vertex in [min, max] on one axis. Tile i spans i*64-32 .. i*64+32 (ZoneSystem.GetZone)
    /// and its edge vertices belong to it and its neighbour.</summary>
    internal static void TileRange(int min, int max, out int first, out int count)
    {
        first = FloorDiv(min + TileHalf, TileSize);
        if (Modulo(min + TileHalf, TileSize) == 0) first--;
        int last = FloorDiv(max + TileHalf, TileSize);
        count = last - first + 1;
    }

    private static ExcavationPlan Refused(ExcavationGeometry geometry, int centreX, int centreZ, ExcavationRefusal refusal,
        int vertices, int firstTileX, int tileCountX, int firstTileZ, int tileCountZ, float datum = float.NaN) =>
        new ExcavationPlan(geometry, centreX, centreZ, ExcavationStatus.Refused, refusal, vertices, datum, 0, 0f,
            float.NaN, float.NaN, 0f, firstTileX, tileCountX, firstTileZ, tileCountZ);

    private static int FloorDiv(int value, int divisor) => (int)Math.Floor(value / (double)divisor);

    private static int Modulo(int value, int divisor) => ((value % divisor) + divisor) % divisor;
}
