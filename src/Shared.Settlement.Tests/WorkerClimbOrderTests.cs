using TheConcernedCat.ConcernedForeman.Domain.Ladders;
using TheConcernedCat.Workers;
using TheConcernedCat.Workers.Traversal;

namespace Shared.Settlement.Tests;

/// <summary>#334: the explicit ladder-route order, driven through the REAL
/// route search, route runner and link traversal. Only the game is faked: the
/// mode arbiter, the live switches, the body and its motor, the receiver and
/// the walk evidence. Every exit must give the body back and release the
/// identity, and nothing may place the body unless the order, the switch, the
/// authority, the body and the hold all still say yes.</summary>
public sealed class WorkerClimbOrderTests
{
    private static readonly WorkPoint Start = new(-4, 0, 0);
    private static readonly WorkPoint Bottom = new(0, 0, 0);
    private static readonly WorkPoint Top = new(0, 4, 0);
    private static readonly WorkPoint Upstairs = new(3, 4, 0);

    private sealed class Modes : IActorModeHold
    {
        public WorkerKey Worker => WorkerKey.Thorstein;
        public bool IsIdentityKnown { get; set; } = true;
        public ActorMode Mode { get; private set; } = ActorMode.Resting;
        public string? JobId { get; set; }
        public bool MayRelocateHome => JobId == null;
        public bool MayRetireBody => JobId == null;
        public int Releases;
        public bool IsHeldBy(string? jobId) => jobId != null && JobId == jobId;
        public ActorModeOutcome Enter(ActorMode mode, string jobId)
        {
            if (!IsIdentityKnown) return ActorModeOutcome.Unspecified;
            if (JobId != null && JobId != jobId) return ActorModeOutcome.RefusedBusy;
            JobId = jobId; Mode = mode; return ActorModeOutcome.Entered;
        }
        public ActorModeOutcome Release(string jobId)
        {
            if (JobId != jobId) return ActorModeOutcome.NotHeld;
            Releases++; JobId = null; Mode = ActorMode.Resting; return ActorModeOutcome.Released;
        }
    }

    private sealed class Gates : IWorkerClimbGates
    {
        public bool NpcClimbing { get; set; } = true;
        public WorkAuthorityVerdict Authority { get; set; } = WorkAuthorityVerdict.Granted;
    }

    private sealed class Links : IWorkerClimbLinks
    {
        private readonly TraversalCapabilities _caps;
        public Links(TraversalCapabilities caps)
        {
            _caps = caps; Network = new TraversalLinkNetwork(); Traversal = new LinkTraversal(Network, caps);
            Publish();
        }
        public TraversalLinkNetwork Network { get; }
        public ILadderTraversal Traversal { get; }
        public bool Admits = true, RebuildOnAdmission;
        public WorkerLadderSurveyResult Survey = WorkerLadderSurveyResult.Ready;
        public int Admissions, Forgets;
        public void Publish()
        {
            Assert.True(TraversalLink.TryCreate("cl:1:Front", TraversalLinkKind.Ladder, Bottom, Top,
                TraversalCapability.Ladders, TraversalLimits.Default, out var link));
            Network.Publish(link);
        }
        public WorkerLadderSurveyResult Tick(float dt, bool npc, bool order, WorkAuthorityVerdict authority, WorkPoint centre, float radius)
            => npc && order && authority == WorkAuthorityVerdict.Granted ? Survey : WorkerLadderSurveyResult.Disabled;
        public bool RevalidateForAdmission(bool npc, bool order, WorkAuthorityVerdict authority, WorkPoint centre, float radius)
        {
            Admissions++;
            if (!npc || !order || authority != WorkAuthorityVerdict.Granted || !_caps.NpcClimbingEnabled) return false;
            if (RebuildOnAdmission && Admissions > 1) { Network.Retire("cl:1:Front"); Publish(); }
            return Admits;
        }
        public void Forget() { Forgets++; Network.Retire("cl:1:Front"); }
    }

    private sealed class Motor : IWorkerLadderMotor
    {
        private readonly Body _body;
        public Motor(Body body, Guid epoch) { _body = body; BodyEpoch = epoch; }
        public WorkerKey Worker => WorkerKey.Thorstein;
        public Guid BodyEpoch { get; }
        public bool Restored, RestoreAllowed = true;
        public bool GroundMotorSuspended => !Restored;
        public bool TryPlaceOwnBody(WorkPoint position)
        {
            Assert.False(Restored);
            Assert.True(_body.Order.JobId != null, "a body was placed with nothing holding it");
            Assert.True(_body.Gates.NpcClimbing, "a body was placed with the switch off");
            Assert.Equal(WorkAuthorityVerdict.Granted, _body.Gates.Authority);
            _body.Placements++; _body.Position = position; return true;
        }
        public bool TryRestore()
        {
            _body.Restores++;
            if (!RestoreAllowed) return false;
            Restored = true; return true;
        }
    }

    private sealed class Body : IWorkerClimbBody
    {
        public Body(WorkerClimbOrder order, Gates gates) { Order = order; Gates = gates; }
        public WorkerClimbOrder Order { get; }
        public Gates Gates { get; }
        public bool IsCurrent { get; set; } = true;
        public WorkPoint Position { get; set; } = Start;
        public bool Landing = true, Wedged;
        public int Placements, Restores, Walks, Stops, Suspensions;
        public Motor? Lease;
        public Action<float>? Ticking;
        public bool WalkToward(WorkPoint target, float tolerance)
        {
            Assert.True(Lease == null || Lease.Restored, "the ground motor walked while the ladder lease held the body");
            Assert.True(tolerance < 1f, "the motor must stop inside the runner's own arrival");
            Walks++; if (!Wedged) Position = target; return true;
        }
        public void StopWalking() => Stops++;
        public IWorkerLadderMotor? TrySuspendGroundMotor(WorkerKey worker, Guid epoch)
        { Suspensions++; Lease = new Motor(this, epoch); return Lease; }
        public bool IsSupportedLanding(WorkPoint target) => Landing;
        public void Attach(Action<float> tick) => Ticking = tick;
        public void Detach() => Ticking = null;
    }

    /// <summary>Same floor walks; nothing else.</summary>
    private sealed class Walk : IWorkerWalkProbe
    {
        public int Unknowns;
        public WorkerWalkEvidence Check(WorkPoint from, WorkPoint to)
        {
            if (Unknowns > 0) { Unknowns--; return new WorkerWalkEvidence(WorkerWalkEvidenceKind.Unknown); }
            return from.VerticalDistanceTo(to) < 0.5f
                ? new WorkerWalkEvidence(WorkerWalkEvidenceKind.Reachable, WorkerRouteSearch.Distance(from, to))
                : new WorkerWalkEvidence(WorkerWalkEvidenceKind.Blocked);
        }
    }

    private sealed class Rig
    {
        public readonly Modes Modes = new();
        public readonly Gates Gates = new();
        public readonly List<string> Log = new();
        public readonly TraversalCapabilities Caps;
        public readonly Links Links;
        public readonly Walk Walk = new();
        public readonly WorkerClimbOrder Order;
        public readonly Body Body;
        public float Now;
        public Rig(bool npcCapability = true)
        {
            Caps = new TraversalCapabilities(npcCapability); Caps.Grant(WorkerKey.Thorstein, TraversalCapability.Ladders);
            Links = new Links(Caps);
            Order = new WorkerClimbOrder(Modes, Gates, Log.Add);
            Body = new Body(Order, Gates);
        }
        public string Begin(WorkPoint? target = null) => Order.Start(target ?? Upstairs, Body, Links, Walk, Caps, Now);
        /// <summary>One worker tick (if the body is still ticking) and one frame of supervision.</summary>
        public void Step(float dt = 0.05f, bool bodyTicks = true)
        {
            Now += dt;
            if (bodyTicks) Body.Ticking?.Invoke(dt);
            Order.Supervise(Now);
        }
        public void RunUntil(Func<bool> done, int steps = 2000)
        { for (int i = 0; i < steps && !done(); i++) Step(); }
    }

    [Fact]
    public void AnOrderedRouteWalksClimbsAndWalksThenGivesEverythingBack()
    {
        var rig = new Rig();
        Assert.StartsWith("Thorstein ordered", rig.Begin());
        Assert.Equal(WorkerClimbState.Searching, rig.Order.State);
        Assert.NotNull(rig.Modes.JobId);
        Assert.False(rig.Modes.MayRetireBody);
        Assert.NotNull(rig.Body.Ticking);

        rig.RunUntil(() => rig.Order.State == WorkerClimbState.Finished);

        Assert.Equal(WorkerClimbState.Finished, rig.Order.State);
        Assert.Contains("arrived", rig.Order.LastOutcome);
        Assert.Equal(Upstairs, rig.Body.Position);
        Assert.Equal(1, rig.Body.Suspensions);
        Assert.True(rig.Body.Placements > 0);
        Assert.True(rig.Body.Lease!.Restored);
        Assert.Null(rig.Modes.JobId);
        Assert.Equal(1, rig.Modes.Releases);
        Assert.Null(rig.Body.Ticking);
        // Once at admission and once again immediately before the ladder entry.
        Assert.Equal(2, rig.Links.Admissions);
        Assert.True(rig.Links.Forgets >= 1);
    }

    [Fact]
    public void NothingStartsWhileTheSwitchIsOff()
    {
        var rig = new Rig();
        rig.Gates.NpcClimbing = false;
        Assert.Contains("NpcClimbing", rig.Begin());
        Assert.Equal(WorkerClimbState.Idle, rig.Order.State);
        Assert.Null(rig.Modes.JobId);
        Assert.Equal(0, rig.Links.Admissions);
        Assert.Null(rig.Body.Ticking);
    }

    [Theory]
    [InlineData((int)WorkAuthorityVerdict.OtherPeersConnected)]
    [InlineData((int)WorkAuthorityVerdict.NotHost)]
    [InlineData((int)WorkAuthorityVerdict.DedicatedServer)]
    [InlineData((int)WorkAuthorityVerdict.Unspecified)]
    public void NothingStartsWithoutWorkAuthority(int verdict)
    {
        var rig = new Rig();
        rig.Gates.Authority = (WorkAuthorityVerdict)verdict;
        Assert.StartsWith("Refused", rig.Begin());
        Assert.Null(rig.Modes.JobId);
        Assert.Equal(0, rig.Links.Admissions);
    }

    [Fact]
    public void ABusyOrUnknownIdentityRefusesAndTakesNothing()
    {
        var rig = new Rig();
        rig.Modes.JobId = "collection:1";
        Assert.Contains("busy", rig.Begin());
        Assert.Equal("collection:1", rig.Modes.JobId);
        Assert.Equal(0, rig.Links.Admissions);

        var unknown = new Rig();
        unknown.Modes.IsIdentityKnown = false;
        Assert.Contains("never accepted", unknown.Begin());
        Assert.Null(unknown.Body.Ticking);
    }

    [Fact]
    public void AFailedAdmissionSurveyReleasesTheIdentityItTook()
    {
        var rig = new Rig();
        rig.Links.Admits = false;
        Assert.StartsWith("Refused", rig.Begin());
        Assert.Null(rig.Modes.JobId);
        Assert.Equal(1, rig.Modes.Releases);
        Assert.Equal(1, rig.Links.Forgets);
        Assert.Null(rig.Body.Ticking);
    }

    [Fact]
    public void ACapabilityTableWithClimbingOffRefuses()
    {
        var rig = new Rig(npcCapability: false);
        Assert.Contains("NPC climbing is off", rig.Begin());
        Assert.Null(rig.Modes.JobId);
    }

    [Fact]
    public void AnAbsentBodyOrAFarDestinationRefuses()
    {
        var rig = new Rig();
        rig.Body.IsCurrent = false;
        Assert.StartsWith("Refused", rig.Begin());
        rig.Body.IsCurrent = true;
        Assert.Contains("64", rig.Begin(new WorkPoint(200, 0, 0)));
        Assert.Contains("not a point", rig.Begin(new WorkPoint(float.NaN, 0, 0)));
        Assert.Null(rig.Modes.JobId);
    }

    [Fact]
    public void ASecondOrderWhileOneRunsIsRefused()
    {
        var rig = new Rig();
        rig.Begin();
        string job = rig.Modes.JobId!;
        Assert.Contains("already running", rig.Begin());
        Assert.Equal(job, rig.Modes.JobId);
    }

    private static Rig ClimbingRig()
    {
        var rig = new Rig();
        rig.Begin();
        rig.RunUntil(() => rig.Order.Runner?.Phase == WorkerRoutePhase.Climbing);
        Assert.Equal(WorkerRoutePhase.Climbing, rig.Order.Runner!.Phase);
        rig.Step();
        Assert.True(rig.Body.Placements > 0);
        return rig;
    }

    [Fact]
    public void SwitchingNpcClimbingOffMidClimbStopsBeforeAnotherPlacement()
    {
        var rig = ClimbingRig();
        int placed = rig.Body.Placements;
        WorkPoint where = rig.Body.Position;
        rig.Gates.NpcClimbing = false;
        rig.Step();
        Assert.Equal(WorkerClimbState.Finished, rig.Order.State);
        Assert.Equal(placed, rig.Body.Placements);
        Assert.Equal(where, rig.Body.Position);
        Assert.True(rig.Body.Lease!.Restored);
        Assert.Null(rig.Modes.JobId);
    }

    [Fact]
    public void LosingAuthorityMidClimbStopsAndRestores()
    {
        var rig = ClimbingRig();
        int placed = rig.Body.Placements;
        rig.Gates.Authority = WorkAuthorityVerdict.OtherPeersConnected;
        rig.Step();
        Assert.Equal(WorkerClimbState.Finished, rig.Order.State);
        Assert.Equal(placed, rig.Body.Placements);
        Assert.True(rig.Body.Lease!.Restored);
        Assert.Null(rig.Modes.JobId);
    }

    [Fact]
    public void ABodyThatDiesOrUnloadsMidClimbIsReleasedFromThePlugin()
    {
        var rig = ClimbingRig();
        int placed = rig.Body.Placements;
        rig.Body.IsCurrent = false;
        rig.Step(bodyTicks: false);
        Assert.Equal(WorkerClimbState.Finished, rig.Order.State);
        Assert.Contains("gone", rig.Order.LastOutcome);
        Assert.Equal(placed, rig.Body.Placements);
        Assert.True(rig.Body.Lease!.Restored);
        Assert.Null(rig.Modes.JobId);
        Assert.Null(rig.Body.Ticking);
    }

    [Fact]
    public void ABodyThatStopsBeingSimulatedEndsTheOrder()
    {
        var rig = ClimbingRig();
        for (int i = 0; i < 30; i++) rig.Step(0.1f, bodyTicks: false);
        Assert.Equal(WorkerClimbState.Finished, rig.Order.State);
        Assert.Contains("simulated", rig.Order.LastOutcome);
        Assert.True(rig.Body.Lease!.Restored);
        Assert.Null(rig.Modes.JobId);
    }

    [Fact]
    public void CancelMidClimbAndWorldUnloadGiveTheBodyBack()
    {
        var rig = ClimbingRig();
        rig.Order.Cancel("cancelled by the player");
        Assert.Equal(WorkerClimbState.Finished, rig.Order.State);
        Assert.Contains("cancelled", rig.Order.LastOutcome);
        Assert.True(rig.Body.Lease!.Restored);
        Assert.Null(rig.Modes.JobId);
        rig.Order.Cancel("again");
        Assert.Equal(1, rig.Modes.Releases);
    }

    [Fact]
    public void AFailedRestorationKeepsTheIdentityHeldUntilItSucceeds()
    {
        var rig = ClimbingRig();
        rig.Body.Lease!.RestoreAllowed = false;
        rig.Order.Cancel("cancelled by the player");
        Assert.Equal(WorkerClimbState.Releasing, rig.Order.State);
        Assert.NotNull(rig.Modes.JobId);
        Assert.False(rig.Modes.MayRetireBody);
        int placed = rig.Body.Placements;
        for (int i = 0; i < 5; i++) rig.Step();
        Assert.Equal(WorkerClimbState.Releasing, rig.Order.State);
        Assert.Equal(placed, rig.Body.Placements);
        Assert.Contains("given his body back", rig.Order.Describe());
        rig.Body.Lease.RestoreAllowed = true;
        rig.Step(bodyTicks: false);
        Assert.Equal(WorkerClimbState.Finished, rig.Order.State);
        Assert.Null(rig.Modes.JobId);
        Assert.Contains("cancelled", rig.Order.LastOutcome);
    }

    [Fact]
    public void ALadderRebuiltBeforeEntryIsNeverEntered()
    {
        var rig = new Rig();
        rig.Links.RebuildOnAdmission = true;
        rig.Begin();
        rig.RunUntil(() => rig.Order.State == WorkerClimbState.Finished);
        Assert.Equal(0, rig.Body.Suspensions);
        Assert.Equal(0, rig.Body.Placements);
        Assert.Contains("MotorFailure", rig.Order.LastOutcome);
        Assert.Null(rig.Modes.JobId);
    }

    [Fact]
    public void ASurveyThatGoesBadMidRouteStopsTheClimb()
    {
        var rig = ClimbingRig();
        rig.Links.Survey = WorkerLadderSurveyResult.Unavailable;
        rig.Links.Forget();
        int placed = rig.Body.Placements;
        rig.Step();
        Assert.Equal(WorkerClimbState.Finished, rig.Order.State);
        Assert.Contains("LinkChanged", rig.Order.LastOutcome);
        Assert.Equal(placed, rig.Body.Placements);
        Assert.True(rig.Body.Lease!.Restored);
    }

    [Fact]
    public void AnUnreachableDestinationEndsWithoutTouchingTheBody()
    {
        var rig = new Rig();
        rig.Begin(new WorkPoint(3, 9, 0));
        rig.RunUntil(() => rig.Order.State == WorkerClimbState.Finished);
        Assert.Contains("no route", rig.Order.LastOutcome);
        Assert.Equal(0, rig.Body.Walks);
        Assert.Equal(0, rig.Body.Suspensions);
        Assert.Null(rig.Modes.JobId);
    }

    [Fact]
    public void CancellingWhilePlanningReleasesAtOnce()
    {
        var rig = new Rig();
        rig.Walk.Unknowns = 1000;
        rig.Begin();
        rig.Step();
        Assert.Equal(WorkerClimbState.Searching, rig.Order.State);
        rig.Order.Cancel("cancelled by the player");
        Assert.Equal(WorkerClimbState.Finished, rig.Order.State);
        Assert.Null(rig.Modes.JobId);
        Assert.Equal(0, rig.Body.Walks);
    }

    [Fact]
    public void WalkEvidenceThatNeverArrivesTimesOut()
    {
        var rig = new Rig();
        rig.Walk.Unknowns = int.MaxValue;
        rig.Begin();
        rig.RunUntil(() => rig.Order.State == WorkerClimbState.Finished, WorkerClimbOrder.MaximumPendingWalkAnswers + 50);
        Assert.Contains("EvidenceTimedOut", rig.Order.LastOutcome);
        Assert.Null(rig.Modes.JobId);
    }

    [Fact]
    public void AWedgedWalkToTheLadderEndsBlockedWithoutEverTakingTheLease()
    {
        var rig = new Rig();
        rig.Body.Wedged = true;
        rig.Begin();
        rig.RunUntil(() => rig.Order.State == WorkerClimbState.Finished, 4000);
        Assert.Contains("WalkBlocked", rig.Order.LastOutcome);
        Assert.Equal(0, rig.Body.Suspensions);
        Assert.Equal(Start, rig.Body.Position);
        Assert.Null(rig.Modes.JobId);
    }

    [Fact]
    public void AnUnsupportedTopIsNeverSteppedOnto()
    {
        var rig = new Rig();
        rig.Body.Landing = false;
        rig.Begin();
        rig.RunUntil(() => rig.Order.State == WorkerClimbState.Finished);
        Assert.Contains("UnsafeLanding", rig.Order.LastOutcome);
        Assert.NotEqual(Top, rig.Body.Position);
        Assert.True(rig.Body.Lease!.Restored);
        Assert.Null(rig.Modes.JobId);
    }

    [Fact]
    public void AnUnpinnedWorkerIsRefusedAtConstruction()
    {
        var other = new OtherModes();
        Assert.Throws<ArgumentException>(() => new WorkerClimbOrder(other, new Gates(), _ => { }));
    }

    private sealed class OtherModes : IActorModeHold
    {
        public WorkerKey Worker => WorkerKey.Gunnar;
        public bool IsIdentityKnown => true;
        public ActorMode Mode => ActorMode.Resting;
        public string? JobId => null;
        public bool MayRelocateHome => true;
        public bool MayRetireBody => true;
        public bool IsHeldBy(string? jobId) => false;
        public ActorModeOutcome Enter(ActorMode mode, string jobId) => ActorModeOutcome.Unspecified;
        public ActorModeOutcome Release(string jobId) => ActorModeOutcome.NotHeld;
    }
}
