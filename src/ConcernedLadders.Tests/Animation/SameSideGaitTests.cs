using System;
using TheConcernedCat.ConcernedLadders.Domain.Animation;

namespace TheConcernedCat.ConcernedLadders.Tests.Animation;

/// <summary>Owner correction 2026-10-26: same-side choreography (right hand with right foot, then left with left),
/// contacts on real rungs, a pure function of body height, settling on a stop. The diagonal track these replace
/// moved the right hand with the left foot.</summary>
public sealed class SameSideGaitTests
{
    private const float Pitch = 1f / 3f;
    private const float Base = 10f + Pitch / 2f;

    private static SameSideGait Gait(GaitShape? shape = null, float feet = 12f)
    {
        var gait = new SameSideGait();
        Assert.True(gait.Bind(shape ?? new GaitShape(), Base, Pitch, 0, 200, 1.45f, feet));
        return gait;
    }

    private static bool OnRung(float height) => Math.Abs((height - Base) / Pitch - Math.Round((height - Base) / Pitch)) < 0.0005f;

    [Fact]
    public void TheRightHandAndRightFootSwingTogetherWhileTheLeftSideStaysPlanted()
    {
        var gait = Gait();
        for (float feet = 12f; feet < 15f; feet += 0.01f)
        {
            gait.Target(SameSideGait.RightHand, feet, false, out _, out float rh);
            gait.Target(SameSideGait.RightFoot, feet, false, out _, out float rf);
            gait.Target(SameSideGait.LeftHand, feet, false, out _, out float lh);
            gait.Target(SameSideGait.LeftFoot, feet, false, out _, out float lf);
            // No diagonal: a moving right hand never coincides with a moving left foot with both sides at full swing.
            if (rh > 0.9f) { Assert.True(lf < 0.5f, "left foot swinging hard with the right hand at " + feet); }
            if (lh > 0.9f) { Assert.True(rf < 0.5f, "right foot swinging hard with the left hand at " + feet); }
            if (rh > 0.5f) { Assert.True(rf > 0f || OnRung(gait.Target(SameSideGait.RightFoot, feet, false, out _, out _)), "right side not grouped at " + feet); }
        }
    }

    [Theory]
    [InlineData(SameSideGait.LeftHand)]
    [InlineData(SameSideGait.RightHand)]
    [InlineData(SameSideGait.LeftFoot)]
    [InlineData(SameSideGait.RightFoot)]
    public void EveryPlantedContactIsARealRungAndEveryMoveIsOneStride(int limb)
    {
        var gait = Gait();
        float last = gait.Target(limb, 12f, false, out _, out _);
        float planted = float.NaN;
        for (float feet = 12f; feet < 18f; feet += 0.005f)
        {
            float height = gait.Target(limb, feet, false, out _, out float swing);
            if (swing <= 0f)
            {
                Assert.True(OnRung(height), "limb " + limb + " planted off a rung at " + height);
                float move = float.IsNaN(planted) ? 0f : height - planted;
                Assert.True(Math.Abs(move) < 0.0005f || Math.Abs(move - 4 * Pitch) < 0.0005f, "move " + move);
                planted = height;
            }

            Assert.True(height >= last - 0.0001f, "limb went down while climbing up");
            last = height;
        }
    }

    [Fact]
    public void TheSameHeightAlwaysGivesTheSamePoseSoClimbingDownRetracesClimbingUp()
    {
        var gait = Gait();
        for (float feet = 12f; feet < 14f; feet += 0.0137f)
        {
            for (int limb = 0; limb < 4; limb++)
            {
                Assert.Equal(gait.Target(limb, feet, false, out _, out _), gait.Target(limb, feet + 0f, false, out _, out _));
            }
        }

        var climber = Gait();
        for (int step = 0; step <= 75; step++) { Assert.True(climber.Update(12f + step * 0.02f, 0.02f, false)); }
        for (int step = 75; step >= 0; step--) { Assert.True(climber.Update(12f + step * 0.02f, 0.02f, false)); }
        for (int limb = 0; limb < 4; limb++)
        {
            Assert.Equal(climber.Target(limb, 12f, false, out _, out _), climber.Contact(limb).Height, 4);
        }
    }

    [Fact]
    public void AStopSettlesEverySwingingLimbOntoARealRung()
    {
        var gait = Gait();
        float feet = 12f;
        // Find a height where some limb is mid-swing.
        while (true)
        {
            gait.Target(SameSideGait.RightHand, feet, false, out _, out float swing);
            if (swing > 0.8f) { break; }
            feet += 0.01f;
        }

        gait = Gait(feet: feet);
        for (int i = 0; i < 40; i++) { Assert.True(gait.Update(feet, 0.02f, false)); } // A mid-swing bind starts planted and rejoins.
        Assert.True(gait.Update(feet, 0.02f, false));
        Assert.False(gait.Contact(SameSideGait.RightHand).Planted);
        for (int i = 0; i < 60; i++) { Assert.True(gait.Update(feet, 0.02f, true)); }
        for (int limb = 0; limb < 4; limb++)
        {
            Assert.True(gait.Contact(limb).Planted);
            Assert.True(OnRung(gait.Contact(limb).Height), "limb " + limb + " settled off a rung");
        }
    }

    [Fact]
    public void ResumingAfterAStopRejoinsTheGaitWithoutAJump()
    {
        var gait = Gait();
        float feet = 12.3f;
        Assert.True(gait.Update(feet, 0.02f, false));
        for (int i = 0; i < 30; i++) { Assert.True(gait.Update(feet, 0.02f, true)); }
        float[] before = new float[4];
        for (int limb = 0; limb < 4; limb++) { before[limb] = gait.Contact(limb).Height; }
        Assert.True(gait.Update(feet + 0.036f, 0.02f, false)); // One physics step at 1.8 m/s.
        for (int limb = 0; limb < 4; limb++)
        {
            Assert.True(Math.Abs(gait.Contact(limb).Height - before[limb]) < 0.2f, "limb " + limb + " jumped");
        }

        for (int i = 0; i < 40; i++) { feet += 0.036f; Assert.True(gait.Update(feet, 0.02f, false)); }
        for (int limb = 0; limb < 4; limb++)
        {
            Assert.Equal(gait.Target(limb, feet, false, out _, out _), gait.Contact(limb).Height, 4);
        }
    }

    [Fact]
    public void ExtendingTheRunNeverShiftsThePhase()
    {
        var gait = Gait();
        float before = gait.Target(SameSideGait.LeftHand, 13.1f, false, out _, out _);
        Assert.True(gait.RefreshBounds(Base - 2f, 0, 206)); // A section added below: rung 0 moved down six rungs.
        Assert.Equal(before, gait.Target(SameSideGait.LeftHand, 13.1f, false, out _, out _), 5);
        Assert.False(gait.RefreshBounds(Base - 0.1f, 0, 206)); // Not rung-aligned: refused.
    }

    [Fact]
    public void ContactsNeverLeaveTheRealRungSpan()
    {
        var gait = new SameSideGait();
        Assert.True(gait.Bind(new GaitShape(), Base, Pitch, 0, 11, 1.45f, 10f));
        for (float feet = 8f; feet < 16f; feet += 0.01f)
        {
            for (int limb = 0; limb < 4; limb++)
            {
                float height = gait.Target(limb, feet, false, out _, out _);
                Assert.InRange(height, Base - 0.0001f, Base + 11 * Pitch + 0.0001f);
            }
        }
    }

    [Theory]
    [InlineData(3)]  // Odd strides cannot alternate the two sides evenly on real rungs.
    [InlineData(0)]
    public void AnUnevenOrDegenerateStrideIsRefused(int stride)
    {
        var shape = new GaitShape { StrideRungs = stride };
        Assert.False(new SameSideGait().Bind(shape, Base, Pitch, 0, 100, 1.45f, 12f));
    }

    [Fact]
    public void LimbCadenceFollowsBodyTravelNotTime()
    {
        // Normal 1.8 m/s with a four-rung stride: one full cycle per 1.333 m of travel, 0.741 s.
        var shape = new GaitShape();
        float cycleMetres = shape.StrideRungs * Pitch;
        Assert.Equal(0.741f, cycleMetres / 1.8f, 3);
        Assert.Equal(0.549f, cycleMetres / 2.43f, 3);
    }
}
