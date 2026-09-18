using TheConcernedCat.Ladders;

namespace TheConcernedCat.Shared.Settlement.Tests;

public sealed class ClimbPresentationTests
{
    [Fact]
    public void Pose_blend_follows_alignment_and_idle_is_upright()
    {
        Assert.Equal(0f, ClimbPresentation.PoseBlend(ClimbTelemetry.Idle));
        Assert.Equal(0f, ClimbPresentation.PoseBlend(Telemetry(velocity: 0f, align: -1f)));
        Assert.Equal(0.4f, ClimbPresentation.PoseBlend(Telemetry(velocity: 0f, align: 0.4f)));
        Assert.Equal(1f, ClimbPresentation.PoseBlend(Telemetry(velocity: 0f, align: 2f)));
    }

    [Theory]
    [InlineData(1.25f, 1.25f)]
    [InlineData(-0.8f, -0.8f)]
    [InlineData(0f, 0f)]
    public void Animator_forward_speed_keeps_climb_direction(float velocity, float expected)
    {
        Assert.Equal(expected, ClimbPresentation.AnimatorForwardSpeed(Telemetry(velocity, 1f)));
    }

    [Fact]
    public void Rung_contact_requires_motion_a_new_rung_and_the_rate_limit()
    {
        Assert.False(ClimbPresentation.ShouldPlayRungContact(-1, 0, 1f, 10f));
        Assert.False(ClimbPresentation.ShouldPlayRungContact(2, 2, 1f, 10f));
        Assert.False(ClimbPresentation.ShouldPlayRungContact(2, 3, 0f, 10f));
        Assert.False(ClimbPresentation.ShouldPlayRungContact(2, 3, 1f, 0.1f));
        Assert.True(ClimbPresentation.ShouldPlayRungContact(2, 3, 1f, 0.3f));
        Assert.True(ClimbPresentation.ShouldPlayRungContact(3, 2, -1f, 0.3f));
    }

    private static ClimbTelemetry Telemetry(float velocity, float align) =>
        new ClimbTelemetry(
            ClimbPhase.OnTheLadder,
            velocity < 0f ? ClimbMotion.Descending : velocity > 0f ? ClimbMotion.Climbing : ClimbMotion.Resting,
            progress: 1f,
            height: 3f,
            velocity: velocity,
            animationSpeed: velocity,
            rung: 2,
            alignFraction: align,
            facing: ClimbHeading.FromXz(0f, 1f));
}
