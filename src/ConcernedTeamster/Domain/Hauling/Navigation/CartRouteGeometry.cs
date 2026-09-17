using System;
using TheConcernedCat.ConcernedTeamster.Domain.Terrain;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Navigation;

/// <summary>The few fixed numbers cart navigation needs that are not tunable
/// limits: each is read from the installed game or from Teamster's own
/// definitions, so changing one would make the planner disagree with the thing
/// it was taken from. Every tunable bound lives in <see cref="HaulLimits"/>.
/// Sources are recorded in <c>docs/mods/concerned-teamster/CART_ROUTES.md</c>.
/// </summary>
internal static class CartRouteGeometry
{
    /// <summary>How close Gunnar must come to a steering target for it to count
    /// as reached: the radius the game's own path following uses to accept a
    /// corner while walking. Gunnar never runs (DECISIONS.md D5), so the
    /// running radius does not apply.</summary>
    public const float WaypointArrivalRadiusMetres = 0.5f;

    /// <summary>The highest step the chosen navmesh agent climbs. A rise or
    /// drop sharper than this plus what the grade limit allows over the same
    /// run is a ledge or a gap, not a slope; and obstacles lower than this are
    /// left to the ground checks rather than the clearance sweeps.</summary>
    public const float StepMetres = 0.3f;

    /// <summary>A stretch of ground counts as level enough to leave a loaded
    /// cart standing when neither its running grade nor its cross slope reaches
    /// the grade below which Teamster's own readout calls ground Level, whichever
    /// way it was trending.</summary>
    public static float LevelGradeRatio => GradeMath.DirectionExitThresholdPercent / 100f;

    /// <summary>Straight-line pulls are swept as one box per sample stretch;
    /// where the cart turns more than this within a stretch the box is widened
    /// to cover the turn, and a stretch never turns further before a new box
    /// starts. A geometry tolerance, not a safety margin: the widening keeps the
    /// sweep conservative either way.</summary>
    public const float MaxSweepTurnDegrees = 10f;

    /// <summary>A cart whose heading differs from the way Gunnar walks by more
    /// than a right angle is being pushed sideways or backwards by the hitch,
    /// not pulled: the turn is sharper than the cart can follow.</summary>
    public const float MaxArticulationDegrees = 90f;

    /// <summary>Steps the kinematic cart prediction takes between samples. Small
    /// against every real hitch length, so the predicted cut into a corner is
    /// accurate to a few centimetres.</summary>
    public const float PredictionStepMetres = 0.1f;

    /// <summary>How thin the slab is that measures free room to either side of
    /// the cart when a sweep is blocked.</summary>
    public const float LateralSlabHalfMetres = 0.05f;

    /// <summary>Two waypoints closer than this, flat, are the same waypoint, and
    /// a waypoint closer than this to the line through its neighbours adds
    /// nothing to a route. Numerical noise, far below any clearance.</summary>
    public const float SamePointMetres = 0.05f;

    /// <summary>How far above or below a doorway's floor a crossing may be and
    /// still pass through it rather than over or under it.</summary>
    public const float DoorwayHeightBandMetres = 1.2f;

    public static float FlatDistance(WorkPoint a, WorkPoint b) => a.HorizontalDistanceTo(b);

    public static float FlatLength(float x, float z) => (float)Math.Sqrt((x * x) + (z * z));

    public static bool IsFiniteValue(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    /// <summary>The flat unit direction from <paramref name="from"/> to
    /// <paramref name="to"/>, or false when they are the same place.</summary>
    public static bool TryFlatDirection(WorkPoint from, WorkPoint to, out float x, out float z)
    {
        float dx = to.X - from.X;
        float dz = to.Z - from.Z;
        float length = FlatLength(dx, dz);
        if (!(length > 1e-4f))
        {
            x = 0f;
            z = 0f;
            return false;
        }

        x = dx / length;
        z = dz / length;
        return true;
    }

    /// <summary>Degrees between two flat unit directions.</summary>
    public static float AngleDegrees(float ax, float az, float bx, float bz)
    {
        double dot = (ax * bx) + (az * bz);
        if (dot > 1d)
        {
            dot = 1d;
        }
        else if (dot < -1d)
        {
            dot = -1d;
        }

        return (float)(Math.Acos(dot) * 180d / Math.PI);
    }

    /// <summary>The flat distance from <paramref name="point"/> to the segment
    /// <paramref name="a"/>–<paramref name="b"/>, and how far along it (0..1)
    /// the nearest point lies.</summary>
    public static float FlatDistanceToSegment(WorkPoint point, WorkPoint a, WorkPoint b, out float along)
    {
        float abx = b.X - a.X;
        float abz = b.Z - a.Z;
        float lengthSquared = (abx * abx) + (abz * abz);
        if (!(lengthSquared > 1e-8f))
        {
            along = 0f;
            return FlatDistance(point, a);
        }

        float t = (((point.X - a.X) * abx) + ((point.Z - a.Z) * abz)) / lengthSquared;
        if (t < 0f)
        {
            t = 0f;
        }
        else if (t > 1f)
        {
            t = 1f;
        }

        along = t;
        float nx = a.X + (abx * t);
        float nz = a.Z + (abz * t);
        return FlatLength(point.X - nx, point.Z - nz);
    }

    public static WorkPoint Lerp(WorkPoint a, WorkPoint b, float t)
    {
        return new WorkPoint(
            a.X + ((b.X - a.X) * t),
            a.Y + ((b.Y - a.Y) * t),
            a.Z + ((b.Z - a.Z) * t));
    }

    public static WorkPoint Offset(WorkPoint point, float x, float z, float metres)
    {
        return new WorkPoint(point.X + (x * metres), point.Y, point.Z + (z * metres));
    }

    public static WorkPoint WithHeight(WorkPoint point, float height) => new WorkPoint(point.X, height, point.Z);
}
