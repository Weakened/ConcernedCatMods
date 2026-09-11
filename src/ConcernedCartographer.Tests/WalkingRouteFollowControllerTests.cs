using System.Collections.Generic;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;
using Xunit;

namespace TheConcernedCat.ConcernedCartographer.Tests;

public class WalkingRouteFollowControllerTests
{
    private static RoadPoint P(float x, float z) => new(x, 0f, z);

    private static RouteFollowPath Path(params RoadPoint[] points)
    {
        Assert.True(RouteFollowPath.TryCreate(
            new List<RoadPoint>(points), out RouteFollowPath? path));
        return path!;
    }

    [Fact]
    public void StartsNearRouteAndProducesBoundedYawOnly()
    {
        var controller = new WalkingRouteFollowController();
        Assert.True(controller.TryStart(
            Path(P(0f, 0f), P(20f, 0f)),
            RouteFollowDirection.Forward, P(0f, 1f)));

        WalkingRouteFollowStep step = controller.Tick(
            new WalkingRouteFollowFrame(P(0f, 1f), 180f, 0.1f));

        Assert.True(step.Steering);
        Assert.False(step.StopVanillaAutorun);
        Assert.Equal(168f, step.DesiredYawDegrees, 3);
    }
    [Theory]
    [InlineData("disabled", 1)]
    [InlineData("toggle", 2)]
    [InlineData("manual", 3)]
    [InlineData("route", 4)]
    [InlineData("lifecycle", 5)]
    [InlineData("movement", 6)]
    public void SafetySignalsCancelImmediately(string signal, int expectedValue)
    {
        var expected = (WalkingRouteFollowCancelReason)expectedValue;
        var controller = Started();
        var frame = new WalkingRouteFollowFrame(
            P(1f, 0f), 0f, 0.02f,
            enabled: signal != "disabled",
            togglePressed: signal == "toggle",
            manualInput: signal == "manual",
            routeUnchanged: signal != "route",
            lifecycleReady: signal != "lifecycle",
            eligibleMovement: signal != "movement");

        WalkingRouteFollowStep step = controller.Tick(frame);

        Assert.False(step.Steering);
        Assert.True(step.StopVanillaAutorun);
        Assert.Equal(expected, step.CancelReason);
        Assert.False(controller.IsFollowing);
    }
    [Fact]
    public void OffRouteAndRouteEndFailClosed()
    {
        var offRoute = Started();
        WalkingRouteFollowStep off = offRoute.Tick(
            new WalkingRouteFollowFrame(P(2f, 30f), 0f, 0.02f));
        Assert.Equal(WalkingRouteFollowCancelReason.OffRoute, off.CancelReason);

        var atEnd = Started();
        WalkingRouteFollowStep end = atEnd.Tick(
            new WalkingRouteFollowFrame(P(19.5f, 0f), 0f, 0.02f));
        Assert.Equal(WalkingRouteFollowCancelReason.RouteEnd, end.CancelReason);
    }

    [Fact]
    public void BlockedWithoutProgressCancelsAfterTimeout()
    {
        var controller = Started();
        WalkingRouteFollowStep step = default;
        for (int index = 0; index < 6; index++)
        {
            step = controller.Tick(new WalkingRouteFollowFrame(
                P(1f, 0f), 0f, 0.5f, blocked: true));
        }

        Assert.Equal(WalkingRouteFollowCancelReason.Stuck, step.CancelReason);
        Assert.True(step.StopVanillaAutorun);
    }

    [Fact]
    public void ReverseDirectionCanStartNearFarEndpoint()
    {
        var controller = new WalkingRouteFollowController();
        Assert.True(controller.TryStart(
            Path(P(0f, 0f), P(20f, 0f)),
            RouteFollowDirection.Reverse, P(19f, 0f)));

        WalkingRouteFollowStep step = controller.Tick(
            new WalkingRouteFollowFrame(P(19f, 0f), 270f, 0.1f));
        Assert.True(step.Steering);
        Assert.True(controller.IsFollowing);
    }

    [Fact]
    public void CannotStartWithoutEligibleGeometry()
    {
        var controller = new WalkingRouteFollowController();
        Assert.False(controller.TryStart(
            null, RouteFollowDirection.Forward, P(0f, 0f)));
        Assert.False(controller.IsFollowing);
    }

    private static WalkingRouteFollowController Started()
    {
        var controller = new WalkingRouteFollowController();
        Assert.True(controller.TryStart(
            Path(P(0f, 0f), P(20f, 0f)),
            RouteFollowDirection.Forward, P(0f, 0f)));
        return controller;
    }
}
