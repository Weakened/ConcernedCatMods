using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.RoadQuality;

/// <summary>Stable spatial segment identity (CT-017): the world is cut into
/// fixed 8 m grid cells, so the same ground always lands in the same
/// segment regardless of trip direction or timing — the precondition for
/// deterministic scores.</summary>
public readonly struct RoadSegmentKey : IEquatable<RoadSegmentKey>
{
    public const float SegmentSizeMeters = 8f;

    public RoadSegmentKey(int cellX, int cellZ)
    {
        CellX = cellX;
        CellZ = cellZ;
    }

    public int CellX { get; }

    public int CellZ { get; }

    public static RoadSegmentKey FromPosition(float x, float z)
    {
        return new RoadSegmentKey(
            (int)Math.Floor(x / SegmentSizeMeters),
            (int)Math.Floor(z / SegmentSizeMeters));
    }

    public bool Equals(RoadSegmentKey other)
    {
        return CellX == other.CellX && CellZ == other.CellZ;
    }

    public override bool Equals(object? obj)
    {
        return obj is RoadSegmentKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (CellX * 397) ^ CellZ;
        }
    }
}
