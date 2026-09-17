using System;
using System.Globalization;

namespace TheConcernedCat.Workers;

/// <summary>A position in world space, independent of any engine type. <c>Y</c>
/// is height, as in the host engine, so a horizontal distance ignores it.
///
/// Duplicates the shape of the settlement layer's <c>SitePoint</c> on purpose:
/// shared areas are adopted independently and may not reference each other.
/// </summary>
internal readonly struct WorkPoint : IEquatable<WorkPoint>
{
    public WorkPoint(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public float X { get; }

    public float Y { get; }

    public float Z { get; }

    public bool IsFinite => IsFiniteValue(X) && IsFiniteValue(Y) && IsFiniteValue(Z);

    public float HorizontalDistanceTo(WorkPoint other)
    {
        float dx = X - other.X;
        float dz = Z - other.Z;
        return (float)Math.Sqrt((dx * dx) + (dz * dz));
    }

    public float VerticalDistanceTo(WorkPoint other) => Math.Abs(Y - other.Y);

    /// <summary><c>x;y;z</c> in the invariant culture, round-trippable. The
    /// form positions travel in across the capability boundary.</summary>
    public string Format()
    {
        return X.ToString("R", CultureInfo.InvariantCulture) + ";" +
            Y.ToString("R", CultureInfo.InvariantCulture) + ";" +
            Z.ToString("R", CultureInfo.InvariantCulture);
    }

    public static bool TryParse(string? text, out WorkPoint point)
    {
        point = default;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        string[] parts = text!.Split(';');
        if (parts.Length != 3 ||
            !TryParseFloat(parts[0], out float x) ||
            !TryParseFloat(parts[1], out float y) ||
            !TryParseFloat(parts[2], out float z))
        {
            return false;
        }

        point = new WorkPoint(x, y, z);
        return point.IsFinite;
    }

    public bool Equals(WorkPoint other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    public override bool Equals(object? obj) => obj is WorkPoint other && Equals(other);

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

    private static bool TryParseFloat(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && IsFiniteValue(value);

    private static bool IsFiniteValue(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
