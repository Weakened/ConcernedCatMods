using System;
using System.Globalization;

namespace TheConcernedCat.ConcernedNPC.Work;

/// <summary>A position in world space, independent of any engine type. <c>Y</c>
/// is height, as in the host engine, so a horizontal distance ignores it.
///
/// <b>What it guarantees.</b> That the contracts in this library can talk about
/// where something is without a Unity type appearing in a signature, and that
/// "how far away" means the same thing everywhere: horizontally, ignoring
/// height, because that is what every area, reach and arrival test in this
/// repository already means by distance.
///
/// <b>Why there is a fourth one of these.</b> <c>SitePoint</c>, <c>WorkPoint</c>
/// and <c>WorldPoint</c> are the same three floats, duplicated because shared
/// source areas are adopted independently and may not reference one another;
/// each carries a comment saying so. This is the fourth, and the reason is
/// different and permanent: those are <c>internal</c> in assemblies this library
/// cannot see. They collapse into one when the roles move onto this runtime, and
/// the one they collapse into is this.
///
/// <b>What is deliberately missing.</b> <c>WorkPoint</c> carries a
/// round-trippable <c>Format()</c>/<c>TryParse</c> pair, because it travels
/// across the capability boundary as text. Nothing in this library writes text,
/// so nothing here needs them; adding them now would be inventing a durable
/// format for a package whose whole safety argument is that it owns none.</summary>
public readonly struct NpcPoint : IEquatable<NpcPoint>
{
    public NpcPoint(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public float X { get; }

    public float Y { get; }

    public float Z { get; }

    /// <summary>False for a NaN or infinite component. A position nobody could
    /// compute is never silently treated as the origin.</summary>
    public bool IsFinite => IsFiniteValue(X) && IsFiniteValue(Y) && IsFiniteValue(Z);

    public float HorizontalDistanceTo(NpcPoint other)
    {
        float dx = X - other.X;
        float dz = Z - other.Z;
        return (float)Math.Sqrt((dx * dx) + (dz * dz));
    }

    public float VerticalDistanceTo(NpcPoint other) => Math.Abs(Y - other.Y);

    public bool Equals(NpcPoint other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    public override bool Equals(object? obj) => obj is NpcPoint other && Equals(other);

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
