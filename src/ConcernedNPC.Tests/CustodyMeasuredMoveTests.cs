using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>#376: a movement a role carried out and measured itself, recorded in
/// the library's ledger rather than in a second copy of its invariant.
///
/// Every refusal here is asserted together with "and nothing changed", because a
/// refusal that still moved units is the failure this file exists to catch.
/// </summary>
public class CustodyMeasuredMoveTests
{
    private readonly NpcWorldEpoch _world = Identities.AWorld();
    private readonly NpcCustodyLedger _ledger = new NpcCustodyLedger();

    public CustodyMeasuredMoveTests()
    {
        _ledger.OpenJob(Custody.Job);
        _ledger.OpenJob(Custody.OtherJob);
    }

    private NpcCustodyLocation Released => new NpcCustodyLocation(NpcCustodyPlace.Released, "a-spill", _world);

    private NpcCustodyLocation Lost => new NpcCustodyLocation(NpcCustodyPlace.Lost, "gone", _world);

    [Fact]
    public void Measured_moves_conserve_at_every_transition()
    {
        Assert.Equal(NpcCustodyOutcome.Applied, Take(0, 10));
        AssertConserved(10);

        Assert.Equal(NpcCustodyOutcome.Applied, Measured(1, Custody.Body(_world), Custody.Chest(_world), 4));
        AssertConserved(10);

        Assert.Equal(NpcCustodyOutcome.Applied, Measured(2, Custody.Chest(_world), Custody.Delivered(_world), 4));
        AssertConserved(10);

        Assert.Equal(NpcCustodyOutcome.Applied, Measured(3, Custody.Body(_world), Released, 3));
        AssertConserved(10);

        Assert.Equal(NpcCustodyOutcome.Applied, Measured(4, Custody.Body(_world), Lost, 3));
        AssertConserved(10);

        // Every unit is somewhere nameable, and nothing is left on the body.
        Assert.Equal(0, Held(Custody.Body(_world)));
        Assert.Equal(0, Held(Custody.Chest(_world)));
        Assert.Equal(4, Held(Custody.Delivered(_world)));
        Assert.Equal(3, Held(Released));
        Assert.Equal(3, Held(Lost));
    }

    [Fact]
    public void The_same_measured_move_restated_moves_nothing()
    {
        Take(0, 10);
        int before = _ledger.Revision;

        Assert.Equal(NpcCustodyOutcome.Applied, Measured(1, Custody.Body(_world), Custody.Chest(_world), 4));
        Assert.Equal(NpcCustodyOutcome.AlreadySatisfied, Measured(1, Custody.Body(_world), Custody.Chest(_world), 4));
        Assert.Equal(NpcCustodyOutcome.AlreadySatisfied, Measured(1, Custody.Body(_world), Custody.Chest(_world), 4));

        Assert.Equal(6, Held(Custody.Body(_world)));
        Assert.Equal(4, Held(Custody.Chest(_world)));
        Assert.Equal(before + 1, _ledger.Revision);
        AssertConserved(10);
    }

    [Fact]
    public void A_name_reused_for_a_different_move_is_refused_and_keeps_the_first()
    {
        Take(0, 10);
        Measured(1, Custody.Body(_world), Custody.Chest(_world), 4);

        Assert.Equal(
            NpcCustodyOutcome.RejectedDifferentPayload, Measured(1, Custody.Body(_world), Custody.Chest(_world), 5));
        Assert.Equal(
            NpcCustodyOutcome.RejectedDifferentPayload, Measured(1, Custody.Body(_world), Custody.Delivered(_world), 4));

        Assert.Equal(6, Held(Custody.Body(_world)));
        Assert.Equal(4, Held(Custody.Chest(_world)));
        Assert.Equal(0, Held(Custody.Delivered(_world)));
        AssertConserved(10);
    }

    [Fact]
    public void More_than_the_record_holds_is_refused_rather_than_clamped()
    {
        Take(0, 3);

        Assert.Equal(NpcCustodyOutcome.Rejected, Measured(1, Custody.Body(_world), Custody.Chest(_world), 4));
        Assert.Equal(3, Held(Custody.Body(_world)));
        Assert.Equal(0, Held(Custody.Chest(_world)));
        AssertConserved(3);

        // A refusal records nothing, so the name is still free for the move the
        // role actually measured.
        Assert.Equal(NpcCustodyOutcome.Applied, Measured(1, Custody.Body(_world), Custody.Chest(_world), 3));
        Assert.Equal(3, Held(Custody.Chest(_world)));
    }

    [Fact]
    public void Material_is_never_moved_out_of_a_terminal_place_again()
    {
        Take(0, 16);
        Measured(1, Custody.Body(_world), Custody.Delivered(_world), 4);
        Measured(2, Custody.Body(_world), Released, 4);
        Measured(3, Custody.Body(_world), Lost, 4);
        var handed = new NpcCustodyLocation(NpcCustodyPlace.Handed, "the-player", _world);
        Measured(4, Custody.Body(_world), handed, 4);

        int step = 10;
        foreach (NpcCustodyLocation terminal in new[] { Custody.Delivered(_world), Released, Lost, handed })
        {
            Assert.Equal(NpcCustodyOutcome.Rejected, Measured(step++, terminal, Custody.Body(_world), 1));
            Assert.Equal(4, Held(terminal));
        }

        Assert.Equal(0, Held(Custody.Body(_world)));
        AssertConserved(16);
    }

    [Fact]
    public void Released_is_terminal_and_is_never_counted_against_the_world()
    {
        Assert.False(Released.MayLeave);
        Assert.False(Released.IsObservable);
        Assert.True(Released.IsSpecified);
    }

    [Fact]
    public void Nothing_is_recorded_onto_the_ground_as_material_the_job_carries()
    {
        Take(0, 5);

        Assert.Equal(NpcCustodyOutcome.Rejected, Measured(1, Custody.Body(_world), Custody.Ground(_world), 5));
        Assert.Equal(5, Held(Custody.Body(_world)));
        Assert.Equal(0, Held(Custody.Ground(_world)));
    }

    [Fact]
    public void A_name_the_executor_has_open_is_never_answered_as_a_measured_move()
    {
        Take(0, 10);
        var intent = new NpcTransferIntent(
            Custody.Step(1), Custody.Body(_world), Custody.Chest(_world), Custody.Timber, 4, _ledger.Revision);
        Assert.Equal(NpcCustodyOutcome.Applied, _ledger.Begin(intent));

        // The same name and payload: the executor's transfer may or may not have
        // happened, so recording it as measured would say units moved that the
        // record never moved.
        Assert.Equal(NpcCustodyOutcome.Rejected, Measured(1, Custody.Body(_world), Custody.Chest(_world), 4));

        // And while it waits, nothing else of that job moves either - the rule the
        // executor already keeps.
        Assert.Equal(NpcCustodyOutcome.Rejected, Measured(2, Custody.Body(_world), Custody.Chest(_world), 1));

        Assert.Equal(10, Held(Custody.Body(_world)));
        Assert.Equal(0, Held(Custody.Chest(_world)));
        AssertConserved(10);
    }

    [Fact]
    public void A_measured_move_is_never_started_again_by_the_executor()
    {
        Take(0, 10);
        Measured(1, Custody.Body(_world), Custody.Chest(_world), 4);

        var again = new NpcTransferIntent(
            Custody.Step(1), Custody.Body(_world), Custody.Chest(_world), Custody.Timber, 4, _ledger.Revision);
        Assert.Equal(NpcTransferOutcome.AlreadySatisfied, _ledger.Check(again, out _));

        var different = new NpcTransferIntent(
            Custody.Step(1), Custody.Body(_world), Custody.Chest(_world), Custody.Timber, 6, _ledger.Revision);
        Assert.Equal(NpcTransferOutcome.RejectedDifferentPayload, _ledger.Check(different, out _));
    }

    [Fact]
    public void A_name_the_world_rolled_back_is_stale_rather_than_done()
    {
        Take(0, 10);
        var intent = new NpcTransferIntent(
            Custody.Step(1), Custody.Body(_world), Custody.Chest(_world), Custody.Timber, 4, _ledger.Revision);
        Assert.Equal(NpcCustodyOutcome.Applied, _ledger.Begin(intent, NpcRowStanding.Voided));

        Assert.Equal(NpcCustodyOutcome.Stale, Measured(1, Custody.Body(_world), Custody.Chest(_world), 4));
        Assert.Equal(10, Held(Custody.Body(_world)));
    }

    [Fact]
    public void Both_places_are_named_in_one_load()
    {
        Take(0, 10);
        NpcWorldEpoch otherWorld = Identities.AWorld();

        Assert.Equal(
            NpcCustodyOutcome.Rejected,
            Measured(1, Custody.Body(_world), Custody.Chest(NpcWorldEpoch.Unknown), 1));
        Assert.Equal(
            NpcCustodyOutcome.Rejected,
            Measured(2, Custody.Body(_world), Custody.Chest(otherWorld), 1));
        Assert.Equal(10, Held(Custody.Body(_world)));

        // A ledger that never outlives its load may name both in the unknown one.
        var ledger = new NpcCustodyLedger();
        ledger.OpenJob(Custody.Job);
        Assert.Equal(
            NpcCustodyOutcome.Applied,
            ledger.Acquire(Custody.Step(0), Custody.Body(NpcWorldEpoch.Unknown), Custody.Timber, 2));
        Assert.Equal(
            NpcCustodyOutcome.Applied,
            ledger.RecordMeasuredMove(
                Custody.Step(1), Custody.Body(NpcWorldEpoch.Unknown), Custody.Chest(NpcWorldEpoch.Unknown),
                Custody.Timber, 2));
        Assert.True(ledger.IsConserved(Custody.Job, Custody.Timber));
    }

    [Fact]
    public void A_job_that_is_not_open_records_no_movement()
    {
        Take(0, 4);
        _ledger.CloseJob(Custody.Job);

        Assert.Equal(NpcCustodyOutcome.Rejected, Measured(1, Custody.Body(_world), Custody.Chest(_world), 4));
        Assert.Equal(4, Held(Custody.Body(_world)));
    }

    [Fact]
    public void A_malformed_movement_is_refused_and_changes_nothing()
    {
        Take(0, 4);
        Assert.Equal(
            NpcCustodyOutcome.Rejected,
            _ledger.RecordMeasuredMove(default, Custody.Body(_world), Custody.Chest(_world), Custody.Timber, 1));
        Assert.Equal(NpcCustodyOutcome.Rejected, Measured(1, default, Custody.Chest(_world), 1));
        Assert.Equal(NpcCustodyOutcome.Rejected, Measured(2, Custody.Body(_world), default, 1));
        Assert.Equal(NpcCustodyOutcome.Rejected, Measured(3, Custody.Body(_world), Custody.Body(_world), 1));
        Assert.Equal(NpcCustodyOutcome.Rejected, Measured(4, Custody.Body(_world), Custody.Chest(_world), 0));
        Assert.Equal(NpcCustodyOutcome.Rejected, Measured(5, Custody.Body(_world), Custody.Chest(_world), -1));
        Assert.Equal(
            NpcCustodyOutcome.Rejected,
            _ledger.RecordMeasuredMove(
                Custody.Step(6), Custody.Body(_world), Custody.Chest(_world), NpcMaterial.Of(null), 1));

        Assert.Equal(4, Held(Custody.Body(_world)));
        Assert.Empty(_ledger.Transfers);
    }

    [Fact]
    public void One_jobs_units_are_never_moved_by_another_jobs_name()
    {
        Take(0, 4);

        Assert.Equal(
            NpcCustodyOutcome.Rejected,
            _ledger.RecordMeasuredMove(
                Custody.OtherStep(1), Custody.Body(_world), Custody.Chest(_world), Custody.Timber, 1));
        Assert.Equal(4, Held(Custody.Body(_world)));
    }

    [Fact]
    public void Defaults_a_role_can_now_hold_never_read_as_success_or_throw()
    {
        Assert.Equal(NpcCustodyOutcome.Unspecified, default(NpcCustodyOutcome));
        Assert.NotEqual(NpcCustodyOutcome.Applied, default(NpcCustodyOutcome));
        Assert.Equal(NpcCustodyPlace.Unspecified, default(NpcCustodyPlace));

        NpcCustodyLocation location = default;
        Assert.False(location.IsSpecified);
        Assert.Equal(string.Empty, location.Key);
        Assert.NotNull(location.ToString());

        NpcHolding holding = default;
        Assert.Equal(string.Empty, holding.JobId);
        Assert.NotNull(holding.ToString());
    }

    private NpcCustodyOutcome Take(int step, int count) =>
        _ledger.Acquire(Custody.Step(step), Custody.Body(_world), Custody.Timber, count);

    private NpcCustodyOutcome Measured(int step, NpcCustodyLocation from, NpcCustodyLocation to, int count) =>
        _ledger.RecordMeasuredMove(Custody.Step(step), from, to, Custody.Timber, count);

    private int Held(NpcCustodyLocation at) => _ledger.HoldingAt(Custody.Job, at, Custody.Timber);

    private void AssertConserved(int expected)
    {
        Assert.Equal(expected, _ledger.Acquired(Custody.Job, Custody.Timber));
        Assert.Equal(expected, _ledger.TotalEverywhere(Custody.Job, Custody.Timber));
        Assert.True(_ledger.IsConserved(Custody.Job, Custody.Timber));
    }
}
