using TheConcernedCat.ConcernedTeamster.Domain.Carts;
using TheConcernedCat.ConcernedTeamster.Domain.Terrain;

namespace ConcernedTeamster.Tests;

/// <summary>CT-003/CT-004: telemetry composes the snapshot faithfully, stamps
/// the sample time, and zeroes-and-flags unavailable velocity and grade so a
/// stale nonzero number can never masquerade as data.</summary>
public class CartTelemetryTests
{
    private static CartSnapshot Snapshot(string id = "7:42")
    {
        return CartSnapshot.Create(
            id, baseMass: 20f, cargoWeight: 60f, cargoDataAvailable: true,
            itemWeightMassFactor: 1f, isAttached: true, isPulledByLocalPlayer: true);
    }

    private static CartTelemetry Create(
        CartSnapshot snapshot,
        bool velocityAvailable = true,
        float speed = 0f,
        float verticalSpeed = 0f,
        bool gradeAvailable = true,
        float instantGrade = 0f,
        float smoothedGrade = 0f,
        GradeDirection direction = GradeDirection.Level,
        TerrainSurfaceKind surface = TerrainSurfaceKind.Untouched,
        double sampleTime = 0d)
    {
        return CartTelemetry.Create(
            snapshot, velocityAvailable, speed, verticalSpeed,
            gradeAvailable, instantGrade, smoothedGrade, direction, surface, sampleTime);
    }

    [Fact]
    public void Create_CopiesEverySnapshotFieldAndStampsTime()
    {
        CartTelemetry telemetry = Create(
            Snapshot(), speed: 3.5f, verticalSpeed: -0.25f,
            instantGrade: 8f, smoothedGrade: 7.5f, direction: GradeDirection.Climbing,
            surface: TerrainSurfaceKind.Paved, sampleTime: 123.75d);

        Assert.Equal("7:42", telemetry.CartId);
        Assert.Equal(20f, telemetry.BaseMass);
        Assert.Equal(60f, telemetry.CargoWeight);
        Assert.True(telemetry.CargoDataAvailable);
        Assert.Equal(1f, telemetry.ItemWeightMassFactor);
        Assert.Equal(80f, telemetry.TotalMass);
        Assert.True(telemetry.IsAttached);
        Assert.True(telemetry.IsPulledByLocalPlayer);
        Assert.True(telemetry.VelocityAvailable);
        Assert.Equal(3.5f, telemetry.SpeedMetersPerSecond);
        Assert.Equal(-0.25f, telemetry.VerticalSpeedMetersPerSecond);
        Assert.True(telemetry.GradeAvailable);
        Assert.Equal(8f, telemetry.InstantGradePercent);
        Assert.Equal(7.5f, telemetry.SmoothedGradePercent);
        Assert.Equal(GradeDirection.Climbing, telemetry.GradeDirection);
        Assert.Equal(TerrainSurfaceKind.Paved, telemetry.Surface);
        Assert.Equal(123.75d, telemetry.SampleTimeSeconds);
    }

    [Fact]
    public void Create_VelocityUnavailable_ZeroesMotionAndFlags()
    {
        CartTelemetry telemetry = Create(
            Snapshot(), velocityAvailable: false, speed: 9f, verticalSpeed: 9f);

        Assert.False(telemetry.VelocityAvailable);
        Assert.Equal(0f, telemetry.SpeedMetersPerSecond);
        Assert.Equal(0f, telemetry.VerticalSpeedMetersPerSecond);
    }

    [Fact]
    public void Create_GradeUnavailable_ZeroesGradeAndReportsLevel()
    {
        CartTelemetry telemetry = Create(
            Snapshot(), gradeAvailable: false,
            instantGrade: float.NaN, smoothedGrade: 12f, direction: GradeDirection.Descending,
            surface: TerrainSurfaceKind.Dirt);

        Assert.False(telemetry.GradeAvailable);
        Assert.Equal(0f, telemetry.InstantGradePercent);
        Assert.Equal(0f, telemetry.SmoothedGradePercent);
        Assert.Equal(GradeDirection.Level, telemetry.GradeDirection);
        // Surface is independent of grade availability.
        Assert.Equal(TerrainSurfaceKind.Dirt, telemetry.Surface);
    }

    [Fact]
    public void Create_CargoUnavailableSnapshot_PropagatesTheFlag()
    {
        CartSnapshot bare = CartSnapshot.Create(
            "1:1", baseMass: 20f, cargoWeight: 0f, cargoDataAvailable: false,
            itemWeightMassFactor: 1f, isAttached: false, isPulledByLocalPlayer: false);

        CartTelemetry telemetry = Create(bare);

        Assert.False(telemetry.CargoDataAvailable);
        Assert.Equal(20f, telemetry.TotalMass);
    }
}
