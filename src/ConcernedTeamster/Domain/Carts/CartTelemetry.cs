namespace TheConcernedCat.ConcernedTeamster.Domain.Carts;

/// <summary>Immutable telemetry for one cart at one sample instant (CT-003).
/// Composes the adapter's <see cref="CartSnapshot"/> with motion data and a
/// timestamp. Fields the adapter could not obtain are explicitly flagged
/// unavailable — consumers must check the flag instead of trusting a
/// defaulted number.</summary>
public sealed class CartTelemetry
{
    private CartTelemetry(
        string cartId,
        float baseMass,
        float cargoWeight,
        bool cargoDataAvailable,
        float itemWeightMassFactor,
        float totalMass,
        bool isAttached,
        bool isPulledByLocalPlayer,
        bool velocityAvailable,
        float speedMetersPerSecond,
        float verticalSpeedMetersPerSecond,
        double sampleTimeSeconds)
    {
        CartId = cartId;
        BaseMass = baseMass;
        CargoWeight = cargoWeight;
        CargoDataAvailable = cargoDataAvailable;
        ItemWeightMassFactor = itemWeightMassFactor;
        TotalMass = totalMass;
        IsAttached = isAttached;
        IsPulledByLocalPlayer = isPulledByLocalPlayer;
        VelocityAvailable = velocityAvailable;
        SpeedMetersPerSecond = speedMetersPerSecond;
        VerticalSpeedMetersPerSecond = verticalSpeedMetersPerSecond;
        SampleTimeSeconds = sampleTimeSeconds;
    }

    /// <summary>Network-stable cart identity from the snapshot.</summary>
    public string CartId { get; }

    public float BaseMass { get; }

    /// <summary>Meaningful only when <see cref="CargoDataAvailable"/>.</summary>
    public float CargoWeight { get; }

    /// <summary>False when the cart exposed no cargo container; then
    /// <see cref="CargoWeight"/> is 0 and <see cref="TotalMass"/> equals
    /// <see cref="BaseMass"/> by construction, not by measurement.</summary>
    public bool CargoDataAvailable { get; }

    public float ItemWeightMassFactor { get; }

    public float TotalMass { get; }

    public bool IsAttached { get; }

    public bool IsPulledByLocalPlayer { get; }

    /// <summary>False when the cart exposed no readable rigidbody; then both
    /// speed values are 0 and mean "unknown", not "standing still".</summary>
    public bool VelocityAvailable { get; }

    /// <summary>Velocity magnitude. Meaningful only when
    /// <see cref="VelocityAvailable"/>.</summary>
    public float SpeedMetersPerSecond { get; }

    /// <summary>Signed vertical (up-positive) velocity component for later
    /// climb/descent modeling. Meaningful only when
    /// <see cref="VelocityAvailable"/>.</summary>
    public float VerticalSpeedMetersPerSecond { get; }

    /// <summary>The sampler clock (seconds) at sampling time; used for
    /// staleness eviction and panel freshness.</summary>
    public double SampleTimeSeconds { get; }

    public static CartTelemetry Create(
        CartSnapshot snapshot,
        bool velocityAvailable,
        float speedMetersPerSecond,
        float verticalSpeedMetersPerSecond,
        double sampleTimeSeconds)
    {
        return new CartTelemetry(
            snapshot.CartId,
            snapshot.BaseMass,
            snapshot.CargoWeight,
            snapshot.CargoDataAvailable,
            snapshot.ItemWeightMassFactor,
            snapshot.TotalMass,
            snapshot.IsAttached,
            snapshot.IsPulledByLocalPlayer,
            velocityAvailable,
            velocityAvailable ? speedMetersPerSecond : 0f,
            velocityAvailable ? verticalSpeedMetersPerSecond : 0f,
            sampleTimeSeconds);
    }
}
