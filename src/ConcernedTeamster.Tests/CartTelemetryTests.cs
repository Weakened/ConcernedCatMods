using TheConcernedCat.ConcernedTeamster.Domain.Carts;

namespace ConcernedTeamster.Tests;

/// <summary>CT-003: telemetry composes the snapshot faithfully, stamps the
/// sample time, and treats unavailable velocity as "unknown", zeroing the
/// motion values so a stale nonzero speed can never masquerade as data.</summary>
public class CartTelemetryTests
{
    private static CartSnapshot Snapshot(string id = "7:42")
    {
        return CartSnapshot.Create(
            id, baseMass: 20f, cargoWeight: 60f, cargoDataAvailable: true,
            itemWeightMassFactor: 1f, isAttached: true, isPulledByLocalPlayer: true);
    }

    [Fact]
    public void Create_CopiesEverySnapshotFieldAndStampsTime()
    {
        CartTelemetry telemetry = CartTelemetry.Create(
            Snapshot(), velocityAvailable: true,
            speedMetersPerSecond: 3.5f, verticalSpeedMetersPerSecond: -0.25f,
            sampleTimeSeconds: 123.75d);

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
        Assert.Equal(123.75d, telemetry.SampleTimeSeconds);
    }

    [Fact]
    public void Create_VelocityUnavailable_ZeroesMotionAndFlags()
    {
        CartTelemetry telemetry = CartTelemetry.Create(
            Snapshot(), velocityAvailable: false,
            speedMetersPerSecond: 9f, verticalSpeedMetersPerSecond: 9f,
            sampleTimeSeconds: 1d);

        Assert.False(telemetry.VelocityAvailable);
        Assert.Equal(0f, telemetry.SpeedMetersPerSecond);
        Assert.Equal(0f, telemetry.VerticalSpeedMetersPerSecond);
    }

    [Fact]
    public void Create_CargoUnavailableSnapshot_PropagatesTheFlag()
    {
        CartSnapshot bare = CartSnapshot.Create(
            "1:1", baseMass: 20f, cargoWeight: 0f, cargoDataAvailable: false,
            itemWeightMassFactor: 1f, isAttached: false, isPulledByLocalPlayer: false);

        CartTelemetry telemetry = CartTelemetry.Create(
            bare, velocityAvailable: true, speedMetersPerSecond: 0f,
            verticalSpeedMetersPerSecond: 0f, sampleTimeSeconds: 0d);

        Assert.False(telemetry.CargoDataAvailable);
        Assert.Equal(20f, telemetry.TotalMass);
    }
}
