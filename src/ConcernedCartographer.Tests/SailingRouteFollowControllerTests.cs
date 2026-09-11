using System.Collections.Generic;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;
using Xunit;

namespace TheConcernedCat.ConcernedCartographer.Tests;

public class SailingRouteFollowControllerTests
{
    private static RoadPoint P(float x, float z) => new(x, 0f, z);

    private static RouteFollowPath Path(params RoadPoint[] points)
    {
        Assert.True(RouteFollowPath.TryCreate(
            new List<RoadPoint>(points), out RouteFollowPath? path));
        return path!;
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(120f, -0.666667f)]
    public void ProducesOnlyBoundedVanillaRudderInput(
        float currentYaw,
        float expected)
    {
        var controller = Started();
        SailingRouteFollowStep step = controller.Tick(
            new SailingRouteFollowFrame(P(0f, 0f), currentYaw, 0.02f));

        Assert.True(step.InjectRudder);
        Assert.InRange(step.RudderInput, -1f, 1f);
        Assert.Equal(expected, step.RudderInput, 5);
    }

    [Theory]
    [InlineData("disabled", 1)]
    [InlineData("toggle", 2)]
    [InlineData("manual", 3)]
    [InlineData("exit", 4)]
    [InlineData("route", 5)]
    [InlineData("lifecycle", 6)]
    [InlineData("helm", 7)]
    public void SafetySignalsCancelImmediately(
        string signal,
        int expectedValue)
    {
        var controller = Started();
        var frame = new SailingRouteFollowFrame(
            P(1f, 0f),
            90f,
            0.02f,
            enabled: signal != "disabled",
            togglePressed: signal == "toggle",
            manualInput: signal == "manual",
            exitAction: signal == "exit",
            routeUnchanged: signal != "route",
            lifecycleReady: signal != "lifecycle",
            helmValid: signal != "helm");

        SailingRouteFollowStep step = controller.Tick(frame);

        Assert.False(step.InjectRudder);
        Assert.Equal(
            (SailingRouteFollowCancelReason)expectedValue,
            step.CancelReason);
        Assert.False(controller.IsFollowing);
    }

    [Fact]
    public void OffRouteAndRouteEndFailClosed()
    {
        var offRoute = Started();
        SailingRouteFollowStep off = offRoute.Tick(
            new SailingRouteFollowFrame(P(2f, 31f), 90f, 0.02f));
        Assert.Equal(
            SailingRouteFollowCancelReason.OffRoute,
            off.CancelReason);

        var atEnd = Started();
        SailingRouteFollowStep end = atEnd.Tick(
            new SailingRouteFollowFrame(P(98f, 0f), 90f, 0.02f));
        Assert.Equal(
            SailingRouteFollowCancelReason.RouteEnd,
            end.CancelReason);
    }

    [Fact]
    public void NoProgressCancelsInsteadOfOverridingWindOrObstacle()
    {
        var controller = Started();
        SailingRouteFollowStep step = default;
        for (int second = 0; second < 16; second++)
        {
            step = controller.Tick(
                new SailingRouteFollowFrame(P(1f, 0f), 90f, 1f));
        }

        Assert.False(step.InjectRudder);
        Assert.Equal(
            SailingRouteFollowCancelReason.NoProgressTimeout,
            step.CancelReason);
        Assert.False(controller.IsFollowing);
    }

    [Fact]
    public void ProgressResetsBoundedNoProgressTimer()
    {
        var controller = Started();
        for (int second = 0; second < 10; second++)
        {
            Assert.True(controller.Tick(
                new SailingRouteFollowFrame(P(1f, 0f), 90f, 1f))
                .InjectRudder);
        }

        Assert.True(controller.Tick(
            new SailingRouteFollowFrame(P(2f, 0f), 90f, 1f))
            .InjectRudder);
        for (int second = 0; second < 10; second++)
        {
            Assert.True(controller.Tick(
                new SailingRouteFollowFrame(P(2f, 0f), 90f, 1f))
                .InjectRudder);
        }
    }

    [Fact]
    public void RawInputPolicyCancelsBeforeHelmInjection()
    {
        Assert.Equal(
            SailingRouteFollowCancelReason.ManualInput,
            SailingRouteFollowInputPolicy.Evaluate(
                togglePressed: false,
                manualRudderOrSail: true,
                exitAction: false));
        Assert.Equal(
            SailingRouteFollowCancelReason.ExitAction,
            SailingRouteFollowInputPolicy.Evaluate(
                togglePressed: false,
                manualRudderOrSail: false,
                exitAction: true));
        Assert.Equal(
            SailingRouteFollowCancelReason.TogglePressed,
            SailingRouteFollowInputPolicy.Evaluate(
                togglePressed: true,
                manualRudderOrSail: true,
                exitAction: true));
        Assert.Equal(
            SailingRouteFollowCancelReason.None,
            SailingRouteFollowInputPolicy.Evaluate(false, false, false));
    }

    [Fact]
    public void ControlPolicyPreservesVanillaUnlessInjectionIsValid()
    {
        float vanilla = -0.25f;
        Assert.False(SailingRouteFollowControlPolicy.TryApply(
            default, ref vanilla));
        Assert.Equal(-0.25f, vanilla);

        var invalid = new SailingRouteFollowStep(
            true,
            float.NaN,
            SailingRouteFollowCancelReason.None);
        Assert.False(SailingRouteFollowControlPolicy.TryApply(
            invalid, ref vanilla));
        Assert.Equal(-0.25f, vanilla);

        var bounded = new SailingRouteFollowStep(
            true,
            4f,
            SailingRouteFollowCancelReason.None);
        Assert.True(SailingRouteFollowControlPolicy.TryApply(
            bounded, ref vanilla));
        Assert.Equal(1f, vanilla);
    }

    [Fact]
    public void ReverseDirectionStartsAtFarEndpoint()
    {
        var controller = new SailingRouteFollowController();
        Assert.True(controller.TryStart(
            Path(P(0f, 0f), P(100f, 0f)),
            RouteFollowDirection.Reverse,
            P(99f, 0f)));

        SailingRouteFollowStep step = controller.Tick(
            new SailingRouteFollowFrame(P(99f, 0f), 270f, 0.02f));
        Assert.True(step.InjectRudder);
    }

    [Theory]
    [InlineData(float.NaN, 0.02f)]
    [InlineData(float.PositiveInfinity, 0.02f)]
    [InlineData(0f, float.NaN)]
    [InlineData(0f, float.PositiveInfinity)]
    public void NonFiniteFrameFailsClosed(float yaw, float delta)
    {
        var controller = Started();
        SailingRouteFollowStep step = controller.Tick(
            new SailingRouteFollowFrame(P(0f, 0f), yaw, delta));

        Assert.False(step.InjectRudder);
        Assert.Equal(
            SailingRouteFollowCancelReason.InvalidState,
            step.CancelReason);
    }

    [Fact]
    public void CannotStartWithoutEligibleGeometry()
    {
        var controller = new SailingRouteFollowController();
        Assert.False(controller.TryStart(
            null,
            RouteFollowDirection.Forward,
            P(0f, 0f)));
    }

    private static SailingRouteFollowController Started()
    {
        var controller = new SailingRouteFollowController();
        Assert.True(controller.TryStart(
            Path(P(0f, 0f), P(100f, 0f)),
            RouteFollowDirection.Forward,
            P(0f, 0f)));
        return controller;
    }
}
