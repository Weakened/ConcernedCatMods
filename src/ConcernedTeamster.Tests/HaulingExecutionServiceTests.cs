using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>#313: Gunnar's <see cref="IHaulService"/> keeps CONTRACTS.md §3.2 —
/// one haul per Gunnar, a revision that moves on phase, lease and reason
/// changes only, stale revisions refused without effect, route refusals that
/// move nothing, and wait and cancel semantics around transfers.</summary>
public class HaulingExecutionServiceTests
{
    [Fact]
    public void TheRevisionMovesOnPhaseLeaseAndReasonChangesButNotOnPositions()
    {
        var rig = new HaulingExecutionRig();
        IHaulService service = rig.Service;
        int start = service.Snapshot.Revision;

        rig.Assign();
        int assigned = service.Snapshot.Revision;
        Assert.True(assigned > start);

        rig.Go();
        rig.Advance(1f);
        int approaching = service.Snapshot.Revision;

        // Gunnar walks and the cart rolls: positions change, the revision does not.
        for (int step = 0; step < 20; step++)
        {
            rig.Body.Facts = rig.Body.Facts.With(f => f.Position = new WorkPoint(0f, 0f, 6f - (step * 0.1f)));
            rig.Seam.Observation = rig.Seam.Observation.With(o => o.CartPosition = new WorkPoint(0f, 0f, step * 0.01f));
            rig.StepOnce();
        }

        Assert.Equal(approaching, service.Snapshot.Revision);

        rig.Executor.ReleaseLease();
        Assert.True(service.Snapshot.Revision > approaching);
        rig.AssertNoBugs();
    }

    [Fact]
    public void AStaleRevisionIsRefusedAndChangesNothing()
    {
        var rig = new HaulingExecutionRig();
        rig.Assign();
        int revision = rig.Executor.Revision;

        HaulCommandResult stale = rig.Service.RequestLeg(
            new HaulLegRequest("haul-1", "order-1", "lease-1", false, HaulingExecutionRig.Target, 2f), revision - 1);

        Assert.Equal(HaulCommandOutcome.Stale, stale.Outcome);
        Assert.Equal(HaulCommandDetail.StaleRevision, rig.Service.LastCommandDetail);
        Assert.Equal(revision, stale.Revision);
        Assert.Equal(HaulPhase.Ready, rig.Service.Snapshot.Phase);
        Assert.Equal(0, rig.Planner.PlanCalls);
    }

    [Fact]
    public void OneHaulPerGunnar()
    {
        var rig = new HaulingExecutionRig();
        rig.AssignAndStartLeg();

        HaulCommandResult second = rig.Service.RequestLeg(
            new HaulLegRequest("haul-2", string.Empty, "lease-1", true, HaulingExecutionRig.Target, 2f), rig.Executor.Revision);

        Assert.Equal(HaulCommandOutcome.Rejected, second.Outcome);
        Assert.Equal(HaulCommandDetail.HaulBusy, rig.Service.LastCommandDetail);
        Assert.Equal("haul-1", rig.Service.Snapshot.HaulId);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ARefusedRouteAnswersItsReasonAndMovesNothing()
    {
        (CartRouteVerdict verdict, HaulCommandOutcome outcome, HaulAttentionReason reason)[] cases =
        {
            (CartRouteVerdict.NoPath, HaulCommandOutcome.Rejected, HaulAttentionReason.NoRoute),
            (CartRouteVerdict.TooSteep, HaulCommandOutcome.Rejected, HaulAttentionReason.TooSteep),
            (CartRouteVerdict.TooNarrow, HaulCommandOutcome.Rejected, HaulAttentionReason.TooNarrow),
            (CartRouteVerdict.ForbiddenDoor, HaulCommandOutcome.Rejected, HaulAttentionReason.ForbiddenDoor),
            (CartRouteVerdict.Water, HaulCommandOutcome.Rejected, HaulAttentionReason.Water),
            (CartRouteVerdict.UnsupportedGap, HaulCommandOutcome.Rejected, HaulAttentionReason.UnsupportedGap),
            (CartRouteVerdict.OutsideLoadedArea, HaulCommandOutcome.Rejected, HaulAttentionReason.OutsideLoadedArea),
            (CartRouteVerdict.UnsafeStop, HaulCommandOutcome.Rejected, HaulAttentionReason.UnsafeParking),
            (CartRouteVerdict.BudgetExhausted, HaulCommandOutcome.Unavailable, HaulAttentionReason.NoRoute),
        };

        foreach ((CartRouteVerdict verdict, HaulCommandOutcome outcome, HaulAttentionReason reason) in cases)
        {
            var rig = new HaulingExecutionRig();
            rig.Assign();
            rig.Planner.NextVerdict = verdict;
            int revision = rig.Executor.Revision;

            HaulCommandResult result = rig.Go();

            Assert.Equal(outcome, result.Outcome);
            Assert.Equal(reason, result.Reason);
            Assert.Equal(revision, rig.Executor.Revision);
            Assert.Equal(HaulPhase.Ready, rig.Executor.Phase);
            Assert.Equal(string.Empty, rig.Executor.HaulId);
            rig.Advance(1f);
            Assert.Equal(0, rig.Body.WalkCommands);
            Assert.Equal(ActorMode.Resting, rig.Executor.Modes.Mode);
        }
    }

    [Fact]
    public void WithoutAuthorityALeaseOrABodyNothingStarts()
    {
        var noAuthority = new HaulingExecutionRig();
        noAuthority.Assign();
        noAuthority.Authority.Verdict = WorkAuthorityVerdict.OtherPeersConnected;
        HaulCommandResult peers = noAuthority.Go();
        Assert.Equal(HaulCommandOutcome.Unavailable, peers.Outcome);
        Assert.Equal(HaulAttentionReason.OtherPeersConnected, peers.Reason);

        var noLease = new HaulingExecutionRig();
        Assert.Equal(HaulCommandOutcome.Rejected, noLease.Go().Outcome);
        Assert.Equal(HaulCommandDetail.NoLease, noLease.Executor.LastCommandDetail);

        var wrongLease = new HaulingExecutionRig();
        wrongLease.Assign();
        Assert.Equal(HaulCommandOutcome.Rejected, wrongLease.Go(leaseId: "lease-9").Outcome);
        Assert.Equal(HaulCommandDetail.LeaseMismatch, wrongLease.Executor.LastCommandDetail);

        var noBody = new HaulingExecutionRig();
        noBody.Assign();
        noBody.Body.Facts = noBody.Body.Facts.With(f => f.Present = false);
        HaulCommandResult bodyless = noBody.Go();
        Assert.Equal(HaulCommandOutcome.Unavailable, bodyless.Outcome);
        Assert.Equal(HaulAttentionReason.WorkerBodyLost, bodyless.Reason);

        var unbound = new GunnarHaulService(new FakeAuthority());
        Assert.Equal(
            HaulCommandOutcome.Unavailable,
            unbound.RequestLeg(new HaulLegRequest("haul-1", string.Empty, "lease-1", true, HaulingExecutionRig.Target, 2f), 0).Outcome);
        Assert.Equal(HaulPhase.Unassigned, unbound.Snapshot.Phase);
        Assert.False(unbound.WorkerAvailable);
        Assert.Equal(Guid.Empty, unbound.WorldLoadEpoch);
    }

    [Fact]
    public void TransferringIsLegalOnlyWaitingWithTheCartStillAndDoneOnlyWhileUnloading()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();
        Assert.Equal(HaulCommandOutcome.Rejected, rig.Service.AcknowledgeWait("haul-1", rig.Executor.Revision, true).Outcome);
        Assert.Equal(HaulCommandDetail.NotWaitingStill, rig.Service.LastCommandDetail);

        rig.ArriveAtTarget();
        rig.StepOnce();
        rig.Advance(rig.Limits.StillForSeconds + 0.1f);
        Assert.Equal(HaulPhase.Waiting, rig.Executor.Phase);

        Assert.Equal(HaulCommandOutcome.Rejected, rig.Service.AcknowledgeWait("haul-1", rig.Executor.Revision, false).Outcome);
        Assert.Equal(HaulCommandDetail.NotUnloading, rig.Service.LastCommandDetail);
        Assert.Equal(HaulCommandOutcome.Rejected, rig.Service.AcknowledgeWait("haul-9", rig.Executor.Revision, true).Outcome);
        Assert.Equal(HaulCommandDetail.UnknownHaul, rig.Service.LastCommandDetail);

        // A cart that is moving is not still, however briefly.
        rig.Seam.Observation = rig.Seam.Observation.With(o => o.SpeedMetresPerSecond = 0.3f);
        rig.StepOnce();
        Assert.Equal(HaulCommandOutcome.Rejected, rig.Service.AcknowledgeWait("haul-1", rig.Executor.Revision, true).Outcome);
        rig.Seam.Observation = rig.Seam.Observation.With(o => o.SpeedMetresPerSecond = 0f);
        rig.Advance(rig.Limits.StillForSeconds + 0.1f);

        HaulCommandResult transferring = rig.Service.AcknowledgeWait("haul-1", rig.Executor.Revision, true);
        Assert.Equal(HaulCommandOutcome.Accepted, transferring.Outcome);
        Assert.Equal(HaulPhase.Unloading, rig.Service.Snapshot.Phase);
        Assert.Equal(rig.Executor.Revision, transferring.Revision);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ArrivedIsTrueOnlyWaitingAtTheGoalWithTheCartStill()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();
        Assert.False(rig.Service.Snapshot.Arrived);

        rig.ArriveAtTarget();
        rig.StepOnce();
        Assert.False(rig.Service.Snapshot.Arrived);
        rig.Advance(rig.Limits.StillForSeconds + 0.1f);

        HaulSnapshot waiting = rig.Service.Snapshot;
        Assert.True(waiting.Arrived);
        Assert.True(waiting.Attached);
        Assert.True(waiting.CartStill);
        Assert.True(waiting.CartUpright);
        Assert.Equal(HaulPhase.Waiting, waiting.Phase);
        Assert.NotNull(waiting.CartPosition);
        Assert.NotNull(waiting.WorkerPosition);

        rig.Seam.Observation = rig.Seam.Observation.With(o => o.SpeedMetresPerSecond = 0.5f);
        rig.StepOnce();
        Assert.False(rig.Service.Snapshot.Arrived);
        rig.AssertNoBugs();
    }

    [Fact]
    public void CancelSemanticsFollowTheContract()
    {
        // Approaching: stops and ends the haul, lease kept.
        var approaching = new HaulingExecutionRig();
        approaching.AssignAndStartLeg();
        Assert.Equal(HaulCommandOutcome.Accepted, approaching.Service.Cancel("haul-1", detachAndPark: false).Outcome);
        Assert.Equal(HaulPhase.Ready, approaching.Executor.Phase);
        Assert.Equal(string.Empty, approaching.Executor.HaulId);
        Assert.NotNull(approaching.Executor.ActiveLease);

        // Pulling, stop and wait: ends in Waiting, still hitched.
        var stopAndWait = new HaulingExecutionRig();
        stopAndWait.RunToPulling();
        Assert.Equal(HaulCommandOutcome.Accepted, stopAndWait.Service.Cancel("haul-1", detachAndPark: false).Outcome);
        stopAndWait.Advance(stopAndWait.Limits.StillForSeconds + 0.1f);
        Assert.Equal(HaulPhase.Waiting, stopAndWait.Executor.Phase);
        Assert.True(stopAndWait.Executor.Attached);
        Assert.Equal("haul-1", stopAndWait.Executor.HaulId);

        // Pulling, detach and park: ends in Ready after Detaching, lease kept.
        var park = new HaulingExecutionRig();
        park.RunToPulling();
        Assert.Equal(HaulCommandOutcome.Accepted, park.Service.Cancel("haul-1", detachAndPark: true).Outcome);
        park.Advance(park.Limits.StillForSeconds + park.Execution.DetachSettleSeconds + 0.5f);
        Assert.Equal(HaulPhase.Ready, park.Executor.Phase);
        Assert.False(park.Executor.Attached);
        Assert.NotNull(park.Executor.ActiveLease);

        // Unknown haul.
        Assert.Equal(HaulCommandOutcome.Rejected, park.Service.Cancel("haul-1", detachAndPark: true).Outcome);
        Assert.Equal(HaulCommandDetail.UnknownHaul, park.Service.LastCommandDetail);

        approaching.AssertNoBugs();
        stopAndWait.AssertNoBugs();
        park.AssertNoBugs();
    }

    [Fact]
    public void ANewWorldNeverReusesARevisionTheConsumerSaw()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToWaiting();
        int seen = rig.Service.Snapshot.Revision;

        rig.ReloadWorld();

        Assert.True(rig.Service.Snapshot.Revision > seen);
        Assert.Equal(HaulingExecutionRig.NextEpoch, rig.Service.WorldLoadEpoch);
        HaulCommandResult stale = rig.Service.RequestLeg(
            new HaulLegRequest("haul-1", string.Empty, "lease-1", true, HaulingExecutionRig.Target, 2f), seen);
        Assert.Equal(HaulCommandOutcome.Stale, stale.Outcome);
        rig.AssertNoBugs();
    }

    [Fact]
    public void TheSnapshotNamesNoHaulWhenNoneIsActive()
    {
        var rig = new HaulingExecutionRig();
        HaulSnapshot idle = rig.Service.Snapshot;
        Assert.Equal(string.Empty, idle.HaulId);
        Assert.Equal(HaulPhase.Unassigned, idle.Phase);
        Assert.Equal(HaulAttentionReason.Unspecified, idle.Attention);
        Assert.False(idle.Attached);
        Assert.Null(rig.Service.ActiveLease);
        Assert.Equal(WorkAuthorityVerdict.Granted, rig.Service.Authority);
        Assert.True(rig.Service.WorkerAvailable);
    }
}
