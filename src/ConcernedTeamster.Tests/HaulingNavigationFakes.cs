using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Navigation;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>A flat rectangle of ground in the synthetic world.</summary>
internal readonly record struct FakeArea(float MinX, float MaxX, float MinZ, float MaxZ)
{
    public bool Contains(float x, float z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
}

/// <summary>Something solid: an axis-aligned box or an upright cylinder.</summary>
internal sealed class FakeObstacle
{
    private FakeObstacle(string name, bool round, float x, float z, float halfX, float halfZ, float radius)
    {
        Name = name;
        Round = round;
        X = x;
        Z = z;
        HalfX = halfX;
        HalfZ = halfZ;
        Radius = radius;
    }

    public string Name { get; }

    public bool Round { get; }

    public float X { get; }

    public float Z { get; }

    public float HalfX { get; }

    public float HalfZ { get; }

    public float Radius { get; }

    public static FakeObstacle Box(string name, float minX, float maxX, float minZ, float maxZ) =>
        new FakeObstacle(name, false, (minX + maxX) / 2f, (minZ + maxZ) / 2f, (maxX - minX) / 2f, (maxZ - minZ) / 2f, 0f);

    public static FakeObstacle Pillar(string name, float x, float z, float radius) =>
        new FakeObstacle(name, true, x, z, 0f, 0f, radius);

    /// <summary>Whether this overlaps the oriented rectangle centred at
    /// (<paramref name="cx"/>, <paramref name="cz"/>) with its length along
    /// (<paramref name="hx"/>, <paramref name="hz"/>).</summary>
    public bool Overlaps(float cx, float cz, float hx, float hz, float halfWidth, float halfLength)
    {
        float rightX = hz;
        float rightZ = -hx;
        if (Round)
        {
            float dx = X - cx;
            float dz = Z - cz;
            float lateral = (dx * rightX) + (dz * rightZ);
            float along = (dx * hx) + (dz * hz);
            float clampedLateral = Math.Clamp(lateral, -halfWidth, halfWidth);
            float clampedAlong = Math.Clamp(along, -halfLength, halfLength);
            float ox = lateral - clampedLateral;
            float oz = along - clampedAlong;
            return (ox * ox) + (oz * oz) <= Radius * Radius;
        }

        // Separating axes: the box's two world axes and the rectangle's two.
        return !Separated(1f, 0f, cx, cz, hx, hz, halfWidth, halfLength) &&
            !Separated(0f, 1f, cx, cz, hx, hz, halfWidth, halfLength) &&
            !Separated(hx, hz, cx, cz, hx, hz, halfWidth, halfLength) &&
            !Separated(rightX, rightZ, cx, cz, hx, hz, halfWidth, halfLength);
    }

    private bool Separated(float ax, float az, float cx, float cz, float hx, float hz, float halfWidth, float halfLength)
    {
        float rightX = hz;
        float rightZ = -hx;
        float boxRadius = (HalfX * Math.Abs(ax)) + (HalfZ * Math.Abs(az));
        float rectRadius = (halfLength * Math.Abs((hx * ax) + (hz * az))) +
            (halfWidth * Math.Abs((rightX * ax) + (rightZ * az)));
        float distance = Math.Abs(((X - cx) * ax) + ((Z - cz) * az));
        return distance > boxRadius + rectRadius;
    }
}

/// <summary>The synthetic world the navigation tests run in. Every answer is
/// computed from simple shapes, every question is counted, and faults can be
/// injected.</summary>
internal sealed class FakeCartWorld : ICartTerrainProbe
{
    public Func<float, float, float> Height { get; set; } = (x, z) => 0f;

    public List<FakeArea> Unloaded { get; } = new List<FakeArea>();

    public List<FakeArea> Unreadable { get; } = new List<FakeArea>();

    public List<FakeArea> Holes { get; } = new List<FakeArea>();

    public List<FakeArea> Lava { get; } = new List<FakeArea>();

    public List<(FakeArea Area, float Level)> Water { get; } = new List<(FakeArea, float)>();

    public List<FakeObstacle> Obstacles { get; } = new List<FakeObstacle>();

    public List<CartDoorway> Doorways { get; } = new List<CartDoorway>();

    public bool DoorsUnreadable { get; set; }

    public bool ThrowOnGround { get; set; }

    public bool ThrowOnSweep { get; set; }

    public int LoadedChecks { get; private set; }

    public int GroundSamples { get; private set; }

    public int Sweeps { get; private set; }

    public int BoxChecks { get; private set; }

    public int DoorScans { get; private set; }

    public bool IsLoaded(WorkPoint point)
    {
        LoadedChecks++;
        return !Unloaded.Any(area => area.Contains(point.X, point.Z));
    }

    public CartGroundSample SampleGround(float x, float z, float nearHeight)
    {
        GroundSamples++;
        if (ThrowOnGround)
        {
            throw new InvalidOperationException("injected ground probe failure");
        }

        if (Unloaded.Any(area => area.Contains(x, z)))
        {
            return CartGroundSample.Missing(CartGroundStatus.Unloaded);
        }

        if (Unreadable.Any(area => area.Contains(x, z)))
        {
            return CartGroundSample.Missing(CartGroundStatus.Unreadable);
        }

        float height = Height(x, z);
        float depth = 0f;
        foreach ((FakeArea area, float level) in Water)
        {
            if (area.Contains(x, z))
            {
                depth = Math.Max(depth, level - height);
            }
        }

        if (Holes.Any(area => area.Contains(x, z)))
        {
            return new CartGroundSample(CartGroundStatus.NoSurface, 0f, 0f, depth, false);
        }

        return new CartGroundSample(
            CartGroundStatus.Surface, height, 1f, depth, Lava.Any(area => area.Contains(x, z)));
    }

    public CartClearanceSample SweepBox(
        WorkPoint from, WorkPoint to, float headingX, float headingZ, float halfWidth, float halfLength,
        bool ignoreStartOverlaps)
    {
        Sweeps++;
        if (ThrowOnSweep)
        {
            throw new InvalidOperationException("injected sweep failure");
        }

        float dx = to.X - from.X;
        float dy = to.Y - from.Y;
        float dz = to.Z - from.Z;
        float length = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        int steps = Math.Max(1, (int)MathF.Ceiling(length / 0.02f));
        var ignored = new HashSet<FakeObstacle>();
        for (int step = 0; step <= steps; step++)
        {
            float t = (float)step / steps;
            float cx = from.X + (dx * t);
            float cz = from.Z + (dz * t);
            foreach (FakeObstacle obstacle in Obstacles)
            {
                if (ignored.Contains(obstacle) || !obstacle.Overlaps(cx, cz, headingX, headingZ, halfWidth, halfLength))
                {
                    continue;
                }

                if (step == 0 && ignoreStartOverlaps)
                {
                    ignored.Add(obstacle);
                    continue;
                }

                return CartClearanceSample.BlockedAt(length * t, obstacle.Name);
            }
        }

        return CartClearanceSample.Clear;
    }

    public CartClearanceSample CheckBox(WorkPoint centre, float headingX, float headingZ, float halfWidth, float halfLength)
    {
        BoxChecks++;
        foreach (FakeObstacle obstacle in Obstacles)
        {
            if (obstacle.Overlaps(centre.X, centre.Z, headingX, headingZ, halfWidth, halfLength))
            {
                return CartClearanceSample.BlockedAt(0f, obstacle.Name);
            }
        }

        return CartClearanceSample.Clear;
    }

    public bool TryFindDoorways(WorkPoint centre, float radius, List<CartDoorway> doorways)
    {
        DoorScans++;
        doorways.Clear();
        if (DoorsUnreadable)
        {
            return false;
        }

        doorways.AddRange(Doorways.Where(door => door.FloorCentre.HorizontalDistanceTo(centre) <= radius));
        return true;
    }
}

/// <summary>A navmesh that answers what the test says: a straight line by
/// default, or scripted corners.</summary>
internal sealed class FakePathSource : ICartPathSource
{
    public List<WorkPoint>? Corners { get; set; }

    public CartPathStatus Status { get; set; } = CartPathStatus.Found;

    public bool Throw { get; set; }

    public int Calls { get; private set; }

    public CartPathStatus FindPath(WorkPoint from, WorkPoint to, List<WorkPoint> corners)
    {
        Calls++;
        corners.Clear();
        if (Throw)
        {
            throw new InvalidOperationException("injected navmesh failure");
        }

        if (Status != CartPathStatus.Found)
        {
            return Status;
        }

        if (Corners != null)
        {
            corners.AddRange(Corners);
        }
        else
        {
            corners.Add(from);
            corners.Add(to);
        }

        return CartPathStatus.Found;
    }
}

internal static class NavigationFixtures
{
    /// <summary>The vanilla cart as read from the installed prefab: wheel faces
    /// 1.72 m apart, handle tip to tail 3.25 m, hitch to axle 2.21 m.</summary>
    public static CartFootprint VanillaCart => new CartFootprint(1.72f, 3.25f, 2.21f);

    public static WorkPoint P(float x, float z, float y = 0f) => new WorkPoint(x, y, z);

    public static CartRouteRequest Request(WorkPoint from, WorkPoint to, float mass = 70f, int revision = 7) =>
        new CartRouteRequest(from, to, VanillaCart, mass, revision);

    public static (CartRoutePlanner Planner, FakeCartWorld World, FakePathSource Paths) Planner(
        HaulLimits? limits = null, CartRouteFailureCache? failures = null)
    {
        var world = new FakeCartWorld();
        var paths = new FakePathSource();
        return (new CartRoutePlanner(limits ?? HaulLimits.Default, paths, world, failures), world, paths);
    }
}
