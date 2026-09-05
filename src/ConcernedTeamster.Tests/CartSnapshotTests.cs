using TheConcernedCat.ConcernedTeamster.Domain.Carts;

namespace ConcernedTeamster.Tests;

/// <summary>CT-002/CT-003: the snapshot must compute total mass with the
/// exact vanilla formula, retain every adapter-supplied value unchanged, and
/// flag unobtainable cargo data instead of defaulting it silently. Vanilla
/// truth first — the domain never invents or clamps what the game reports.</summary>
public class CartSnapshotTests
{
    [Fact]
    public void Create_ComputesTotalMassWithTheVanillaFormula()
    {
        CartSnapshot snapshot = CartSnapshot.Create(
            "1234:5", baseMass: 20f, cargoWeight: 150f, cargoDataAvailable: true,
            itemWeightMassFactor: 1f, isAttached: true, isPulledByLocalPlayer: false);

        Assert.Equal(170f, snapshot.TotalMass);
    }

    [Fact]
    public void Create_ZeroFactor_TotalMassEqualsBaseMass()
    {
        CartSnapshot snapshot = CartSnapshot.Create(
            "1:1", baseMass: 20f, cargoWeight: 500f, cargoDataAvailable: true,
            itemWeightMassFactor: 0f, isAttached: false, isPulledByLocalPlayer: false);

        Assert.Equal(20f, snapshot.TotalMass);
    }

    [Fact]
    public void Create_RetainsEveryValueUnchanged()
    {
        CartSnapshot snapshot = CartSnapshot.Create(
            "42:7", baseMass: 25.5f, cargoWeight: 99.25f, cargoDataAvailable: true,
            itemWeightMassFactor: 0.5f, isAttached: true, isPulledByLocalPlayer: true);

        Assert.Equal("42:7", snapshot.CartId);
        Assert.Equal(25.5f, snapshot.BaseMass);
        Assert.Equal(99.25f, snapshot.CargoWeight);
        Assert.True(snapshot.CargoDataAvailable);
        Assert.Equal(0.5f, snapshot.ItemWeightMassFactor);
        Assert.Equal(25.5f + 99.25f * 0.5f, snapshot.TotalMass);
        Assert.True(snapshot.IsAttached);
        Assert.True(snapshot.IsPulledByLocalPlayer);
    }

    [Fact]
    public void Create_CargoUnavailable_FlagsAndZeroesInsteadOfTrustingInput()
    {
        // The adapter passes cargoWeight 0 when no container exists, but even
        // a nonzero value must not leak through with the flag down: the flag
        // is the truth marker (CT-003 acceptance: unobtainable is marked
        // unavailable, never defaulted into a plausible number).
        CartSnapshot snapshot = CartSnapshot.Create(
            "1:1", baseMass: 20f, cargoWeight: 123f, cargoDataAvailable: false,
            itemWeightMassFactor: 1f, isAttached: false, isPulledByLocalPlayer: false);

        Assert.False(snapshot.CargoDataAvailable);
        Assert.Equal(0f, snapshot.CargoWeight);
        Assert.Equal(20f, snapshot.TotalMass);
    }

    [Fact]
    public void Create_NullCartId_BecomesEmptyNeverNull()
    {
        CartSnapshot snapshot = CartSnapshot.Create(
            null, baseMass: 0f, cargoWeight: 0f, cargoDataAvailable: true,
            itemWeightMassFactor: 0f, isAttached: false, isPulledByLocalPlayer: false);

        Assert.Equal(string.Empty, snapshot.CartId);
    }

    [Fact]
    public void Create_OddButValidValues_PassThroughRaw()
    {
        // Vanilla truth first: an unusual but FINITE, non-negative reading
        // (a modded heavy cart, an odd multiplier) relays unchanged, so the
        // panel can explain the real number.
        CartSnapshot odd = CartSnapshot.Create(
            "1:1", baseMass: 99999f, cargoWeight: 12345f, cargoDataAvailable: true,
            itemWeightMassFactor: 3.5f, isAttached: false, isPulledByLocalPlayer: false);
        Assert.Equal(99999f, odd.BaseMass);
        Assert.Equal(12345f, odd.CargoWeight);
        Assert.Equal(99999f + 12345f * 3.5f, odd.TotalMass);
    }

    [Fact]
    public void Create_ImpossibleNetworkValues_AreBounded()
    {
        // CT-029: on a remote-owned cart these fields are network-derived, so
        // impossible values (negative, NaN, infinite) are corruption, not
        // vanilla truth — they are bounded to a finite, non-negative,
        // never-NaN mass rather than relayed. This deliberately supersedes
        // the old relay-everything behavior for impossible values only.
        CartSnapshot negative = CartSnapshot.Create(
            "1:1", baseMass: -5f, cargoWeight: -10f, cargoDataAvailable: true,
            itemWeightMassFactor: 2f, isAttached: false, isPulledByLocalPlayer: false);
        Assert.Equal(0f, negative.BaseMass);
        Assert.True(negative.TotalMass >= 0f);
        Assert.False(float.IsNaN(negative.TotalMass));

        CartSnapshot notANumber = CartSnapshot.Create(
            "1:1", baseMass: 20f, cargoWeight: float.NaN, cargoDataAvailable: true,
            itemWeightMassFactor: 1f, isAttached: false, isPulledByLocalPlayer: false);
        Assert.False(float.IsNaN(notANumber.CargoWeight));
        Assert.False(float.IsNaN(notANumber.TotalMass));
        Assert.Equal(20f, notANumber.TotalMass); // 20 base + 0 (NaN cargo → 0)
    }
}
