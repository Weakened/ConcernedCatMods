using System;
using System.Globalization;

namespace TheConcernedCat.Companions.Placement;

/// <summary>A position in world space, independent of any engine type.
///
/// The shared layer plans placement without referencing Unity, so the game
/// adapter converts at the boundary. <c>Y</c> is height, matching the host
/// engine's convention, so a horizontal distance deliberately ignores it.</summary>
internal readonly struct WorldPoint : IEquatable<WorldPoint>
{
    public WorldPoint(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public float X { get; }
    public float Y { get; }
    public float Z { get; }

    /// <summary>Distance on the ground plane. Placement radii are horizontal:
    /// a companion standing three metres away and two metres up a slope is
    /// three metres away, not four.</summary>
    public float HorizontalDistanceTo(WorldPoint other)
    {
        float dx = X - other.X;
        float dz = Z - other.Z;
        return (float)Math.Sqrt((dx * dx) + (dz * dz));
    }

    public float VerticalDistanceTo(WorldPoint other)
    {
        return Math.Abs(Y - other.Y);
    }

    public WorldPoint WithHeight(float y)
    {
        return new WorldPoint(X, y, Z);
    }

    public bool Equals(WorldPoint other)
    {
        // Exact comparison: these values are carried, not computed on, and an
        // epsilon here would only hide a real mismatch.
        return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
    }

    public override bool Equals(object? obj)
    {
        return obj is WorldPoint other && Equals(other);
    }

    public override int GetHashCode()
    {
        int hash = X.GetHashCode();
        hash = (hash * 397) ^ Y.GetHashCode();
        hash = (hash * 397) ^ Z.GetHashCode();
        return hash;
    }

    public override string ToString()
    {
        return string.Format(
            CultureInfo.InvariantCulture, "({0:0.##}, {1:0.##}, {2:0.##})", X, Y, Z);
    }
}
