using System.Reflection;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.ConcernedTeamster.Domain.Workers;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>The idle hammer, driven end to end through its game-free half
/// (owner decision 2026-10-31, <c>DECISIONS.md</c> D19).
///
/// <c>CartUpkeepIdleTests</c> proves the decision changes nothing. These prove
/// the step after it does not either: the driver is run beside a <b>real</b>
/// haul executor holding a real lease, for an in-game hour, and afterwards the
/// cart, the lease, the phase, the identity hold, the executor's revision and a
/// carried-material ledger are exactly what they were. The only things that
/// happened are his own body's walk, turn and stop, and the swing callback.
///
/// The structural half says why that is not luck: the driver is constructed
/// from a decision, a motor of three own-body calls and one callback, and is
/// handed the facts as a value - never the executor, a cart or an inventory.
/// </summary>
public sealed class CartUpkeepDriverTests
{
    private const float Step = HaulingExecutionRig.Step;

    [Fact]
    public void An_hour_beside_his_idle_cart_changes_nothing_but_the_swing_count()
    {
        var rig = new HaulingExecutionRig();
        Assert.Equal(AssignmentOutcome.Assigned, rig.Assign().Outcome);
        Assert.Equal(HaulPhase.Ready, rig.Executor.Phase);
        rig.Executor.ObserveFrame();
        rig.Journal.Clear();

        var ledger = new CargoLedger();
        ledger.Take("t1", "Stone", 7);
        var carried = ledger.Holding(CargoPlace.Carried).ToArray();

        var motor = new WalkingMotor(rig.Body);
        int swings = 0;
        var driver = new CartUpkeepDriver(new CartUpkeepIdle(intervalSeconds: 45d), motor, () => swings++);

        CartCondition cartBefore = Facts(rig).Cart;
        CartLease leaseBefore = rig.Executor.ActiveLease!;
        int revisionBefore = rig.Executor.Revision;
        int holdRevisionBefore = rig.Executor.Modes.Revision;
        int attachCallsBefore = rig.Seam.AttachCalls;
        int releaseCallsBefore = rig.Seam.ReleaseCalls;

        Run(rig, driver, seconds: 3600f);

        Assert.True(swings > 0, "an hour beside his own idle cart should produce some swinging");
        Assert.Equal(swings, driver.Swings);
        Assert.True(motor.Walks > 0, "he started six metres away, so he must have walked over");

        Assert.Equal(cartBefore, Facts(rig).Cart);
        Assert.Same(leaseBefore, rig.Executor.ActiveLease);
        Assert.True(leaseBefore.IsActive);
        Assert.Equal(HaulPhase.Ready, rig.Executor.Phase);
        Assert.Equal(revisionBefore, rig.Executor.Revision);
        Assert.Equal(string.Empty, rig.Executor.HaulId);
        Assert.False(rig.Executor.Attached);
        Assert.Null(rig.Executor.Modes.JobId);
        Assert.Equal(ActorMode.Resting, rig.Executor.Modes.Mode);
        Assert.Equal(holdRevisionBefore, rig.Executor.Modes.Revision);
        Assert.Equal(attachCallsBefore, rig.Seam.AttachCalls);
        Assert.Equal(releaseCallsBefore, rig.Seam.ReleaseCalls);
        Assert.Equal(carried, ledger.Holding(CargoPlace.Carried).ToArray());
        Assert.True(ledger.IsConserved);

        // Every port call of the hour was his own body walking, turning or
        // stopping: no attach, no release, no ground read, no calibration, no
        // retirement.
        Assert.All(rig.Journal, entry => Assert.True(
            entry.StartsWith("WalkTo ", StringComparison.Ordinal) || entry == "Face" || entry == "Stop",
            "an idle hour made a port call that is not his own body's motion: " + entry));
        rig.AssertNoBugs();
    }

    [Fact]
    public void He_walks_over_faces_the_cart_and_only_then_swings()
    {
        var rig = AssignedRig();
        var motor = new WalkingMotor(rig.Body);
        var seen = new List<CartUpkeepAction>();
        var driver = new CartUpkeepDriver(new CartUpkeepIdle(intervalSeconds: 5d), motor, () => { });

        for (int index = 0; index < (int)(30f / Step); index++)
        {
            rig.Clock.Now += Step;
            rig.Executor.ObserveFrame();
            CartUpkeepAction action = driver.Step(Facts(rig), rig.Clock.Now);
            if (seen.Count == 0 || seen[seen.Count - 1] != action)
            {
                seen.Add(action);
            }

            if (action == CartUpkeepAction.Nothing)
            {
                rig.Executor.Tick();
            }
        }

        int walk = seen.IndexOf(CartUpkeepAction.WalkToCart);
        int face = seen.IndexOf(CartUpkeepAction.FaceCart);
        int swing = seen.IndexOf(CartUpkeepAction.PlayHammerOnce);
        Assert.True(walk >= 0 && face > walk && swing > face, string.Join(", ", seen));
        Assert.True(rig.Body.Facts.Position.HorizontalDistanceTo(rig.Seam.Observation.CartPosition) <= 2.5f);
    }

    [Fact]
    public void The_swing_stops_him_and_turns_him_to_the_cart_before_the_trigger()
    {
        var rig = AssignedRig();
        rig.Body.Facts = rig.Body.Facts.With(f => f.Position = new WorkPoint(1f, 0f, 1f));
        var order = new List<string>();
        var motor = new WalkingMotor(rig.Body, order);
        var driver = new CartUpkeepDriver(new CartUpkeepIdle(intervalSeconds: 1d), motor, () => order.Add("swing"));

        RunUntilSwing(rig, driver);

        int swing = order.IndexOf("swing");
        Assert.True(swing >= 2, string.Join(", ", order));
        Assert.Equal("Stop", order[swing - 2]);
        Assert.Equal("Face", order[swing - 1]);
        Assert.True(motor.LastFaceX < 0f && motor.LastFaceZ < 0f, "he faced away from a cart at the origin");
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("authority")]
    [InlineData("body")]
    [InlineData("far")]
    [InlineData("hitched")]
    [InlineData("held")]
    public void A_gate_that_does_not_hold_means_no_swing_no_motion_and_the_executor_keeps_the_tick(string gate)
    {
        var rig = AssignedRig();
        rig.Body.Facts = rig.Body.Facts.With(f => f.Position = new WorkPoint(0f, 0f, 1f));
        bool switchedOn = true;
        switch (gate)
        {
            case "switch":
                switchedOn = false;
                break;
            case "authority":
                rig.Authority.Verdict = WorkAuthorityVerdict.OtherPeersConnected;
                break;
            case "body":
                rig.Body.Facts = rig.Body.Facts.With(f => f.Dead = true);
                break;
            case "far":
                rig.Body.Facts = rig.Body.Facts.With(f => f.Position = new WorkPoint(0f, 0f, 40f));
                break;
            case "hitched":
                rig.Seam.Observation = rig.Seam.Observation.With(o =>
                {
                    o.HasJoint = true;
                    o.JointConnectedToLocalPlayer = true;
                });
                break;
            case "held":
                Assert.Equal(WorkerHoldOutcome.Taken, rig.Identity.Take("collection-round-1"));
                break;
        }

        var motor = new WalkingMotor(rig.Body);
        int swings = 0;
        var driver = new CartUpkeepDriver(new CartUpkeepIdle(intervalSeconds: 1d), motor, () => swings++);
        int handedToExecutor = 0;
        for (int index = 0; index < (int)(120f / Step); index++)
        {
            rig.Clock.Now += Step;
            rig.Executor.ObserveFrame();
            WorkAuthorityVerdict authority = rig.Authority.Evaluate();
            if (driver.Step(CartUpkeepFacts.Read(switchedOn, authority, rig.Executor), rig.Clock.Now) ==
                CartUpkeepAction.Nothing)
            {
                handedToExecutor++;
            }
        }

        Assert.Equal(0, swings);
        Assert.Equal(0, motor.Walks + motor.Faces + motor.Stops);
        Assert.Equal((int)(120f / Step), handedToExecutor);
    }

    [Fact]
    public void Somebody_elses_cart_or_no_cart_at_all_is_nothing_to_him()
    {
        var rig = new HaulingExecutionRig();
        rig.Body.Facts = rig.Body.Facts.With(f => f.Position = new WorkPoint(0f, 0f, 1f));
        var motor = new WalkingMotor(rig.Body);
        int swings = 0;
        var driver = new CartUpkeepDriver(new CartUpkeepIdle(intervalSeconds: 1d), motor, () => swings++);

        Run(rig, driver, seconds: 60f);

        Assert.Null(rig.Executor.ActiveLease);
        Assert.Equal(0, swings);
        Assert.Equal(0, motor.Walks + motor.Faces + motor.Stops);
    }

    [Fact]
    public void A_job_arriving_mid_walk_takes_him_and_the_routine_lets_go()
    {
        var rig = AssignedRig();
        var motor = new WalkingMotor(rig.Body);
        int swings = 0;
        var driver = new CartUpkeepDriver(new CartUpkeepIdle(intervalSeconds: 5d), motor, () => swings++);

        RunUntil(rig, driver, CartUpkeepAction.WalkToCart);
        Assert.Equal(HaulCommandOutcome.Accepted, rig.Go().Outcome);
        Assert.Equal(HaulPhase.Approaching, rig.Executor.Phase);

        // From the next tick on, the executor steers him and the routine asks
        // for nothing at all, however long the job lasts.
        int walksBefore = motor.Walks;
        for (int index = 0; index < (int)(60f / Step); index++)
        {
            Assert.Equal(CartUpkeepAction.Nothing, Tick(rig, driver));
        }

        Assert.Equal(walksBefore, motor.Walks);
        Assert.Equal(0, motor.Faces);
        Assert.Equal(0, swings);
        Assert.NotEqual(HaulPhase.Ready, rig.Executor.Phase);
    }

    [Fact]
    public void A_walk_that_never_arrives_is_given_up_and_the_interval_starts_again()
    {
        var rig = AssignedRig();
        var motor = new WalkingMotor(rig.Body) { Moves = false };
        int swings = 0;
        var driver = new CartUpkeepDriver(new CartUpkeepIdle(intervalSeconds: 5d), motor, () => swings++);

        RunUntil(rig, driver, CartUpkeepAction.WalkToCart);
        int ticks = 0;
        while (Tick(rig, driver) == CartUpkeepAction.WalkToCart)
        {
            ticks++;
            Assert.True(ticks * Step < CartUpkeepDriver.GiveUpWalkingAfterSeconds + 1d, "he walked into the cart for ever");
        }

        Assert.True(motor.Stops > 0, "giving up stops his walk");
        Assert.Equal(CartUpkeepAction.Nothing, Tick(rig, driver));
        Assert.Equal(0, swings);
    }

    [Fact]
    public void Reset_forgets_the_timer_and_the_walk()
    {
        var rig = AssignedRig();
        rig.Body.Facts = rig.Body.Facts.With(f => f.Position = new WorkPoint(0f, 0f, 1f));
        int swings = 0;
        var driver = new CartUpkeepDriver(new CartUpkeepIdle(intervalSeconds: 5d), new WalkingMotor(rig.Body), () => swings++);

        Run(rig, driver, seconds: 4f);
        driver.Reset();
        Run(rig, driver, seconds: 4f);

        Assert.Equal(0, swings);
    }

    [Fact]
    public void The_driver_is_built_from_a_decision_three_motor_calls_and_one_callback_and_nothing_else()
    {
        ConstructorInfo constructor = Assert.Single(typeof(CartUpkeepDriver).GetConstructors(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.Equal(
            new[] { typeof(CartUpkeepIdle), typeof(ICartUpkeepMotor), typeof(Action) },
            constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray());

        Type[] fields = typeof(CartUpkeepDriver)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(field => !field.IsLiteral)
            .Select(field => field.FieldType)
            .ToArray();
        Assert.All(fields, type => Assert.Contains(
            type, new[] { typeof(CartUpkeepIdle), typeof(ICartUpkeepMotor), typeof(Action), typeof(double) }));

        Assert.Equal(
            new[] { "Face", "Stop", "WalkTo" },
            typeof(ICartUpkeepMotor).GetMethods().Select(method => method.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());

        MethodInfo step = typeof(CartUpkeepDriver).GetMethod(nameof(CartUpkeepDriver.Step))!;
        Assert.Equal(typeof(CartUpkeepAction), step.ReturnType);
        Assert.All(step.GetParameters(), parameter => Assert.False(parameter.ParameterType.IsByRef));
        Assert.All(
            typeof(CartUpkeepFacts).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => Assert.False(property.CanWrite, property.Name + " is settable"));
    }

    [Fact]
    public void The_facts_reader_reads_the_executor_and_leaves_it_as_it_was()
    {
        var rig = AssignedRig();
        int revision = rig.Executor.Revision;
        int observes = rig.Seam.ObserveCalls;
        rig.Journal.Clear();

        CartUpkeepFacts facts = CartUpkeepFacts.Read(true, WorkAuthorityVerdict.Granted, rig.Executor);

        Assert.True(facts.Idle);
        Assert.True(facts.CartIsHis);
        Assert.Equal(revision, rig.Executor.Revision);
        Assert.Equal(observes, rig.Seam.ObserveCalls);
        Assert.Empty(rig.Journal);
    }

    private static HaulingExecutionRig AssignedRig()
    {
        var rig = new HaulingExecutionRig();
        Assert.Equal(AssignmentOutcome.Assigned, rig.Assign().Outcome);
        rig.Executor.ObserveFrame();
        return rig;
    }

    private static CartUpkeepFacts Facts(HaulingExecutionRig rig) =>
        CartUpkeepFacts.Read(true, rig.Authority.Evaluate(), rig.Executor);

    /// <summary>One frame and one worker tick, the way the runtime runs them:
    /// the idle hammer first, and the executor only when it took nothing.</summary>
    private static CartUpkeepAction Tick(HaulingExecutionRig rig, CartUpkeepDriver driver)
    {
        rig.Clock.Now += Step;
        rig.Executor.ObserveFrame();
        CartUpkeepAction action = driver.Step(Facts(rig), rig.Clock.Now);
        if (action == CartUpkeepAction.Nothing)
        {
            rig.Executor.Tick();
        }

        return action;
    }

    private static void Run(HaulingExecutionRig rig, CartUpkeepDriver driver, float seconds)
    {
        for (int index = 0; index < (int)(seconds / Step); index++)
        {
            Tick(rig, driver);
        }
    }

    private static void RunUntil(HaulingExecutionRig rig, CartUpkeepDriver driver, CartUpkeepAction wanted)
    {
        for (int index = 0; index < (int)(120f / Step); index++)
        {
            if (Tick(rig, driver) == wanted)
            {
                return;
            }
        }

        Assert.Fail("the routine never asked for " + wanted);
    }

    private static void RunUntilSwing(HaulingExecutionRig rig, CartUpkeepDriver driver) =>
        RunUntil(rig, driver, CartUpkeepAction.PlayHammerOnce);

    /// <summary>His own body, as the routine sees it: each walk command moves
    /// him a walking step toward the target, through the rig's fake body so the
    /// executor reads the same position.</summary>
    private sealed class WalkingMotor : ICartUpkeepMotor
    {
        private readonly FakeBody _body;
        private readonly List<string>? _order;

        public WalkingMotor(FakeBody body, List<string>? order = null)
        {
            _body = body;
            _order = order;
        }

        public bool Moves { get; set; } = true;

        public int Walks { get; private set; }

        public int Faces { get; private set; }

        public int Stops { get; private set; }

        public float LastFaceX { get; private set; }

        public float LastFaceZ { get; private set; }

        public void WalkTo(WorkPoint target, float arrivalRadiusMetres)
        {
            Walks++;
            _order?.Add("WalkTo");
            _body.WalkTo(target, arrivalRadiusMetres);
            WorkPoint at = _body.Facts.Position;
            float distance = at.HorizontalDistanceTo(target);
            if (!Moves || distance <= arrivalRadiusMetres)
            {
                return;
            }

            float stride = Math.Min(0.1f, distance - arrivalRadiusMetres);
            float x = at.X + ((target.X - at.X) / distance * stride);
            float z = at.Z + ((target.Z - at.Z) / distance * stride);
            _body.Facts = _body.Facts.With(f => f.Position = new WorkPoint(x, at.Y, z));
        }

        public void Face(float directionX, float directionZ)
        {
            Faces++;
            LastFaceX = directionX;
            LastFaceZ = directionZ;
            _order?.Add("Face");
            _body.Face(directionX, directionZ);
        }

        public void Stop()
        {
            Stops++;
            _order?.Add("Stop");
            _body.Stop();
        }
    }
}
