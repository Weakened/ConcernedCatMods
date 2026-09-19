using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

public class CustodyLedgerTests
{
    private readonly NpcWorldEpoch _world = Identities.AWorld();
    private readonly NpcCustodyLedger _ledger = new NpcCustodyLedger();

    public CustodyLedgerTests()
    {
        _ledger.OpenJob(Custody.Job);
        _ledger.OpenJob(Custody.OtherJob);
    }

    [Fact]
    public void Material_conserves_across_every_transition()
    {
        // The invariant, checked at every step rather than at the end: what was
        // acquired equals what is held, everywhere, always. Every way material
        // has gone missing in this repository began with a step that was only
        // checked afterwards.
        Assert.Equal(NpcCustodyOutcome.Applied, Acquire(0, Custody.Ground(_world), 10));
        AssertConserved(10);

        Move(1, Custody.Ground(_world), Custody.Body(_world), 10);
        AssertConserved(10);

        Move(2, Custody.Body(_world), Custody.Chest(_world), 6);
        AssertConserved(10);

        Move(3, Custody.Chest(_world), Custody.Delivered(_world), 6);
        AssertConserved(10);

        Assert.Equal(
            NpcCustodyOutcome.Applied,
            _ledger.RecordLoss(Custody.Step(4), Custody.Body(_world), Custody.Timber, 4));
        AssertConserved(10);

        // And every unit is somewhere nameable: four lost, six delivered.
        Assert.Equal(6, _ledger.HoldingAt(Custody.Job, Custody.Delivered(_world), Custody.Timber));
        Assert.Equal(0, _ledger.HoldingAt(Custody.Job, Custody.Body(_world), Custody.Timber));
    }

    [Fact]
    public void The_same_acquisition_restored_twice_adds_nothing()
    {
        // "A reservation must never duplicate an item because it was restored
        // twice", at its root: the name is derived from the job and the step,
        // so restoring is re-stating a name the ledger already has.
        Assert.Equal(NpcCustodyOutcome.Applied, Acquire(0, Custody.Ground(_world), 10));
        Assert.Equal(NpcCustodyOutcome.AlreadySatisfied, Acquire(0, Custody.Ground(_world), 10));
        Assert.Equal(NpcCustodyOutcome.AlreadySatisfied, Acquire(0, Custody.Ground(_world), 10));

        Assert.Equal(10, _ledger.TotalEverywhere(Custody.Job, Custody.Timber));
        AssertConserved(10);
    }

    [Fact]
    public void The_same_name_carrying_a_different_payload_is_refused_rather_than_answered_as_done()
    {
        // Answering "already done" to a different payload is what told a caller
        // reusing a name that the wrong thing had happened successfully, and
        // then silently kept the first one.
        Assert.Equal(NpcCustodyOutcome.Applied, Acquire(0, Custody.Ground(_world), 10));

        Assert.Equal(NpcCustodyOutcome.RejectedDifferentPayload, Acquire(0, Custody.Ground(_world), 11));
        Assert.Equal(NpcCustodyOutcome.RejectedDifferentPayload, Acquire(0, Custody.Body(_world), 10));

        Assert.Equal(10, _ledger.TotalEverywhere(Custody.Job, Custody.Timber));
    }

    [Fact]
    public void A_transfer_is_never_started_twice_even_when_its_result_is_unknown()
    {
        Acquire(0, Custody.Body(_world), 5);
        var intent = Intent(1, Custody.Body(_world), Custody.Chest(_world), 5);

        Assert.Equal(NpcTransferOutcome.Unspecified, _ledger.Check(intent, out _));
        _ledger.CountRecord();
        Assert.Equal(NpcCustodyOutcome.Applied, _ledger.Begin(intent));

        // Open, so it may have happened. Never started again, by anybody.
        Assert.Equal(NpcTransferOutcome.Uncertain, _ledger.Check(intent, out string reason));
        Assert.Contains("not recorded", reason, StringComparison.Ordinal);

        _ledger.MarkUncertain(intent.Request, "the engine threw");
        Assert.Equal(NpcTransferOutcome.Uncertain, _ledger.Check(intent, out _));
        AssertConserved(5);
    }

    [Fact]
    public void An_interrupted_session_turns_every_unfinished_intent_into_an_uncertainty()
    {
        Acquire(0, Custody.Body(_world), 5);
        var intent = Intent(1, Custody.Body(_world), Custody.Chest(_world), 5);
        _ledger.CountRecord();
        _ledger.Begin(intent);

        // Replayed from a record: nothing replayed is still in flight.
        Assert.Equal(1, _ledger.CloseOpenIntents());
        Assert.Equal(0, _ledger.CloseOpenIntents());

        Assert.True(_ledger.TryGetTransfer(intent.Request, out NpcTransferRecord record));
        Assert.Equal(NpcTransferStatus.Uncertain, record.Status);
        Assert.True(record.AwaitsResolution);
        AssertConserved(5);
    }

    [Fact]
    public void A_person_resolving_an_uncertain_transfer_settles_it_exactly_once()
    {
        Acquire(0, Custody.Body(_world), 5);
        var intent = Intent(1, Custody.Body(_world), Custody.Chest(_world), 5);
        _ledger.CountRecord();
        _ledger.Begin(intent);
        _ledger.CloseOpenIntents();

        Assert.Equal(
            NpcCustodyOutcome.Applied, _ledger.Resolve(intent.Request, NpcTransferSide.Destination, 5));
        Assert.Equal(5, _ledger.HoldingAt(Custody.Job, Custody.Chest(_world), Custody.Timber));

        // A second answer, the same or different, changes nothing. Terminal is
        // terminal, or a player who answers twice moves the material twice.
        Assert.Equal(
            NpcCustodyOutcome.AlreadySatisfied, _ledger.Resolve(intent.Request, NpcTransferSide.Destination, 5));
        Assert.Equal(
            NpcCustodyOutcome.AlreadySatisfied, _ledger.Resolve(intent.Request, NpcTransferSide.Source, 5));
        Assert.Equal(5, _ledger.HoldingAt(Custody.Job, Custody.Chest(_world), Custody.Timber));
        AssertConserved(5);
    }

    [Fact]
    public void A_resolution_can_never_move_more_than_the_record_holds()
    {
        Acquire(0, Custody.Body(_world), 5);
        var intent = Intent(1, Custody.Body(_world), Custody.Chest(_world), 5);
        _ledger.CountRecord();
        _ledger.Begin(intent);
        _ledger.CloseOpenIntents();

        // A person saying "forty arrived" does not make forty exist.
        Assert.Equal(
            NpcCustodyOutcome.Applied, _ledger.Resolve(intent.Request, NpcTransferSide.Destination, 40));

        Assert.Equal(5, _ledger.HoldingAt(Custody.Job, Custody.Chest(_world), Custody.Timber));
        AssertConserved(5);
    }

    [Fact]
    public void A_row_the_world_rolled_back_is_remembered_and_applied_to_nothing()
    {
        // The player loads yesterday's save. The world has undone this; the
        // record agreeing with it is not a correction, and replaying it would
        // credit material into a chest that does not contain it.
        Assert.Equal(
            NpcCustodyOutcome.Applied,
            _ledger.Acquire(Custody.Step(0), Custody.Body(_world), Custody.Timber, 10, NpcRowStanding.Voided));

        Assert.Equal(0, _ledger.TotalEverywhere(Custody.Job, Custody.Timber));
        Assert.Equal(0, _ledger.Acquired(Custody.Job, Custody.Timber));
        AssertConserved(0);

        // And its name is still known, so a LIVE retry under it is refused
        // rather than started afresh. This assertion used to read
        // AlreadySatisfied under a comment saying "refused", which is not the
        // same thing at all: AlreadySatisfied is documented as "nothing changed
        // and nothing is wrong", and nothing was recorded. See the test below
        // for what that costs.
        Assert.Equal(NpcCustodyOutcome.Stale, Acquire(0, Custody.Body(_world), 10));
        Assert.Equal(0, _ledger.TotalEverywhere(Custody.Job, Custody.Timber));

        // Restating the rolled-back row itself, which is what a replay does, is
        // still an ordinary success: there is nothing to apply either way.
        Assert.Equal(
            NpcCustodyOutcome.AlreadySatisfied,
            _ledger.Acquire(Custody.Step(0), Custody.Body(_world), Custody.Timber, 10, NpcRowStanding.Voided));
    }

    [Fact]
    public void Forty_stone_out_of_a_chest_can_never_be_answered_as_already_recorded()
    {
        // The concrete failure, start to finish.
        //
        // The player loads yesterday's save. The role's marker rule replays
        // step 3 - forty stone out of chest A - as Voided, and the ledger
        // remembers the name and holds nothing, which is right: the stone is
        // back in the chest. This session the job resumes, physically withdraws
        // the forty stone through the port, and records it under the name its
        // plan derives, which is the same name.
        //
        // Answering that "already satisfied" put forty units of the player's
        // stone out of a chest and into no holding. Acquired and
        // TotalEverywhere both omitted them, so IsConserved stayed true
        // VACUOUSLY - nothing had ever been recorded to conserve - every later
        // transfer of them was refused as not held, and reconciliation
        // eventually reported material from nowhere for a person to guess at.
        Assert.Equal(
            NpcCustodyOutcome.Applied,
            _ledger.Acquire(Custody.Step(3), Custody.Chest(_world), Custody.Timber, 40, NpcRowStanding.Voided));

        NpcCustodyOutcome retry = _ledger.Acquire(Custody.Step(3), Custody.Chest(_world), Custody.Timber, 40);

        Assert.NotEqual(NpcCustodyOutcome.AlreadySatisfied, retry);
        Assert.Equal(NpcCustodyOutcome.Stale, retry);

        // The job now knows it must take a new name, and under one it works.
        Assert.Equal(
            NpcCustodyOutcome.Applied,
            _ledger.Acquire(Custody.Step(4), Custody.Chest(_world), Custody.Timber, 40));
        Assert.Equal(40, _ledger.HoldingAt(Custody.Job, Custody.Chest(_world), Custody.Timber));
        Assert.Equal(40, _ledger.Acquired(Custody.Job, Custody.Timber));
        Assert.True(_ledger.IsConserved(Custody.Job, Custody.Timber));
    }

    [Fact]
    public void A_write_off_is_never_swallowed_by_the_acquisition_at_the_same_step()
    {
        // The second face of the same defect. Acquire and RecordLoss shared one
        // name space, so a role naming a write-off after the step the loss
        // happened at - the obvious convention - collided with that step's
        // acquisition: same job, same place, same material, same count. The
        // person's answer was discarded and the caller was told it had been
        // applied, while the ledger went on saying the material was on the
        // body.
        Assert.Equal(NpcCustodyOutcome.Applied, Acquire(3, Custody.Body(_world), 40));

        Assert.Equal(
            NpcCustodyOutcome.Applied,
            _ledger.RecordLoss(Custody.Step(3), Custody.Body(_world), Custody.Timber, 40));

        Assert.Equal(0, _ledger.HoldingAt(Custody.Job, Custody.Body(_world), Custody.Timber));
        Assert.Equal(
            40,
            _ledger.HoldingAt(
                Custody.Job,
                new NpcCustodyLocation(NpcCustodyPlace.Lost, Custody.Body(_world).Key, _world),
                Custody.Timber));
        AssertConserved(40);

        // Each name space is still idempotent in its own right.
        Assert.Equal(NpcCustodyOutcome.AlreadySatisfied, Acquire(3, Custody.Body(_world), 40));
        Assert.Equal(
            NpcCustodyOutcome.AlreadySatisfied,
            _ledger.RecordLoss(Custody.Step(3), Custody.Body(_world), Custody.Timber, 40));
        AssertConserved(40);
    }

    [Fact]
    public void A_rolled_back_row_is_remembered_even_when_its_job_has_not_been_opened_yet()
    {
        // Replay order is not something a role should have to get right. A
        // voided row refused for arriving before its job left the name free,
        // and the next live row under that name then applied units the world
        // had already undone - a duplication, arrived at by being careful in
        // the wrong order. The transfer half always remembered unconditionally;
        // these two halves of one rule now agree.
        var ledger = new NpcCustodyLedger();

        Assert.Equal(
            NpcCustodyOutcome.Applied,
            ledger.Acquire(Custody.Step(0), Custody.Body(_world), Custody.Timber, 10, NpcRowStanding.Voided));

        ledger.OpenJob(Custody.Job);

        Assert.Equal(
            NpcCustodyOutcome.Stale,
            ledger.Acquire(Custody.Step(0), Custody.Body(_world), Custody.Timber, 10));
        Assert.Equal(0, ledger.TotalEverywhere(Custody.Job, Custody.Timber));

        // A live row for a job nobody opened is still refused, because that is
        // fail-closed and costs nothing.
        Assert.Equal(
            NpcCustodyOutcome.Rejected,
            new NpcCustodyLedger().Acquire(Custody.Step(1), Custody.Body(_world), Custody.Timber, 10));
    }

    [Fact]
    public void A_row_the_marker_rule_could_not_place_credits_nothing()
    {
        // Custody fails closed. Crediting an unplaceable row and being wrong
        // asks a player to write off material that never existed; not
        // crediting it and being wrong leaves real material for reconciliation
        // to find. Only one of those two destroys something.
        Assert.Equal(
            NpcCustodyOutcome.Applied,
            _ledger.Acquire(
                Custody.Step(0), Custody.Body(_world), Custody.Timber, 10, NpcRowStanding.Ambiguous));

        Assert.Equal(0, _ledger.TotalEverywhere(Custody.Job, Custody.Timber));
        Assert.Equal(0, _ledger.Acquired(Custody.Job, Custody.Timber));
        Assert.Equal(
            NpcCustodyOutcome.Stale,
            _ledger.Acquire(Custody.Step(0), Custody.Body(_world), Custody.Timber, 10));
    }

    [Fact]
    public void A_voided_result_never_overwrites_a_transfer_that_already_moved_units()
    {
        // Voided means "never credited, refunded or replayed; there is nothing
        // to undo". Writing it over a Completed record left the units moved in
        // the holdings and the record saying they never moved - and Voided
        // counts as settled, so reconciliation never looked at it again.
        // Silent divergence, no finding raised. Its neighbouring branch had
        // this guard and it did not.
        Acquire(0, Custody.Body(_world), 5);
        Move(1, Custody.Body(_world), Custody.Chest(_world), 5);

        Assert.True(_ledger.TryGetTransfer(Custody.Step(1), out NpcTransferRecord record));
        Assert.Equal(NpcTransferStatus.Completed, record.Status);

        Assert.Equal(
            NpcCustodyOutcome.Rejected,
            _ledger.Finish(
                new NpcTransferReceipt(
                    Custody.Step(1), NpcTransferOutcome.Completed, 5, Custody.Body(_world), "again"),
                NpcRowStanding.Voided));

        Assert.Equal(NpcTransferStatus.Completed, record.Status);
        Assert.Equal(5, _ledger.HoldingAt(Custody.Job, Custody.Chest(_world), Custody.Timber));
        AssertConserved(5);
    }

    [Fact]
    public void A_voided_intent_over_a_live_record_voids_it_rather_than_being_dropped()
    {
        // The other half of the same hole: Begin answered "already satisfied"
        // for a second intent row and dropped whatever standing it carried, so
        // a rolled-back transfer could sit in the record as live.
        Acquire(0, Custody.Body(_world), 5);
        var intent = Intent(1, Custody.Body(_world), Custody.Chest(_world), 5);
        _ledger.CountRecord();
        Assert.Equal(NpcCustodyOutcome.Applied, _ledger.Begin(intent));

        Assert.Equal(NpcCustodyOutcome.Applied, _ledger.Begin(intent, NpcRowStanding.Voided));

        Assert.True(_ledger.TryGetTransfer(intent.Request, out NpcTransferRecord record));
        Assert.Equal(NpcTransferStatus.Voided, record.Status);

        // Again is idempotent; over a record that has moved units it is
        // refused, exactly as the receipt half is.
        Assert.Equal(NpcCustodyOutcome.AlreadySatisfied, _ledger.Begin(intent, NpcRowStanding.Voided));
        AssertConserved(5);
    }

    [Fact]
    public void A_voided_transfer_answers_stale_and_an_ambiguous_one_waits_for_a_person()
    {
        Acquire(0, Custody.Body(_world), 10);

        var voided = Intent(1, Custody.Body(_world), Custody.Chest(_world), 4);
        _ledger.Begin(voided, NpcRowStanding.Voided);
        Assert.Equal(NpcTransferOutcome.Stale, _ledger.Check(voided, out _));

        var ambiguous = Intent(2, Custody.Body(_world), Custody.Chest(_world), 3);
        _ledger.Begin(ambiguous, NpcRowStanding.Ambiguous);
        Assert.Equal(NpcTransferOutcome.Uncertain, _ledger.Check(ambiguous, out _));
        Assert.True(_ledger.TryGetTransfer(ambiguous.Request, out NpcTransferRecord record));
        Assert.True(record.AwaitsResolution);

        AssertConserved(10);
    }

    [Fact]
    public void A_receipt_the_record_cannot_believe_is_kept_as_evidence_and_never_applied()
    {
        Acquire(0, Custody.Body(_world), 3);
        var intent = Intent(1, Custody.Body(_world), Custody.Chest(_world), 3);
        _ledger.CountRecord();
        _ledger.Begin(intent);

        // Five arrived out of three. Somewhere, something is wrong; crediting
        // it is where a ledger starts inventing units.
        Assert.Equal(
            NpcCustodyOutcome.Applied,
            _ledger.Finish(new NpcTransferReceipt(
                intent.Request, NpcTransferOutcome.Partial, 5, intent.From, "five")));

        Assert.True(_ledger.TryGetTransfer(intent.Request, out NpcTransferRecord record));
        Assert.Equal(NpcTransferStatus.Uncertain, record.Status);
        Assert.Equal(0, record.Applied);
        Assert.Equal(3, _ledger.HoldingAt(Custody.Job, Custody.Body(_world), Custody.Timber));
        AssertConserved(3);
    }

    [Fact]
    public void Material_delivered_is_never_moved_again_by_the_job()
    {
        Acquire(0, Custody.Body(_world), 5);
        Move(1, Custody.Body(_world), Custody.Delivered(_world), 5);

        var back = Intent(2, Custody.Delivered(_world), Custody.Body(_world), 5);
        Assert.Equal(NpcTransferOutcome.Refused, _ledger.Check(back, out string reason));
        Assert.Contains("not moved again", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_transfer_planned_against_an_older_view_of_custody_is_stale()
    {
        // What stops two steps of one job both spending the same units.
        Acquire(0, Custody.Body(_world), 5);
        int seen = _ledger.Revision;
        _ledger.CountRecord();

        var intent = new NpcTransferIntent(
            Custody.Step(1), Custody.Body(_world), Custody.Chest(_world), Custody.Timber, 5, seen);

        Assert.Equal(NpcTransferOutcome.Stale, _ledger.Check(intent, out _));
    }

    [Fact]
    public void A_job_with_a_transfer_awaiting_an_answer_starts_no_more_of_them()
    {
        Acquire(0, Custody.Body(_world), 10);
        var stuck = Intent(1, Custody.Body(_world), Custody.Chest(_world), 4);
        _ledger.CountRecord();
        _ledger.Begin(stuck);
        _ledger.MarkUncertain(stuck.Request, "unknown");

        var next = Intent(2, Custody.Body(_world), Custody.Chest(_world), 4);
        Assert.Equal(NpcTransferOutcome.Refused, _ledger.Check(next, out string reason));
        Assert.Contains("waiting on a person", reason, StringComparison.Ordinal);

        // Another job is not held up by it.
        _ledger.Acquire(Custody.OtherStep(0), Custody.Body(_world), Custody.Timber, 4);
        var theirs = new NpcTransferIntent(
            Custody.OtherStep(1), Custody.Body(_world), Custody.Chest(_world), Custody.Timber, 4,
            _ledger.Revision);
        Assert.Equal(NpcTransferOutcome.Unspecified, _ledger.Check(theirs, out _));
    }

    [Fact]
    public void A_job_nobody_opened_holds_nothing()
    {
        var ledger = new NpcCustodyLedger();

        Assert.Equal(
            NpcCustodyOutcome.Rejected,
            ledger.Acquire(Custody.Step(0), Custody.Body(_world), Custody.Timber, 5));
        Assert.Equal(0, ledger.TotalEverywhere(Custody.Job, Custody.Timber));
    }

    [Fact]
    public void Closing_a_job_stops_new_work_and_keeps_what_it_still_holds()
    {
        Acquire(0, Custody.Body(_world), 7);
        _ledger.CloseJob(Custody.Job);

        Assert.False(_ledger.IsOpen(Custody.Job));
        Assert.Equal(7, _ledger.TotalEverywhere(Custody.Job, Custody.Timber));

        var intent = Intent(1, Custody.Body(_world), Custody.Chest(_world), 7);
        Assert.Equal(NpcTransferOutcome.Refused, _ledger.Check(intent, out _));
        AssertConserved(7);
    }

    [Fact]
    public void Loss_is_a_place_rather_than_a_subtraction()
    {
        // So a shortfall can be reported honestly instead of vanishing, and so
        // the invariant still holds after somebody writes material off.
        Acquire(0, Custody.Body(_world), 9);
        Assert.Equal(
            NpcCustodyOutcome.Applied,
            _ledger.RecordLoss(Custody.Step(1), Custody.Body(_world), Custody.Timber, 9));

        Assert.Equal(0, _ledger.HoldingAt(Custody.Job, Custody.Body(_world), Custody.Timber));
        Assert.Equal(9, _ledger.TotalEverywhere(Custody.Job, Custody.Timber));
        AssertConserved(9);

        // And it happens exactly once.
        Assert.Equal(
            NpcCustodyOutcome.AlreadySatisfied,
            _ledger.RecordLoss(Custody.Step(1), Custody.Body(_world), Custody.Timber, 9));
        AssertConserved(9);
    }

    [Fact]
    public void More_cannot_be_written_off_than_the_record_holds()
    {
        Acquire(0, Custody.Body(_world), 3);
        Assert.Equal(
            NpcCustodyOutcome.Rejected,
            _ledger.RecordLoss(Custody.Step(1), Custody.Body(_world), Custody.Timber, 4));
        AssertConserved(3);
    }

    [Fact]
    public void A_place_the_record_has_emptied_is_still_looked_at()
    {
        Acquire(0, Custody.Body(_world), 4);
        Move(1, Custody.Body(_world), Custody.Chest(_world), 4);

        Assert.DoesNotContain(_ledger.Holdings, holding => holding.Location.Equals(Custody.Body(_world)));
        Assert.Contains(_ledger.EverHeld, holding => holding.Location.Equals(Custody.Body(_world)));
    }

    [Fact]
    public void What_the_record_says_the_body_carries_is_readable_without_a_second_store()
    {
        Acquire(0, Custody.Body(_world), 6);
        _ledger.Acquire(Custody.Step(1), Custody.Body(_world), Custody.Nails, 12);

        IReadOnlyList<NpcMaterialStack> carried =
            NpcCarriedInventory.CarriedBy(_ledger, Custody.Job, Custody.Body(_world));

        Assert.Equal(2, carried.Count);
        Assert.Equal(6, carried[0].Count);
        Assert.Equal(12, carried[1].Count);

        var body = new FakeInventory("the body").With(Custody.Timber, 6);
        Assert.Equal(0, NpcCarriedInventory.Difference(_ledger, Custody.Body(_world), Custody.Timber, body));

        body.IsAvailable = false;
        Assert.Null(NpcCarriedInventory.Difference(_ledger, Custody.Body(_world), Custody.Timber, body));

        // A body that cannot be counted is unknown, never zero: zero is a
        // shortfall, and a shortfall is something a player is asked to write
        // off.
        Assert.Null(NpcCarriedInventory.Difference(_ledger, Custody.Body(_world), Custody.Timber, null));
    }

    private NpcCustodyOutcome Acquire(int step, NpcCustodyLocation at, int count) =>
        _ledger.Acquire(Custody.Step(step), at, Custody.Timber, count);

    private NpcTransferIntent Intent(int step, NpcCustodyLocation from, NpcCustodyLocation to, int count) =>
        new NpcTransferIntent(Custody.Step(step), from, to, Custody.Timber, count, _ledger.Revision);

    /// <summary>Records an intent and a completed receipt, the way the executor
    /// would, so a ledger test can move material without an engine.</summary>
    private void Move(int step, NpcCustodyLocation from, NpcCustodyLocation to, int count)
    {
        NpcTransferIntent intent = Intent(step, from, to, count);
        Assert.Equal(NpcTransferOutcome.Unspecified, _ledger.Check(intent, out string reason));
        Assert.Equal(string.Empty, reason);

        _ledger.CountRecord();
        Assert.Equal(NpcCustodyOutcome.Applied, _ledger.Begin(intent));
        _ledger.CountRecord();
        Assert.Equal(
            NpcCustodyOutcome.Applied,
            _ledger.Finish(new NpcTransferReceipt(
                intent.Request, NpcTransferOutcome.Completed, count, intent.From, "moved")));
    }

    private void AssertConserved(int expected)
    {
        Assert.Equal(expected, _ledger.Acquired(Custody.Job, Custody.Timber));
        Assert.Equal(expected, _ledger.TotalEverywhere(Custody.Job, Custody.Timber));
        Assert.True(_ledger.IsConserved(Custody.Job, Custody.Timber));
        Assert.True(_ledger.IsConserved(Custody.Job, Custody.Nails));
    }
}
