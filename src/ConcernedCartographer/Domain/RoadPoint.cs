using System;
using System.Globalization;

namespace TheConcernedCat.ConcernedCartographer.Roads;

internal readonly struct RoadPoint
{
    public RoadPoint(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public float X { get; }
    public float Y { get; }
    public float Z { get; }

    public float HorizontalDistanceTo(in RoadPoint other)
    {
        float dx = X - other.X;
        float dz = Z - other.Z;
        return (float)Math.Sqrt((dx * dx) + (dz * dz));
    }

    public override string ToString()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "({0:0.##}, {1:0.##}, {2:0.##})",
            X,
            Y,
            Z);
    }
}
