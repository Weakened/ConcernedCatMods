using System;
using TheConcernedCat.ConcernedLadders.Domain.Animation;

namespace TheConcernedCat.ConcernedLadders.Tests.Animation;

/// <summary>Owner references and spec 2026-10-26/27: independent limb windows, real-rung contacts, a distinct descent,
/// no cycling while the body is stuck, bounded body offset, reversal without a reset, and recovery speeds that are not
/// abrupt at the owner's climb speeds.</summary>
public sealed class LimbGaitTests
{
    private const float Pitch = 1f / 3f;
    private const float Base = 10f + Pitch / 2f;

    private static LimbGait Gait(float feet = 12f)
    {
        var gait = new LimbGait();
        Assert.True(gait.Bind(new GaitShape(), Base, Pitch, 0, 200, 1.38f, feet));
        return gait;
    }

    private static bool OnRung(float height) => Math.Abs((height - Base) / Pitch - Math.Round((height - Base) / Pitch)) < 0.0005f;

    private static void Climb(LimbGait gait, float from, float to, float step = 0.018f)
    {
        float direction = Math.Sign(to - from);
        for (float feet = from; direction * (to - feet) > 1e-5f; feet += direction * step) { Assert.True(gait.Update(feet, 0.02f, false)); }
        Assert.True(gait.Update(to, 0.02f, false));
    }

    [Fact]
    public void NeverMoreThanTwoLimbsOffTheLadder()
    {
        foreach (ClimbLoop loop in new[] { new GaitShape().Up, new GaitShape().Down })
        {
            for (int i = 0; i < 400; i++)
            {
                float t = i / 400f;
                int moving = 0;
                for (int limb = 0; limb < 4; limb++)
                {
                    float d = t - loop.Start[limb]; d -= (float)Math.Floor(d);
                    if (d < loop.Length[limb]) { moving++; }
                }

                Assert.True(moving <= 2, loop.Name + " has " + moving + " limbs off at phase " + t);
            }
        }
    }

    [Theory]
    [InlineData(LimbGait.LeftHand)]
    [InlineData(LimbGait.RightHand)]
    [InlineData(LimbGait.LeftFoot)]
    [InlineData(LimbGait.RightFoot)]
    public void EveryPlantedContactIsARealRungClimbingUpAndDown(int limb)
    {
        var gait = Gait();
        Climb(gait, 12f, 12.02f);
        for (float feet = 12.02f; feet < 17f; feet += 0.004f)
        {
            Assert.True(gait.Update(feet, 0.01f, false));
            if (gait.Contact(limb).Planted) { Assert.True(OnRung(gait.Contact(limb).Height), "up: limb " + limb + " planted off a rung"); }
        }

        for (float feet = 17f; feet > 12f; feet -= 0.004f)
        {
            Assert.True(gait.Update(feet, 0.01f, false));
            if (gait.Contact(limb).Planted) { Assert.True(OnRung(gait.Contact(limb).Height), "down: limb " + limb + " planted off a rung"); }
        }
    }

    [Fact]
    public void DescentIsItsOwnPerformanceAFootLeadsDown()
    {
        var shape = new GaitShape();
        Assert.NotEqual(shape.Up.Start, shape.Down.Start);
        int first = -1; float best = -1f;
        for (int limb = 0; limb < 4; limb++)
        {
            float end = (shape.Down.Start[limb] + shape.Down.Length[limb]) % 1f;
            if (end > best) { best = end; first = limb; }
        }

        Assert.True(first == LimbGait.LeftFoot || first == LimbGait.RightFoot);
        var gait = Gait();
        Climb(gait, 12.5f, 12.4f);
        Assert.True(gait.Descending);
    }

    [Fact]
    public void ABodyThatDoesNotMoveDoesNotCycleItsLimbs()
    {
        var gait = Gait();
        Climb(gait, 12f, 12.3f);
        for (int i = 0; i < 60; i++) { Assert.True(gait.Update(12.3f, 0.02f, false)); } // Let any blend and stall settle finish.
        float[] before = new float[4];
        for (int limb = 0; limb < 4; limb++) { before[limb] = gait.Contact(limb).Height; }
        for (int i = 0; i < 200; i++) { Assert.True(gait.Update(12.3f, 0.02f, false)); } // Blocked: input held, body stuck.
        for (int limb = 0; limb < 4; limb++) { Assert.Equal(before[limb], gait.Contact(limb).Height, 4); }
    }

    [Fact]
    public void ABlockedClimberSettlesEveryLimbOnARung()
    {
        var gait = Gait();
        Climb(gait, 12f, 12.55f);
        for (int i = 0; i < 60; i++) { Assert.True(gait.Update(12.55f + i * 0.0002f, 0.02f, false)); } // Creeping, blocked.
        for (int limb = 0; limb < 4; limb++)
        {
            Assert.True(gait.Contact(limb).Planted, "limb " + limb + " left hanging while blocked");
            Assert.True(OnRung(gait.Contact(limb).Height));
        }
    }

    [Fact]
    public void ReversingMidClimbBlendsWithoutAJump()
    {
        var gait = Gait();
        Climb(gait, 12f, 12.37f);
        float[] before = new float[4];
        for (int limb = 0; limb < 4; limb++) { before[limb] = gait.Contact(limb).Height; }
        Assert.True(gait.Update(12.352f, 0.02f, false));
        for (int limb = 0; limb < 4; limb++) { Assert.True(Math.Abs(gait.Contact(limb).Height - before[limb]) < 0.25f, "limb " + limb + " jumped on reversal"); }
    }

    [Fact]
    public void AStopSettlesEveryLimbOnARealRungInsideItsBand()
    {
        var shape = new GaitShape();
        for (float feet = 12f; feet < 14f; feet += 0.037f)
        {
            var gait = Gait(feet);
            Climb(gait, feet, feet + 0.2f);
            for (int i = 0; i < 60; i++) { Assert.True(gait.Update(feet + 0.2f, 0.02f, true)); }
            for (int limb = 0; limb < 4; limb++)
            {
                Assert.True(gait.Contact(limb).Planted);
                Assert.True(OnRung(gait.Contact(limb).Height), "limb " + limb + " settled off a rung");
            }

            foreach (int hand in new[] { LimbGait.LeftHand, LimbGait.RightHand })
            {
                float rel = gait.Contact(hand).Height - (feet + 0.2f) - 1.38f;
                Assert.InRange(rel, -shape.HandSettleBelow - 0.001f, shape.HandSettleAbove + 0.001f);
            }
        }
    }

    [Fact]
    public void TheBodyCurveIsBoundedAndSlowsWhileAFootRecovers()
    {
        var gait = Gait();
        Climb(gait, 12f, 12.1f);
        float cycle = gait.CycleMetres, max = 0f;
        for (int i = 0; i < 300; i++)
        {
            gait.Body(12f + cycle * i / 300f, out float up, out _, out _);
            max = Math.Max(max, Math.Abs(up));
        }

        Assert.InRange(max, 0.005f, 0.12f);
        float at = Base + cycle * (6f + 0.20f + 0.02f);
        gait.Body(at, out float a, out _, out _);
        gait.Body(at + cycle * 0.12f, out float b, out _, out _);
        Assert.True(b < a, "body should lag early in the left foot's recovery");
    }

    [Fact]
    public void ExtendingTheRunNeverShiftsThePhase()
    {
        var gait = Gait();
        float before = gait.Target(LimbGait.LeftHand, 13.1f, false, out _);
        Assert.True(gait.RefreshBounds(Base - 2f, 0, 206));
        Assert.Equal(before, gait.Target(LimbGait.LeftHand, 13.1f, false, out _), 5);
        Assert.False(gait.RefreshBounds(Base - 0.1f, 0, 206));
    }

    [Fact]
    public void ContactsNeverLeaveTheRealRungSpan()
    {
        var gait = new LimbGait();
        Assert.True(gait.Bind(new GaitShape(), Base, Pitch, 0, 11, 1.38f, 10f));
        for (float feet = 8f; feet < 16f; feet += 0.01f)
        {
            for (int limb = 0; limb < 4; limb++) { Assert.InRange(gait.Target(limb, feet, false, out _), Base - 0.0001f, Base + 11 * Pitch + 0.0001f); }
        }
    }

    [Theory]
    [InlineData(0.9f)]
    [InlineData(1.2f)]
    public void RecoveriesAreNotAbruptAtTheOwnersSpeeds(float speed)
    {
        ClimbLoop loop = new GaitShape().Up;
        LimbGait.Recovery(loop, LimbGait.LeftFoot, Pitch, speed, out float seconds, out float mean, out float peak);
        // A two-rung stride (owner, 2026-10-27: knees folded to the chest on three) makes the cycle 0.67 m, so a
        // foot moves in 0.37 s at 0.9 m/s and 0.28 s at Run; the 0.35 s floor still holds at normal speed.
        Assert.True(seconds >= (speed <= 1f ? 0.35f : 0.27f), "foot recovery " + seconds + " s");
        Assert.True(peak <= 4.0f, "foot peak " + peak + " m/s");
        Assert.True(mean <= peak);
    }

    [Fact]
    public void TheExportedBlenderGaitIsValidAndBindsToARealRun()
    {
        GaitShape shape = GeneratedGait.Shape();
        Assert.True(shape.Valid);
        Assert.Equal(64, GeneratedGait.BlendSha256.Length);
        Assert.True(new LimbGait().Bind(shape, Base, Pitch, 0, 100, 1.38f, 12f));
    }
}
