using System;
using System.Globalization;

namespace TheConcernedCat.Settlement.Worker;

/// <summary>A position in world space, independent of any engine type.
///
/// The settlement layer plans movement without referencing Unity, so the game
/// adapter converts at the boundary. <c>Y</c> is height, matching the host
/// engine's convention, so a horizontal distance deliberately ignores it: a
/// worker standing three metres away and two metres up a bank is three metres
/// away, because that is the distance it has to walk.
///
/// This duplicates the shape of the companion layer's <c>WorldPoint</c> on
/// purpose. A shared area is not a framework and is not mandatory, so
/// Settlement may not reference Companions — a product is free to adopt one
/// without the other, and a type shared between them would quietly make that
/// false.</summary>
internal readonly struct SitePoint : IEquatable<SitePoint>
{
    public SitePoint(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public float X { get; }
    public float Y { get; }
    public float Z { get; }

    /// <summary>Distance on the ground plane.</summary>
    public float HorizontalDistanceTo(SitePoint other)
    {
        float dx = X - other.X;
        float dz = Z - other.Z;
        return (float)Math.Sqrt((dx * dx) + (dz * dz));
    }

    public float VerticalDistanceTo(SitePoint other)
    {
        return Math.Abs(Y - other.Y);
    }

    public bool Equals(SitePoint other)
    {
        return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
    }

    public override bool Equals(object? obj) => obj is SitePoint other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + X.GetHashCode();
            hash = (hash * 31) + Y.GetHashCode();
            hash = (hash * 31) + Z.GetHashCode();
            return hash;
        }
    }

    public override string ToString()
    {
        return string.Format(
            CultureInfo.InvariantCulture, "({0:0.##}, {1:0.##}, {2:0.##})", X, Y, Z);
    }
}
