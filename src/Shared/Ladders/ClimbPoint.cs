using System;
using System.Globalization;

namespace TheConcernedCat.Ladders;

/// <summary>A position in world space, independent of any engine type. <c>Y</c>
/// is height, as in the host engine, so a horizontal distance ignores it.
///
/// Duplicates the shape of the settlement layer's <c>SitePoint</c> and the
/// worker layer's <c>WorkPoint</c> on purpose: shared areas are adopted
/// independently and may not reference each other.</summary>
internal readonly struct ClimbPoint : IEquatable<ClimbPoint>
{
    public ClimbPoint(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public float X { get; }

    public float Y { get; }

    public float Z { get; }

    public bool IsFinite => IsFiniteValue(X) && IsFiniteValue(Y) && IsFiniteValue(Z);

    public float HorizontalDistanceTo(ClimbPoint other)
    {
        float dx = X - other.X;
        float dz = Z - other.Z;
        return (float)Math.Sqrt((dx * dx) + (dz * dz));
    }

    public float VerticalDistanceTo(ClimbPoint other) => Math.Abs(Y - other.Y);

    public ClimbPoint WithHeight(float y) => new ClimbPoint(X, y, z: Z);

    public ClimbPoint Offset(ClimbHeading heading, float metres) =>
        new ClimbPoint(X + (heading.X * metres), Y, Z + (heading.Z * metres));

    public ClimbPoint Raise(float metres) => new ClimbPoint(X, Y + metres, Z);

    public bool Equals(ClimbPoint other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    public override bool Equals(object? obj) => obj is ClimbPoint other && Equals(other);

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

    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "({0:0.##}, {1:0.##}, {2:0.##})", X, Y, Z);

    private static bool IsFiniteValue(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

/// <summary>A horizontal direction: the compass part of a look or a facing,
/// always unit length or the explicit <see cref="None"/>. Climbing cares about
/// which way a body is turned, never about how it is pitched, because the pitch
/// is the ladder's business.</summary>
internal readonly struct ClimbHeading : IEquatable<ClimbHeading>
{
    private ClimbHeading(float x, float z)
    {
        X = x;
        Z = z;
    }

    /// <summary>No direction at all: a character standing perfectly still with
    /// no look direction, or a degenerate ladder. Every decision that needs a
    /// direction refuses on it rather than guessing one.</summary>
    public static ClimbHeading None => default;

    public float X { get; }

    public float Z { get; }

    public bool IsKnown => (X * X) + (Z * Z) > 0f;

    public static ClimbHeading FromXz(float x, float z)
    {
        float length = (float)Math.Sqrt((x * x) + (z * z));
        if (length <= 1e-4f || float.IsNaN(length) || float.IsInfinity(length))
        {
            return None;
        }

        return new ClimbHeading(x / length, z / length);
    }

    /// <summary>The direction from one point to another, ignoring height.</summary>
    public static ClimbHeading FromTo(ClimbPoint from, ClimbPoint to) =>
        FromXz(to.X - from.X, to.Z - from.Z);

    public ClimbHeading Opposite() => IsKnown ? new ClimbHeading(-X, -Z) : None;

    /// <summary>1 when the two point the same way, -1 when opposite, 0 at right
    /// angles. An unknown heading agrees with nothing.</summary>
    public float Agreement(ClimbHeading other)
    {
        if (!IsKnown || !other.IsKnown)
        {
            return 0f;
        }

        return (X * other.X) + (Z * other.Z);
    }

    public bool Equals(ClimbHeading other) => X.Equals(other.X) && Z.Equals(other.Z);

    public override bool Equals(object? obj) => obj is ClimbHeading other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return (X.GetHashCode() * 31) + Z.GetHashCode();
        }
    }

    public override string ToString() =>
        IsKnown ? string.Format(CultureInfo.InvariantCulture, "({0:0.##}, {1:0.##})", X, Z) : "(none)";
}
