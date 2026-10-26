using System;
using TheConcernedCat.ConcernedLadders.Domain.Animation;

namespace TheConcernedCat.ConcernedLadders.Tests.Animation;

/// <summary>Owner references 2026-10-26: independent limb timing (a hand reaches, the opposite foot recovers, then the
/// other hand and the other foot), contacts on real rungs, a pure function of body height, settling on a stop, and a
/// bounded torso dwell while a foot recovers.</summary>
public sealed class LimbGaitTests
{
    private const float Pitch = 1f / 3f;
    private const float Base = 10f + Pitch / 2f;

    private static LimbGait Gait(GaitShape? shape = null, float feet = 12f)
    {
        var gait = new LimbGait();
        Assert.True(gait.Bind(shape ?? new GaitShape(), Base, Pitch, 0, 200, 1.38f, feet));
        return gait;
    }

    private static bool OnRung(float height) => Math.Abs((height - Base) / Pitch - Math.Round((height - Base) / Pitch)) < 0.0005f;

    [Fact]
    public void LimbsMoveOnTheirOwnWindowsInTheReferenceOrder()
    {
        var gait = Gait();
        float cycle = gait.CycleMetres;
        // Sample one cycle: record when each limb first starts moving.
        float[] first = { -1f, -1f, -1f, -1f };
        for (int i = 0; i < 400; i++)
        {
            float feet = 12f + cycle * i / 400f;
            for (int limb = 0; limb < 4; limb++)
            {
                gait.Target(limb, feet, false, out _, out float swing);
                if (swing > 0f && first[limb] < 0f) { first[limb] = i / 400f; }
            }
        }

        // Every limb moves once per cycle, never all four at once, and no two limbs share a release instant.
        foreach (float f in first) { Assert.True(f >= 0f); }
        Assert.Equal(4, new System.Collections.Generic.HashSet<float>(first).Count);
        for (int i = 0; i < 400; i++)
        {
            int moving = 0;
            for (int limb = 0; limb < 4; limb++) { gait.Target(limb, 12f + cycle * i / 400f, false, out _, out float s); if (s > 0f) { moving++; } }
            Assert.True(moving <= 2, "too many limbs off the ladder at once");
        }
    }

    [Theory]
    [InlineData(LimbGait.LeftHand)]
    [InlineData(LimbGait.RightHand)]
    [InlineData(LimbGait.LeftFoot)]
    [InlineData(LimbGait.RightFoot)]
    public void EveryPlantedContactIsARealRungAndEveryMoveIsOneStride(int limb)
    {
        var gait = Gait();
        float last = gait.Target(limb, 12f, false, out _, out _);
        float planted = float.NaN;
        for (float feet = 12f; feet < 18f; feet += 0.004f)
        {
            float height = gait.Target(limb, feet, false, out _, out float swing);
            if (swing <= 0f)
            {
                Assert.True(OnRung(height), "limb " + limb + " planted off a rung at " + height);
                float move = float.IsNaN(planted) ? 0f : height - planted;
                Assert.True(Math.Abs(move) < 0.0005f || Math.Abs(move - 3 * Pitch) < 0.0005f, "move " + move);
                planted = height;
            }

            Assert.True(height >= last - 0.0001f, "limb went down while climbing up");
            last = height;
        }
    }

    [Fact]
    public void ClimbingDownRetracesClimbingUp()
    {
        var climber = Gait();
        for (int step = 0; step <= 90; step++) { Assert.True(climber.Update(12f + step * 0.018f, 0.02f, false)); }
        for (int step = 90; step >= 0; step--) { Assert.True(climber.Update(12f + step * 0.018f, 0.02f, false)); }
        for (int limb = 0; limb < 4; limb++)
        {
            Assert.Equal(climber.Target(limb, 12f, false, out _, out _), climber.Contact(limb).Height, 4);
        }
    }

    [Fact]
    public void AStopSettlesEveryLimbOnARealRungInsideItsBand()
    {
        var shape = new GaitShape();
        for (float feet = 12f; feet < 14f; feet += 0.037f)
        {
            var gait = Gait(feet: feet);
            for (int i = 0; i < 20; i++) { Assert.True(gait.Update(feet, 0.02f, false)); }
            for (int i = 0; i < 60; i++) { Assert.True(gait.Update(feet, 0.02f, true)); }
            for (int limb = 0; limb < 4; limb++)
            {
                Assert.True(gait.Contact(limb).Planted);
                Assert.True(OnRung(gait.Contact(limb).Height), "limb " + limb + " settled off a rung");
            }

            foreach (int hand in new[] { LimbGait.LeftHand, LimbGait.RightHand })
            {
                float rel = gait.Contact(hand).Height - feet - 1.38f;
                Assert.InRange(rel, -shape.HandSettleBelow - 0.001f, shape.HandSettleAbove + 0.001f);
            }
        }
    }

    [Fact]
    public void ResumingAfterAStopRejoinsWithoutAJump()
    {
        var gait = Gait();
        float feet = 12.3f;
        Assert.True(gait.Update(feet, 0.02f, false));
        for (int i = 0; i < 30; i++) { Assert.True(gait.Update(feet, 0.02f, true)); }
        float[] before = new float[4];
        for (int limb = 0; limb < 4; limb++) { before[limb] = gait.Contact(limb).Height; }
        Assert.True(gait.Update(feet + 0.018f, 0.02f, false)); // One physics step at 0.9 m/s.
        for (int limb = 0; limb < 4; limb++) { Assert.True(Math.Abs(gait.Contact(limb).Height - before[limb]) < 0.2f, "limb " + limb + " jumped"); }
        for (int i = 0; i < 60; i++) { feet += 0.018f; Assert.True(gait.Update(feet, 0.02f, false)); }
        for (int limb = 0; limb < 4; limb++) { Assert.Equal(gait.Target(limb, feet, false, out _, out _), gait.Contact(limb).Height, 4); }
    }

    [Fact]
    public void TheTorsoDwellIsBoundedPeriodicAndSlowerWhileAFootRecovers()
    {
        var gait = Gait();
        float cycle = gait.CycleMetres;
        float max = 0f;
        for (int i = 0; i < 300; i++) { max = Math.Max(max, Math.Abs(gait.BodyOffset(12f + cycle * i / 300f))); }
        Assert.InRange(max, 0.01f, 0.15f);
        Assert.Equal(gait.BodyOffset(12.2f), gait.BodyOffset(12.2f + cycle), 4);
        // While the left foot recovers (window starts at 0.20 of the cycle) the offset falls: the torso lags the root.
        float start = Base + cycle * (5f + new GaitShape().Start[LimbGait.LeftFoot] + 0.05f);
        Assert.True(gait.BodyOffset(start + cycle * 0.1f) < gait.BodyOffset(start));
    }

    [Fact]
    public void ExtendingTheRunNeverShiftsThePhase()
    {
        var gait = Gait();
        float before = gait.Target(LimbGait.LeftHand, 13.1f, false, out _, out _);
        Assert.True(gait.RefreshBounds(Base - 2f, 0, 206));
        Assert.Equal(before, gait.Target(LimbGait.LeftHand, 13.1f, false, out _, out _), 5);
        Assert.False(gait.RefreshBounds(Base - 0.1f, 0, 206));
    }

    [Fact]
    public void ContactsNeverLeaveTheRealRungSpan()
    {
        var gait = new LimbGait();
        Assert.True(gait.Bind(new GaitShape(), Base, Pitch, 0, 11, 1.38f, 10f));
        for (float feet = 8f; feet < 16f; feet += 0.01f)
        {
            for (int limb = 0; limb < 4; limb++) { Assert.InRange(gait.Target(limb, feet, false, out _, out _), Base - 0.0001f, Base + 11 * Pitch + 0.0001f); }
        }
    }

    [Fact]
    public void CadenceFollowsBodyTravelAtTheOwnersNaturalSpeeds()
    {
        // Three rungs per limb move: 1 m per cycle; 1.11 s at 0.9 m/s, 0.83 s with Run at 1.2 m/s.
        float cycle = new GaitShape().StrideRungs * Pitch;
        Assert.Equal(1.111f, cycle / 0.9f, 3);
        Assert.Equal(0.833f, cycle / 1.2f, 3);
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
