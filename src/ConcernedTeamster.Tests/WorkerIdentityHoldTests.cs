using System;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.ConcernedTeamster.Domain.Workers;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>Gunnar's identity, and the hold behind it (#381, adopting #371).
///
/// <b>What changed when this product adopted the shared NPC runtime.</b> The
/// mode vocabulary is unchanged - the same five modes, the same outcomes, the
/// same rule that only releasing a job returns the identity to Resting - because
/// every one of those is shipped behaviour with tests on it. What changed is
/// that the hold is no longer this product's to grant: it is asked for, and a
/// refusal is a refusal. These tests are about that boundary.</summary>
public sealed class WorkerIdentityHoldTests
{
    private static WorkerIdentityHold Hold(IWorkerIdentityAuthority? authority = null) =>
        new WorkerIdentityHold(WorkerKey.Gunnar, authority ?? new LocalWorkerIdentityAuthority());

    [Fact]
    public void He_starts_resting_and_holding_nothing()
    {
        WorkerIdentityHold hold = Hold();

        Assert.Equal(ActorMode.Resting, hold.Mode);
        Assert.Null(hold.JobId);
        Assert.True(hold.MayRetireBody);
        Assert.True(hold.MayRelocateHome);
    }

    [Fact]
    public void One_job_takes_him_and_a_second_is_refused()
    {
        // The whole reason for the adoption: a haul and a collection round are
        // two jobs, and Gunnar is one NPC.
        WorkerIdentityHold hold = Hold();

        Assert.Equal(ActorModeOutcome.Entered, hold.Enter(ActorMode.Working, "haul-1"));
        Assert.Equal(ActorModeOutcome.RefusedBusy, hold.Enter(ActorMode.Surveying, "collect-1"));
        Assert.Equal(ActorMode.Working, hold.Mode);
        Assert.True(hold.IsHeldBy("haul-1"));
        Assert.False(hold.IsHeldBy("collect-1"));
    }

    [Fact]
    public void The_same_job_moves_freely_between_modes()
    {
        WorkerIdentityHold hold = Hold();
        hold.Enter(ActorMode.Working, "haul-1");

        Assert.Equal(ActorModeOutcome.AlreadyInMode, hold.Enter(ActorMode.Working, "haul-1"));
        Assert.Equal(ActorModeOutcome.Entered, hold.Enter(ActorMode.Recovering, "haul-1"));
        Assert.Equal(ActorModeOutcome.Entered, hold.Enter(ActorMode.Paused, "haul-1"));
        Assert.Equal(ActorMode.Paused, hold.Mode);
    }

    [Fact]
    public void Only_releasing_the_job_returns_him_to_resting()
    {
        WorkerIdentityHold hold = Hold();
        hold.Enter(ActorMode.Working, "haul-1");

        Assert.Throws<ArgumentOutOfRangeException>(() => hold.Enter(ActorMode.Resting, "haul-1"));
        Assert.Equal(ActorModeOutcome.NotHeld, hold.Release("somebody-else"));
        Assert.Equal(ActorModeOutcome.Released, hold.Release("haul-1"));
        Assert.Equal(ActorMode.Resting, hold.Mode);
        Assert.Null(hold.JobId);
        Assert.True(hold.MayRetireBody);
    }

    [Fact]
    public void An_authority_that_cannot_answer_refuses_rather_than_granting()
    {
        // No world, no registration, no arbiter: authority and custody fail
        // closed, which is what CLAUDE.md requires of both.
        WorkerIdentityHold hold = Hold(new SilentAuthority());

        Assert.Equal(ActorModeOutcome.RefusedBusy, hold.Enter(ActorMode.Working, "haul-1"));
        Assert.Equal(ActorMode.Resting, hold.Mode);
    }

    [Fact]
    public void A_hold_that_ended_underneath_him_is_not_a_hold()
    {
        // The improvement over a mode owner this product kept for itself: the
        // arbiter ends every hold when a world goes away, and this reads
        // through to it rather than remembering an answer from the last world.
        var authority = new LocalWorkerIdentityAuthority();
        WorkerIdentityHold hold = Hold(authority);
        hold.Enter(ActorMode.Working, "haul-1");

        authority.Give("haul-1");

        Assert.Null(hold.JobId);
        Assert.False(hold.IsHeldBy("haul-1"));
    }

    [Fact]
    public void Forgetting_a_world_forgets_the_word_and_never_somebody_elses_hold()
    {
        var authority = new LocalWorkerIdentityAuthority();
        WorkerIdentityHold hold = Hold(authority);
        hold.Enter(ActorMode.Working, "haul-1");

        hold.ForgetWorld();

        Assert.Equal(ActorMode.Resting, hold.Mode);
        // The authority still records the holder: ending a world is the
        // arbiter's event, and a product reaching in to release a hold it does
        // not own is the failure the arbiter exists to prevent.
        Assert.Equal("haul-1", authority.Holder);
    }

    [Fact]
    public void Every_change_moves_the_revision_so_an_observer_can_tell_a_stale_read()
    {
        WorkerIdentityHold hold = Hold();
        int start = hold.Revision;

        hold.Enter(ActorMode.Working, "haul-1");
        hold.Enter(ActorMode.Recovering, "haul-1");
        hold.Release("haul-1");

        Assert.Equal(start + 3, hold.Revision);
    }

    [Fact]
    public void A_job_with_no_name_and_an_identity_with_no_worker_are_both_refused()
    {
        Assert.Throws<ArgumentException>(
            () => new WorkerIdentityHold(default, new LocalWorkerIdentityAuthority()));
        Assert.Throws<ArgumentNullException>(() => new WorkerIdentityHold(WorkerKey.Gunnar, null!));
        Assert.Throws<ArgumentException>(() => Hold().Enter(ActorMode.Working, string.Empty));
    }

    [Fact]
    public void The_executor_takes_its_hold_from_outside_and_cannot_mint_one()
    {
        // The rule in its smallest form: the only constructor takes an
        // authority, so there is no executor anywhere that granted itself the
        // identity it is moving a body with.
        var parameters = typeof(HaulExecutor).GetConstructors()[0].GetParameters();

        Assert.Contains(parameters, parameter => parameter.ParameterType == typeof(IWorkerIdentityAuthority));
        Assert.Single(typeof(HaulExecutor).GetConstructors());
    }

    /// <summary>An authority in no position to answer - the shape of a process
    /// with no world loaded.</summary>
    private sealed class SilentAuthority : IWorkerIdentityAuthority
    {
        public string? Holder => null;

        public WorkerHoldOutcome Take(string jobId) => WorkerHoldOutcome.Unavailable;

        public WorkerHoldOutcome Give(string jobId) => WorkerHoldOutcome.NotHeld;
    }
}
