using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Tests;

public sealed class LadderRulesTests
{
    [Theory]
    [InlineData(0.3f, 0.24f)]
    [InlineData(0.25f, 0.1f)]
    [InlineData(0.6f, 0.4f)]
    public void BodyCapsuleClearsTheAuthoredFrontFace(float radius, float frontFace)
    {
        float centre = LadderRules.BodyOffset(radius, frontFace);
        Assert.Equal(0.06f, centre - radius - frontFace, 5);
    }

    private static LadderGeometry Segment(float bottom, float x = 0f, float facing = 1f)
    {
        Assert.True(LadderGeometry.TryMeasure(new ClimbPoint(x, bottom, 0f), new ClimbPoint(x, bottom + 2f, 0f),
            ClimbHeading.FromXz(0f, facing), 0.8f, 1f / 3f, out LadderGeometry geometry));
        return geometry;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(24)]
    [InlineData(64)]
    public void CompleteLoadedStackIsFoundFromAnySegmentWithoutASurveyRadius(int count)
    {
        var samples = Enumerable.Range(0, count).Select(i => new LadderSample(i + 1, Segment(i * 2f))).Reverse().ToArray();
        int anchor = Math.Max(1, count / 2);
        Assert.True(LadderRules.TryBuildRun(samples, anchor, out List<int> ids, out LadderGeometry run));
        Assert.Equal(Enumerable.Range(1, count), ids);
        Assert.Equal(0f, run.Bottom.Y);
        Assert.Equal(count * 2f, run.Top.Y);
    }

    [Fact]
    public void TooLongAConnectedStackRefusesInsteadOfTruncatingItsTop()
    {
        var samples = Enumerable.Range(0, 65).Select(i => new LadderSample(i, Segment(i * 2f))).ToArray();
        Assert.False(LadderRules.TryBuildRun(samples, 0, out _, out _));
    }

    [Fact]
    public void UnrelatedNearbyLaddersDoNotJoinOrShortenTheRun()
    {
        LadderSample[] samples =
        {
            new LadderSample(1, Segment(0f)), new LadderSample(2, Segment(2f)),
            new LadderSample(3, Segment(2f, 1f)), new LadderSample(4, Segment(4f, 0f, -1f)),
            new LadderSample(5, Segment(6f)),
        };
        Assert.True(LadderRules.TryBuildRun(samples, 1, out List<int> ids, out LadderGeometry run));
        Assert.Equal(new[] { 1, 2 }, ids);
        Assert.Equal(4f, run.Top.Y);
    }

    [Fact]
    public void DuplicateConnectionIsAmbiguous()
    {
        var samples = new[] { new LadderSample(1, Segment(0)), new LadderSample(2, Segment(2)), new LadderSample(3, Segment(2)) };
        Assert.False(LadderRules.TryBuildRun(samples, 1, out _, out _));
    }

    [Fact]
    public void DuplicateOrOverlappingAnchorIsRefused()
    {
        foreach (float overlappingBottom in new[] { 0f, 0.5f, 1f })
        {
            var samples = new[] { new LadderSample(1, Segment(0)), new LadderSample(2, Segment(overlappingBottom)) };
            Assert.False(LadderRules.TryBuildRun(samples, 1, out _, out _));
        }
    }

    [Fact]
    public void SmallJointErrorsCannotAccumulateIntoASlantedRun()
    {
        var samples = new[] { new LadderSample(1, Segment(0, 0)), new LadderSample(2, Segment(2, 0.02f)), new LadderSample(3, Segment(4, 0.04f)) };
        Assert.False(LadderRules.TryBuildRun(samples, 1, out _, out _));
    }

    [Theory]
    [InlineData(0.04f)]
    [InlineData(0.5f)]
    [InlineData(2f)]
    public void AGapDoesNotBecomeAnInvisibleLadder(float gap)
    {
        var samples = new[] { new LadderSample(1, Segment(0)), new LadderSample(2, Segment(2 + gap)) };
        Assert.True(LadderRules.TryBuildRun(samples, 1, out List<int> ids, out _));
        Assert.Equal(new[] { 1 }, ids);
    }

    [Fact]
    public void SupportedLandingsCanMountAtBottomTopOrIntermediateHeight()
    {
        Assert.True(LadderGeometry.TryMeasure(new ClimbPoint(0, 0, 0), new ClimbPoint(0, 10, 0),
            ClimbHeading.FromXz(0, 1), 0.8f, 1f / 3f, out LadderGeometry run));
        foreach (float height in new[] { 0f, 4f, 10f })
        {
            foreach (float side in new[] { -0.6f, 0.6f })
            {
                Assert.True(LadderRules.CanMount(run, new ClimbPoint(0, height, side), true, 0, height,
                    true, true, false, 0));
            }
        }
    }

    [Theory]
    [InlineData(false, 0f, 0f, true, true, false, 0f)]
    [InlineData(true, -2f, 0f, true, true, false, 0f)]
    [InlineData(true, 0f, 5f, true, true, false, 0f)]
    [InlineData(true, 0f, 0f, false, true, false, 0f)]
    [InlineData(true, 0f, 0f, true, false, false, 0f)]
    [InlineData(true, 0f, 0f, true, true, true, 0f)]
    [InlineData(true, 0f, 0f, true, true, false, 0.2f)]
    public void MountRejectsUnsupportedFallingUnownedBusyOverlayAndCooldown(bool support, float speed, float peak,
        bool owned, bool free, bool overlay, float cooldown)
    {
        Assert.False(LadderRules.CanMount(Segment(0), new ClimbPoint(0, 0, 0.5f), support, speed, peak,
            owned, free, overlay, cooldown));
    }

    [Fact]
    public void NonfiniteMountEvidenceNeverPasses()
    {
        foreach (float bad in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity })
        {
            Assert.False(LadderRules.CanMount(Segment(0), new ClimbPoint(0, bad, 0.5f), true, 0, 0, true, true, false, 0));
            Assert.False(LadderRules.CanMount(Segment(0), new ClimbPoint(0, 0, 0.5f), true, bad, 0, true, true, false, 0));
            Assert.False(LadderRules.CanMount(Segment(0), new ClimbPoint(0, 0, 0.5f), true, 0, bad, true, true, false, 0));
        }
    }

    [Fact]
    public void StopReverseAndEndClampsArePredictable()
    {
        Assert.Equal(1f, LadderRules.Advance(1, 0, .02f, 0, 2));
        float up = LadderRules.Advance(1, 1, .02f, 0, 2);
        Assert.Equal(1.036f, up, 5);
        Assert.Equal(1f, LadderRules.Advance(up, -1, .02f, 0, 2), 5);
        Assert.Equal(2f, LadderRules.Advance(1.99f, 1, .02f, 0, 2));
        Assert.Equal(0f, LadderRules.Advance(.01f, -1, .02f, 0, 2));
    }

    [Fact]
    public void LongOrInvalidFrameCannotPlanAnUnboundedMove()
    {
        Assert.Equal(1.09f, LadderRules.Advance(1, 1, 10f, 0, 10), 5);
        Assert.Equal(1f, LadderRules.Advance(1, 1, float.PositiveInfinity, 0, 10));
        Assert.Equal(1f, LadderRules.Advance(1, float.NaN, .02f, 0, 10));
        Assert.Equal(1f, LadderRules.Advance(1, 1, -.02f, 0, 10));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    [InlineData(0.5f)]
    public void OverlayNeutralisesBothKeyboardAndAnalogIntent(float input) => Assert.Equal(0f, LadderRules.Input(input, true));

    [Fact]
    public void EveryLostSafetyConditionReleasesRatherThanContinuing()
    {
        for (int missing = 0; missing < 7; missing++)
        {
            bool[] facts = Enumerable.Repeat(true, 7).ToArray();
            facts[missing] = false;
            Assert.True(LadderRules.MustRelease(facts[0], facts[1], facts[2], facts[3], facts[4], facts[5], facts[6], 0));
        }

        Assert.False(LadderRules.MustRelease(true, true, true, true, true, true, true, 0));
        Assert.True(LadderRules.MustRelease(true, true, true, true, true, true, true, 2));
        Assert.True(LadderRules.MustRelease(true, true, true, true, true, true, true, float.NaN));
        Assert.True(LadderRules.MustRelease(true, true, true, true, true, true, true, float.PositiveInfinity));
    }
}
