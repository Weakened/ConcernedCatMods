using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Tests;

/// <summary>Owner addenda N07 (separate Jump and Drop) and N08 (building while attached). Each test fails
/// against 0.2.1, which had no Drop, no shared-binding rule, no build-action exemption and a session-fixed run.</summary>
public sealed class ClimbControlsTests
{
    private static LadderGeometry Run(float bottom, float height, float x = 0f, float headingX = 0f, float headingZ = 1f)
    {
        Assert.True(LadderGeometry.TryMeasure(new ClimbPoint(x, bottom, 0f), new ClimbPoint(x, bottom + height, 0f),
            ClimbHeading.FromXz(headingX, headingZ), 0.8f, 1f / 3f, out LadderGeometry geometry));
        return geometry;
    }

    [Theory]
    [InlineData(true, true, true, (int)ClimbAction.Jump)]
    [InlineData(false, true, true, (int)ClimbAction.Drop)]
    [InlineData(false, false, true, (int)ClimbAction.Use)]
    [InlineData(true, false, true, (int)ClimbAction.Jump)]
    [InlineData(false, false, false, (int)ClimbAction.None)]
    public void JumpBeatsDropBeatsUseOnceWithGameplayFocus(bool jump, bool drop, bool use, int expected) =>
        Assert.Equal((ClimbAction)expected, ClimbControls.Resolve(true, jump, drop, use, false));

    [Fact]
    public void AFocusedMenuOwnsEveryPress() =>
        Assert.Equal(ClimbAction.None, ClimbControls.Resolve(false, true, true, true, false));

    [Fact]
    public void InBuildModeTheHammerOwnsUseButJumpAndDropStillDepart()
    {
        Assert.Equal(ClimbAction.None, ClimbControls.Resolve(true, false, false, true, true));
        Assert.Equal(ClimbAction.Drop, ClimbControls.Resolve(true, false, true, true, true));
        Assert.Equal(ClimbAction.Jump, ClimbControls.Resolve(true, true, true, true, true));
    }

    [Fact]
    public void AFreshCrouchPressDropsOnce()
    {
        var trigger = new DropTrigger();
        trigger.Arm(false);
        trigger.Observe(true, true);
        Assert.True(trigger.Take(true));
        Assert.False(trigger.Take(true)); // The same press cannot drop twice.
        trigger.Observe(false, true);
        trigger.Observe(true, true);
        Assert.True(trigger.Take(true));
    }

    [Fact]
    public void ACrouchAlreadyHeldWhenGrabbingIsNotADrop()
    {
        var trigger = new DropTrigger();
        trigger.Arm(true);
        trigger.Observe(true, true);
        Assert.False(trigger.Take(true));
        trigger.Observe(false, true); // Released, then pressed again: now it is a request.
        Assert.True(trigger.Take(true));
    }

    [Fact]
    public void ACrouchHeldThroughAMenuDoesNotDropWhenTheMenuCloses()
    {
        var trigger = new DropTrigger();
        trigger.Arm(false);
        trigger.Observe(true, false); // Pressed while the inventory or a menu had focus.
        trigger.Observe(true, true);  // Focus returns with the key still down; the game reports a crouch edge now.
        Assert.False(trigger.Take(true));
    }

    [Fact]
    public void NoEdgeNoDrop()
    {
        var trigger = new DropTrigger();
        trigger.Arm(false);
        trigger.Observe(true, true);
        Assert.False(trigger.Take(false));
        Assert.True(trigger.Armed);
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]  // A swing outside build mode is combat.
    [InlineData(true, false, true, false)]  // Build mode without a building tool in hand.
    [InlineData(true, true, false, false)]  // Merely holding the hammer is not an action.
    public void OnlyTheNativeHammerSwingIsABuildAction(bool placeMode, bool tool, bool inAttack, bool expected) =>
        Assert.Equal(expected, ClimbControls.IsNativeBuildAction(placeMode, tool, inAttack));

    [Fact]
    public void AnAlignedExtensionAboveIsAdoptedWithoutMovingTheClimber()
    {
        LadderGeometry current = Run(0f, 2f);
        LadderGeometry extended = Run(0f, 4f);
        Assert.True(ClimbControls.CanAdoptRun(current, extended, 1.2f, -0.5f));
    }

    [Fact]
    public void RemovingTheSectionUnderTheClimberEndsTheClimb()
    {
        LadderGeometry current = Run(0f, 4f);
        LadderGeometry shortened = Run(0f, 2f);
        Assert.False(ClimbControls.CanAdoptRun(current, shortened, 3f, -0.5f));
    }

    [Fact]
    public void RemovingAnUnrelatedLowerSectionKeepsTheClimb()
    {
        LadderGeometry current = Run(0f, 4f);
        LadderGeometry upperOnly = Run(2f, 2f);
        Assert.True(ClimbControls.CanAdoptRun(current, upperOnly, 2.6f, 1.5f));
    }

    [Theory]
    [InlineData(2f, 5)]   // One 2 m section: rungs 0..5, the six authored rung boxes.
    [InlineData(4f, 11)]  // Two sections: 12 rungs, unwrapped across the seam.
    [InlineData(0.1f, -1)] // Shorter than the first rung centre: no rung to fit a grip to.
    public void PresentationNeverOffersARungBeyondTheRun(float height, int lastRung) =>
        Assert.Equal(lastRung, ClimbPresentationFrame.LastRungOf(height, 1f / 6f, 1f / 3f));

    [Fact]
    public void TheAuthoredRungBoxIsWhatPresentationIsTold()
    {
        // The piece builder uses these exact values for its rung boxes (0.62 x 0.08 x 0.12 at 0.16 m).
        Assert.Equal(0.62f, 2f * RungShape.HalfWidth, 5);
        Assert.Equal(0.08f, 2f * RungShape.HalfHeight, 5);
        Assert.Equal(0.12f, 2f * RungShape.HalfDepth, 5);
        Assert.Equal(0.16f, RungShape.Plane, 5);
        Assert.Equal(ClimbPolicy.RungHalfWidth, RungShape.HalfWidth);
    }

    [Theory]
    [InlineData(0.5f, 0f, 1f)]   // A parallel shaft half a metre to the side.
    [InlineData(0f, 1f, 0f)]     // The same foot line, turned 90 degrees.
    [InlineData(0f, 0f, -1f)]    // Turned around.
    public void AnotherShaftIsNeverAShortcut(float x, float headingX, float headingZ)
    {
        LadderGeometry current = Run(0f, 2f);
        LadderGeometry other = Run(0f, 4f, x, headingX, headingZ);
        Assert.False(ClimbControls.CanAdoptRun(current, other, 1f, -0.5f));
    }
}
