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

/// <summary>What every excavation plan shares (#487; the player-sized dig since #551, <see cref="DigPlanner"/>): the level
/// tolerance, the engine's dig limit, and the tile range a set of vertices falls in.</summary>
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

    /// <summary>Tiles holding a vertex in [min, max] on one axis. Tile i spans i*64-32 .. i*64+32 (ZoneSystem.GetZone)
    /// and its edge vertices belong to it and its neighbour.</summary>
    internal static void TileRange(int min, int max, out int first, out int count)
    {
        first = FloorDiv(min + TileHalf, TileSize);
        if (Modulo(min + TileHalf, TileSize) == 0) first--;
        int last = FloorDiv(max + TileHalf, TileSize);
        count = last - first + 1;
    }

    private static int FloorDiv(int value, int divisor) => (int)Math.Floor(value / (double)divisor);

    private static int Modulo(int value, int divisor) => ((value % divisor) + divisor) % divisor;
}
