using System;
using TheConcernedCat.ConcernedTeamster.Domain.Carts;
using TheConcernedCat.ConcernedTeamster.Domain.Net;

namespace ConcernedTeamster.Tests;

/// <summary>CT-029: every network-derived numeric is bounded to a finite,
/// in-range value with at most one log per key; remote readings go stale on
/// schedule; and the snapshot that consumes these values can never produce a
/// non-finite or negative mass no matter how hostile the input. Includes a
/// seeded fuzz sweep so a randomized adversary cannot find a throw or a
/// non-finite escape.</summary>
public class NetworkInputHardeningTests
{
    // -- adversarial bounds matrix --

    public static IEnumerable<object[]> HostileFloats()
    {
        foreach (float bad in new[]
        {
            float.NaN, float.PositiveInfinity, float.NegativeInfinity,
            -1f, -1e30f, 1e30f, float.MaxValue, float.MinValue, -0f,
        })
        {
            yield return new object[] { bad };
        }
    }

    [Theory]
    [MemberData(nameof(HostileFloats))]
    public void Mass_AnyHostileInput_IsFiniteNonNegativeAndCapped(float bad)
    {
        NetworkInputGuard.Sanitized result = NetworkInputGuard.Mass(bad);

        Assert.False(float.IsNaN(result.Value));
        Assert.False(float.IsInfinity(result.Value));
        Assert.InRange(result.Value, 0f, NetworkInputGuard.MaxMass);
    }

    [Theory]
    [MemberData(nameof(HostileFloats))]
    public void Speed_AnyHostileInput_IsFiniteAndWithinSignedCap(float bad)
    {
        NetworkInputGuard.Sanitized result = NetworkInputGuard.Speed(bad);

        Assert.False(float.IsNaN(result.Value));
        Assert.False(float.IsInfinity(result.Value));
        Assert.InRange(result.Value, -NetworkInputGuard.MaxSpeed, NetworkInputGuard.MaxSpeed);
    }

    [Fact]
    public void NonNegative_MapsNaNAndNegativeToZeroAndFlags()
    {
        Assert.True(NetworkInputGuard.NonNegative(float.NaN, 100f).Adjusted);
        Assert.Equal(0f, NetworkInputGuard.NonNegative(float.NaN, 100f).Value);
        Assert.Equal(0f, NetworkInputGuard.NonNegative(-5f, 100f).Value);
        Assert.True(NetworkInputGuard.NonNegative(-5f, 100f).Adjusted);
    }

    [Fact]
    public void NonNegative_ClampsOverCapAndFlags()
    {
        NetworkInputGuard.Sanitized r = NetworkInputGuard.NonNegative(250f, 100f);
        Assert.Equal(100f, r.Value);
        Assert.True(r.Adjusted);
    }

    [Fact]
    public void Signed_ClampsBothEndsMapsNonFiniteToZero()
    {
        Assert.Equal(50f, NetworkInputGuard.Signed(999f, 50f).Value);
        Assert.Equal(-50f, NetworkInputGuard.Signed(-999f, 50f).Value);
        Assert.Equal(0f, NetworkInputGuard.Signed(float.PositiveInfinity, 50f).Value);
    }

    [Fact]
    public void LegitimateValues_PassUnchangedAndUnflagged()
    {
        NetworkInputGuard.Sanitized mass = NetworkInputGuard.Mass(320f);
        Assert.Equal(320f, mass.Value);
        Assert.False(mass.Adjusted);

        NetworkInputGuard.Sanitized grade = NetworkInputGuard.Grade(-12.5f);
        Assert.Equal(-12.5f, grade.Value);
        Assert.False(grade.Adjusted);
    }

    // -- label bounding (hostile names) --

    [Fact]
    public void Label_TrimsCapsAndStripsControlChars()
    {
        Assert.Equal("Ana", NetworkInputGuard.Label("  Ana  "));
        Assert.Equal(string.Empty, NetworkInputGuard.Label(null));
        Assert.Equal(10, NetworkInputGuard.Label(new string('x', 500), maxLength: 10).Length);
        Assert.DoesNotContain('\n', NetworkInputGuard.Label("line\nbreak"));
        Assert.DoesNotContain('\t', NetworkInputGuard.Label("tab\there"));
    }

    // -- single-shot logging gate --

    [Fact]
    public void Gate_LogsOncePerKeyThenSuppresses()
    {
        var gate = new OncePerKeyGate();
        Assert.True(gate.ShouldLog("cart-7:mass"));
        Assert.False(gate.ShouldLog("cart-7:mass"));
        Assert.False(gate.ShouldLog("cart-7:mass"));
        Assert.True(gate.ShouldLog("cart-7:speed"));
        Assert.Equal(2, gate.Count);
    }

    [Fact]
    public void Gate_ResetOnWorldSwitch_ClearsSlate()
    {
        var gate = new OncePerKeyGate();
        gate.ShouldLog("k");
        gate.Reset();
        Assert.Equal(0, gate.Count);
        Assert.True(gate.ShouldLog("k"));
    }

    [Fact]
    public void Gate_IsBounded_NeverGrowsPastCapacity()
    {
        var gate = new OncePerKeyGate(capacity: 4);
        for (int i = 0; i < 100; i++)
        {
            gate.ShouldLog("key-" + i.ToString());
        }

        Assert.Equal(4, gate.Count);
        // A brand-new key past the cap is suppressed, not admitted.
        Assert.False(gate.ShouldLog("key-brand-new"));
    }

    // -- staleness policy --

    [Theory]
    [InlineData(0.0, 3.0, false)]   // 3 s old < 5 s threshold → fresh
    [InlineData(0.0, 5.0, true)]    // exactly the threshold → stale
    [InlineData(0.0, 9.0, true)]    // well past → stale
    [InlineData(10.0, 9.0, false)]  // backwards (clock skew) → fresh, not stale
    public void Staleness_ThresholdBehavior(double sampledAt, double now, bool expectStale)
    {
        Assert.Equal(expectStale, RemoteStalenessPolicy.IsStale(sampledAt, now));
    }

    [Fact]
    public void Staleness_NonFiniteAge_IsStaleFailClosed()
    {
        Assert.True(RemoteStalenessPolicy.IsStale(double.NaN, 0.0));
        Assert.True(RemoteStalenessPolicy.IsStale(double.NegativeInfinity, 0.0));
    }

    [Fact]
    public void Staleness_LocalAuthority_NeverStale()
    {
        Assert.False(RemoteStalenessPolicy.IsStaleForRemote(
            isLocalAuthority: true, sampledAt: 0.0, now: 1000.0));
        Assert.True(RemoteStalenessPolicy.IsStaleForRemote(
            isLocalAuthority: false, sampledAt: 0.0, now: 1000.0));
    }

    // -- snapshot hardening: garbage in, finite bounded snapshot out --

    [Theory]
    [MemberData(nameof(HostileFloats))]
    public void Snapshot_HostileMassFields_ProduceFiniteBoundedTotalMass(float bad)
    {
        CartSnapshot snapshot = CartSnapshot.Create(
            "9:9", baseMass: bad, cargoWeight: bad, cargoDataAvailable: true,
            itemWeightMassFactor: bad, isAttached: false, isPulledByLocalPlayer: false);

        Assert.False(float.IsNaN(snapshot.TotalMass));
        Assert.False(float.IsInfinity(snapshot.TotalMass));
        Assert.True(snapshot.TotalMass >= 0f);
        Assert.False(float.IsNaN(snapshot.BaseMass));
        Assert.True(snapshot.BaseMass >= 0f);
        Assert.True(snapshot.CargoWeight >= 0f);
    }

    [Fact]
    public void Snapshot_LegitimateValues_Unchanged()
    {
        CartSnapshot snapshot = CartSnapshot.Create(
            "1:1", baseMass: 40f, cargoWeight: 300f, cargoDataAvailable: true,
            itemWeightMassFactor: 1f, isAttached: true, isPulledByLocalPlayer: true);

        Assert.Equal(40f, snapshot.BaseMass);
        Assert.Equal(300f, snapshot.CargoWeight);
        Assert.Equal(340f, snapshot.TotalMass);
    }

    // -- seeded fuzz sweep: no throw, no non-finite escape --

    [Fact]
    public void Fuzz_10000HostileInputs_NeverThrowNeverEscapeBounds()
    {
        // Fixed seed → deterministic, reproducible. Draws from a hostile
        // distribution (specials + extreme magnitudes + ordinary values).
        var random = new Random(20260905);
        const int iterations = 10_000;

        for (int i = 0; i < iterations; i++)
        {
            float raw = NextHostile(random);

            NetworkInputGuard.Sanitized mass = NetworkInputGuard.Mass(raw);
            Assert.False(float.IsNaN(mass.Value) || float.IsInfinity(mass.Value));
            Assert.InRange(mass.Value, 0f, NetworkInputGuard.MaxMass);

            NetworkInputGuard.Sanitized speed = NetworkInputGuard.Speed(raw);
            Assert.InRange(speed.Value, -NetworkInputGuard.MaxSpeed, NetworkInputGuard.MaxSpeed);

            NetworkInputGuard.Sanitized grade = NetworkInputGuard.Grade(raw);
            Assert.InRange(grade.Value, -NetworkInputGuard.MaxGradePercent, NetworkInputGuard.MaxGradePercent);

            // The consuming snapshot never yields a bad TotalMass either.
            CartSnapshot snapshot = CartSnapshot.Create(
                "f:" + i.ToString(), raw, raw, true, raw, false, false);
            Assert.False(float.IsNaN(snapshot.TotalMass) || float.IsInfinity(snapshot.TotalMass));
            Assert.True(snapshot.TotalMass >= 0f);
        }
    }

    private static float NextHostile(Random random)
    {
        switch (random.Next(8))
        {
            case 0: return float.NaN;
            case 1: return float.PositiveInfinity;
            case 2: return float.NegativeInfinity;
            case 3: return (float)((random.NextDouble() - 0.5) * 2e38);   // extreme magnitude
            case 4: return -(float)(random.NextDouble() * 1e6);           // negative
            case 5: return (float)(random.NextDouble() * 5000.0);         // plausible cargo
            case 6: return 0f;
            default: return (float)((random.NextDouble() - 0.5) * 200.0); // plausible grade/speed
        }
    }
}
