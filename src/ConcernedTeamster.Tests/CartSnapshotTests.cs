using TheConcernedCat.ConcernedTeamster.Domain.Carts;

namespace ConcernedTeamster.Tests;

/// <summary>CT-002: the snapshot must compute total mass with the exact
/// vanilla formula, retain every adapter-supplied value unchanged, and stay
/// deterministic for odd inputs (vanilla truth first — the domain never
/// invents or clamps what the game reports).</summary>
public class CartSnapshotTests
{
    [Fact]
    public void Create_ComputesTotalMassWithTheVanillaFormula()
    {
        CartSnapshot snapshot = CartSnapshot.Create(
            "1234:5", baseMass: 20f, cargoWeight: 150f, itemWeightMassFactor: 1f,
            isAttached: true, isPulledByLocalPlayer: false);

        Assert.Equal(170f, snapshot.TotalMass);
    }

    [Fact]
    public void Create_ZeroFactor_TotalMassEqualsBaseMass()
    {
        CartSnapshot snapshot = CartSnapshot.Create(
            "1:1", baseMass: 20f, cargoWeight: 500f, itemWeightMassFactor: 0f,
            isAttached: false, isPulledByLocalPlayer: false);

        Assert.Equal(20f, snapshot.TotalMass);
    }

    [Fact]
    public void Create_RetainsEveryValueUnchanged()
    {
        CartSnapshot snapshot = CartSnapshot.Create(
            "42:7", baseMass: 25.5f, cargoWeight: 99.25f, itemWeightMassFactor: 0.5f,
            isAttached: true, isPulledByLocalPlayer: true);

        Assert.Equal("42:7", snapshot.CartId);
        Assert.Equal(25.5f, snapshot.BaseMass);
        Assert.Equal(99.25f, snapshot.CargoWeight);
        Assert.Equal(0.5f, snapshot.ItemWeightMassFactor);
        Assert.Equal(25.5f + 99.25f * 0.5f, snapshot.TotalMass);
        Assert.True(snapshot.IsAttached);
        Assert.True(snapshot.IsPulledByLocalPlayer);
    }

    [Fact]
    public void Create_NullCartId_BecomesEmptyNeverNull()
    {
        CartSnapshot snapshot = CartSnapshot.Create(
            null, baseMass: 0f, cargoWeight: 0f, itemWeightMassFactor: 0f,
            isAttached: false, isPulledByLocalPlayer: false);

        Assert.Equal(string.Empty, snapshot.CartId);
    }

    [Fact]
    public void Create_OddGameValues_PassThroughRaw()
    {
        // Vanilla truth first: a modded or broken game surface may report
        // negatives or NaN, and the snapshot must relay, not sanitize.
        CartSnapshot negative = CartSnapshot.Create(
            "1:1", baseMass: -5f, cargoWeight: -10f, itemWeightMassFactor: 2f,
            isAttached: false, isPulledByLocalPlayer: false);
        Assert.Equal(-5f, negative.BaseMass);
        Assert.Equal(-25f, negative.TotalMass);

        CartSnapshot notANumber = CartSnapshot.Create(
            "1:1", baseMass: 20f, cargoWeight: float.NaN, itemWeightMassFactor: 1f,
            isAttached: false, isPulledByLocalPlayer: false);
        Assert.True(float.IsNaN(notANumber.CargoWeight));
        Assert.True(float.IsNaN(notANumber.TotalMass));
    }
}
