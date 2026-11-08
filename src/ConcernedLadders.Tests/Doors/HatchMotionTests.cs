using TheConcernedCat.ConcernedLadders.Domain.Doors;

namespace TheConcernedCat.ConcernedLadders.Tests.Doors;

/// <summary>CL-004 #485 acceptance 4 (physical operation) and 6 (traversal) as policy: the lid follows the native door
/// state over the donor's swing time, never moves into a character, and is a passage or a landing only at rest.</summary>
public sealed class HatchMotionTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(-1, true)]
    public void BothNativeOpenDirectionsOpenTheOneLid(int state, bool open)
    {
        Assert.Equal(open, HatchMotion.IsOpenState(state));
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    [InlineData(2.0f)]
    public void AFullSwingTakesTheDonorsClipLength(float swing)
    {
        float progress = 0f, elapsed = 0f;
        const float frame = 1f / 60f;
        while (progress < 1f && elapsed < 10f)
        {
            progress = HatchMotion.Advance(progress, HatchMotion.Proposed(progress, true, frame, swing), false);
            elapsed += frame;
        }

        Assert.Equal(1f, progress);
        Assert.InRange(elapsed, swing - frame, swing + frame);
        while (progress > 0f && elapsed < 20f)
        {
            progress = HatchMotion.Advance(progress, HatchMotion.Proposed(progress, false, frame, swing), false);
            elapsed += frame;
        }

        Assert.Equal(0f, progress);
    }

    [Fact]
    public void AnOccupiedPoseHoldsTheLidWhereItIs()
    {
        float proposed = HatchMotion.Proposed(0f, true, 0.02f, 0.5f);
        Assert.True(proposed > 0f);
        Assert.Equal(0f, HatchMotion.Advance(0f, proposed, proposedPoseOccupied: true));
        // Closing over a head mid-swing: held at the current progress, not snapped open or shut.
        float closing = HatchMotion.Proposed(0.6f, false, 0.02f, 0.5f);
        Assert.Equal(0.6f, HatchMotion.Advance(0.6f, closing, proposedPoseOccupied: true));
    }

    [Fact]
    public void AHeldLidResumesTowardItsTargetOnceClear()
    {
        float progress = 0f;
        for (int i = 0; i < 10; ++i) { progress = HatchMotion.Advance(progress, HatchMotion.Proposed(progress, true, 0.02f, 0.5f), true); }
        Assert.Equal(0f, progress);
        progress = HatchMotion.Advance(progress, HatchMotion.Proposed(progress, true, 0.02f, 0.5f), false);
        Assert.True(progress > 0f);
    }

    [Fact]
    public void AReversalContinuesFromTheCurrentProgress()
    {
        float up = HatchMotion.Proposed(0.4f, true, 0.02f, 1f);
        float down = HatchMotion.Proposed(0.4f, false, 0.02f, 1f);
        Assert.True(up > 0.4f && down < 0.4f);
        Assert.Equal(0.4f - down, up - 0.4f, 5);
    }

    [Fact]
    public void AHitchCannotJumpTheLidThroughABodyInOneStep()
    {
        float proposed = HatchMotion.Proposed(0f, true, 1f, 0.5f);
        Assert.Equal(HatchMotion.MaximumStepSeconds / 0.5f, proposed, 5);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(-1f)]
    [InlineData(0f)]
    public void NoTimeOrBadTimeDoesNotMoveTheLid(float seconds)
    {
        Assert.Equal(0.3f, HatchMotion.Proposed(0.3f, true, seconds, 0.5f));
    }

    [Fact]
    public void ABadSwingTimeIsBoundedRatherThanInstantOrFrozen()
    {
        Assert.Equal(HatchMotion.MaximumStepSeconds / HatchMotion.MinimumSwingSeconds, HatchMotion.Proposed(0f, true, 0.05f, 0f), 5);
        Assert.Equal(HatchMotion.MaximumStepSeconds / HatchMotion.MaximumSwingSeconds, HatchMotion.Proposed(0f, true, 0.05f, float.NaN), 5);
    }

    [Fact]
    public void TheEaseStartsAndEndsAtRestAndIsMonotonic()
    {
        Assert.Equal(0f, HatchMotion.Angle(0f));
        Assert.Equal(HatchGeometry.OpenAngleDegrees, HatchMotion.Angle(1f));
        float previous = -1f;
        for (int i = 0; i <= 100; ++i)
        {
            float angle = HatchMotion.Angle(i / 100f);
            Assert.True(angle >= previous);
            previous = angle;
        }

        // Ease-in-out: the first and last tenth move less than a linear swing would.
        Assert.True(HatchMotion.Angle(0.1f) < 9f);
        Assert.True(HatchMotion.Angle(0.9f) > 81f);
    }

    [Theory]
    [InlineData(0f, false, false, true)]
    [InlineData(0f, true, false, false)]   // Asked to open but held (someone on it): not a landing.
    [InlineData(0.5f, true, false, false)]
    [InlineData(0.5f, false, false, false)] // Closing: not yet a landing.
    [InlineData(1f, true, true, false)]
    [InlineData(1f, false, true, false)]    // Asked to close from fully open: still a passage until it moves.
    public void OnlyARestingLidIsALandingAndOnlyAFullyOpenOneIsAPassage(float progress, bool open, bool passage, bool landing)
    {
        Assert.Equal(passage, HatchMotion.IsPassage(progress));
        Assert.Equal(landing, HatchMotion.IsLanding(progress, open));
    }

    [Theory]
    [InlineData(0f, true, true)]
    [InlineData(1f, true, false)]
    [InlineData(1f, false, true)]
    [InlineData(0f, false, false)]
    public void MovingMeansTheLidHasSomewhereToGo(float progress, bool open, bool moving)
    {
        Assert.Equal(moving, HatchMotion.IsMoving(progress, open));
    }
}
