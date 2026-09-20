using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.Workers;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
namespace ConcernedTeamster.Tests;

public class CollectionIdentityLeaseTests
{
    [Fact]
    public void Collection_refuses_a_haul_and_does_not_release_it()
    {
        var rig = new HaulingExecutionRig();
        rig.Assign(); rig.Go();
        string? haul = rig.Executor.Modes.JobId;
        Assert.NotNull(haul);
        var pick = new CollectionIdentityLease();
        Assert.False(pick.TryAcquire(rig.Executor.Modes));
        pick.Release();
        Assert.Equal(haul, rig.Executor.Modes.JobId);
    }
    [Fact]
    public void Collection_holds_identity_across_idle_haul_ticks_and_teardown()
    {
        var rig = new HaulingExecutionRig();
        rig.Assign();
        var pick = new CollectionIdentityLease();
        Assert.True(pick.TryAcquire(rig.Executor.Modes));
        Assert.False(rig.Executor.Modes.MayRetireBody);
        Assert.Equal(HaulCommandOutcome.Rejected, rig.Go().Outcome);
        Assert.Equal(HaulCommandDetail.HaulBusy, rig.Executor.LastCommandDetail);
        rig.AdvanceFramesOnly(1f);
        Assert.True(pick.IsHeld);
        rig.Executor.Teardown("haul stopped", TheConcernedCat.ConcernedTeamster.Domain.Hauling.LeaseInvalidation.WorldReloaded);
        Assert.True(pick.IsHeld);
        pick.Release();
        Assert.Null(rig.Executor.Modes.JobId);
        Assert.True(rig.Executor.Modes.MayRetireBody);
    }
    [Fact]
    public void Another_pick_and_a_missing_authority_refuse_until_completion()
    {
        var rig = new HaulingExecutionRig();
        var first = new CollectionIdentityLease();
        var second = new CollectionIdentityLease();
        Assert.False(first.TryAcquire(null));
        Assert.True(first.TryAcquire(rig.Executor.Modes));
        Assert.False(second.TryAcquire(rig.Executor.Modes));
        second.Release();
        Assert.True(first.IsHeld);
        first.Release();
        Assert.True(second.TryAcquire(rig.Executor.Modes));
        second.Release();
    }
}
