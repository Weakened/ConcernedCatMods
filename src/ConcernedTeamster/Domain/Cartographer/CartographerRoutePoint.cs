using System.Globalization;

namespace TheConcernedCat.ConcernedTeamster.Domain.Cartographer;

/// <summary>One polyline vertex of a Cartographer route, copied out of the
/// live store into Teamster's own immutable value (CT-021). World
/// coordinates: X/Z horizontal, Y is height — the same convention the trip
/// recorder uses, so route geometry and trip samples compare directly.</summary>
public readonly struct CartographerRoutePoint
{
    public CartographerRoutePoint(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public float X { get; }

    public float Y { get; }

    public float Z { get; }

    public override string ToString()
    {
        return string.Format(
            CultureInfo.InvariantCulture, "({0:0.##}, {1:0.##}, {2:0.##})", X, Y, Z);
    }
}
