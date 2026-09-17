using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>Gunnar's executor wired to fakes that record every port call in
/// one ordered journal, so tests can assert what happened <i>and in which
/// order</i> (detach before retirement, stop before release). The defaults are
/// a healthy world: authority granted, a working calibrated body, an owned
/// upright idle hand cart two metres of detach distance away, a clear route.
/// </summary>
internal sealed class HaulingExecutionRig
{
    public static readonly Guid Epoch = new Guid("bbbbbbbb-0000-0000-0000-000000000001");
    public static readonly Guid NextEpoch = new Guid("bbbbbbbb-0000-0000-0000-000000000002");

    public const float Step = 0.05f;

    public HaulingExecutionRig(
        Action<HaulLimits>? tuneLimits = null,
        Action<HaulExecutionLimits>? tuneExecution = null,
        FakeNavigation? navigation = null)
    {
        Navigation = navigation;
        Limits = HaulLimits.Default;
        tuneLimits?.Invoke(Limits);
        Execution = HaulExecutionLimits.Default;
        tuneExecution?.Invoke(Execution);
        Clock = new FakeClock { Now = 100f };
        Authority = new FakeAuthority();
        Body = new FakeBody(Journal);
        Seam = new FakeSeam(Journal);
        Planner = new FakePlanner(Journal);
        Monitor = new FakeMonitor();
        Log = new FakeLog();
        Service = new GunnarHaulService(Authority);
        Executor = NewExecutor(Epoch, 0);
        Service.Bind(Executor);
        Cart = new CartKey("-100:7", Epoch);
    }

    public List<string> Journal { get; } = new List<string>();

    public HaulLimits Limits { get; }

    public HaulExecutionLimits Execution { get; }

    public FakeClock Clock { get; }

    public FakeAuthority Authority { get; }

    public FakeBody Body { get; }

    public FakeSeam Seam { get; }

    public FakePlanner Planner { get; }

    public FakeMonitor Monitor { get; }

    public FakeLog Log { get; }

    public FakeNavigation? Navigation { get; }

    public GunnarHaulService Service { get; }

    public HaulExecutor Executor { get; private set; }

    public CartKey Cart { get; private set; }

    public static WorkPoint Target => new WorkPoint(0f, 0f, 30f);

    public HaulExecutor NewExecutor(Guid epoch, int startingRevision)
    {
        var ports = new HaulExecutorPorts(Body, Seam, Planner, Monitor, Authority, Clock, Log, Navigation);
        var executor = new HaulExecutor(ports, WorkerKey.Gunnar, epoch, startingRevision, Limits, Execution);
        Seam.ModeProbe = () => executor.Modes.Mode;
        return executor;
    }

    /// <summary>A new world load: a new epoch, a new executor, the old one torn
    /// down first.</summary>
    public void ReloadWorld()
    {
        Executor.Teardown("world unloading", LeaseInvalidation.WorldReloaded);
        Service.Unbind();
        Executor = NewExecutor(NextEpoch, Service.NextStartingRevision);
        Service.Bind(Executor);
        Cart = new CartKey("-100:7", NextEpoch);
    }

    public static CartAssignmentFacts SelectableCart() => new CartAssignmentFacts
    {
        Selection = CartSelectionState.OneCart,
        SelectedInThisWorldLoad = true,
        Resolved = true,
        RecordExists = true,
        IsHandCart = true,
        ViewValid = true,
        IsOwner = true,
        CapabilityOk = true,
        InUse = false,
        Braked = false,
        UpDot = 1f,
        PlayerDistanceMetres = 3f,
    };

    public AssignmentVerdict Assign(string leaseId = "lease-1") =>
        Executor.AssignCart(leaseId, Cart, SelectableCart());

    public HaulCommandResult Go(string haulId = "haul-1", string leaseId = "lease-1", float arrivalRadius = 2f) =>
        Executor.RequestLeg(new HaulLegRequest(haulId, string.Empty, leaseId, true, Target, arrivalRadius), Executor.Revision);

    /// <summary>One worker step: a rendered frame's signals, then the 20 Hz
    /// tick.</summary>
    public void StepOnce()
    {
        Clock.Now += Step;
        Executor.ObserveFrame();
        Executor.Tick();
    }

    public void Advance(float seconds)
    {
        int steps = (int)Math.Ceiling(seconds / Step);
        for (int index = 0; index < steps; index++)
        {
            StepOnce();
        }
    }

    /// <summary>Gunnar stands at the handle, facing along the cart's heading.
    /// </summary>
    public void PutBodyAtHandle()
    {
        Seam.Observation = Seam.Observation.With(o => o.HitchDistanceMetres = 0.5f);
        Body.Facts = Body.Facts.With(f =>
        {
            f.ForwardX = Seam.Observation.HeadingX;
            f.ForwardZ = Seam.Observation.HeadingZ;
        });
    }

    public void AssignAndStartLeg()
    {
        Assert.Equal(AssignmentOutcome.Assigned, Assign().Outcome);
        Assert.Equal(HaulCommandOutcome.Accepted, Go().Outcome);
        Assert.Equal(HaulPhase.Approaching, Executor.Phase);
    }

    public void RunToPulling()
    {
        AssignAndStartLeg();
        PutBodyAtHandle();
        StepOnce();
        Assert.Equal(HaulPhase.Pulling, Executor.Phase);
        Assert.True(Executor.Attached);
    }

    public void ArriveAtTarget()
    {
        Body.Facts = Body.Facts.With(f => f.Position = Target);
    }

    public void RunToWaiting()
    {
        RunToPulling();
        ArriveAtTarget();
        StepOnce();
        Assert.Equal(HaulPhase.Stopping, Executor.Phase);
        Advance(Limits.StillForSeconds + (2 * Step));
        Assert.Equal(HaulPhase.Waiting, Executor.Phase);
    }

    public void AssertNoBugs() => Assert.Empty(Log.Bugs);

    public int IndexOf(string entry)
    {
        int index = Journal.FindIndex(line => line.StartsWith(entry, StringComparison.Ordinal));
        Assert.True(index >= 0, "journal has no '" + entry + "': " + string.Join(", ", Journal));
        return index;
    }
}

internal static class ObservationExtensions
{
    public static CartObservation With(this CartObservation value, Action<CartObservationBox> change)
    {
        var box = new CartObservationBox(value);
        change(box);
        return box.Value;
    }

    public static PullerBodyFacts With(this PullerBodyFacts value, Action<PullerBodyFactsBox> change)
    {
        var box = new PullerBodyFactsBox(value);
        change(box);
        return box.Value;
    }
}

/// <summary>A mutable box so lambdas can change a struct copy.</summary>
internal sealed class CartObservationBox
{
    public CartObservation Value;

    public CartObservationBox(CartObservation value) => Value = value;

    public bool Resolved { set => Value.Resolved = value; }
    public bool RecordExists { set => Value.RecordExists = value; }
    public bool IsHandCart { set => Value.IsHandCart = value; }
    public bool ViewValid { set => Value.ViewValid = value; }
    public bool IsOwner { set => Value.IsOwner = value; }
    public bool CapabilityOk { set => Value.CapabilityOk = value; }
    public float BodyMassSumKg { set => Value.BodyMassSumKg = value; }
    public float ExpectedMassKg { set => Value.ExpectedMassKg = value; }
    public bool ContainerOpen { set => Value.ContainerOpen = value; }
    public bool InUse { set => Value.InUse = value; }
    public bool HasJoint { set => Value.HasJoint = value; }
    public bool JointConnectedToPuller { set => Value.JointConnectedToPuller = value; }
    public bool JointConnectedToLocalPlayer { set => Value.JointConnectedToLocalPlayer = value; }
    public bool AttachFlag { set => Value.AttachFlag = value; }
    public bool BrakeEngaged { set => Value.BrakeEngaged = value; }
    public bool RootFrozen { set => Value.RootFrozen = value; }
    public bool AnyJointOnClient { set => Value.AnyJointOnClient = value; }
    public bool LocalPlayerHasJoint { set => Value.LocalPlayerHasJoint = value; }
    public bool LocalPlayerHoveringCart { set => Value.LocalPlayerHoveringCart = value; }
    public float UpDot { set => Value.UpDot = value; }
    public float HitchDistanceMetres { set => Value.HitchDistanceMetres = value; }
    public float DetachDistanceMetres { set => Value.DetachDistanceMetres = value; }
    public WorkPoint CartPosition { set => Value.CartPosition = value; }
    public float HeadingX { set => Value.HeadingX = value; }
    public float HeadingZ { set => Value.HeadingZ = value; }
    public float SpeedMetresPerSecond { set => Value.SpeedMetresPerSecond = value; }
    public float JointForceNewtons { set => Value.JointForceNewtons = value; }
    public float BreakForceNewtons { set => Value.BreakForceNewtons = value; }
    public float FootprintWidthMetres { set => Value.FootprintWidthMetres = value; }
}

internal sealed class PullerBodyFactsBox
{
    public PullerBodyFacts Value;

    public PullerBodyFactsBox(PullerBodyFacts value) => Value = value;

    public bool Present { set => Value.Present = value; }
    public bool Dead { set => Value.Dead = value; }
    public bool Faulted { set => Value.Faulted = value; }
    public WorkPoint Position { set => Value.Position = value; }
    public float ForwardX { set => Value.ForwardX = value; }
    public float ForwardZ { set => Value.ForwardZ = value; }
    public bool HasRigidbody { set => Value.HasRigidbody = value; }
    public bool IsKinematic { set => Value.IsKinematic = value; }
    public bool UsesGravity { set => Value.UsesGravity = value; }
    public bool DetectsCollisions { set => Value.DetectsCollisions = value; }
    public bool RotationLockedUpright { set => Value.RotationLockedUpright = value; }
    public bool UnitScale { set => Value.UnitScale = value; }
    public float BodyMassKg { set => Value.BodyMassKg = value; }
    public float BaseMassKg { set => Value.BaseMassKg = value; }
    public float CalibratedMassKg { set => Value.CalibratedMassKg = value; }
    public bool HasPath { set => Value.HasPath = value; }
}

internal sealed class FakeClock : IHaulClock
{
    public float Now { get; set; }
}

internal sealed class FakeAuthority : IHaulAuthority
{
    public WorkAuthorityVerdict Verdict { get; set; } = WorkAuthorityVerdict.Granted;

    public WorkAuthorityVerdict Evaluate() => Verdict;
}

internal sealed class FakeBody : IPullerBody
{
    private readonly List<string> _journal;

    public FakeBody(List<string> journal)
    {
        _journal = journal;
        Facts = Healthy();
    }

    public PullerBodyFacts Facts { get; set; }

    public int WalkCommands { get; private set; }

    public int SteerCommands { get; private set; }

    public bool CalibrateSucceeds { get; set; } = true;

    public static PullerBodyFacts Healthy() => new PullerBodyFacts
    {
        Present = true,
        Position = new WorkPoint(0f, 0f, 6f),
        ForwardX = 1f,
        ForwardZ = 0f,
        HasRigidbody = true,
        IsKinematic = false,
        UsesGravity = true,
        DetectsCollisions = true,
        RotationLockedUpright = true,
        UnitScale = true,
        BodyMassKg = 70f,
        BaseMassKg = 70f,
        CalibratedMassKg = 70f,
        HasPath = true,
    };

    public PullerBodyFacts Read() => Facts;

    public void WalkTo(WorkPoint target, float arrivalRadiusMetres)
    {
        WalkCommands++;
        _journal.Add("WalkTo " + target);
        Facts = Facts.With(f => f.Value.MotorCommanded = true);
    }

    public void SteerToward(WorkPoint target)
    {
        SteerCommands++;
        _journal.Add("SteerToward " + target);
        Facts = Facts.With(f => f.Value.MotorCommanded = true);
    }

    public void Face(float directionX, float directionZ)
    {
        _journal.Add("Face");
        Facts = Facts.With(f =>
        {
            f.ForwardX = directionX;
            f.ForwardZ = directionZ;
        });
    }

    public void Stop()
    {
        _journal.Add("Stop");
        Facts = Facts.With(f => f.Value.MotorCommanded = false);
    }

    public bool Calibrate()
    {
        _journal.Add("Calibrate");
        if (CalibrateSucceeds)
        {
            Facts = Facts.With(f =>
            {
                f.BodyMassKg = 70f;
                f.BaseMassKg = 70f;
                f.CalibratedMassKg = 70f;
            });
        }

        return CalibrateSucceeds;
    }

    public void Retire()
    {
        _journal.Add("Retire");
        Facts = Facts.With(f => f.Present = false);
    }
}

internal sealed class FakeSeam : ICartHitchSeam
{
    private readonly List<string> _journal;

    public FakeSeam(List<string> journal)
    {
        _journal = journal;
        Observation = HealthyCart();
        Ground = new ParkingGround { Measured = true, GradeAlongRatio = 0.01f, GradeAcrossRatio = 0.01f, InWater = false };
    }

    public bool IsAvailable { get; set; } = true;

    public string UnavailableDetail => IsAvailable ? string.Empty : "Vagon.AttachTo (method not found)";

    public CartObservation Observation { get; set; }

    public ParkingGround Ground { get; set; }

    /// <summary>What the next attach does: true verifies a joint to Gunnar.
    /// </summary>
    public bool AttachWorks { get; set; } = true;

    public int AttachCalls { get; private set; }

    public int ReleaseCalls { get; private set; }

    public Func<ActorMode>? ModeProbe { get; set; }

    public ActorMode ModeAtLastRelease { get; private set; }

    public static CartObservation HealthyCart() => new CartObservation
    {
        Resolved = true,
        RecordExists = true,
        IsHandCart = true,
        ViewValid = true,
        IsOwner = true,
        CapabilityOk = true,
        BodyMassSumKg = 20f,
        ExpectedMassKg = 20f,
        UpDot = 1f,
        HitchDistanceMetres = 6f,
        DetachDistanceMetres = 2f,
        CartPosition = new WorkPoint(0f, 0f, 0f),
        HandlePosition = new WorkPoint(0f, 1f, 1.5f),
        ApproachPoint = new WorkPoint(0f, 0.2f, 1.5f),
        HeadingX = 0f,
        HeadingZ = 1f,
        BreakForceNewtons = 10000f,
        FootprintWidthMetres = 1.5f,
        FootprintLengthMetres = 2.5f,
        HitchLengthMetres = 1.5f,
    };

    public CartObservation Observe(CartKey cart) => Observation;

    public ParkingGround ReadGround(CartKey cart)
    {
        _journal.Add("ReadGround");
        return Ground;
    }

    public AttachResult AttachAndVerify(CartKey cart)
    {
        AttachCalls++;
        _journal.Add("AttachAndVerify");
        if (!AttachWorks)
        {
            return AttachResult.Refused(HitchRefusal.VerifyFailed, "connected body is not Gunnar's");
        }

        Observation = Observation.With(o =>
        {
            o.HasJoint = true;
            o.JointConnectedToPuller = true;
            o.AttachFlag = true;
            o.InUse = true;
            o.AnyJointOnClient = true;
        });
        return AttachResult.Verified();
    }

    public ReleaseResult ReleaseJoint(CartKey cart)
    {
        ReleaseCalls++;
        ModeAtLastRelease = ModeProbe?.Invoke() ?? ActorMode.Unspecified;
        _journal.Add("ReleaseJoint");
        CartObservation current = Observation;
        if (!current.Resolved)
        {
            return ReleaseResult.NoCart;
        }

        if (current.HasJoint && !current.JointConnectedToPuller)
        {
            return ReleaseResult.NotOurs;
        }

        bool had = current.HasJoint;
        Observation = current.With(o =>
        {
            o.HasJoint = false;
            o.JointConnectedToPuller = false;
            o.AttachFlag = false;
            o.InUse = false;
            o.AnyJointOnClient = false;
        });
        return had ? ReleaseResult.Released : ReleaseResult.NoJoint;
    }
}

internal sealed class FakePlanner : ICartRoutePlanner
{
    private readonly List<string> _journal;

    public FakePlanner(List<string> journal) => _journal = journal;

    public CartRouteVerdict NextVerdict { get; set; } = CartRouteVerdict.Suitable;

    public bool GoalsValid { get; set; } = true;

    public int PlanCalls { get; private set; }

    public CartRoutePlan Plan(CartRouteRequest request, float now)
    {
        PlanCalls++;
        _journal.Add("Plan");
        if (NextVerdict != CartRouteVerdict.Suitable)
        {
            return CartRoutePlan.Refused(NextVerdict, request.Revision);
        }

        return new CartRoutePlan(
            CartRouteVerdict.Suitable,
            new[] { request.From, request.To },
            request.From.HorizontalDistanceTo(request.To),
            0.02f,
            2.1f,
            request.To,
            request.Revision);
    }

    public SteeringGoal? NextGoal(CartRoutePlan plan, WorkPoint pullerPosition, WorkPoint cartPosition)
    {
        if (!GoalsValid || !plan.IsSuitable)
        {
            return null;
        }

        return new SteeringGoal(plan.Waypoints[plan.Waypoints.Count - 1], 1.5f, true, 1, plan.RequestRevision);
    }
}

internal sealed class FakeMonitor : IHaulMotionMonitor
{
    public HaulMotion Verdict { get; set; } = HaulMotion.Idle;

    public int Samples { get; private set; }

    public int Resets { get; private set; }

    public HaulMotion Current => Verdict;

    public void Sample(float now, WorkPoint puller, WorkPoint cart, bool motorCommanded) => Samples++;

    public void Reset()
    {
        Resets++;
        Verdict = HaulMotion.Idle;
    }
}

internal sealed class FakeLog : IHaulExecutionLog
{
    public List<string> Infos { get; } = new List<string>();

    public List<string> Warnings { get; } = new List<string>();

    public List<string> Bugs { get; } = new List<string>();

    public void Info(string message) => Infos.Add(message);

    public void Warning(string message) => Warnings.Add(message);

    public void Bug(string message) => Bugs.Add(message);
}

internal sealed class FakeNavigation : IHaulNavigation
{
    private readonly List<string> _journal;

    public FakeNavigation(List<string>? journal = null) => _journal = journal ?? new List<string>();

    public bool UseCartSucceeds { get; set; } = true;

    public int UseCartCalls { get; private set; }

    public int ReleaseCartCalls { get; private set; }

    public List<WorkPoint?> PlannedFrom { get; } = new List<WorkPoint?>();

    public List<CartRouteRequest> Requests { get; } = new List<CartRouteRequest>();

    public List<(WorkPoint Target, WorkPoint Cart, WorkPoint Puller)> Stalls { get; } = new List<(WorkPoint, WorkPoint, WorkPoint)>();

    public CartRouteVerdict NextVerdict { get; set; } = CartRouteVerdict.Suitable;

    public HaulSteeringStatus SteeringStatus { get; set; } = HaulSteeringStatus.Following;

    public bool RefreshDue { get; set; }

    public CartRouteVerdict RefreshVerdict { get; set; } = CartRouteVerdict.Suitable;

    public CartRoutePlan? LastRefreshed { get; private set; }

    public int RefreshCalls { get; private set; }

    public CartFootprint? MeasuredFootprint { get; set; } = new CartFootprint(1.72f, 2.9f, 1.6f);

    private bool _inUse;

    public CartFootprint? Footprint => _inUse ? MeasuredFootprint : null;

    public bool UseCart(CartKey cart)
    {
        UseCartCalls++;
        _journal.Add("UseCart");
        _inUse = UseCartSucceeds;
        return _inUse;
    }

    public void ReleaseCart()
    {
        ReleaseCartCalls++;
        _journal.Add("ReleaseCart");
        _inUse = false;
    }

    public CartRoutePlan Plan(CartRouteRequest request, WorkPoint? pullerPosition, float now)
    {
        PlannedFrom.Add(pullerPosition);
        Requests.Add(request);
        return NextVerdict == CartRouteVerdict.Suitable
            ? new CartRoutePlan(CartRouteVerdict.Suitable, new[] { request.From, request.To }, 30f, 0.02f, 2.7f, request.To, request.Revision)
            : CartRoutePlan.Refused(NextVerdict, request.Revision);
    }

    public HaulSteering Steer(CartRoutePlan plan, WorkPoint pullerPosition, WorkPoint cartPosition)
    {
        if (!plan.IsSuitable)
        {
            return new HaulSteering(HaulSteeringStatus.NotSuitable, null);
        }

        return SteeringStatus == HaulSteeringStatus.Following
            ? new HaulSteering(HaulSteeringStatus.Following, new SteeringGoal(plan.Waypoints[plan.Waypoints.Count - 1], 1f, true, 1, plan.RequestRevision))
            : new HaulSteering(SteeringStatus, null);
    }

    public bool NeedsRefresh(CartRoutePlan plan, float now) => RefreshDue;

    public CartRoutePlan Refresh(CartRoutePlan plan, WorkPoint pullerPosition, WorkPoint cartPosition, float now)
    {
        RefreshCalls++;
        RefreshDue = false;
        LastRefreshed = RefreshVerdict == CartRouteVerdict.Suitable
            ? new CartRoutePlan(CartRouteVerdict.Suitable, new[] { pullerPosition, plan.Waypoints[plan.Waypoints.Count - 1] }, 20f, 0.02f, 2.7f, plan.StopPoint, plan.RequestRevision)
            : CartRoutePlan.Refused(RefreshVerdict, plan.RequestRevision);
        return LastRefreshed;
    }

    public void RememberStall(WorkPoint legTarget, WorkPoint cartPosition, WorkPoint pullerPosition, float now)
    {
        _journal.Add("RememberStall");
        Stalls.Add((legTarget, cartPosition, pullerPosition));
    }
}
