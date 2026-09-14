using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>#243 sailing Route Follow: deterministic control, geometry,
/// safety, same-call cancellation, lifecycle and steady-state tests.
///
/// The closed-loop cases drive a small SHIP MODEL, not Valheim. It
/// reproduces exactly the vanilla equation the controller talks to —
/// <c>m_rudderValue += dir.x * lerp(0.5,1,|m_rudderValue|) * m_rudderSpeed *
/// dt</c>, clamped to [-1,1], read from the installed
/// <c>Ship.ApplyControlls</c> — and models the hull as a FIRST-ORDER lag on
/// yaw rate whose turn authority scales with speed, because vanilla applies a
/// torque impulse to a rigidbody with angular damping
/// (<c>m_stearForce</c>/<c>m_stearVelForceFactor</c> scaled by forward
/// speed). A zero-lag model would flatter a proportional-only controller, so
/// the stability cases sweep the lag and the speed rather than fixing both at
/// their most forgiving values.
///
/// <c>m_rudderSpeed = 0.5</c> is the <c>Ship</c> field default; per-prefab
/// values for raft/karve/longship/Drakkar live in soft-referenced asset
/// bundles and were NOT read, so that constant is an assumption.
///
/// None of this is live multiplayer or physics evidence and it is never
/// presented as such.</summary>
public class SailingRouteFollowControllerTests
{
    private const float FixedDelta = 0.02f;
    private const float VanillaRudderSpeed = 0.5f;

    private static RouteFollowPath Path(params (float X, float Z)[] points)
    {
        var road = new List<RoadPoint>();
        foreach ((float x, float z) in points)
        {
            road.Add(new RoadPoint(x, 0f, z));
        }

        Assert.True(RouteFollowPath.TryCreate(road, out RouteFollowPath? path));
        return path!;
    }

    private static SailingRouteFollowFrame Frame(
        float x, float z, float heading, float rudder,
        bool enabled = true, bool helmGranted = true, bool togglePressed = false,
        bool manualRudderInput = false, bool manualSailInput = false,
        bool exitInput = false, bool routeUnchanged = true, bool lifecycleReady = true,
        float delta = FixedDelta)
    {
        return new SailingRouteFollowFrame(
            new RoadPoint(x, 0f, z), heading, rudder, delta,
            enabled, helmGranted, togglePressed, manualRudderInput,
            manualSailInput, exitInput, routeUnchanged, lifecycleReady);
    }

    private static SailingRouteFollowController Started(
        RouteFollowPath path, float x = 0f, float z = 0f,
        RouteFollowDirection direction = RouteFollowDirection.Forward)
    {
        var controller = new SailingRouteFollowController();
        Assert.True(controller.TryStart(path, direction, new RoadPoint(x, 0f, z)));
        return controller;
    }

    // ---------------------------------------------------------- start gates

    [Fact]
    public void DoesNotStartWithoutARoute()
    {
        var controller = new SailingRouteFollowController();
        Assert.False(controller.TryStart(null, RouteFollowDirection.Forward, new RoadPoint(0f, 0f, 0f)));
        Assert.False(controller.IsFollowing);
    }

    [Fact]
    public void DoesNotStartFarOffTheRoute()
    {
        var controller = new SailingRouteFollowController();
        Assert.False(controller.TryStart(
            Path((0f, 0f), (0f, 400f)),
            RouteFollowDirection.Forward,
            new RoadPoint(SailingRouteFollowController.MaximumCrossTrackMeters + 5f, 0f, 0f)));
    }

    [Fact]
    public void DoesNotStartAtTheRouteEnd()
    {
        var controller = new SailingRouteFollowController();
        Assert.False(controller.TryStart(
            Path((0f, 0f), (0f, 400f)),
            RouteFollowDirection.Forward,
            new RoadPoint(0f, 0f, 400f)));
    }

    [Fact]
    public void TickBeforeStartDoesNothing()
    {
        var controller = new SailingRouteFollowController();
        SailingRouteFollowStep step = controller.Tick(Frame(0f, 0f, 0f, 0f));

        Assert.False(step.Steering);
        Assert.False(step.Cancelled);
        Assert.Equal(0f, step.RudderInput);
    }

    // ------------------------------------------------------------- control

    [Fact]
    public void OnCourseAndOnRudderWritesNothingNew()
    {
        SailingRouteFollowController controller = Started(Path((0f, 0f), (0f, 400f)));
        SailingRouteFollowStep step = controller.Tick(Frame(0f, 0f, heading: 0f, rudder: 0f));

        Assert.False(step.Steering);
        Assert.False(step.Cancelled);
        Assert.Equal(0f, step.RudderInput);
        Assert.True(controller.IsFollowing);
    }

    [Theory]
    // Route runs north; the ship is heading off to one side. The rudder-rate
    // sign must be the one that turns it back.
    [InlineData(30f, -1f)]
    [InlineData(-30f, 1f)]
    [InlineData(5f, -1f)]
    [InlineData(-5f, 1f)]
    public void SteersTowardTheRouteBearing(float heading, float expectedSign)
    {
        SailingRouteFollowController controller = Started(Path((0f, 0f), (0f, 400f)));
        SailingRouteFollowStep step = controller.Tick(Frame(0f, 0f, heading, rudder: 0f));

        Assert.True(step.Steering);
        Assert.Equal(expectedSign, Math.Sign(step.RudderInput));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(45f)]
    [InlineData(120f)]
    [InlineData(179f)]
    [InlineData(-179f)]
    [InlineData(270f)]
    public void RudderInputIsAlwaysWithinTheVanillaAxisRange(float heading)
    {
        SailingRouteFollowController controller = Started(Path((0f, 0f), (0f, 400f)));
        SailingRouteFollowStep step = controller.Tick(Frame(0f, 0f, heading, rudder: 0f));

        Assert.InRange(step.RudderInput, -1f, 1f);
    }

    [Fact]
    public void HoldsTheRudderInsideTheDeadband()
    {
        // Heading error asks for roughly half rudder; the rudder is already
        // there, so vanilla is left exactly where it is.
        SailingRouteFollowController controller = Started(Path((0f, 0f), (0f, 400f)));
        float heading = -SailingRouteFollowController.FullRudderHeadingErrorDegrees / 2f;
        SailingRouteFollowStep onRudder = controller.Tick(Frame(0f, 0f, heading, rudder: 0.5f));

        Assert.False(onRudder.Steering);
        Assert.False(onRudder.Cancelled);
        Assert.Equal(0f, onRudder.RudderInput);
        Assert.True(controller.IsFollowing);
    }

    [Fact]
    public void AsksForOppositeRudderWhenTheHelmIsOverCorrected()
    {
        SailingRouteFollowController controller = Started(Path((0f, 0f), (0f, 400f)));
        // Dead on course but the rudder is hard over: unwind it.
        SailingRouteFollowStep step = controller.Tick(Frame(0f, 0f, heading: 0f, rudder: 1f));

        Assert.True(step.Steering);
        Assert.True(step.RudderInput < 0f);
    }

    [Fact]
    public void FollowsAReversedRouteFromItsFarEnd()
    {
        RouteFollowPath path = Path((0f, 0f), (0f, 400f));
        SailingRouteFollowController controller =
            Started(path, 0f, 400f, RouteFollowDirection.Reverse);

        // Heading south is the correct bearing when travelling in reverse:
        // on course, so the rudder is left exactly where vanilla put it.
        SailingRouteFollowStep step = controller.Tick(Frame(0f, 400f, heading: 180f, rudder: 0f));
        Assert.False(step.Steering);
        Assert.False(step.Cancelled);
        Assert.True(controller.IsFollowing);
    }

    // -------------------------------------------------------------- policy

    [Fact]
    public void PolicyWritesOnlyTheRudderAxisAndOnlyWhenSteering()
    {
        float axis = 0.75f;
        Assert.False(SailingRouteFollowControlPolicy.TryApply(
            new SailingRouteFollowStep(false, 0.5f, false, SailingRouteFollowCancelReason.None),
            ref axis));
        Assert.Equal(0.75f, axis);

        Assert.True(SailingRouteFollowControlPolicy.TryApply(
            new SailingRouteFollowStep(true, -0.25f, false, SailingRouteFollowCancelReason.None),
            ref axis));
        Assert.Equal(-0.25f, axis);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(1.5f)]
    [InlineData(-1.5f)]
    public void PolicyRefusesAnyValueOutsideTheVanillaAxis(float rudder)
    {
        float axis = 0.3f;
        Assert.False(SailingRouteFollowControlPolicy.TryApply(
            new SailingRouteFollowStep(true, rudder, false, SailingRouteFollowCancelReason.None),
            ref axis));
        Assert.Equal(0.3f, axis);
    }

    [Fact]
    public void PolicyRefusesACancelledStep()
    {
        float axis = 0f;
        Assert.False(SailingRouteFollowControlPolicy.TryApply(
            new SailingRouteFollowStep(false, 0f, true, SailingRouteFollowCancelReason.ExitInput),
            ref axis));
        Assert.Equal(0f, axis);
    }

    // ------------------------------------------- same-call cancellation

    [Theory]
    [InlineData("manualRudder")]
    [InlineData("manualSail")]
    [InlineData("exit")]
    [InlineData("toggle")]
    [InlineData("helmLost")]
    [InlineData("routeChanged")]
    [InlineData("lifecycle")]
    [InlineData("disabled")]
    public void CancelsInTheSameCallAndAuthorisesNoWrite(string condition)
    {
        SailingRouteFollowCancelReason expected = condition switch
        {
            "manualRudder" => SailingRouteFollowCancelReason.ManualRudderInput,
            "manualSail" => SailingRouteFollowCancelReason.ManualSailInput,
            "exit" => SailingRouteFollowCancelReason.ExitInput,
            "toggle" => SailingRouteFollowCancelReason.TogglePressed,
            "helmLost" => SailingRouteFollowCancelReason.HelmLost,
            "routeChanged" => SailingRouteFollowCancelReason.RouteChanged,
            "lifecycle" => SailingRouteFollowCancelReason.Lifecycle,
            _ => SailingRouteFollowCancelReason.Disabled,
        };

        SailingRouteFollowController controller = Started(Path((0f, 0f), (0f, 400f)));
        SailingRouteFollowStep step = controller.Tick(Frame(
            0f, 0f, heading: 40f, rudder: 0f,
            enabled: condition != "disabled",
            helmGranted: condition != "helmLost",
            togglePressed: condition == "toggle",
            manualRudderInput: condition == "manualRudder",
            manualSailInput: condition == "manualSail",
            exitInput: condition == "exit",
            routeUnchanged: condition != "routeChanged",
            lifecycleReady: condition != "lifecycle"));

        Assert.True(step.Cancelled);
        Assert.False(step.Steering);
        Assert.Equal(expected, step.CancelReason);
        Assert.False(controller.IsFollowing);

        float axis = 0.9f;
        Assert.False(SailingRouteFollowControlPolicy.TryApply(step, ref axis));
        Assert.Equal(0.9f, axis);
    }

    [Fact]
    public void CancellationOrderPutsManualInputAheadOfEverySoftCondition()
    {
        // A frame that is simultaneously off-route-eligible and manually
        // steered must report the manual input, never a geometry reason.
        SailingRouteFollowController controller = Started(Path((0f, 0f), (0f, 400f)));
        SailingRouteFollowStep step = controller.Tick(Frame(
            5000f, 5000f, heading: 0f, rudder: 0f, manualRudderInput: true));

        Assert.Equal(SailingRouteFollowCancelReason.ManualRudderInput, step.CancelReason);
    }

    [Fact]
    public void CancelWithReportsOnceAndThenGoesQuiet()
    {
        SailingRouteFollowController controller = Started(Path((0f, 0f), (0f, 400f)));

        SailingRouteFollowStep first = controller.CancelWith(SailingRouteFollowCancelReason.Lifecycle);
        Assert.True(first.Cancelled);
        Assert.Equal(SailingRouteFollowCancelReason.Lifecycle, first.CancelReason);

        SailingRouteFollowStep second = controller.CancelWith(SailingRouteFollowCancelReason.Lifecycle);
        Assert.False(second.Cancelled);
        Assert.Equal(SailingRouteFollowCancelReason.None, second.CancelReason);
    }

    // -------------------------------------------------------------- safety

    [Fact]
    public void CancelsWhenPushedBeyondTheCrossTrackBound()
    {
        SailingRouteFollowController controller = Started(Path((0f, 0f), (0f, 400f)));
        SailingRouteFollowStep step = controller.Tick(Frame(
            SailingRouteFollowController.MaximumCrossTrackMeters + 10f, 50f, 0f, 0f));

        Assert.Equal(SailingRouteFollowCancelReason.OffRoute, step.CancelReason);
        Assert.False(controller.IsFollowing);
    }

    [Fact]
    public void CancelsAtTheRouteEnd()
    {
        SailingRouteFollowController controller = Started(Path((0f, 0f), (0f, 400f)));
        SailingRouteFollowStep step = controller.Tick(Frame(0f, 400f, 0f, 0f));

        Assert.Equal(SailingRouteFollowCancelReason.RouteEnd, step.CancelReason);
        Assert.False(controller.IsFollowing);
    }

    [Fact]
    public void AdverseWindOrObstacleCancelsThroughTheNoProgressBound()
    {
        // Becalmed, head to wind, or nosed into a rock: the ship simply stops
        // gaining ground. No wind heuristic is inferred.
        SailingRouteFollowController controller = Started(Path((0f, 0f), (0f, 400f)));
        SailingRouteFollowStep step = default;
        int ticks = 0;
        while (controller.IsFollowing && ticks < 5000)
        {
            step = controller.Tick(Frame(0f, 0f, heading: 0f, rudder: 0f));
            ticks++;
        }

        Assert.Equal(SailingRouteFollowCancelReason.NoProgressTimeout, step.CancelReason);
        Assert.InRange(
            ticks * FixedDelta,
            SailingRouteFollowController.NoProgressTimeoutSeconds - 0.1f,
            SailingRouteFollowController.NoProgressTimeoutSeconds + 0.1f);
    }

    [Fact]
    public void RealProgressKeepsTheNoProgressTimerFromFiring()
    {
        SailingRouteFollowController controller = Started(Path((0f, 0f), (0f, 400f)));
        float z = 0f;
        for (int tick = 0; tick < 2000; tick++)
        {
            z += 0.12f;
            SailingRouteFollowStep step = controller.Tick(Frame(0f, z, 0f, 0f));
            Assert.False(step.Cancelled);
        }

        Assert.True(controller.IsFollowing);
        Assert.True(controller.NoProgressSeconds < SailingRouteFollowController.NoProgressTimeoutSeconds);
    }

    [Theory]
    [InlineData(float.NaN, 0f, 0f, 0f)]
    [InlineData(0f, float.PositiveInfinity, 0f, 0f)]
    [InlineData(0f, 0f, float.NaN, 0f)]
    [InlineData(0f, 0f, 0f, float.NaN)]
    [InlineData(0f, 0f, 0f, -0.02f)]
    public void NonFiniteOrImpossibleFramesFailClosed(
        float x, float heading, float rudder, float delta)
    {
        SailingRouteFollowController controller = Started(Path((0f, 0f), (0f, 400f)));
        SailingRouteFollowStep step = controller.Tick(
            Frame(x, 0f, heading, rudder, delta: delta));

        Assert.True(step.Cancelled);
        Assert.Equal(SailingRouteFollowCancelReason.InvalidState, step.CancelReason);
        Assert.False(controller.IsFollowing);
    }

    [Theory]
    [InlineData(1.5f)]
    [InlineData(-1.5f)]
    public void AnImpossibleRudderReadingFailsClosed(float rudder)
    {
        SailingRouteFollowController controller = Started(Path((0f, 0f), (0f, 400f)));
        SailingRouteFollowStep step = controller.Tick(Frame(0f, 0f, 0f, rudder));

        Assert.Equal(SailingRouteFollowCancelReason.InvalidState, step.CancelReason);
    }

    // --------------------------------------------- closed-loop behaviour

    /// <summary>The vanilla rudder integrator plus a first-order hull lag.
    /// Deterministic; not the game.</summary>
    private sealed class ShipModel
    {
        public float X;
        public float Z;
        public float HeadingDegrees;
        public float RudderValue;
        public float YawRateDegreesPerSecond;
        public float SpeedMetersPerSecond = 6f;

        /// <summary>Yaw rate at full rudder and <see cref="ReferenceSpeed"/>.</summary>
        public float FullRudderYawRateDegreesPerSecond = 20f;

        /// <summary>Time constant of the hull's yaw response. 0 reproduces
        /// the old zero-inertia model; real hulls are seconds.</summary>
        public float YawLagSeconds;

        /// <summary>Vanilla steering force scales with forward speed, so turn
        /// authority does too.</summary>
        public const float ReferenceSpeed = 6f;

        public void Step(float rudderInput)
        {
            float gain = 0.5f + (0.5f * Math.Min(1f, Math.Abs(RudderValue)));
            RudderValue += rudderInput * gain * VanillaRudderSpeed * FixedDelta;
            RudderValue = Math.Max(-1f, Math.Min(1f, RudderValue));

            // Positive m_rudderValue turns the bow to starboard: vanilla
            // applies -m_rudderValue * transform.right at m_stearForceOffset
            // (-10 m, i.e. astern), whose Y torque is positive.
            float commandedYawRate = RudderValue *
                FullRudderYawRateDegreesPerSecond *
                (SpeedMetersPerSecond / ReferenceSpeed);
            if (YawLagSeconds <= 0f)
            {
                YawRateDegreesPerSecond = commandedYawRate;
            }
            else
            {
                float blend = Math.Min(1f, FixedDelta / YawLagSeconds);
                YawRateDegreesPerSecond +=
                    (commandedYawRate - YawRateDegreesPerSecond) * blend;
            }

            HeadingDegrees += YawRateDegreesPerSecond * FixedDelta;
            double radians = HeadingDegrees * Math.PI / 180d;
            X += (float)(Math.Sin(radians) * SpeedMetersPerSecond * FixedDelta);
            Z += (float)(Math.Cos(radians) * SpeedMetersPerSecond * FixedDelta);
        }
    }

    private readonly record struct SailResult(
        SailingRouteFollowCancelReason Reason,
        float WorstCrossTrack,
        int Ticks,
        int RudderSignReversals);

    private static SailResult Sail(
        RouteFollowPath path, ShipModel ship, int maxTicks = 20000)
    {
        var controller = new SailingRouteFollowController();
        Assert.True(controller.TryStart(
            path, RouteFollowDirection.Forward, new RoadPoint(ship.X, 0f, ship.Z)));

        float worst = 0f;
        int reversals = 0;
        int lastSign = 0;
        for (int tick = 0; tick < maxTicks; tick++)
        {
            SailingRouteFollowStep step = controller.Tick(
                Frame(ship.X, ship.Z, ship.HeadingDegrees, ship.RudderValue));
            if (step.Cancelled)
            {
                return new SailResult(step.CancelReason, worst, tick, reversals);
            }

            worst = Math.Max(worst, CrossTrack(path, ship.X, ship.Z));
            float axis = 0f;
            SailingRouteFollowControlPolicy.TryApply(step, ref axis);
            int sign = Math.Sign(axis);
            if (sign != 0)
            {
                if (lastSign != 0 && sign != lastSign)
                {
                    reversals++;
                }

                lastSign = sign;
            }

            ship.Step(axis);
        }

        return new SailResult(SailingRouteFollowCancelReason.None, worst, maxTicks, reversals);
    }

    private static float CrossTrack(RouteFollowPath path, float x, float z)
    {
        float best = float.MaxValue;
        for (int index = 0; index <= path.LastSegmentIndex; index++)
        {
            RoadPoint from = path[index];
            RoadPoint to = path[index + 1];
            float dx = to.X - from.X;
            float dz = to.Z - from.Z;
            float lengthSquared = (dx * dx) + (dz * dz);
            float fraction = lengthSquared <= 0f
                ? 0f
                : Math.Max(0f, Math.Min(1f,
                    (((x - from.X) * dx) + ((z - from.Z) * dz)) / lengthSquared));
            float px = from.X + (dx * fraction);
            float pz = from.Z + (dz * fraction);
            best = Math.Min(best, (float)Math.Sqrt(
                ((x - px) * (x - px)) + ((z - pz) * (z - pz))));
        }

        return best;
    }

    // The stability sweep. A proportional-only controller passes only the
    // (0 s, 6 m/s) corner of this table; the yaw-rate damping term and the
    // speed-scaled look-ahead are what make the rest hold.
    public static TheoryData<float, float> HullSweep => new()
    {
        { 0f, 6f },
        { 1f, 6f },
        { 2f, 6f },
        { 0f, 10f },
        { 1f, 10f },
        { 2f, 10f },
        { 1f, 12f },
        { 2f, 12f },
    };

    [Theory]
    [MemberData(nameof(HullSweep))]
    public void ConvergesOntoAStraightRouteAndReachesItsEnd(float yawLag, float speed)
    {
        RouteFollowPath path = Path((0f, 0f), (0f, 1200f));
        var ship = new ShipModel
        {
            X = 20f,
            Z = 0f,
            HeadingDegrees = 0f,
            YawLagSeconds = yawLag,
            SpeedMetersPerSecond = speed,
        };

        SailResult result = Sail(path, ship);

        Assert.Equal(SailingRouteFollowCancelReason.RouteEnd, result.Reason);
        Assert.True(result.WorstCrossTrack <= SailingRouteFollowController.MaximumCrossTrackMeters,
            $"worst cross-track {result.WorstCrossTrack} m exceeded the bound (lag {yawLag}s, {speed} m/s)");
        Assert.True(Math.Abs(ship.X) < 15f,
            $"did not converge onto the line: x={ship.X} (lag {yawLag}s, {speed} m/s)");
    }

    [Theory]
    [MemberData(nameof(HullSweep))]
    public void RoundsAnIslandCornerWithinTheBoundedTolerance(float yawLag, float speed)
    {
        // The classic case from #104: a hand-drawn route that bends around an
        // island must not be replaced by a straight line to the destination.
        RouteFollowPath path = Path((0f, 0f), (0f, 400f), (400f, 400f), (400f, 800f));
        var ship = new ShipModel
        {
            X = 0f,
            Z = 0f,
            HeadingDegrees = 0f,
            YawLagSeconds = yawLag,
            SpeedMetersPerSecond = speed,
        };

        SailResult result = Sail(path, ship);

        Assert.Equal(SailingRouteFollowCancelReason.RouteEnd, result.Reason);
        Assert.True(result.WorstCrossTrack <= SailingRouteFollowController.MaximumCrossTrackMeters,
            $"corner cut by {result.WorstCrossTrack} m, beyond the {SailingRouteFollowController.MaximumCrossTrackMeters} m tolerance (lag {yawLag}s, {speed} m/s)");
    }

    [Theory]
    [MemberData(nameof(HullSweep))]
    public void FollowsAMultiWaypointRouteWithoutHunting(float yawLag, float speed)
    {
        RouteFollowPath path = Path(
            (0f, 0f), (0f, 300f), (200f, 460f), (200f, 760f), (400f, 920f));
        var ship = new ShipModel
        {
            X = 0f,
            Z = 0f,
            HeadingDegrees = 0f,
            YawLagSeconds = yawLag,
            SpeedMetersPerSecond = speed,
        };

        SailResult result = Sail(path, ship);

        Assert.Equal(SailingRouteFollowCancelReason.RouteEnd, result.Reason);
        Assert.True(result.WorstCrossTrack <= SailingRouteFollowController.MaximumCrossTrackMeters,
            $"worst cross-track {result.WorstCrossTrack} m exceeded the bound (lag {yawLag}s, {speed} m/s)");

        // Hunting is what a proportional-only controller does on a lagging
        // hull, so assert it directly rather than only bounding the error.
        // Three corners cannot need more than a handful of reversals each.
        Assert.True(result.RudderSignReversals <= 24,
            $"rudder hunted: {result.RudderSignReversals} sign reversals (lag {yawLag}s, {speed} m/s)");
    }

    [Fact]
    public void ADriftingShipCancelsInsteadOfFightingTheRoute()
    {
        // A hull that cannot turn (broken steering / hard current) must be
        // released, not steered at forever.
        RouteFollowPath path = Path((0f, 0f), (0f, 600f));
        var ship = new ShipModel
        {
            X = 0f,
            Z = 0f,
            HeadingDegrees = 90f,
            FullRudderYawRateDegreesPerSecond = 0f,
        };

        SailResult result = Sail(path, ship);

        Assert.Equal(SailingRouteFollowCancelReason.OffRoute, result.Reason);
    }

    // ------------------------------------------------------ steady state

    [Fact]
    public void SteadyStateTickAllocatesNothing()
    {
        RouteFollowPath path = Path((0f, 0f), (0f, 200f), (120f, 320f), (120f, 520f));
        SailingRouteFollowController controller = Started(path);

        // Warm up so no first-call JIT allocation is measured.
        for (int tick = 0; tick < 100; tick++)
        {
            controller.Tick(Frame(0f, tick * 0.1f, 2f, 0.1f));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int tick = 0; tick < 1000; tick++)
        {
            controller.Tick(Frame(0f, 10f + (tick * 0.1f), 2f, 0.1f));
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Without these the test would pass vacuously if the controller had
        // cancelled during warm-up: a cancelled Tick returns default and
        // allocates nothing while measuring nothing.
        Assert.True(controller.IsFollowing, "controller cancelled; nothing was measured");
        Assert.True(allocated == 0, $"steady-state Tick allocated {allocated} bytes");
    }

    [Fact]
    public void DeadbandAuthorisesNoWriteAtAll()
    {
        // Holding exactly what vanilla holds means writing nothing, not
        // writing zero: a zero write would still overwrite the raw axis.
        SailingRouteFollowController controller = Started(Path((0f, 0f), (0f, 400f)));
        SailingRouteFollowStep step = controller.Tick(Frame(0f, 0f, heading: 0f, rudder: 0f));

        Assert.False(step.Steering);
        Assert.False(step.Cancelled);

        float axis = 0.004f;
        Assert.False(SailingRouteFollowControlPolicy.TryApply(step, ref axis));
        Assert.Equal(0.004f, axis);
        Assert.True(controller.IsFollowing);
    }

    [Fact]
    public void DerivedSpeedAndYawRateTrackTheHull()
    {
        RouteFollowPath path = Path((0f, 0f), (0f, 600f));
        var ship = new ShipModel { X = 0f, Z = 0f, HeadingDegrees = 0f, SpeedMetersPerSecond = 9f };
        var controller = new SailingRouteFollowController();
        Assert.True(controller.TryStart(path, RouteFollowDirection.Forward, new RoadPoint(0f, 0f, 0f)));

        for (int tick = 0; tick < 400; tick++)
        {
            SailingRouteFollowStep step = controller.Tick(
                Frame(ship.X, ship.Z, ship.HeadingDegrees, ship.RudderValue));
            Assert.False(step.Cancelled);
            float axis = 0f;
            SailingRouteFollowControlPolicy.TryApply(step, ref axis);
            ship.Step(axis);
        }

        Assert.InRange(controller.DerivedSpeedMetersPerSecond, 8.5f, 9.5f);
        Assert.InRange(
            controller.LookAheadMeters,
            SailingRouteFollowController.MinimumLookAheadMeters,
            SailingRouteFollowController.MaximumLookAheadMeters);
    }

    [Fact]
    public void ATeleportSizedJumpCannotInjectAWildCommand()
    {
        SailingRouteFollowController controller = Started(Path((0f, 0f), (0f, 4000f)));
        for (int tick = 0; tick < 50; tick++)
        {
            controller.Tick(Frame(0f, tick * 0.12f, 0f, 0f));
        }

        // A 2 km jump in one frame is not a speed; it must be rejected by the
        // plausibility bound rather than blowing up the look-ahead.
        controller.Tick(Frame(0f, 2000f, 0f, 0f));

        Assert.True(controller.DerivedSpeedMetersPerSecond < 60f);
        Assert.InRange(
            controller.LookAheadMeters,
            SailingRouteFollowController.MinimumLookAheadMeters,
            SailingRouteFollowController.MaximumLookAheadMeters);
    }
}
